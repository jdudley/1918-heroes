namespace Sim;

/// <summary>
/// The skirmish AI: every few seconds each squad is handed an objective —
/// capture the nearest unowned/enemy point (victory points preferred, squads
/// spread across objectives), defend owned points that enemies approach,
/// retreat when broken rather than die in place, or hunt once everything
/// flies our colours. The sim's own attack-move engagement does the fighting;
/// the AI only picks destinations. Fully deterministic.
/// </summary>
public sealed class RudimentaryAi
{
    private const int CrowdPenaltyMeters = 20;
    private const int VictoryPointBiasMeters = 30;

    private readonly Side _side;
    private readonly int _replanIntervalTicks;
    private readonly Dictionary<int, Fixed2> _goals = new();
    private readonly HashSet<int> _retreating = new();
    private int _lastBarrageTick = -1_000_000;

    public RudimentaryAi(Side side, int replanIntervalTicks = 90)
    {
        _side = side;
        _replanIntervalTicks = replanIntervalTicks;
    }

    /// <summary>Call once per tick. Returns commands to apply this tick (often none).</summary>
    public IReadOnlyList<Command> Think(World world)
    {
        var commands = new List<Command>();
        if (world.Match.Finished || _side == Side.Neutral)
            return commands;

        if (world.Tick % _replanIntervalTicks == 0)
        {
            Replan(world);
            TryBarrage(world, commands);
        }

        // Re-issue goals for living units whose squads went idle off-target
        // (arrived somewhere that stopped mattering) so they never stand around.
        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.Side != _side)
                continue;

            if (!_goals.TryGetValue(u.Id, out var goal))
                continue;

            if (_retreating.Contains(u.Id))
            {
                // Broken squads fall back ignoring enemies entirely.
                if (u.Order != OrderKind.Move && u.Pos != goal)
                    commands.Add(new Command(u.Id, CommandType.Move, goal));
                else if (u.Order == OrderKind.Move && u.Goal != goal && u.Pos != goal)
                    commands.Add(new Command(u.Id, CommandType.Move, goal));
                continue;
            }

            bool needsOrder = u.Order == OrderKind.Idle || u.Goal != goal;
            if (needsOrder && u.Pos != goal)
                commands.Add(new Command(u.Id, CommandType.AttackMove, goal));
        }
        return commands;
    }

    /// <summary>
    /// Fire on the densest enemy cluster when the guns are ready. The sim enforces
    /// the authoritative cooldown; this heuristic just avoids wasting the window.
    /// </summary>
    private void TryBarrage(World world, List<Command> commands)
    {
        if (world.Tick - _lastBarrageTick < SimConfig.BarrageCooldownTicks)
            return;

        var units = world.Units;
        int bestCenter = -1;
        int bestCount = 1;
        for (int i = 0; i < units.Count; i++)
        {
            var e = units[i];
            if (!e.Alive || e.Side == _side || e.Side == Side.Neutral)
                continue;

            int neighbors = 0;
            for (int j = 0; j < units.Count; j++)
            {
                var o = units[j];
                if (!o.Alive || o.Side != e.Side)
                    continue;
                if (e.Pos.DistanceSquaredTo(o.Pos) <= Fixed.FromInt(8) * Fixed.FromInt(8))
                    neighbors++;
            }

            if (neighbors > bestCount)
            {
                bestCount = neighbors;
                bestCenter = i;
            }
        }

        if (bestCenter < 0)
            return;

        int caller = -1;
        for (int i = 0; i < units.Count && caller < 0; i++)
            if (units[i].Alive && units[i].Side == _side)
                caller = i;
        if (caller < 0)
            return;

        commands.Add(new Command(caller, CommandType.Barrage,
            units[bestCenter].Pos, units[bestCenter].Pos));
        _lastBarrageTick = world.Tick;
    }

    private void Replan(World world)
    {
        PruneDead(world);
        _retreating.Clear();

        var myUnitIds = new List<int>();
        var available = new List<int>();
        var units = world.Units;

        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.Side != _side)
                continue;
            myUnitIds.Add(i);
            available.Add(i);
        }
        if (myUnitIds.Count == 0)
            return;

        // 1) Broken squads near enemies fall back to safety instead of dying in place.
        foreach (int id in myUnitIds)
        {
            var u = units[id];
            Fixed hpFrac = u.Hp / u.Type.MaxHp;
            int nearestEnemy = -1;
            long nearestSq = long.MaxValue;
            for (int j = 0; j < units.Count; j++)
            {
                var e = units[j];
                if (!e.Alive || e.Side == _side || e.Side == Side.Neutral)
                    continue;
                long dSq = u.Pos.DistanceSquaredTo(e.Pos).Raw;
                if (dSq < nearestSq) { nearestSq = dSq; nearestEnemy = j; }
            }

            bool hurt = hpFrac < SimConfig.RetreatHpFraction;
            bool enemyClose = nearestEnemy >= 0 &&
                nearestSq <= (SimConfig.RetreatEnemyProximity * SimConfig.RetreatEnemyProximity).Raw;

            if (hurt && enemyClose)
            {
                _retreating.Add(id);
                _goals[id] = SafestOwnedSpot(world, u.Pos);
            }
        }

        // 2) Defend owned points that enemies are approaching.
        var defenders = new HashSet<int>(_retreating);
        for (int p = 0; p < world.Points.Count; p++)
        {
            var point = world.Points[p];
            if (point.Owner != _side)
                continue;

            bool threatened = false;
            for (int j = 0; j < units.Count && !threatened; j++)
            {
                var e = units[j];
                if (!e.Alive || e.Side == _side || e.Side == Side.Neutral)
                    continue;
                threatened = e.Pos.DistanceSquaredTo(point.Pos) <=
                    SimConfig.DefendTriggerRadius * SimConfig.DefendTriggerRadius;
            }
            if (!threatened)
                continue;

            int bestDefender = -1;
            long bestDistSq = long.MaxValue;
            foreach (int id in available)
            {
                if (defenders.Contains(id))
                    continue;
                long dSq = units[id].Pos.DistanceSquaredTo(point.Pos).Raw;
                if (dSq < bestDistSq) { bestDistSq = dSq; bestDefender = id; }
            }

            if (bestDefender >= 0)
            {
                defenders.Add(bestDefender);
                _goals[bestDefender] = point.Pos;
            }
        }

        // 3) Everyone else captures or hunts.
        var rest = new List<int>();
        foreach (int id in myUnitIds)
            if (!defenders.Contains(id))
                rest.Add(id);

        bool allPointsOurs = true;
        for (int p = 0; p < world.Points.Count; p++)
            if (world.Points[p].Owner != _side)
                allPointsOurs = false;

        if (allPointsOurs || world.Points.Count == 0)
            AssignHunts(world, rest);
        else
            AssignCaptures(world, rest);
    }

    /// <summary>Nearest owned point as refuge; home edge midpoint when we hold nothing.</summary>
    private Fixed2 SafestOwnedSpot(World world, Fixed2 from)
    {
        CapturePoint? best = null;
        long bestScore = long.MaxValue;
        var units = world.Units;

        for (int p = 0; p < world.Points.Count; p++)
        {
            var point = world.Points[p];
            if (point.Owner != _side)
                continue;

            // Prefer owned points far from any enemy.
            long nearestEnemySq = long.MaxValue;
            for (int j = 0; j < units.Count; j++)
            {
                var e = units[j];
                if (!e.Alive || e.Side == _side || e.Side == Side.Neutral)
                    continue;
                long dSq = point.Pos.DistanceSquaredTo(e.Pos).Raw;
                if (dSq < nearestEnemySq) nearestEnemySq = dSq;
            }

            long score = from.DistanceSquaredTo(point.Pos).Raw - Math.Min(nearestEnemySq, 1L << 40);
            if (score < bestScore) { bestScore = score; best = point; }
        }

        if (best is not null)
            return best.Value.Pos;

        return _side == Side.Allies
            ? new Fixed2(TestWorldsHomeX(world, allies: true), world.Map.Height / Fixed.FromInt(2))
            : new Fixed2(TestWorldsHomeX(world, allies: false), world.Map.Height / Fixed.FromInt(2));
    }

    private static Fixed TestWorldsHomeX(World world, bool allies) =>
        allies ? Fixed.FromInt(6) : world.Map.Width - Fixed.FromInt(6);

    private void AssignCaptures(World world, List<int> unitIds)
    {
        var units = world.Units;
        var load = new int[world.Points.Count];

        foreach (int id in unitIds)
        {
            var u = units[id];

            int bestPoint = -1;
            Fixed bestCost = default;

            for (int p = 0; p < world.Points.Count; p++)
            {
                var point = world.Points[p];
                if (point.Owner == _side)
                    continue; // already ours

                var cost = CaptureCost(u.Pos, point, load[p]);

                if (bestPoint < 0 || cost < bestCost)
                {
                    bestPoint = p;
                    bestCost = cost;
                }
            }

            if (bestPoint >= 0)
            {
                _goals[id] = world.Points[bestPoint].Pos;
                load[bestPoint]++;
            }
            else
            {
                AssignHunt(world, id);
            }
        }
    }

    private static Fixed CaptureCost(Fixed2 from, in CapturePoint point, int crowd)
    {
        Fixed travel = from.DistanceTo(point.Pos);
        Fixed bias = point.IsVictoryPoint ? -Fixed.FromInt(VictoryPointBiasMeters) : Fixed.Zero;
        Fixed crowdPenalty = Fixed.FromInt(crowd * CrowdPenaltyMeters / 4);
        return travel + bias + crowdPenalty;
    }

    private void AssignHunts(World world, List<int> unitIds)
    {
        foreach (int id in unitIds)
            AssignHunt(world, id);
    }

    private void AssignHunt(World world, int unitId)
    {
        var u = world.Units[unitId];
        int nearest = -1;
        long bestDistSq = long.MaxValue;
        var units = world.Units;

        for (int j = 0; j < units.Count; j++)
        {
            var e = units[j];
            if (!e.Alive || e.Side == _side || e.Side == Side.Neutral)
                continue;
            long dSq = u.Pos.DistanceSquaredTo(e.Pos).Raw;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                nearest = j;
            }
        }

        _goals.Remove(unitId);
        if (nearest >= 0)
            _goals[unitId] = world.Units[nearest].Pos;
    }

    private void PruneDead(World world)
    {
        List<int>? doomed = null;
        var units = world.Units;
        foreach (var (id, _) in _goals)
        {
            if (id >= units.Count || !units[id].Alive)
                (doomed ??= new List<int>()).Add(id);
        }
        if (doomed is not null)
            foreach (int id in doomed)
                _goals.Remove(id);
    }
}
