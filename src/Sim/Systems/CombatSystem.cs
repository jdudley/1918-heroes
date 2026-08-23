namespace Sim;

public static class CombatSystem
{
    public static void Step(World world)
    {
        DecrementCooldowns(world);

        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.FireCooldownTicks > 0)
                continue;

            // Hard-pinned squads are combat-ineffective until they recover.
            if (SimConfig.IsPinned(u.Suppression))
                continue;

            // Plain Move ignores enemies entirely: no acquiring, no firing.
            if (u.Order == OrderKind.Move)
                continue;

            // Stagger target acquisition across ticks so cost stays flat and order is stable.
            bool retargetDue = (world.Tick + u.Id) % SimConfig.RetargetIntervalTicks == 0;

            int targetId = u.TargetId;
            if (!IsValidTarget(world, targetId) || retargetDue)
                targetId = AcquireTarget(world, i);

            if (targetId < 0)
            {
                if (u.TargetId != targetId) { u.TargetId = -1; units[i] = u; }
                continue;
            }

            var target = world.Units[targetId];
            Fixed distSq = u.Pos.DistanceSquaredTo(target.Pos);
            Weapon weapon = u.Type.Weapon;

            bool inRange = distSq <= weapon.Range * weapon.Range;
            bool losClear = LineOfSight.Clear(world.Map, u.Pos, target.Pos);

            if (!inRange || !losClear)
            {
                if (u.TargetId != targetId) { u.TargetId = targetId; units[i] = u; }
                continue;
            }

            ResolveShot(world, ref u, targetId);
            units[i] = u;
        }
    }

    private static void DecrementCooldowns(World world)
    {
        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.FireCooldownTicks == 0) continue;
            u.FireCooldownTicks--;
            units[i] = u;
        }
    }

    /// <summary>Nearest visible enemy. Ties break toward the lower id for determinism.</summary>
    private static int AcquireTarget(World world, int shooterId)
    {
        var shooter = world.Units[shooterId];
        Side enemySide = shooter.Side.EnemyOf();
        if (enemySide == Side.Neutral) return -1;

        int best = -1;
        long bestDistSq = long.MaxValue;

        var sightSqRaw = shooter.Type.Sight * shooter.Type.Sight;
        var units = world.Units;
        for (int j = 0; j < units.Count; j++)
        {
            var t = units[j];
            if (!t.Alive || t.Side != enemySide) continue;

            long dSq = shooter.Pos.DistanceSquaredTo(t.Pos).Raw;
            if (dSq >= bestDistSq || dSq > sightSqRaw.Raw) continue;
            if (!LineOfSight.Clear(world.Map, shooter.Pos, t.Pos)) continue;

            best = j;
            bestDistSq = dSq;
        }
        return best;
    }

    private static void ResolveShot(World world, ref Unit shooter, int targetId)
    {
        var units = world.Units;
        var target = units[targetId];
        Weapon weapon = shooter.Type.Weapon;

        Fixed accuracy = weapon.Accuracy
            * SimConfig.AccuracyMultiplier(shooter.Suppression)
            * (Fixed.One + SimConfig.AccuracyBonusPerRank(shooter.Rank));

        // Cover of the target: the best (smallest) multiplier among overlapping cover objects applies.
        bool inCover = TryGetBestCover(world.Map, target.Pos, out CoverKind coverKind);
        if (inCover)
            accuracy *= SimConfig.CoverHitMultiplier(coverKind);

        bool hit = world.Rng.Chance(accuracy);

        if (hit)
            target.Hp -= weapon.Damage;

        Fixed supGain = hit ? weapon.SuppressionPerHit : weapon.SuppressionPerNearMiss;
        if (inCover)
            supGain *= SimConfig.CoverSuppressionMultiplier(coverKind);
        supGain *= Fixed.One - SimConfig.SuppressionResistPerRank(target.Rank);

        target.Suppression = Fixed.Clamp(target.Suppression + supGain, Fixed.Zero, SimConfig.MaxSuppression);

        if (target.Hp <= Fixed.Zero)
        {
            target.Alive = false;
            shooter.Kills++;
            PromoteIfEarned(ref shooter);
        }

        shooter.FireCooldownTicks = weapon.CooldownTicks;
        shooter.TargetId = targetId;

        world.Events.Add(new ShotEvent(shooter.Id, targetId, hit));

        // Persist target damage / suppression / death.
        units[targetId] = target;
    }

    private static void PromoteIfEarned(ref Unit u)
    {
        while (u.Rank < SimConfig.RankKillThresholds.Length &&
               u.Kills >= SimConfig.RankKillThresholds[u.Rank])
        {
            u.Rank++;
        }
    }

    public static bool TryGetBestCover(MapDef map, Fixed2 pos, out CoverKind kind)
    {
        kind = default;
        bool found = false;
        long bestHitMultRaw = long.MaxValue;

        var cover = map.Cover;
        for (int i = 0; i < cover.Count; i++)
        {
            var c = cover[i];
            // Fixed multiplication, never raw squaring: raw squares overflow long beyond ~1.4 m radii.
            if (pos.DistanceSquaredTo(c.Pos) > c.Radius * c.Radius)
                continue;

            long multRaw = SimConfig.CoverHitMultiplier(c.Kind).Raw;
            if (multRaw < bestHitMultRaw)
            {
                bestHitMultRaw = multRaw;
                kind = c.Kind;
                found = true;
            }
        }
        return found;
    }

    private static bool IsValidTarget(World world, int targetId) =>
        targetId >= 0 && targetId < world.Units.Count && world.Units[targetId].Alive;
}
