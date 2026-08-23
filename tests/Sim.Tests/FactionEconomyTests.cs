using Xunit;

namespace Sim.Tests;

/// <summary>
/// The requisition economy: manpower drips with territory, buys squads that
/// march in from home. Faction kits gate what you can buy; faction modifiers
/// change how units behave (stormtroopers shrug off suppression, AEF earns fast).
/// </summary>
public class FactionEconomyTests
{
    private static Fixed M(int v) => TestWorlds.M(v);

    [Fact]
    public void Manpower_Accrues_AndScalesWithTerritory()
    {
        var rich = TestWorlds.Create(61);
        var poor = TestWorlds.Create(62);

        // Give the rich side a captured point.
        var p = rich.Points[1];
        p.Owner = Side.Allies;
        rich.Points[1] = p;

        int startRich = rich.Match.Manpower(Side.Allies);
        int startPoor = poor.Match.Manpower(Side.Allies);
        Assert.Equal(startRich, startPoor);

        for (int t = 0; t < 600; t++) // 20 s
        {
            rich.Step();
            poor.Step();
        }

        int gainedRich = rich.Match.Manpower(Side.Allies) - startRich;
        int gainedPoor = poor.Match.Manpower(Side.Allies) - startPoor;
        Assert.True(gainedRich > gainedPoor,
            $"holding territory must pay: rich {gainedRich} vs poor {gainedPoor}");
        // Base trickle only: starting funds + income * seconds, exact under fixed-point.
        Assert.Equal(80 + SimConfig.BaseIncomePerSecond.ToInt() * 20,
            poor.Match.Manpower(Side.Allies));
    }

    [Fact]
    public void Requisition_DeductsManpower_AndSquadMarchesIn()
    {
        var world = TestWorlds.Create(63);
        int caller = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(M(10), M(50)));
        world.Match.AddManpower(Side.Allies, 500);

        var before = world.Units.Count;
        world.Step(new[] { new Command(caller, CommandType.Requisition, default, default, UnitTypes.RifleSquad.Id) });

        Assert.Equal(before + 1, world.Units.Count);
        var bought = world.Units[world.Units.Count - 1];
        Assert.True(bought.Side == Side.Allies && bought.Alive);
        Assert.Equal(OrderKind.Move, bought.Order); // marching in from the edge
        Assert.True(bought.Pos.X.Raw < M(10).Raw, "spawned at the home edge");
        Assert.Equal(500 + world.Match.StartingTickets * 0 + 80 /* starting */ - UnitTypes.RifleSquad.ManpowerCost + 0,
            world.Match.Manpower(Side.Allies) - 0);
    }

    [Fact]
    public void Requisition_RejectsForeignType_AndPoverty()
    {
        var world = TestWorlds.Create(64); // default: Allies are BEF
        int caller = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(M(10), M(50)));

        // Stormtroopers are not in the BEF kit.
        world.Match.AddManpower(Side.Allies, 1000);
        var before = world.Units.Count;
        world.Step(new[] { new Command(caller, CommandType.Requisition, default, default, UnitTypes.Stormtroopers.Id) });
        Assert.Equal(before, world.Units.Count);

        // Poverty: drain funds, try again with a legal type.
        ref var m = ref world.Match;
        m.AddManpower(Side.Allies, -2000);
        world.Step(new[] { new Command(caller, CommandType.Requisition, default, default, UnitTypes.RifleSquad.Id) });
        Assert.Equal(before, world.Units.Count);
    }

    [Fact]
    public void Stormtroopers_TakeRoughlyHalfSuppression_FromBlasts()
    {
        var sturm = TestWorlds.Create(71);
        var rifle = TestWorlds.Create(72);

        int sId = sturm.Spawn(Side.Central, UnitTypes.Stormtroopers.Id, new Fixed2(M(48), M(32)));
        int rId = rifle.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(M(48), M(32)));
        int spotterS = sturm.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(M(90), M(60)));
        int spotterR = rifle.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(M(90), M(60)));

        var strike = new[] { new Command(spotterS, CommandType.Barrage,
            new Fixed2(M(48), M(32)), new Fixed2(M(48), M(32))) };
        sturm.Step(strike);
        rifle.Step(new[] { new Command(spotterR, CommandType.Barrage,
            new Fixed2(M(48), M(32)), new Fixed2(M(48), M(32))) });

        // Step until the first shell lands in each world, then read suppression
        // immediately - before decay can eat into it.
        while (sturm.Explosions.Count == 0) sturm.Step();
        float sturmSup = (float)sturm.Units[sId].Suppression.Raw / Fixed.OneRaw;
        while (rifle.Explosions.Count == 0) rifle.Step();
        float rifleSup = (float)rifle.Units[rId].Suppression.Raw / Fixed.OneRaw;

        Assert.True(rifleSup > 5, $"baseline must gain real suppression ({rifleSup})");
        double ratio = sturmSup / Math.Max(rifleSup, 0.001f);
        Assert.InRange(ratio, 0.40, 0.70); // ~0.55 modifier, small numeric slop
    }

    [Fact]
    public void AefPlatoons_EarnVetTwiceAsFast()
    {
        AssertRanksUpWith(UnitTypes.RifleSquad.Id, requiredKills: 4, seed: 81);
        AssertRanksUpWith(UnitTypes.AefRiflePlatoon.Id, requiredKills: 2, seed: 82);
    }

    private static void AssertRanksUpWith(int hunterType, int requiredKills, ulong seed)
    {
        var world = TestWorlds.Create(seed);
        int hunter = world.Spawn(Side.Allies, hunterType, new Fixed2(M(40), M(32)));

        var victims = new List<int>();
        for (int i = 0; i < requiredKills; i++)
        {
            int v = world.Spawn(Side.Central, UnitTypes.RifleSquad.Id,
                new Fixed2(M(44 + i * 3), M(32)));
            var vu = world.Units[v];
            vu.Hp = Fixed.FromInt(25); // one-shot
            vu.Suppression = SimConfig.MaxSuppression;
            world.Units[v] = vu;
            victims.Add(v);
        }

        for (int t = 0; t < 900 && world.Units[hunter].Rank < 1; t++)
        {
            foreach (var id in victims)
            {
                if (!world.Units[id].Alive) continue;
                var v = world.Units[id];
                v.Suppression = SimConfig.MaxSuppression;
                world.Units[id] = v;
            }
            world.Step();
        }

        Assert.True(world.Units[hunter].Rank >= 1,
            $"{UnitTypes.Get(hunterType).Name} should reach rank 1 within {requiredKills} kills");
    }

    [Fact]
    public void BarrageCooldown_IsFactionFlavored()
    {
        var bef = TestWorlds.Create(91); // Allies = BEF by default
        int callerB = bef.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(M(10), M(50)));
        bef.Step(new[] { new Command(callerB, CommandType.Barrage,
            new Fixed2(M(48), M(32)), new Fixed2(M(48), M(32))) });
        int befCd = bef.Match.NextBarrageTick(Side.Allies) - bef.Tick;

        Assert.True(befCd < SimConfig.BarrageCooldownTicks,
            "BEF barrage readiness must come sooner than the generic cooldown");
    }
}
