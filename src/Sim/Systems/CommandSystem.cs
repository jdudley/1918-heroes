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
                case CommandType.Dig:
                    // Re-issuing Dig must not wipe accumulated progress.
                    if (u.Order != OrderKind.Digging)
                        u.DigWork = Fixed.Zero;
                    u.Order = OrderKind.Digging;
                    u.Goal = u.Pos;
                    u.TargetId = -1;
                    break;
                case CommandType.Barrage:
                    TryCallBarrage(world, ref u, cmd);
                    break;
                case CommandType.Gas:
                    TryCallGas(world, ref u, cmd);
                    break;
            }
            world.Units[cmd.UnitId] = u;
        }
    }

    private static void TryCallBarrage(World world, ref Unit issuer, in Command cmd)
    {
        Side side = issuer.Side;
        if (side is not (Side.Allies or Side.Central))
            return;

        ref MatchState match = ref world.Match;
        if (world.Tick < match.NextBarrageTick(side))
            return; // guns are still repositioning

        var walk = cmd.Alt - cmd.Pos;
        var step = walk / Fixed.FromInt(SimConfig.ShellsPerBarrage - 1);

        world.Barrages.Add(new Barrage
        {
            Side = side,
            Cursor = cmd.Pos,
            Step = step,
            Remaining = SimConfig.ShellsPerBarrage,
            NextTick = world.Tick + 15, // flight time before first shell lands
        });

        match.SetNextBarrageTick(side, world.Tick + SimConfig.BarrageCooldownTicks);
    }

    private static void TryCallGas(World world, ref Unit issuer, in Command cmd)
    {
        Side side = issuer.Side;
        if (side is not (Side.Allies or Side.Central))
            return;

        ref MatchState match = ref world.Match;
        if (world.Tick < match.NextGasTick(side))
            return;

        // One shell, one lingering cloud. Drift comes from the match wind.
        world.Clouds.Add(new GasCloud
        {
            Id = ++world._nextGasId,
            Pos = cmd.Pos,
            Velocity = world.WindVelocity,
            Radius = SimConfig.GasCloudRadius,
            TicksRemaining = SimConfig.GasCloudLifetimeTicks,
        });

        match.SetNextGasTick(side, world.Tick + SimConfig.GasCooldownTicks);
    }
}
