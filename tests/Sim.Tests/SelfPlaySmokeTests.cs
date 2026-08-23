using Xunit;

namespace Sim.Tests;

/// <summary>
/// Headless AI-vs-AI self-play: the template for the overnight balance sweeps.
/// Both sides receive identical scripted attack-move orders; the assertions verify
/// determinism at scale and that real combat emerges.
/// </summary>
public class SelfPlaySmokeTests
{
    private static World StandardBattle(ulong seed)
    {
        var world = new World(TestWorlds.SmallMap(), seed);

        for (int i = 0; i < 8; i++)
            world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id,
                new Fixed2(TestWorlds.M(8), TestWorlds.M(8 + i * 6)));
        world.Spawn(Side.Allies, UnitTypes.MachineGunSection.Id,
            new Fixed2(TestWorlds.M(14), TestWorlds.M(28)));

        for (int i = 0; i < 8; i++)
            world.Spawn(Side.Central, UnitTypes.RifleSquad.Id,
                new Fixed2(TestWorlds.M(88), TestWorlds.M(56 - i * 6)));
        world.Spawn(Side.Central, UnitTypes.MachineGunSection.Id,
            new Fixed2(TestWorlds.M(82), TestWorlds.M(36)));

        return world;
    }

    /// <summary>Every 60 ticks, all living squads get a fresh attack-move waypoint in the middle band.</summary>
    private static List<List<Command>> BuildAggressiveScript(int totalTicks)
    {
        var rng = Rng.FromSeed(2026);
        var script = new List<List<Command>>(totalTicks);
        for (int t = 1; t <= totalTicks; t++)
        {
            var frame = new List<Command>();
            if (t % 60 == 0)
            {
                for (int id = 0; id < 18; id++)
                {
                    int x = rng.Range(28, 69);
                    int y = rng.Range(10, 55);
                    frame.Add(new Command(id, CommandType.AttackMove,
                        new Fixed2(Fixed.FromInt(x), Fixed.FromInt(y))));
                }
            }
            script.Add(frame);
        }
        return script;
    }

    [Fact]
    public void NinetySecondBattle_DeterministicAndBloody()
    {
        const int totalTicks = 2700;
        var script = BuildAggressiveScript(totalTicks);

        var a = StandardBattle(555);
        var b = StandardBattle(555);

        var hashesA = new List<ulong>();
        var hashesB = new List<ulong>();
        int shotCountA = 0;

        for (int t = 0; t < totalTicks; t++)
        {
            a.Step(script[t]);
            b.Step(script[t]);
            shotCountA += a.Events.Count;

            if ((t + 1) % 150 == 0)
            {
                hashesA.Add(a.StateHash());
                hashesB.Add(b.StateHash());
            }
        }

        // Determinism at scale.
        Assert.Equal(hashesA.Count, hashesB.Count);
        for (int i = 0; i < hashesA.Count; i++)
            Assert.True(hashesA[i] == hashesB[i], $"diverged at sample {i} (tick {(i + 1) * 150})");

        // Real combat emerged.
        Assert.True(shotCountA > 200, $"expected heavy fighting, saw {shotCountA} shots");
        bool casualtiesOrWounds = a.Units.Any(u => !u.Alive || u.Hp != u.Type.MaxHp);
        Assert.True(casualtiesOrWounds, "battle should have produced casualties");
    }
}
