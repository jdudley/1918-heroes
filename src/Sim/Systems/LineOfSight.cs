namespace Sim;

/// <summary>
/// True line of sight: a shot or view is blocked iff its segment passes through
/// any sight-blocking obstacle circle. Pure integer math.
/// </summary>
public static class LineOfSight
{
    public static bool Clear(MapDef map, Fixed2 from, Fixed2 to)
    {
        var blockers = map.SightBlockers;
        for (int i = 0; i < blockers.Count; i++)
            if (SegmentEntersCircle(from, to, blockers[i].Pos, blockers[i].Radius))
                return false;
        return true;
    }

    public static bool SegmentEntersCircle(Fixed2 a, Fixed2 b, Fixed2 center, Fixed radius)
    {
        var d = b - a;
        var lenSq = d.LengthSquared();
        var ac = center - a;

        Fixed t;
        if (lenSq.Raw == 0)
        {
            t = Fixed.Zero;
        }
        else
        {
            // Projection of center onto segment, clamped to [0,1]. Proper fixed-point division.
            t = Fixed.Clamp(ac.Dot(d) / lenSq, Fixed.Zero, Fixed.One);
        }

        var closest = a + d * t;
        return closest.DistanceSquaredTo(center) < radius * radius;
    }
}
