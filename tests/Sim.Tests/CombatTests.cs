using Xunit;

namespace Sim.Tests;

public class CombatTests
{
    private static (World world, int shooterId, int targetId) SpawnDuel(ulong seed = 5)
    {
        var world = TestWorlds.Create(seed);
        int shooter = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(40), TestWorlds.M(32)));
        int target = world.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(46), TestWorlds.M(32)));
        return (world, shooter, target);
    }

    [Fact]
    public void SquadEngagesNearestEnemy_DealsDamageAndSuppresses()
    {
        var (world, shooter, target) = SpawnDuel();

        for (int t = 0; t < 120; t++) // 4 seconds: ~4 volleys
            world.Step();

        var victim = world.Units[target];
        Assert.True(victim.Hp < victim.Type.MaxHp, "target should have taken damage");
        Assert.True(victim.Suppression > Fixed.Zero, "target should be suppressed");
    }

    [Fact]
    public void MoveOrderUnits_DoNotShoot()
    {
        var (world, shooter, target) = SpawnDuel();
        // March beyond sight of the enemy; plain Move ignores enemies en route,
        // and the corner is far enough that going idle finds no target either.
        var rallyPoint = new Fixed2(TestWorlds.M(8), TestWorlds.M(56)); // ~40 m march
        for (int t = 0; t < 420; t++)
            world.Step(new[] { new Command(shooter, CommandType.Move, rallyPoint) });

        Assert.Equal(world.Units[target].Type.MaxHp, world.Units[target].Hp);
        Assert.Equal(OrderKind.Idle, world.Units[shooter].Order); // arrived
    }

    [Fact]
    public void Cover_ReducesHitsTaken()
    {
        // Aggregate over several seeds so a single unlucky stream cannot mask the effect.
        const int ticksPerSeed = 400;
        int openHits = 0, coveredHits = 0;

        for (ulong seed = 1; seed <= 8; seed++)
        {
            var (open, _, openTarget) = SpawnDuel(seed);
            var (covered, _, coveredTarget) = SpawnDuel(seed);
            var coveredUnit = covered.Units[coveredTarget];
            coveredUnit.Pos = new Fixed2(TestWorlds.M(56), TestWorlds.M(32)); // in the trench
            covered.Units[coveredTarget] = coveredUnit;

            for (int t = 0; t < ticksPerSeed; t++)
            {
                open.Step();
                openHits += open.Events.Count(e => e.Hit && e.TargetId == openTarget);

                covered.Step();
                coveredHits += covered.Events.Count(e => e.Hit && e.TargetId == coveredTarget);
            }
        }

        Assert.True(openHits > 0 && coveredHits > 0, "sanity: both scenarios must land hits");
        Assert.True(coveredHits < openHits,
            $"trench should reduce hits: covered {coveredHits} vs open {openHits}");
    }

    [Fact]
    public void Suppression_DecaysToZeroWhenFireStops()
    {
        var (world, shooter, target) = SpawnDuel();
        for (int t = 0; t < 120; t++)
            world.Step();
        Assert.True(world.Units[target].Suppression > Fixed.Zero);

        // March the shooter far away; no more incoming fire.
        var away = new Fixed2(TestWorlds.M(90), TestWorlds.M(60));
        for (int t = 0; t < 900; t++) // 30 s of decay at 12/s clears anything from 100
            world.Step(new[] { new Command(shooter, CommandType.Move, away) });

        Assert.Equal(Fixed.Zero, world.Units[target].Suppression);
    }

    [Fact]
    public void PinnedSquads_Crawl()
    {
        var empty = TestWorlds.Create(8);

        int normal = empty.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(10), TestWorlds.M(10)));
        var goal = new Fixed2(TestWorlds.M(40), TestWorlds.M(10)); // 30 m march

        var pinned = TestWorlds.Create(8);
        int pinnedId = pinned.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(10), TestWorlds.M(10)));
        var u = pinned.Units[pinnedId];
        u.Suppression = SimConfig.MaxSuppression; // hard-pinned
        pinned.Units[pinnedId] = u;

        for (int t = 0; t < 120; t++)
        {
            empty.Step(new[] { new Command(normal, CommandType.Move, goal) });
            pinned.Step(new[] { new Command(pinnedId, CommandType.Move, goal) });
        }

        Fixed normalProgress = empty.Units[normal].Pos.X - TestWorlds.M(10);
        Fixed pinnedProgress = pinned.Units[pinnedId].Pos.X - TestWorlds.M(10);
        Assert.True(pinnedProgress < normalProgress,
            $"pinned moved {pinnedProgress} vs normal {normalProgress}");
    }

    [Fact]
    public void Kills_EarnVeterancyRanks()
    {
        var world = TestWorlds.Create(17);
        int hunter = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(40), TestWorlds.M(32)));

        // Four one-shot sacrificial targets in range, permanently pinned so they
        // cannot fight back and muddy the deterministic kill count.
        var victims = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            int victim = world.Spawn(Side.Central, UnitTypes.RifleSquad.Id,
                new Fixed2(TestWorlds.M(44 + i * 3), TestWorlds.M(32)));
            victims.Add(victim);
            var v = world.Units[victim];
            v.Hp = Fixed.FromInt(25); // dies to a single rifle hit
            v.Suppression = SimConfig.MaxSuppression;
            world.Units[victim] = v;
        }

        for (int t = 0; t < 600 && world.Units[hunter].Kills < 4; t++)
        {
            // Keep the survivors pinned for the duration.
            foreach (var id in victims)
            {
                if (!world.Units[id].Alive) continue;
                var v = world.Units[id];
                v.Suppression = SimConfig.MaxSuppression;
                world.Units[id] = v;
            }
            world.Step();
        }

        var killer = world.Units[hunter];
        Assert.True(killer.Kills >= 4, $"expected >= 4 kills, got {killer.Kills}");
        Assert.Equal(1, killer.Rank); // first rank threshold is 4 kills
    }

    [Fact]
    public void Obstacles_BlockLineOfSight()
    {
        // The building at (48,22) with radius 4 sits between y=18 and y=26.
        var map = TestWorlds.SmallMap();
        var north = new Fixed2(TestWorlds.M(48), TestWorlds.M(30));
        var south = new Fixed2(TestWorlds.M(48), TestWorlds.M(14));
        var eastFar = new Fixed2(TestWorlds.M(80), TestWorlds.M(30));

        Assert.False(LineOfSight.Clear(map, north, south), "segment through building must be blocked");
        Assert.True(LineOfSight.Clear(map, eastFar, south), "segment around building must be clear");
    }
}
