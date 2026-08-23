namespace Sim;

/// <summary>
/// The entire mutable simulation state plus the tick pipeline.
/// Everything inside is integer-exact; the same seed and input log always produce
/// the same state, verifiable via <see cref="StateHash"/>.
/// </summary>
public sealed class World
{
    public MapDef Map { get; }
    public MatchOptions Options { get; }

    /// <summary>The seed this world was created with. Replays must reuse it.</summary>
    public ulong MatchSeed { get; }

    public Rng Rng;
    public int Tick;

    /// <summary>Units are indexed by Id. Slots are never removed; death is a flag.</summary>
    public List<Unit> Units = new();

    public List<CapturePoint> Points = new();
    public MatchState Match;

    /// <summary>Transient per-tick output. Excluded from hashing.</summary>
    public List<ShotEvent> Events = new();

    public World(MapDef map, ulong seed, MatchOptions? options = null)
    {
        Map = map;
        Options = options ?? new MatchOptions();
        MatchSeed = seed;
        Rng = Rng.FromSeed(seed);
        Tick = 0;

        foreach (var spec in map.CapturePoints)
            Points.Add(new CapturePoint
            {
                Pos = spec.Pos,
                Radius = spec.Radius,
                IsVictoryPoint = spec.IsVictoryPoint,
                Owner = Side.Neutral,
                Progress = Fixed.Zero,
            });

        Match = new MatchState
        {
            StartingTickets = Options.StartingTickets,
            TicketsAllies = Options.StartingTickets,
            TicketsCentral = Options.StartingTickets,
            AccumAllies = Fixed.Zero,
            AccumCentral = Fixed.Zero,
            Finished = false,
            Winner = Side.Neutral,
        };
    }

    public int Spawn(Side side, int typeId, Fixed2 pos, int rank = 0)
    {
        var type = UnitTypes.Get(typeId);
        var u = new Unit
        {
            Id = Units.Count,
            Side = side,
            TypeId = typeId,
            Pos = pos,
            Facing = new Fixed2(Fixed.One, Fixed.Zero),
            Hp = type.MaxHp,
            Suppression = Fixed.Zero,
            Rank = rank,
            Kills = 0,
            Order = OrderKind.Idle,
            Goal = pos,
            TargetId = -1,
            FireCooldownTicks = 0,
            Alive = true,
        };
        Units.Add(u);
        return u.Id;
    }

    /// <summary>
    /// Advance one fixed timestep: apply inputs, then run systems in fixed order.
    /// The caller owns the input log; feeding the same log from tick 0 reproduces this world exactly.
    /// </summary>
    public void Step(IReadOnlyList<Command>? commands = null)
    {
        Tick++;
        Events.Clear();

        if (commands is { Count: > 0 })
            CommandSystem.Apply(this, commands);

        MovementSystem.Step(this);
        CombatSystem.Step(this);
        SuppressionSystem.Step(this);
        CaptureSystem.Step(this);
        VictorySystem.Step(this);
    }

    /// <summary>
    /// Deterministic digest of all state that influences future evolution.
    /// Equal hashes across two runs at equal ticks prove lockstep compatibility.
    /// </summary>
    public ulong StateHash()
    {
        var h = new Hasher();
        h.Mix(Tick);
        h.Mix(Rng.StateA);
        h.Mix(Rng.StateB);

        h.Mix(Match.StartingTickets);
        h.Mix(Match.TicketsAllies);
        h.Mix(Match.TicketsCentral);
        h.Mix(Match.AccumAllies.Raw);
        h.Mix(Match.AccumCentral.Raw);
        h.Mix(Match.Finished);
        h.Mix((int)Match.Winner);

        h.Mix(Points.Count);
        for (int i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            h.Mix(p.Pos);
            h.Mix(p.Radius.Raw);
            h.Mix(p.IsVictoryPoint);
            h.Mix((int)p.Owner);
            h.Mix(p.Progress.Raw);
        }

        h.Mix(Units.Count);
        for (int i = 0; i < Units.Count; i++)
        {
            var u = Units[i];
            h.Mix(u.Id);
            h.Mix((int)u.Side);
            h.Mix(u.TypeId);
            h.Mix(u.Pos);
            h.Mix(u.Facing.X.Raw);
            h.Mix(u.Facing.Y.Raw);
            h.Mix(u.Hp.Raw);
            h.Mix(u.Suppression.Raw);
            h.Mix(u.Rank);
            h.Mix(u.Kills);
            h.Mix((int)u.Order);
            h.Mix(u.Goal);
            h.Mix(u.TargetId);
            h.Mix(u.FireCooldownTicks);
            h.Mix(u.Alive);
        }

        return h.Digest;
    }
}
