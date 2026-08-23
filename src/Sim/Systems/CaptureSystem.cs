namespace Sim;

public static class CaptureSystem
{
    /// <summary>
    /// Classic territory capture: a single side present inside the radius pushes progress;
    /// contested points freeze; unpushed progress bleeds back toward zero.
    /// Pinned squads cannot capture ground — that is what suppression is for.
    /// </summary>
    public static void Step(World world)
    {
        Fixed ratePerSquadPerTick =
            Fixed.FromRatio(100, SimConfig.CaptureSecondsPerPointPerSquad) * SimConfig.Dt;

        var points = world.Points;
        for (int p = 0; p < points.Count; p++)
        {
            var point = points[p];

            int allies = CountPresent(world, point, Side.Allies);
            int central = CountPresent(world, point, Side.Central);

            if (allies > 0 && central == 0)
                point = Push(world, point, Side.Allies, allies, ratePerSquadPerTick);
            else if (central > 0 && allies == 0)
                point = Push(world, point, Side.Central, central, ratePerSquadPerTick);
            else if (allies == 0 && central == 0 && point.Progress != Fixed.Zero)
            {
                // Nobody nearby: progress decays back to zero (never flips ownership by itself).
                point.Progress -= Fixed.FromRatio(100, 20) * SimConfig.Dt;
                if (point.Progress.Raw < 0) point.Progress = Fixed.Zero;
            }
            // Both present: contested, frozen.

            points[p] = point;
        }
    }

    private static CapturePoint Push(World world, CapturePoint point, Side pusher, int squads, Fixed rate)
    {
        Fixed delta = rate * Fixed.FromInt(squads);        if (point.Owner == pusher)
        {
            // Friendly presence reclaims enemy progress and holds the line at zero.
            point.Progress -= delta;
            if (point.Progress.Raw < 0) point.Progress = Fixed.Zero;
            return point;
        }

        point.Progress += delta;
        if (point.Progress >= Fixed.FromInt(100))
        {
            point.Owner = pusher;
            point.Progress = Fixed.Zero;
        }
        return point;
    }

    private static int CountPresent(World world, in CapturePoint point, Side side)
    {
        int count = 0;
        var radiusSq = point.Radius * point.Radius; // Fixed math: raw squaring would overflow
        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.Side != side || u.Suppression >= SimConfig.PinnedThreshold)
                continue;
            if (u.Pos.DistanceSquaredTo(point.Pos) <= radiusSq)
                count++;
        }
        return count;
    }
}
