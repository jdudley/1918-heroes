namespace Sim;

public static class MovementSystem
{
    public static void Step(World world)
    {
        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.Order == OrderKind.Idle)
                continue;

            // Attack-move chases a live target; plain Move ignores enemies entirely.
            Fixed2 dest = u.Order == OrderKind.AttackMove && IsValidTarget(world, u.TargetId)
                ? units[u.TargetId].Pos
                : u.Goal;

            var toDest = dest - u.Pos;
            Fixed distSq = toDest.LengthSquared();

            Fixed speed = u.Type.Speed * SimConfig.SpeedMultiplier(u.Suppression);
            Fixed step = speed * SimConfig.Dt;

            var dir = toDest.Normalized();

            if (distSq <= step * step)
            {
                // Within one step of the destination: snap.
                u.Pos = dest;
                if (u.Order == OrderKind.Move)
                {
                    u.Order = OrderKind.Idle;
                    u.Goal = u.Pos;
                }
                else if (dir == Fixed2.Zero)
                {
                    dir = u.Facing;
                }
            }
            else
            {
                u.Pos += dir * step;
            }

            if (dir != Fixed2.Zero)
                u.Facing = dir;

            units[i] = u;
        }
    }

    private static bool IsValidTarget(World world, int targetId) =>
        targetId >= 0 && targetId < world.Units.Count && world.Units[targetId].Alive;
}
