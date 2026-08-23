namespace Sim;

public enum OrderKind : byte
{
    /// <summary>Hold position; engage enemies of opportunity in range.</summary>
    Idle = 0,
    /// <summary>Move to the goal ignoring enemies entirely (the tactical march).</summary>
    Move = 1,
    /// <summary>Advance to the goal, engaging anything that comes into range.</summary>
    AttackMove = 2,
}

/// <summary>
/// One sim entity: a squad. The simulation never models individual soldiers;
/// hp is the squad's collective strength and the renderer puppets men around this position.
/// </summary>
public struct Unit
{
    public int Id;
    public Side Side;
    public int TypeId;

    public Fixed2 Pos;
    /// <summary>Unit-length direction the squad is oriented toward.</summary>
    public Fixed2 Facing;

    public Fixed Hp;
    /// <summary>0..100. Above thresholds it degrades speed, accuracy, and capture.</summary>
    public Fixed Suppression;

    public int Rank; // veterancy 0..3
    public int Kills;

    public OrderKind Order;
    public Fixed2 Goal;
    /// <summary>Index into World.Units, or -1.</summary>
    public int TargetId;

    public int FireCooldownTicks;
    public bool Alive;

    public readonly UnitType Type => UnitTypes.Get(TypeId);
}
