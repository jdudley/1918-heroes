using Xunit;

namespace Sim.Tests;

/// <summary>
/// Gas: lingering, wind-drifting area denial. The cloud chokes everyone inside
/// regardless of side, dissipates on schedule, and the guns respect their cooldown.
/// </summary>
public class GasTests
{
    private static (World world, int caller, int victim) Setup(ulong seed = 21)
    {
        var world = new World(TestWorlds.SmallMap(), seed);
        int caller = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(8), TestWorlds.M(56)));
        int victim = world.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(TestWorlds.M(48), TestWorlds.M(32)));
        return (world, caller, victim);
    }

    [Fact]
    public void GasCloud_DamagesAndSuppressesInside_NotOutside()
    {
        var (inside, _, vIn) = Setup();
        var (outside, _, vOut) = Setup();

        var target = new Fixed2(TestWorlds.M(48), TestWorlds.M(32));
        inside.Step(new[] { new Command(0, CommandType.Gas, target, default) });
        outside.Step(); // keep tick parity; no gas called

        Assert.Single(inside.Clouds);

        for (int t = 0; t < 300; t++)
        {
            inside.Step();
            outside.Step();
        }

        // The cloud drifts; chase it with assertions on "was in cloud at some point"
        // by comparing final states loosely: inside-world victim must be hurt.
        var a = inside.Units[vIn];
        var b = outside.Units[vOut];
        Assert.True(a.Hp < b.Hp, $"gassed hp {a.Hp} should trail clean hp {b.Hp}");
        Assert.True(b.Alive && b.Hp == b.Type.MaxHp, "clean-world squad must be untouched");
    }

    [Fact]
    public void GasCloud_DriftsWithWind()
    {
        var (world, caller, _) = Setup(seed: 33);
        var drop = new Fixed2(TestWorlds.M(48), TestWorlds.M(32));

        Assert.True(world.WindVelocity.LengthSquared() > Fixed.Zero, "wind must exist");
        world.Step(new[] { new Command(caller, CommandType.Gas, drop, default) });

        var initial = world.Clouds[0].Pos;
        Fixed travelled = Fixed.Zero;
        for (int t = 0; t < 150; t++)
        {
            world.Step();
            if (world.Clouds.Count > 0)
                travelled = world.Clouds[0].Pos.DistanceTo(initial);
        }

        Assert.True(travelled > TestWorlds.M(3),
            $"cloud must drift with the wind, moved {travelled}");
    }

    [Fact]
    public void GasCloud_Dissipates()
    {
        var (world, caller, _) = Setup(seed: 44);
        world.Step(new[] { new Command(caller, CommandType.Gas,
            new Fixed2(TestWorlds.M(48), TestWorlds.M(32)), default) });

        Assert.Single(world.Clouds);

        int guard = SimConfig.GasCloudLifetimeTicks + 120;
        while (world.Clouds.Count > 0 && guard-- > 0)
            world.Step();

        Assert.Empty(world.Clouds);
    }

    [Fact]
    public void GasCooldown_IsAuthoritative()
    {
        var (world, caller, _) = Setup(seed: 55);
        var there = new Fixed2(TestWorlds.M(48), TestWorlds.M(32));

        world.Step(new[] { new Command(caller, CommandType.Gas, there, default) });
        int cooldownUntil = world.Match.NextGasTick(Side.Allies);
        Assert.True(cooldownUntil > world.Tick);

        // Spam: only one cloud may ever appear during the cooldown.
        for (int t = 0; t < 400; t++)
            world.Step(new[] { new Command(caller, CommandType.Gas, there, default) });
        Assert.Single(world.Clouds);
        Assert.Equal(cooldownUntil, world.Match.NextGasTick(Side.Allies));

        for (int t = 0; t < SimConfig.GasCooldownTicks + SimConfig.GasCloudLifetimeTicks; t++)
            world.Step();

        world.Step(new[] { new Command(caller, CommandType.Gas, there, default) });
        Assert.NotEmpty(world.Clouds);
    }
}
