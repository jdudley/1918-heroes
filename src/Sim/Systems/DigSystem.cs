namespace Sim;

/// <summary>
/// Progresses digging squads. A squad stands fast (it may still fire), accumulates
/// work scaled by its unit type, and on completion stamps a trench cover object
/// into the world at its position.
/// </summary>
public static class DigSystem
{
    public static void Step(World world)
    {
        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.Order != OrderKind.Digging)
                continue;
            if (SimConfig.IsPinned(u.Suppression))
                continue;

            u.DigWork += u.Type.DigSpeedMultiplier * SimConfig.Dt;

            if (u.DigWork >= SimConfig.DigSeconds)
            {
                u.DigWork = Fixed.Zero;
                u.Order = OrderKind.Idle;

                if (world.DynamicCover.Count < SimConfig.MaxDynamicCoverObjects)
                    world.DynamicCover.Add(new CoverObject(u.Pos, SimConfig.TrenchRadius, CoverKind.Trench));
            }

            units[i] = u;
        }
    }
}
