using Xunit;

namespace Sim.Tests;

/// <summary>
/// Artillery is the centerpiece mechanic: shells must hurt, craters must become
/// real cover, buildings must become rubble, creeping barrages must walk, and
/// guns must obey their cooldown.
/// </summary>
public class ArtilleryTests
{
    private static MapDef BareMap(params Obstacle[] blockers) => new()
    {
        Name = "artillery-test",
        Width = TestWorlds.M(96),
        Height = TestWorlds.M(64),
        SightBlockers = blockers,
    };

    private static (World world, int observer, int victim) SpawnDuelPair(MapDef map, ulong seed)
    {
        var world = new World(map, seed);
        // Observer attributes barrage calls; victim stands at ground zero.
        int observer = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(8), TestWorlds.M(8)));
        int victim = world.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));
        return (world, observer, victim);
    }

    private static List<Fixed2> RunBarrage(World world, Fixed2 start, Fixed2 end, int ticks)
    {
        var impacts = new List<Fixed2>();
        world.Step(new[]
        {
            new Command(0, CommandType.Barrage, start, end),
        });

        for (int t = 0; t < ticks && !world.Match.Finished; t++)
        {
            world.Step();
            impacts.AddRange(world.Explosions);
        }
        return impacts;
    }

    [Fact]
    public void StationaryBarrage_DamagesSuppressesAndCraters()
    {
        var (world, _, victim) = SpawnDuelPair(BareMap(), seed: 5);

        var impacts = RunBarrage(world,
            new Fixed2(TestWorlds.M(48), TestWorlds.M(32)),
            new Fixed2(TestWorlds.M(48), TestWorlds.M(32)), ticks: 260);

        var v = world.Units[victim];
        Assert.True(v.Hp < v.Type.MaxHp || !v.Alive, "ground-zero squad must take shell damage");
        Assert.True(impacts.Count >= SimConfig.ShellsPerBarrage - 2, $"expected ~{SimConfig.ShellsPerBarrage} impacts, saw {impacts.Count}");
        Assert.True(world.DynamicCover.Count(c => c.Kind == CoverKind.Crater) >= SimConfig.ShellsPerBarrage - 2,
            "every impact leaves a crater");
    }

    [Fact]
    public void CreepingBarrage_WalksTheLine()
    {
        var (world, _, _) = SpawnDuelPair(BareMap(), seed: 6);

        var start = new Fixed2(TestWorlds.M(40), TestWorlds.M(32));
        var end = new Fixed2(TestWorlds.M(55), TestWorlds.M(32)); // 15 m creep

        var impacts = RunBarrage(world, start, end, ticks: 260);
        Assert.True(impacts.Count >= SimConfig.ShellsPerBarrage - 2);

        Fixed minX = impacts.Min(p => p.X);
        Fixed maxX = impacts.Max(p => p.X);
        Fixed spread = maxX - minX;
        Assert.True(spread > TestWorlds.M(9), $"creep spread {spread} too small for a 15 m line");
        Assert.True(Fixed.Abs(minX - start.X) < TestWorlds.M(6), "walk must start near the ordered start");
    }

    [Fact]
    public void Blast_ConvertsBuildingToRubble()
    {
        var blockerPos = new Fixed2(TestWorlds.M(48), TestWorlds.M(32));
        var (world, _, _) = SpawnDuelPair(
            BareMap(new Obstacle(blockerPos, TestWorlds.M(4))), seed: 7);

        RunBarrage(world,
            new Fixed2(TestWorlds.M(48), TestWorlds.M(32)),
            new Fixed2(TestWorlds.M(48), TestWorlds.M(32)), ticks: 260);

        Assert.Empty(world.Blockers);
        Assert.Contains(world.DynamicCover, c => c.Kind == CoverKind.Rubble);
    }

    [Fact]
    public void FreshCraters_GiveRealCover()
    {
        // Per seed: shell the VP area with nobody home, then run an identical MG
        // duel with the target held on the crater field vs untouched open ground.
        const int ticksShooting = 500;

        int openTotal = 0, coveredTotal = 0;
        for (int seed = 1; seed <= 6; seed++)
        {
            // Phase A on the real duel world: crater (48,32) using an off-map spotter.
            var cratered = TestWorlds.Create((ulong)(seed + 100));
            int spotterA = cratered.Spawn(Side.Allies, UnitTypes.RifleSquad.Id,
                new Fixed2(TestWorlds.M(4), TestWorlds.M(60)));
            cratered.Step(new[] { new Command(spotterA, CommandType.Barrage,
                new Fixed2(TestWorlds.M(48), TestWorlds.M(32)), new Fixed2(TestWorlds.M(48), TestWorlds.M(32))) });
            for (int t = 0; t < 200; t++)
                cratered.Step();
            Assert.True(cratered.DynamicCover.Count >= 5, $"phase A must crater (seed {seed})");

            var open = TestWorlds.Create((ulong)seed);
            int shooterO = open.Spawn(Side.Allies, UnitTypes.MachineGunSection.Id, new Fixed2(TestWorlds.M(20), TestWorlds.M(32)));
            int targetO = open.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));

            int shooterS = cratered.Spawn(Side.Allies, UnitTypes.MachineGunSection.Id, new Fixed2(TestWorlds.M(20), TestWorlds.M(32)));
            int targetS = cratered.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));

            for (int t = 0; t < ticksShooting; t++)
            {
                var po = open.Units[targetO];
                po.Suppression = SimConfig.MaxSuppression; // hold pinned: no return fire
                open.Units[targetO] = po;

                var ps = cratered.Units[targetS];
                ps.Suppression = SimConfig.MaxSuppression;
                cratered.Units[targetS] = ps;

                bool oa = po.Alive;
                open.Step();
                if (oa) openTotal += open.Events.Count(e => e.Hit && e.TargetId == targetO);

                bool ca = ps.Alive;
                cratered.Step();
                if (ca) coveredTotal += cratered.Events.Count(e => e.Hit && e.TargetId == targetS);

                if (!oa && !ca) break;
            }
        }

        Assert.True(openTotal > 12, $"sanity: reference duels must land hits ({openTotal})");
        Assert.True(coveredTotal < openTotal * 8 / 10,
            $"craters must cut hits: covered {coveredTotal} vs open {openTotal}");
    }

    [Fact]
    public void DynamicCrater_IsFoundByCoverLookup()
    {
        // Direct tombstone: dynamic cover participates in cover lookups exactly
        // like static map cover. No statistics, no dice.
        var world = TestWorlds.Create(3);
        var spot = new Fixed2(TestWorlds.M(48), TestWorlds.M(32));

        Assert.False(CombatSystem.TryGetBestCover(world, spot, out _),
            "bare ground must read as no cover");

        world.DynamicCover.Add(new CoverObject(spot, SimConfig.CraterRadius, CoverKind.Crater));

        Assert.True(CombatSystem.TryGetBestCover(world, spot, out var kind),
            "a fresh crater underfoot must read as cover");
        Assert.Equal(CoverKind.Crater, kind);
    }

    [Fact]
    public void Digging_CreatesTrench_AndEngineersOutdigRifles()
    {
        var rifles = TestWorlds.Create(11);
        int r = rifles.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(30), TestWorlds.M(32)));
        var eng = TestWorlds.Create(11);
        int e = eng.Spawn(Side.Allies, UnitTypes.Engineers.Id, new Fixed2(TestWorlds.M(30), TestWorlds.M(32)));

        bool rifleTrenched = false, engTrenched = false;
        for (int t = 0; t < 500; t++)
        {
            rifles.Step(new[] { new Command(r, CommandType.Dig, default) });
            eng.Step(new[] { new Command(e, CommandType.Dig, default) });

            if (!engTrenched && eng.DynamicCover.Any(c => c.Kind == CoverKind.Trench))
            {
                engTrenched = true;
                Assert.False(rifleTrenched, "engineers must out-dig riflemen");
            }
            if (!rifleTrenched && rifles.DynamicCover.Any(c => c.Kind == CoverKind.Trench))
                rifleTrenched = true;
        }

        Assert.True(rifleTrenched, "rifles must eventually finish a trench");
        Assert.True(engTrenched, "engineers must finish a trench");
    }

    [Fact]
    public void BarrageCooldown_IsAuthoritative()
    {
        var (world, caller, _) = SpawnDuelPair(BareMap(), seed: 9);
        var there = new Fixed2(TestWorlds.M(48), TestWorlds.M(32));
        var elsewhere = new Fixed2(TestWorlds.M(60), TestWorlds.M(40));

        world.Step(new[]
        {
            new Command(caller, CommandType.Barrage, there, there),
            new Command(caller, CommandType.Barrage, elsewhere, elsewhere),
        });

        Assert.Single(world.Barrages); // duplicate same-tick call refused
        int cooldownUntil = world.Match.NextBarrageTick(Side.Allies);
        Assert.True(cooldownUntil > world.Tick, "firing must start the cooldown");

        // Spam through the whole flight window: no second strike may appear,
        // even after the first one finishes walking and is removed.
        for (int t = 0; t < 150; t++)
            world.Step(new[] { new Command(caller, CommandType.Barrage, elsewhere, elsewhere) });
        Assert.True(world.Match.NextBarrageTick(Side.Allies) == cooldownUntil,
            "cooldown must not reset");
        Assert.DoesNotContain(world.Barrages, b => b.Cursor.DistanceTo(elsewhere) < Fixed.FromInt(2));

        // After the full cooldown a new call is accepted.
        for (int t = 0; t < SimConfig.BarrageCooldownTicks; t++)
            world.Step();

        world.Step(new[] { new Command(caller, CommandType.Barrage, elsewhere, elsewhere) });
        Assert.Single(world.Barrages);
    }

    [Fact]
    public void FriendlyFire_IsReal()
    {
        // Shell your own men, bury your own men: the blast does not check uniforms.
        var world = TestWorlds.Create(13);
        int friend = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));

        world.Step(new[] { new Command(friend, CommandType.Barrage,
            new Fixed2(TestWorlds.M(48), TestWorlds.M(32)), new Fixed2(TestWorlds.M(48), TestWorlds.M(32))) });

        for (int t = 0; t < 200 && world.Units[friend].Alive; t++)
            world.Step();

        Assert.True(!world.Units[friend].Alive || world.Units[friend].Hp < world.Units[friend].Type.MaxHp,
            "standing in your own barrage must hurt");
    }
}
