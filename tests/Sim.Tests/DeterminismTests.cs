using Xunit;

namespace Sim.Tests;

/// <summary>
/// The core promise of the architecture: same seed + same input log = identical world,
/// verifiable by state hashes. Everything downstream (lockstep netcode, replays, saves)
/// stands on these tests.
/// </summary>
public class DeterminismTests
{
    /// <summary>Pregenerate a deterministic command script from an isolated Rng.</summary>
    private static List<List<Command>> ScriptAggressiveAdvance(World world, ulong scriptSeed, int ticks)
    {
        var scriptRng = Rng.FromSeed(scriptSeed);
        var log = new List<List<Command>>();
        var units = world.Units;

        for (int tick = 1; tick <= ticks; tick++)
        {
            var frame = new List<Command>();
            if (tick % 90 == 0)
            {
                foreach (var u in units)
                {
                    if (!u.Alive) continue;
                    // Attack-move to a random waypoint across the middle of the map.
                    int x = scriptRng.Range(28, 68);
                    int y = scriptRng.Range(8, 56);
                    frame.Add(new Command(u.Id, CommandType.AttackMove,
                        new Fixed2(Fixed.FromInt(x), Fixed.FromInt(y))));
                }
            }
            log.Add(frame);
        }
        return log;
    }

    private static World StandardBattle(ulong seed)
    {
        var world = TestWorlds.Create(seed);
        // Two companies facing each other across the center.
        for (int i = 0; i < 4; i++)
            world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id,
                new Fixed2(TestWorlds.M(10), TestWorlds.M(12 + i * 8)));
        world.Spawn(Side.Allies, UnitTypes.MachineGunSection.Id,
            new Fixed2(TestWorlds.M(14), TestWorlds.M(30)));

        for (int i = 0; i < 4; i++)
            world.Spawn(Side.Central, UnitTypes.RifleSquad.Id,
                new Fixed2(TestWorlds.M(86), TestWorlds.M(52 - i * 8)));
        world.Spawn(Side.Central, UnitTypes.MachineGunSection.Id,
            new Fixed2(TestWorlds.M(82), TestWorlds.M(34)));

        return world;
    }

    [Fact]
    public void SameScript_Twice_ProducesIdenticalHashSequence()
    {
        const int ticks = 900;
        List<List<Command>> script;

        {
            var template = StandardBattle(777);
            script = ScriptAggressiveAdvance(template, scriptSeed: 2026, ticks: ticks);
        }

        var hashesA = RunAndSampleHashes(StandardBattle(777), script);
        var hashesB = RunAndSampleHashes(StandardBattle(777), script);

        Assert.Equal(hashesA.Count, hashesB.Count);
        for (int i = 0; i < hashesA.Count; i++)
            Assert.True(hashesA[i] == hashesB[i], $"hash mismatch at sample {i}");
    }

    private static List<ulong> RunAndSampleHashes(World world, List<List<Command>> script)
    {
        var hashes = new List<ulong>();
        for (int t = 0; t < script.Count; t++)
        {
            world.Step(script[t]);
            if (world.Tick % 30 == 0)
                hashes.Add(world.StateHash());
        }
        return hashes;
    }

    [Fact]
    public void InputLog_Replay_MatchesOriginalFinalHash()
    {
        const int ticks = 600;
        var original = StandardBattle(31337);
        var inputLog = new List<IReadOnlyList<Command>>(ticks);

        for (int t = 0; t < ticks; t++)
        {
            IReadOnlyList<Command> frame;
            if ((t + 1) % 60 == 0)
            {
                var frameList = new List<Command>();
                foreach (var u in original.Units)
                    if (u.Alive)
                        frameList.Add(new Command(u.Id, CommandType.AttackMove,
                            new Fixed2(TestWorlds.M(48), TestWorlds.M(32))));
                frame = frameList;
            }
            else frame = Array.Empty<Command>();

            inputLog.Add(frame);
            original.Step(frame);
        }

        var replay = StandardBattle(31337);
        foreach (var frame in inputLog)
            replay.Step(frame);

        Assert.Equal(original.StateHash(), replay.StateHash());
    }

    [Fact]
    public void DivergentInput_WorldsDivergeOnlyAfterTheExtraCommand()
    {
        const int divergenceTick = 300;
        const int totalTicks = 450;

        var a = StandardBattle(999);
        var b = StandardBattle(999);

        for (int t = 1; t <= totalTicks; t++)
        {
            var sharedFrame = Array.Empty<Command>();
            if (t % 120 == 0)
                sharedFrame = new[]
                {
                    new Command(0, CommandType.AttackMove, new Fixed2(TestWorlds.M(48), TestWorlds.M(32))),
                };

            if (t == divergenceTick)
            {
                // B alone receives one extra move order for unit 0.
                var extra = new List<Command>(sharedFrame)
                {
                    new(0, CommandType.Move, new Fixed2(TestWorlds.M(20), TestWorlds.M(50))),
                };
                b.Step(extra);
                a.Step(sharedFrame);
            }
            else
            {
                a.Step(sharedFrame);
                b.Step(sharedFrame);
            }

            if (t == divergenceTick - 1)
                Assert.Equal(a.StateHash(), b.StateHash());
        }

        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void DifferentSeeds_DivergeOnceCombatRollsOccur()
    {
        var a = TestWorlds.Create(1);
        var b = TestWorlds.Create(2);

        // Two rifle squads per side, in each other's faces; shots start flying immediately.
        foreach (var world in new[] { a, b })
        {
            world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(40), TestWorlds.M(32)));
            world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(40), TestWorlds.M(36)));
            world.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(46), TestWorlds.M(32)));
            world.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(46), TestWorlds.M(36)));
        }

        for (int t = 0; t < 300; t++)
        {
            a.Step();
            b.Step();
        }

        Assert.True(a.Units.Any(u => u.Hp != u.Type.MaxHp), "scenario should have produced combat");
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }
}
