namespace Sim;

/// <summary>Per-tick commands from the players (or AI). Applied at tick start under lockstep.</summary>
public enum CommandType : byte
{
    Stop = 0,
    Move = 1,
    AttackMove = 2,
}

public readonly record struct Command(int UnitId, CommandType Type, Fixed2 Pos);
