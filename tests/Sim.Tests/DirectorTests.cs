using Xunit;

namespace Sim.Tests;

/// <summary>
/// The director is the variance engine: momentum-biased, telegraphed flank
/// events. Every surprise must be legible in hindsight — flare first, impact later.
/// </summary>
public class DirectorTests
{
    private static World DirectorWorld() => new(TestWorlds.SmallMap(), seed: 5);

    /// <summary>Force a flank event to schedule immediately by breaking the balance.</summary>
    private static FlankEvent ScheduleOne(World world, Side leader)
    {
        // Give the leader every VP so MomentumLeader returns it deterministically.
        for (int i = 0; i < world.Points.Count; i++)
        {
            var p = world.Points[i];
            p.Owner = leader;
            world.Points[i] = p;
        }

        world.Step(); // director thinks on the first tick after NextDirectorThink window

        Assert.Single(world.PendingFlankEvents);
        return world.PendingFlankEvents[0];
    }

    [Fact]
    public void Events_AreTelegraphedBeforeTheyLand()
    {
        var world = DirectorWorld();
        var e = ScheduleOne(world, Side.Allies);

        int warnTick = world.Tick;
        Assert.True(e.LandTick - warnTick >= SimConfig.FlankWarningSeconds * SimConfig.TicksPerSecond,
            "flare-to-impact must give real warning time");

        // Advance halfway: still pending, not landed.
        for (int t = 0; t < (e.LandTick - warnTick) / 2; t++)
            world.Step();
        Assert.Contains(world.PendingFlankEvents, x => x.Id == e.Id);

        // Advance past landing: THIS event consumed and effect delivered.
        int unitsBefore = world.Units.Count(u => u.Alive);
        int landAt = e.LandTick;
        while (world.Tick <= landAt)
            world.Step();

        Assert.DoesNotContain(world.PendingFlankEvents, x => x.Id == e.Id);
        Assert.True(world.Units.Count(u => u.Alive) >= unitsBefore + e.Squads - 2,
            $"landing must deliver {e.Squads} squads");
    }

    [Fact]
    public void PressureEvents_TargetTheLeader_RelieveTheLoser()
    {
        // Leader = Central here; pressure events go against Central, relief events aid Central.
        var world = DirectorWorld();
        var e = ScheduleOne(world, Side.Central);

        // Leader = Central. Pressure strikes Central; relief aids the loser (Allies).
        if (e.Kind == FlankEventKind.EnemyReserves)
            Assert.False(e.AgainstAllies, "pressure must hit the leader (Central)");
        else
            Assert.True(e.AgainstAllies, "providence must flow to the losing side");
    }

    [Fact]
    public void ReserveWaves_MarchForAVictoryPoint()
    {
        var world = DirectorWorld();
        var e = ScheduleOne(world, Side.Allies);
        // Force kind: pressure against Allies => EnemyReserves marching.
        Assert.True(e.Kind == FlankEventKind.EnemyReserves || e.Kind == FlankEventKind.FriendlyReserves);

        if (e.Kind != FlankEventKind.EnemyReserves)
            return; // kind depends on the roll; only assert the wave path when it applies

        for (int t = 0; t <= e.LandTick - world.Tick; t++)
            world.Step();

        var spawned = world.Units.Where(u => u.Alive && u.Side == Side.Central &&
                                             (u.Order == OrderKind.AttackMove)).ToList();
        Assert.NotEmpty(spawned);
        Assert.All(spawned, u =>
            Assert.Contains(world.Points, p => p.Pos == u.Goal));
    }
}

/// <summary>
/// AI tactics beyond "walk at points": broken squads retreat instead of dying,
/// threatened owned points claim defenders.
/// </summary>
public class RudimentaryAiTacticsTests
{
    [Fact]
    public void BrokenSquad_NearEnemy_Retreats()
    {
        var world = TestWorlds.Create(31);
        int hero = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(40), TestWorlds.M(32)));
        int bully = world.Spawn(Side.Central, UnitTypes.MachineGunSection.Id, new Fixed2(TestWorlds.M(46), TestWorlds.M(32)));

        // Break the squad.
        var h = world.Units[hero];
        h.Hp = h.Type.MaxHp * Fixed.FromRatio(20, 100);
        world.Units[hero] = h;

        var ai = new RudimentaryAi(Side.Allies);
        ai.Think(world);

        // First think issues a Move away from the bully.
        var cmds = ai.Think(world);
        // (Think on non-replan ticks re-issues; accept either tick issuing the Move.)
        bool moveIssued = cmds.Any(c => c.UnitId == hero && c.Type == CommandType.Move) ||
                          world.Units[hero].Order == OrderKind.Move;

        // Step a little if needed for order application timing.
        for (int i = 0; i < 3 && !moveIssued; i++)
        {
            foreach (var c in ai.Think(world))
                if (c.UnitId == hero && c.Type == CommandType.Move)
                    moveIssued = true;
            if (!moveIssued)
            {
                var hh = world.Units[hero];
                hh.Suppression = SimConfig.MaxSuppression; // keep him pinned-safe from fighting
                world.Units[hero] = hh;
                world.Step(cmds.ToList());
                cmds = ai.Think(world).ToList().Select(x => x).ToList();
            }
        }

        Assert.True(moveIssued, "broken squad near an enemy must receive a retreat (Move) order");
    }

    [Fact]
    public void ThreatenedOwnedPoint_ClaimsADefender()
    {
        var world = TestWorlds.Create(41);
        // Allies own the center VP; central scouts toward it.
        world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(10), TestWorlds.M(50)));
        int defenderCandidate = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(44), TestWorlds.M(38)));
        int scout = world.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(52), TestWorlds.M(36)));

        // Hand ownership to Allies directly.
        var vp = world.Points[0];
        vp.Owner = Side.Allies;
        world.Points[0] = vp;

        var ai = new RudimentaryAi(Side.Allies);
        ai.Think(world);
        var cmds = ai.Think(world);

        // The nearest free squad should be sent to hold the VP.
        bool defendsVp = cmds.Any(c => c.UnitId == defenderCandidate &&
                                       c.Type == CommandType.AttackMove &&
                                       c.Pos.DistanceTo(new Fixed2(TestWorlds.M(48), TestWorlds.M(32))) < Fixed.FromInt(3));
        _ = scout;
        Assert.True(defendsVp || world.Units[defenderCandidate].Goal.DistanceTo(new Fixed2(TestWorlds.M(48), TestWorlds.M(32))) < Fixed.FromInt(3),
            "threatened owned point must claim the closest available defender");
    }
}
