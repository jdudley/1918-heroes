using Xunit;

namespace Sim.Tests;

/// <summary>
/// Headless AI-vs-AI matches — the same components the game runs, minus rendering.
/// This is the template for the overnight balance sweeps from the design doc.
/// </summary>
public class HeadlessMatchTests
{
    public static MapDef BattleMap() => new()
    {
        Name = "headless-battle",
        Width = TestWorlds.M(96),
        Height = TestWorlds.M(64),
        CapturePoints = new[]
        {
            new CapturePointSpec(new Fixed2(TestWorlds.M(48), TestWorlds.M(32)), TestWorlds.M(6), IsVictoryPoint: true),
            new CapturePointSpec(new Fixed2(TestWorlds.M(28), TestWorlds.M(32)), TestWorlds.M(6), IsVictoryPoint: false),
            new CapturePointSpec(new Fixed2(TestWorlds.M(68), TestWorlds.M(32)), TestWorlds.M(6), IsVictoryPoint: false),
        },
        Cover = new[]
        {
            new CoverObject(new Fixed2(TestWorlds.M(40), TestWorlds.M(26)), TestWorlds.M(3), CoverKind.Crater),
            new CoverObject(new Fixed2(TestWorlds.M(56), TestWorlds.M(38)), TestWorlds.M(4), CoverKind.Trench),
        },
        Spawns = new[]
        {
            // Allies, west
            new SpawnSpec(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(10), TestWorlds.M(16))),
            new SpawnSpec(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(10), TestWorlds.M(26))),
            new SpawnSpec(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(10), TestWorlds.M(38))),
            new SpawnSpec(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(10), TestWorlds.M(48))),
            new SpawnSpec(Side.Allies, UnitTypes.MachineGunSection.Id, new Fixed2(TestWorlds.M(16), TestWorlds.M(32))),
            // Central, east
            new SpawnSpec(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(86), TestWorlds.M(16))),
            new SpawnSpec(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(86), TestWorlds.M(26))),
            new SpawnSpec(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(86), TestWorlds.M(38))),
            new SpawnSpec(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(86), TestWorlds.M(48))),
            new SpawnSpec(Side.Central, UnitTypes.MachineGunSection.Id, new Fixed2(TestWorlds.M(80), TestWorlds.M(32))),
        },
    };

    private static (World world, RudimentaryAi allies, RudimentaryAi central) RunMatch(
        ulong seed, int ticks, out int loudEvents)
    {
        var world = new World(BattleMap(), seed);
        var alliesAi = new RudimentaryAi(Side.Allies);
        var centralAi = new RudimentaryAi(Side.Central);
        loudEvents = 0;

        for (int t = 0; t < ticks && !world.Match.Finished; t++)
        {
            var alliesCommands = alliesAi.Think(world);
            var centralCommands = centralAi.Think(world);

            var combined = new List<Command>(alliesCommands.Count + centralCommands.Count);
            combined.AddRange(alliesCommands);
            combined.AddRange(centralCommands);

            world.Step(combined);
            loudEvents += world.Events.Count + world.Explosions.Count;
        }
        return (world, alliesAi, centralAi);
    }

    [Fact]
    public void AiVsAi_ProducesARealBattle()
    {
        var (world, _, _) = RunMatch(seed: 20260822, ticks: 5400, out int loudEvents);

        // Artillery ends fights faster now, so fewer rifle volleys accumulate
        // before a verdict - count shell impacts alongside bullets.
        Assert.True(loudEvents > 250, $"expected a real firefight, saw {loudEvents} shots+blasts");
        Assert.True(world.Units.Any(u => !u.Alive), "expected casualties");
        Assert.True(world.Points.Any(p => p.Owner != Side.Neutral) || world.Match.TicketsAllies < 500,
            "territory or tickets should have moved");
    }

    [Fact]
    public void AiVsAi_MatchCanFinishByTicketDrain()
    {
        // Faster drain than live tuning: symmetric AIs grind on the center VP,
        // so give whichever side wins the middle a quick clock to run out.
        var options = new MatchOptions { StartingTickets = 100, TicketDrainPerVpPerSecond = Fixed.FromInt(2) };
        var map = BattleMap();
        var world = new World(map, seed: 7, options);
        var aiA = new RudimentaryAi(Side.Allies);
        var aiB = new RudimentaryAi(Side.Central);

        int guard = 5400;
        while (!world.Match.Finished && guard-- > 0)
        {
            var cmds = new List<Command>();
            cmds.AddRange(aiA.Think(world));
            cmds.AddRange(aiB.Think(world));
            world.Step(cmds);
        }

        Assert.True(world.Match.Finished, "a 150-ticket match should resolve within 3 minutes of sim time");
        Assert.Contains(world.Match.Winner, new[] { Side.Allies, Side.Central });
    }

    [Fact]
    public void AiVsAi_IsDeterministicAcrossRuns()
    {
        var (a, _, _) = RunMatch(seed: 31415, ticks: 1200, out int shotsA);
        var (b, _, _) = RunMatch(seed: 31415, ticks: 1200, out int shotsB);

        Assert.Equal(shotsA, shotsB);
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
