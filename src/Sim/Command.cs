namespace Sim;

/// <summary>Per-tick commands from the players (or AI). Applied at tick start under lockstep.</summary>
public enum CommandType : byte
{
    Stop = 0,
    Move = 1,
    AttackMove = 2,
    /// <summary>Call an artillery barrage. UnitId attributes the side (any living unit of the caller);
    /// Pos is the barrage start, Alt the walk end (Pos == Alt for a stationary strike).</summary>
    Barrage = 3,
    /// <summary>Dig in at current position; eventually creates a trench cover object.</summary>
    Dig = 4,
    /// <summary>Call a gas barrage: drops one lingering, drifting cloud at Pos.</summary>
    Gas = 5,
}

/// <summary>Alt carries the second point for two-point commands (barrage walk end); ignored elsewhere.</summary>
public readonly record struct Command(int UnitId, CommandType Type, Fixed2 Pos, Fixed2 Alt = default);
