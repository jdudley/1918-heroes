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
                case CommandType.Requisition:
                    TryRequisition(world, ref u, cmd.Param);
                    break;
            }
            world.Units[cmd.UnitId] = u;
        }
    }

    /// <summary>
    /// Buy a squad: validates faction roster and funds, then marches it in from
    /// the side's home edge toward the middle of the map.
    /// </summary>
    private static void TryRequisition(World world, ref Unit issuer, int typeId)
    {
        Side side = issuer.Side;
        if (side is not (Side.Allies or Side.Central))
            return;

        var faction = world.FactionOf(side);
        int rosterIndex = Array.IndexOf(faction.Roster, typeId);
        if (rosterIndex < 0)
            return; // not in this army's kit

        var type = UnitTypes.Get(typeId);
        ref MatchState match = ref world.Match;
        if (match.Manpower(side) < type.ManpowerCost)
            return;

        match.AddManpower(side, -type.ManpowerCost);

        // March in from the home edge, staggered so squads don't stack.
        Fixed x = side == Side.Allies ? Fixed.FromInt(3) : world.Map.Width - Fixed.FromInt(3);
        int slot = (side == Side.Allies ? match.RequisitionsAllies : match.RequisitionsCentral) % 7;
        var pos = new Fixed2(x, world.Map.Height / Fixed.FromInt(2) + Fixed.FromInt(slot * 5 - 15));

        int id = world.Spawn(side, typeId, pos, rank: StartRankFor(faction, rosterIndex));
        var spawned = world.Units[id];
        spawned.Order = OrderKind.Move;
        spawned.Goal = new Fixed2(world.Map.Width / Fixed.FromInt(2), spawned.Pos.Y);
        world.Units[id] = spawned;

        if (side == Side.Allies) match.RequisitionsAllies++;
        else match.RequisitionsCentral++;
    }

    private static int StartRankFor(FactionDef faction, int rosterIndex) =>
        rosterIndex < faction.StartRanks.Length ? faction.StartRanks[rosterIndex] : 0;

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

        // Faction artillery character: BEF runs its superb creeping barrages sooner.
        int cooldown = SimConfig.BarrageCooldownTicks;
        var factionMult = world.FactionOf(side).BarrageCooldownMultiplier;
        cooldown = (int)((Int128)cooldown * factionMult.Raw >> Fixed.Shift);
        match.SetNextBarrageTick(side, world.Tick + cooldown);
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
