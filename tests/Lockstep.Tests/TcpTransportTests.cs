using Lockstep;
using Sim;
using Xunit;

namespace Lockstep.Tests;

/// <summary>
/// Real-socket tests: the same path two copies of the game use over LAN/Tailscale.
/// Single-threaded by design — both ends are pumped round-robin from the test.
/// </summary>
public class TcpTransportTests : IDisposable
{
    private readonly List<TcpTransport> _transports = new();

    private TcpTransport Track(TcpTransport t)
    {
        _transports.Add(t);
        return t;
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Pump both ends until connected (or budget out).</summary>
    private bool PumpUntilConnected(TcpTransport host, TcpTransport join, int maxPumps = 500)
    {
        for (int i = 0; i < maxPumps; i++)
        {
            if (host.Connected && join.Connected)
                return true;
            host.Pump();
            join.Pump();
        }
        return host.Connected && join.Connected;
    }

    [Fact]
    public void ConnectsOverLoopback()
    {
        int port = FreePort();
        var host = Track(TcpTransport.Listen(port));
        var join = Track(TcpTransport.Connect("127.0.0.1", port));

        Assert.True(PumpUntilConnected(host, join), "failed to connect");
    }

    [Fact]
    public void FramesSurviveTheWire_BothDirections()
    {
        int port = FreePort();
        var host = Track(TcpTransport.Listen(port));
        var join = Track(TcpTransport.Connect("127.0.0.1", port));
        Assert.True(PumpUntilConnected(host, join));

        // Varied sizes including multi-KB frames that force fragmentation.
        var toJoin = new List<byte[]>();
        var toHost = new List<byte[]>();
        var rng = new Random(99);
        for (int i = 0; i < 40; i++)
        {
            int size = i switch
            {
                < 10 => 1 + i,
                < 20 => 100 + i,
                _ => 4000 + rng.Next(3000),
            };
            var a = new byte[size];
            var b = new byte[size];
            rng.NextBytes(a);
            rng.NextBytes(b);
            toJoin.Add(a);
            toHost.Add(b);
            host.Send(a);
            join.Send(b);
        }

        // Drain BOTH ends simultaneously: each side must pump to send while the other receives.
        var byHost = new List<byte[]>();
        var byJoin = new List<byte[]>();
        var buf = new byte[ITransport.MaxPacketSize];
        int guard = 100_000;
        while ((byHost.Count < toJoin.Count || byJoin.Count < toHost.Count) && guard-- > 0)
        {
            host.Pump();
            join.Pump();

            int n = host.Receive(buf);
            if (n > 0) byHost.Add(buf.AsSpan(0, n).ToArray());
            n = join.Receive(buf);
            if (n > 0) byJoin.Add(buf.AsSpan(0, n).ToArray());
        }

        // host receives what join sent, and vice versa.
        Assert.Equal(toHost.Count, byHost.Count);
        Assert.Equal(toJoin.Count, byJoin.Count);
        for (int i = 0; i < toHost.Count; i++)
            Assert.Equal(toHost[i], byHost[i]);
        for (int i = 0; i < toJoin.Count; i++)
            Assert.Equal(toJoin[i], byJoin[i]);
    }

    [Fact]
    public void Disconnection_IsDetected()
    {
        int port = FreePort();
        var host = Track(TcpTransport.Listen(port));
        var join = Track(TcpTransport.Connect("127.0.0.1", port));
        Assert.True(PumpUntilConnected(host, join));

        join.Dispose();
        bool detected = false;
        for (int i = 0; i < 200 && !detected; i++)
        {
            host.Pump();
            detected = host.Failed;
        }
        Assert.True(detected, "host should notice the closed link");
    }

    [Fact]
    public void LockstepSessions_RunOverRealSockets()
    {
        int port = FreePort();
        var map = Harness.Map();

        ulong hostSeed = 987654321UL;

        // Armies live in the map: after the joiner adopts the host's seed and its world
        // is rebuilt, both peers spawn identical forces from the same definition.
        var battleMap = map with
        {
            Spawns = new[]
            {
                new SpawnSpec(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(Harness.M(20), Harness.M(32))),
                new SpawnSpec(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(Harness.M(76), Harness.M(32))),
            },
        };

        var worldHost = new World(battleMap, hostSeed);

        // Joiner starts with a DIFFERENT seed and adopts the host's during handshake.
        var worldJoin = new World(battleMap, 42UL);

        var hostTcp = Track(TcpTransport.Listen(port));
        var joinTcp = Track(TcpTransport.Connect("127.0.0.1", port));
        Assert.True(PumpUntilConnected(hostTcp, joinTcp));

        var sessionHost = new LockstepSession(worldHost, hostTcp, inputDelayTicks: 2);
        var sessionJoin = new LockstepSession(worldJoin, joinTcp, inputDelayTicks: 2) { AdoptPeerSeed = true };

        World? joinerWorld = worldJoin;
        sessionJoin.WorldReplaced += w => joinerWorld = w;
        World? hostWorld = worldHost;
        sessionHost.WorldReplaced += w => hostWorld = w;

        sessionHost.Start();
        sessionJoin.Start();

        var center = new Fixed2(Harness.M(48), Harness.M(32));
        bool pumped = Harness.PumpUntil(sessionHost, sessionJoin, targetTick: 240, maxRoundsPerTick: 12,
            onRound: () =>
            {
                hostTcp.Pump();
                joinTcp.Pump();
                if (sessionHost.ExecutedTick % 60 == 0)
                    sessionHost.EnqueueLocal(new[]
                    {
                        new Command(0, CommandType.AttackMove, center),
                        new Command(1, CommandType.AttackMove, center),
                    });
                if (sessionJoin.ExecutedTick % 60 == 0)
                    sessionJoin.EnqueueLocal(new[]
                    {
                        new Command(1, CommandType.AttackMove, center),
                    });
            });

        Assert.True(pumped, $"sessions stalled: host={sessionHost.Status} join={sessionJoin.Status}");
        Assert.Equal(SessionStatus.Running, sessionHost.Status);
        Assert.Equal(SessionStatus.Running, sessionJoin.Status);
        Assert.NotNull(hostWorld);
        Assert.NotNull(joinerWorld);
        Assert.Equal(hostWorld!.MatchSeed, joinerWorld!.MatchSeed); // adoption happened
        Assert.Equal(hostWorld.StateHash(), joinerWorld.StateHash());
    }

    public void Dispose()
    {
        foreach (var t in _transports)
            t.Dispose();
    }
}
