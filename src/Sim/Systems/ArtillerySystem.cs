namespace Sim;

using System.Diagnostics;

/// <summary>
/// Resolves scheduled shell impacts: damage and suppression with distance falloff,
/// a crater punched into the ground at every impact point, and buildings blasted
/// into rubble cover. Friendly fire is real — mistimed creeping barrages bury
/// your own advance.
/// </summary>
public static class ArtillerySystem
{
    public static void Step(World world)
    {
        var barrages = world.Barrages;
        for (int i = barrages.Count - 1; i >= 0; i--)
        {
            var b = barrages[i];
            bool removed = false;

            while (b.Remaining > 0 && world.Tick >= b.NextTick)
            {
                Fixed2 impact = b.Cursor + ScatterOffset(ref world.Rng);
                Detonate(world, impact);
                world.Explosions.Add(impact);

                AddCrater(world, impact);
                ConvertBlockersToRubble(world, impact);

                b.Remaining--;
                b.Cursor += b.Step;
                b.NextTick += SimConfig.BarrageImpactIntervalTicks;

                if (b.Remaining == 0)
                {
                    barrages.RemoveAt(i);
                    removed = true;
                    break;
                }
            }

            if (!removed)
                barrages[i] = b;
        }
    }

    private static void Detonate(World world, Fixed2 impact)
    {
        long radiusSqRaw = SimConfig.BlastRadius.Raw;
        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive)
                continue;

            Fixed dist = u.Pos.DistanceTo(impact);
            if (dist.Raw >= radiusSqRaw)
                continue;

            // Linear falloff from ground zero.
            Fixed falloff = Fixed.One - (dist / SimConfig.BlastRadius);
            u.Hp -= ShellDamageFor(u) * falloff;
            u.Suppression = Fixed.Clamp(
                u.Suppression + SimConfig.ShellSuppression * falloff,
                Fixed.Zero, SimConfig.MaxSuppression);

            if (u.Hp <= Fixed.Zero)
                u.Alive = false;

            units[i] = u;
        }
    }

    private static Fixed ShellDamageFor(in Unit u) => SimConfig.ShellDamage;

    private static void AddCrater(World world, Fixed2 at)
    {
        if (world.DynamicCover.Count >= SimConfig.MaxDynamicCoverObjects)
            return;
        world.DynamicCover.Add(new CoverObject(at, SimConfig.CraterRadius, CoverKind.Crater));
    }

    private static void ConvertBlockersToRubble(World world, Fixed2 impact)
    {
        var blockers = world.Blockers;
        for (int i = blockers.Count - 1; i >= 0; i--)
        {
            var ob = blockers[i];
            if (impact.DistanceTo(ob.Pos) > ob.Radius * Fixed.FromRatio(12, 10))
                continue;

            blockers.RemoveAt(i);
            if (world.DynamicCover.Count < SimConfig.MaxDynamicCoverObjects)
                world.DynamicCover.Add(new CoverObject(
                    ob.Pos, ob.Radius * Fixed.FromRatio(11, 10), CoverKind.Rubble));
        }
    }

    /// <summary>
    /// Uniform-ish disc scatter via rejection sampling from the world RNG
    /// (bounded attempts keep it deterministic and cheap).
    /// </summary>
    private static Fixed2 ScatterOffset(ref Rng rng)
    {
        var r = SimConfig.ScatterRadius;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            long x = (long)rng.NextU32() - uint.MaxValue / 2;
            long y = (long)rng.NextU32() - uint.MaxValue / 2;
            // Scale into [-r, r] using raw-space math that cannot overflow.
            var vx = Fixed.FromRaw(x >> 1) * r / Fixed.FromRaw(uint.MaxValue >> 1);
            var vy = Fixed.FromRaw(y >> 1) * r / Fixed.FromRaw(uint.MaxValue >> 1);
            var v = new Fixed2(vx, vy);
            if (v.LengthSquared().Raw <= (r * r).Raw)
                return v;
        }
        return Fixed2.Zero; // statistically near-impossible fallback
    }
}
