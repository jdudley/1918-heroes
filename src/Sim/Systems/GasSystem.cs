namespace Sim;

/// <summary>
/// Advances gas clouds: drift with the wind, choke everyone inside (both sides —
/// the wind does not check uniforms), and dissipate.
/// </summary>
public static class GasSystem
{
    public static void Step(World world)
    {
        var clouds = world.Clouds;
        var units = world.Units;

        for (int i = clouds.Count - 1; i >= 0; i--)
        {
            var cloud = clouds[i];

            cloud.Pos += cloud.Velocity * SimConfig.Dt;
            cloud.TicksRemaining--;

            Fixed rSq = cloud.Radius * cloud.Radius;
            Fixed damage = SimConfig.GasDamagePerSecond * SimConfig.Dt;
            Fixed suppression = SimConfig.GasSuppressionPerSecond * SimConfig.Dt;

            for (int u = 0; u < units.Count; u++)
            {
                var unit = units[u];
                if (!unit.Alive)
                    continue;
                if (unit.Pos.DistanceSquaredTo(cloud.Pos) > rSq)
                    continue;

                unit.Hp -= damage;
                unit.Suppression = Fixed.Clamp(unit.Suppression + suppression,
                    Fixed.Zero, SimConfig.MaxSuppression);

                if (unit.Hp <= Fixed.Zero)
                    unit.Alive = false;

                units[u] = unit;
            }

            if (cloud.TicksRemaining <= 0)
                clouds.RemoveAt(i);
            else
                clouds[i] = cloud;
        }
    }
}
