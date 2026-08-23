using Lockstep;
using Xunit;

using Sim;

namespace Lockstep.Tests;

/// <summary>
/// The heart of the netcode promise: two peers, two worlds, one shared input stream.
/// Whatever one world becomes, the other becomes too — or the session says so loudly.
/// </summary>
public class SessionTests
{
    [Fact]
    public void EmptyMatch_BothPeersAdvance_Identically()
    {
        var (wa, wb, a, b, _, _) = Harness.CreatePairedSessions();
        (a, b).Start();

        Assert.True(Harness.PumpUntil(a, b, targetTick: 300), "sessions stalled");

        Assert.Equal(300, a.ExecutedTick);
        Assert.Equal(300, b.ExecutedTick);
        Assert.Equal(wa.StateHash(), wb.StateHash());
        Assert.Equal(SessionStatus.Running, a.Status);
        Assert.Equal(SessionStatus.Running, b.Status);
    }

    [Fact]
    public void CommandsCrossTheWire_AndBothWorldsStayIdentical()
    {
        var (wa, wb, a, b, _, _) = Harness.CreatePairedSessions();
        (a, b).Start();

        // Lockstep rule: both peers construct IDENTICAL worlds. Player A "owns"
        // the allies squad, player B the central one, but both units exist in
        // both worlds - ownership only decides who issues its commands.
        int alliesSquadA = wa.Spawn(Side.Allies, UnitTypes.RifleSquad.Id,
            new Fixed2(Harness.M(20), Harness.M(32)));
        int centralSquadA = wa.Spawn(Side.Central, UnitTypes.RifleSquad.Id,
            new Fixed2(Harness.M(76), Harness.M(32)));
        int alliesSquadB = wb.Spawn(Side.Allies, UnitTypes.RifleSquad.Id,
            new Fixed2(Harness.M(20), Harness.M(32)));
        int centralSquadB = wb.Spawn(Side.Central, UnitTypes.RifleSquad.Id,
            new Fixed2(Harness.M(76), Harness.M(32)));
        Assert.Equal(alliesSquadA, alliesSquadB);
        Assert.Equal(centralSquadA, centralSquadB);

        var center = new Fixed2(Harness.M(48), Harness.M(32));
        int shotsSeen = 0;

        for (int round = 0; round < 900 * 4 && a.ExecutedTick < 900; round++)
        {
            if (a.ExecutedTick % 90 == 0 && !wa.Match.Finished)
                a.EnqueueLocal(new[] { new Command(alliesSquadA, CommandType.AttackMove, center) });
            if (b.ExecutedTick % 90 == 0 && !wb.Match.Finished)
                b.EnqueueLocal(new[] { new Command(centralSquadB, CommandType.AttackMove, center) });

            a.Update();
            b.Update();
            shotsSeen += wa.Events.Count;
        }

        Assert.True(a.ExecutedTick >= 900 || wa.Match.Finished, "match should have run its course");
        Assert.Equal(wa.StateHash(), wb.StateHash());
        Assert.True(shotsSeen > 0, "the scripted squads should have exchanged fire");
        Assert.True(wa.Units[alliesSquadA].Pos != new Fixed2(Harness.M(20), Harness.M(32)),
            "player A's command must have moved his squad");
    }

    [Fact]
    public void NetworkLatency_DoesNotBreakConvergence()
    {
        // Five pumps of delay each way: frames and hash reports cross slowly, but
        // the send window absorbs it and execution stays identical.
        var (wa, wb, a, b, _, _) = Harness.CreatePairedSessions(delayPumpsEachWay: 5);
        (a, b).Start();

        // Identical worlds on both peers; each side commands its own squad's id.
        int squadA = wa.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(Harness.M(30), Harness.M(30)));
        wa.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(Harness.M(66), Harness.M(34)));
        int squadB = wb.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(Harness.M(30), Harness.M(30)));
        wb.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(Harness.M(66), Harness.M(34)));
        Assert.Equal(squadA, squadB);

        var center = new Fixed2(Harness.M(48), Harness.M(32));
        bool ok = Harness.PumpUntil(a, b, targetTick: 400, maxRoundsPerTick: 12,
            onRound: () =>
            {
                if (a.ExecutedTick % 60 == 0)
                    a.EnqueueLocal(new[] { new Command(squadA, CommandType.AttackMove, center) });
                if (b.ExecutedTick % 60 == 0)
                    b.EnqueueLocal(new[] { new Command(squadB, CommandType.AttackMove, center) });
            });

        Assert.True(ok, $"latency stalled execution entirely: a={a.Status} b={b.Status}");
        Assert.Equal(400, a.ExecutedTick);
        Assert.Equal(400, b.ExecutedTick); // both peers, not just the fast one
        Assert.Equal(wa.StateHash(), wb.StateHash());
    }

    [Fact]
    public void SabotagedState_IsCaughtAsDesync()
    {
        var (wa, wb, a, b, _, _) = Harness.CreatePairedSessions(inputDelay: 1);
        // One squad per side, spawned identically on both peers.
        wa.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(Harness.M(30), Harness.M(30)));
        wa.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(Harness.M(66), Harness.M(34)));
        wb.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(Harness.M(30), Harness.M(30)));
        wb.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(Harness.M(66), Harness.M(34)));
        (a, b).Start();
        Assert.True(Harness.PumpUntil(a, b, targetTick: 60));

        // Reach into world A directly — the kind of corruption a non-deterministic
        // system call would cause. Hash reports must expose it within a tick.
        var saboteur = wa.Units[0];
        saboteur.Suppression = Fixed.FromInt(42);
        wa.Units[0] = saboteur;

        int guard = 2000;
        while (guard-- > 0 &&
               a.Status == SessionStatus.Running &&
               b.Status == SessionStatus.Running)
        {
            a.Update();
            b.Update();
        }

        Assert.Equal(SessionStatus.Desynced, a.Status);
        Assert.Equal(SessionStatus.Desynced, b.Status);
        Assert.NotNull(a.Desync);
        Assert.NotNull(b.Desync);
        // Both peers agree about WHERE reality split.
        Assert.Equal(a.Desync!.Tick, b.Desync!.Tick);
        Assert.NotEqual(a.Desync.LocalHash, a.Desync.RemoteHash);
    }

    [Fact]
    public void SeedMismatch_RefusesHandshake()
    {
        var (_, _, a, b, _, _) = Harness.CreatePairedSessions(seed: 100, overrideSeedB: 200);
        (a, b).Start();

        int guard = 100;
        while (guard-- > 0 &&
               a.Status is SessionStatus.Created or SessionStatus.AwaitingHandshake or SessionStatus.Running &&
               b.Status is SessionStatus.Created or SessionStatus.AwaitingHandshake or SessionStatus.Running)
        {
            a.Update();
            b.Update();
        }

        Assert.Equal(SessionStatus.Faulted, a.Status);
        Assert.Equal(SessionStatus.Faulted, b.Status);
        Assert.Contains("seed", a.FaultReason);
        Assert.Contains("seed", b.FaultReason);
    }

    [Fact]
    public void MapMismatch_RefusesHandshake()
    {
        var otherMap = Harness.Map() with { Width = Harness.M(120) };
        var (_, _, a, b, _, _) = Harness.CreatePairedSessions(mapOverride: otherMap);
        // Note: worldB gets the DEFAULT map, world A the widened one.
        (a, b).Start();

        int guard = 100;
        while (guard-- > 0 &&
               a.Status is not SessionStatus.Faulted &&
               b.Status is not SessionStatus.Faulted)
        {
            a.Update();
            b.Update();
        }

        Assert.Equal(SessionStatus.Faulted, a.Status);
        Assert.Contains("map", a.FaultReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputDelay_MatchesAcrossHandshake()
    {
        var (_, _, a, b, _, _) = Harness.CreatePairedSessions(inputDelay: 6);
        (a, b).Start();
        Assert.True(Harness.PumpUntil(a, b, targetTick: 10));
        Assert.Equal((byte)6, a.InputDelayTicks);
        Assert.Equal((byte)6, b.InputDelayTicks);
    }
}
