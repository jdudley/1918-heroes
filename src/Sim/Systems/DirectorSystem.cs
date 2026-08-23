namespace Sim;

public enum FlankEventKind : byte
{
    /// <summary>Enemy reserves pour onto the map and march for a victory point.</summary>
    EnemyReserves = 0,
    /// <summary>A holding neighbor releases spare men to you. Rare enough to feel like providence.</summary>
    FriendlyReserves = 1,
}

/// <summary>
/// A telegraphed flank event. Visible from WarnTick (the flare goes up), lands at
/// LandTick — every surprise is legible in hindsight; vary when and where, never whether.
/// </summary>
public struct FlankEvent
{
    public int Id;
    /// <summary>true: the event pressures the Allies (enemy wave / their windfall).</summary>
    public bool AgainstAllies;
    public FlankEventKind Kind;
    public bool LeftFlank;
    public Fixed2 SpawnPos;
    /// <summary>Squad count for reserve waves.</summary>
    public int Squads;
    public int WarnTick;
    public int LandTick;
}

/// <summary>
/// The director: sits above the battle, reads momentum (tickets, victory points,
/// casualties) once every interval, and rolls telegraphed flank events biased to
/// keep matches tense rather than steamrolled — pressure on whoever pulls ahead,
/// providence for whoever bleeds.
/// </summary>
public static class DirectorSystem
{
    private static readonly Fixed RetreatSafeDistance = Fixed.FromInt(20);

    public static void Step(World world)
    {
        LandDueEvents(world);

        if (world.Match.Finished)
            return;
        if (world.Tick < world.NextDirectorThink || world.Tick == 0)
            return;
        world.NextDirectorThink = world.Tick + SimConfig.DirectorIntervalTicks;

        // Cap concurrent drama.
        if (world.PendingFlankEvents.Count >= 2)
            return;

        Side winner = MomentumLeader(world);
        if (winner == Side.Neutral)
            return;

        // 55% of events pressure the leader; 45% relieve the loser.
        bool pressTheLeader = world.Rng.Chance(Fixed.FromRatio(55, 100));
        Side actedOn = pressTheLeader ? winner : winner.EnemyOf();
        bool againstAllies = actedOn == Side.Allies;

        FlankEventKind kind = pressTheLeader ? FlankEventKind.EnemyReserves : FlankEventKind.FriendlyReserves;
        int squads = kind == FlankEventKind.EnemyReserves ? 2 + (int)(world.Rng.NextU32() % 2) : 1;

        bool leftFlank = world.Rng.Chance(Fixed.Half);
        var map = world.Map;
        long edgeX = leftFlank ? Fixed.FromInt(4).Raw : (map.Width - Fixed.FromInt(4)).Raw;
        int y = 8 + (int)(world.Rng.NextU32() % Math.Max(1, map.Height.ToInt() - 16));
        var spawnPos = new Fixed2(Fixed.FromRaw(edgeX), Fixed.FromInt(y));

        world.PendingFlankEvents.Add(new FlankEvent
        {
            Id = ++world._nextFlankEventId,
            AgainstAllies = againstAllies,
            Kind = kind,
            LeftFlank = leftFlank,
            SpawnPos = spawnPos,
            Squads = squads,
            WarnTick = world.Tick,
            LandTick = world.Tick + SimConfig.FlankWarningSeconds * SimConfig.TicksPerSecond,
        });
    }

    private static void LandDueEvents(World world)
    {
        var pending = world.PendingFlankEvents;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var e = pending[i];
            if (world.Tick < e.LandTick)
                continue;

            pending.RemoveAt(i);
            LandEvent(world, e);
        }
    }

    private static void LandEvent(World world, in FlankEvent e)
    {
        Side side = e.AgainstAllies ? Side.Allies : Side.Central;
        Side enemy = side.EnemyOf();

        if (e.Kind == FlankEventKind.FriendlyReserves)
        {
            var holdCenter = new Fixed2(world.Map.Width / Fixed.FromInt(2), world.Map.Height / Fixed.FromInt(2));
            SpawnWave(world, side, e.SpawnPos, e.Squads, OrderKind.Idle, holdCenter);
        }
        else
        {
            // Reserves march for the victory point closest to their entry flank.
            Fixed2 objective = NearestPointTo(world, e.SpawnPos);
            SpawnWave(world, enemy, e.SpawnPos, e.Squads, OrderKind.AttackMove, objective);
        }

        // Signal flare so the renderer can mark the landing.
        world.Explosions.Add(e.SpawnPos);
    }

    private static void SpawnWave(World world, Side side, Fixed2 at, int squads, OrderKind order, Fixed2 goal)
    {
        for (int i = 0; i < squads; i++)
        {
            var pos = at + new Fixed2(Fixed.Zero, Fixed.FromInt(i * 3 - 3));
            int id = world.Spawn(side, UnitTypes.RifleSquad.Id, pos);
            var u = world.Units[id];
            u.Order = order;
            u.Goal = order == OrderKind.AttackMove ? goal : pos;
            u.Facing = new Fixed2(side == Side.Allies ? Fixed.One : -Fixed.One, Fixed.Zero);
            world.Units[id] = u;
        }
    }

    private static Fixed2 NearestPointTo(World world, Fixed2 pos)
    {
        CapturePoint best = default;
        long bestDistSq = long.MaxValue;
        for (int i = 0; i < world.Points.Count; i++)
        {
            long dSq = world.Points[i].Pos.DistanceSquaredTo(pos).Raw;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = world.Points[i];
            }
        }
        return best.Pos;
    }

    /// <summary>
    /// Who is winning: victory points owned, then ticket gap, then surviving strength.
    /// Neutral means genuinely balanced — no drama this interval.
    /// </summary>
    private static Side MomentumLeader(World world)
    {
        int vpAllies = 0, vpCentral = 0;
        for (int i = 0; i < world.Points.Count; i++)
        {
            if (!world.Points[i].IsVictoryPoint)
                continue;
            switch (world.Points[i].Owner)
            {
                case Side.Allies: vpAllies++; break;
                case Side.Central: vpCentral++; break;
            }
        }

        if (vpAllies != vpCentral)
            return vpAllies > vpCentral ? Side.Allies : Side.Central;

        long ticketGap = (long)world.Match.TicketsAllies - world.Match.TicketsCentral;
        if (Math.Abs(ticketGap) > 60)
            return ticketGap > 0 ? Side.Allies : Side.Central;

        long hpAllies = 0, hpCentral = 0;
        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            if (!units[i].Alive)
                continue;
            if (units[i].Side == Side.Allies) hpAllies += units[i].Hp.Raw;
            else if (units[i].Side == Side.Central) hpCentral += units[i].Hp.Raw;
        }
        // 15% strength lead counts as momentum.
        if (hpAllies * 100 > hpCentral * 115) return Side.Allies;
        if (hpCentral * 100 > hpAllies * 115) return Side.Central;
        return Side.Neutral;
    }
}
