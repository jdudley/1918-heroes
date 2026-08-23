using Lockstep;
using Xunit;

using Sim;

namespace Lockstep.Tests;

/// <summary>Shared helpers: a small map plus paired running sessions.</summary>
public static class Harness
{
    public static Fixed M(int v) => Fixed.FromInt(v);

    public static MapDef Map() => new()
    {
        Name = "lockstep-test",
        Width = M(96),
        Height = M(64),
        CapturePoints = new[]
        {
            new CapturePointSpec(new Fixed2(M(48), M(32)), M(6), IsVictoryPoint: true),
        },
    };

    /// <summary>Two worlds + two sessions over a loopback with optional per-direction pump delay.
    /// mapOverride/overrideSeedB apply to world A only, to create handshake mismatches on demand.</summary>
    public static (World worldA, World worldB, LockstepSession a, LockstepSession b, LoopbackTransport ta, LoopbackTransport tb)
        CreatePairedSessions(ulong seed = 777, byte inputDelay = 3, int delayPumpsEachWay = 0,
            ulong? overrideSeedB = null, MapDef? mapOverride = null)
    {
        var worldA = new World(mapOverride ?? Map(), seed);
        var worldB = new World(Map(), overrideSeedB ?? seed);

        var (ta, tb) = LoopbackTransport.CreatePair(delayPumpsEachWay);
        var a = new LockstepSession(worldA, ta, inputDelay);
        var b = new LockstepSession(worldB, tb, inputDelay);
        return (worldA, worldB, a, b, ta, tb);
    }

    public static void Start(this (LockstepSession a, LockstepSession b) pair)
    {
        pair.a.Start();
        pair.b.Start();
    }

    public static bool PumpUntil(LockstepSession a, LockstepSession b, int targetTick, int maxRoundsPerTick = 8,
        Action? onRound = null)
    {
        int budget = targetTick * maxRoundsPerTick + 64;
        while (a.ExecutedTick < targetTick || b.ExecutedTick < targetTick)
        {
            if (budget-- <= 0)
                return false;
            onRound?.Invoke();
            a.Update();
            b.Update();

            if (a.Status is SessionStatus.Desynced or SessionStatus.Faulted &&
                b.Status is SessionStatus.Desynced or SessionStatus.Faulted)
                return false;
        }
        return true;
    }
}
