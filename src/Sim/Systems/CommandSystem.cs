namespace Sim;

public static class CommandSystem
{
    /// <summary>Apply player inputs at tick start. Inputs are trusted in slice 1.</summary>
    public static void Apply(World world, IReadOnlyList<Command> commands)
    {
        for (int i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            if (cmd.UnitId < 0 || cmd.UnitId >= world.Units.Count) continue;
            var u = world.Units[cmd.UnitId];
            if (!u.Alive) continue;

            switch (cmd.Type)
            {
                case CommandType.Stop:
                    u.Order = OrderKind.Idle;
                    u.Goal = u.Pos;
                    u.TargetId = -1;
                    break;
                case CommandType.Move:
                    u.Order = OrderKind.Move;
                    u.Goal = cmd.Pos;
                    u.TargetId = -1;
                    break;
                case CommandType.AttackMove:
                    u.Order = OrderKind.AttackMove;
                    u.Goal = cmd.Pos;
                    u.TargetId = -1;
                    break;
            }
            world.Units[cmd.UnitId] = u;
        }
    }
}
