using Xunit;

namespace Sim.Tests;

public class CaptureAndVictoryTests
{
    private const int VpIndex = 0; // center victory point at (48,32), radius 6

    [Fact]
    public void LoneSquad_CapturesNeutralPoint()
    {
        var world = TestWorlds.Create();
        world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));

        Assert.Equal(Side.Neutral, world.Points[VpIndex].Owner);

        for (int t = 0; t < 200; t++) // capture takes 6 s = 180 ticks
            world.Step();

        Assert.Equal(Side.Allies, world.Points[VpIndex].Owner);
        Assert.Equal(Fixed.Zero, world.Points[VpIndex].Progress);
    }

    [Fact]
    public void ContestedPoint_IsFrozen()
    {
        var world = TestWorlds.Create();
        int ally = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));
        int enemy = world.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(50), TestWorlds.M(34)));

        // Hold both squads hard-pinned so presence is stable and the freeze rule is isolated.
        for (int t = 0; t < 120; t++)
        {
            foreach (var id in new[] { ally, enemy })
            {
                var u = world.Units[id];
                u.Suppression = SimConfig.MaxSuppression;
                world.Units[id] = u;
            }
            world.Step();
        }

        Assert.Equal(Side.Neutral, world.Points[VpIndex].Owner);
        Assert.Equal(Fixed.Zero, world.Points[VpIndex].Progress);
    }

    [Fact]
    public void PinningTheOnlyDefender_LetsTheAttackerTakeGround()
    {
        // The point of suppression: a pinned defender stops counting, the attacker captures.
        var world = TestWorlds.Create();
        int defender = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));
        int attacker = world.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(50), TestWorlds.M(34)));

        for (int t = 0; t < 400; t++)
        {
            var d = world.Units[defender];
            d.Suppression = SimConfig.MaxSuppression;
            world.Units[defender] = d;
            world.Step();
        }

        Assert.Equal(Side.Central, world.Points[VpIndex].Owner);
    }

    [Fact]
    public void EnemyPresence_ReversesProgress_AndFlipsOwnership()
    {
        var world = TestWorlds.Create();
        int ally = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));

        // Ally pushes to roughly half.
        for (int t = 0; t < 90; t++)
            world.Step();
        Assert.Equal(Side.Neutral, world.Points[VpIndex].Owner); // not yet flipped

        // Ally marches away, enemy arrives. Generous budget: the ally may crawl while suppressed.
        var homeEdge = new Fixed2(TestWorlds.M(6), TestWorlds.M(58));
        world.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(50), TestWorlds.M(30)));
        for (int t = 0; t < 900; t++)
            world.Step(new[] { new Command(ally, CommandType.Move, homeEdge) });

        Assert.Equal(Side.Central, world.Points[VpIndex].Owner);
    }

    [Fact]
    public void UnpushedProgress_DecaysToZero()
    {
        var world = TestWorlds.Create();
        int ally = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));

        for (int t = 0; t < 90; t++) // partial progress
            world.Step();
        Fixed mid = world.Points[VpIndex].Progress;
        Assert.True(mid > Fixed.Zero);

        var away = new Fixed2(TestWorlds.M(6), TestWorlds.M(58));
        for (int t = 0; t < 900; t++) // exit radius, then ~10 s of decay clears any remainder
            world.Step(new[] { new Command(ally, CommandType.Move, away) });

        Assert.Equal(Fixed.Zero, world.Points[VpIndex].Progress);
    }

    [Fact]
    public void PinnedSquads_CannotCapture()
    {
        // Pinned world vs identical unpinned control: pinning must stall capture completely.
        var pinned = TestWorlds.Create(21);
        var control = TestWorlds.Create(21);

        int pinnedId = pinned.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));
        control.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));

        for (int t = 0; t < 150; t++)
        {
            var u = pinned.Units[pinnedId];
            u.Suppression = SimConfig.MaxSuppression;
            pinned.Units[pinnedId] = u;

            pinned.Step();
            control.Step();
        }

        Assert.Equal(Fixed.Zero, pinned.Points[VpIndex].Progress);
        Assert.Equal(Side.Neutral, pinned.Points[VpIndex].Owner);
        Assert.True(control.Points[VpIndex].Progress > pinned.Points[VpIndex].Progress,
            $"control captured {control.Points[VpIndex].Progress} but pinned world matched it");
    }

    [Fact]
    public void HoldingVp_DrainsEnemyTickets_ExactWholeTickets()
    {
        var world = TestWorlds.Create(options: new MatchOptions { StartingTickets = 500 });
        world.Points[VpIndex] = new CapturePoint
        {
            Pos = world.Points[VpIndex].Pos,
            Radius = world.Points[VpIndex].Radius,
            IsVictoryPoint = true,
            Owner = Side.Allies,
            Progress = Fixed.Zero,
        };

        for (int t = 0; t < 600; t++) // exactly 20 seconds
            world.Step();

        Assert.Equal(480, world.Match.TicketsCentral); // drained 1/s for 20 s
        Assert.Equal(500, world.Match.TicketsAllies);
    }

    [Fact]
    public void TicketDepletion_EndsMatchWithCorrectWinner()
    {
        var world = TestWorlds.Create(options: new MatchOptions { StartingTickets = 10 });
        world.Points[VpIndex] = new CapturePoint
        {
            Pos = world.Points[VpIndex].Pos,
            Radius = world.Points[VpIndex].Radius,
            IsVictoryPoint = true,
            Owner = Side.Allies,
            Progress = Fixed.Zero,
        };

        int guard = 0;
        while (!world.Match.Finished && guard++ < 1000)
            world.Step();

        Assert.True(world.Match.Finished);
        Assert.Equal(Side.Allies, world.Match.Winner);
        Assert.True(world.Match.TicketsCentral <= 0);
    }
}
