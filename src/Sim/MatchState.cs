namespace Sim;

/// <summary>Runtime capture point state. Ownership and progress are world-mutable.</summary>
public struct CapturePoint
{
    public Fixed2 Pos;
    public Fixed Radius;
    public bool IsVictoryPoint;

    public Side Owner;
    /// <summary>0..100 capture progress toward the next flip. Decays when nobody pushes it.</summary>
    public Fixed Progress;
}

public sealed record MatchOptions
{
    public int StartingTickets { get; init; } = 500;
    /// <summary>Enemy tickets drained per second per victory point held.</summary>
    public Fixed TicketDrainPerVpPerSecond { get; init; } = Fixed.One;
}

/// <summary>Match-level mutable state: tickets, drain accumulators, outcome.</summary>
public struct MatchState
{
    public int StartingTickets;
    public int TicketsAllies;
    public int TicketsCentral;
    // Fractional drain accumulators so per-tick drains stay exact.
    public Fixed AccumAllies;
    public Fixed AccumCentral;

    public bool Finished;
    public Side Winner;

    /// <summary>Tick at which the side may call its next barrage (Tick >= value means ready).</summary>
    public int NextBarrageTickAllies;
    public int NextBarrageTickCentral;

    public int NextBarrageTick(Side side) => side switch
    {
        Side.Allies => NextBarrageTickAllies,
        Side.Central => NextBarrageTickCentral,
        _ => int.MaxValue,
    };

    public void SetNextBarrageTick(Side side, int tick)
    {
        if (side == Side.Allies) NextBarrageTickAllies = tick;
        else if (side == Side.Central) NextBarrageTickCentral = tick;
    }

    /// <summary>Tick at which the side may call its next gas barrage.</summary>
    public int NextGasTickAllies;
    public int NextGasTickCentral;

    public int NextGasTick(Side side) => side switch
    {
        Side.Allies => NextGasTickAllies,
        Side.Central => NextGasTickCentral,
        _ => int.MaxValue,
    };

    public void SetNextGasTick(Side side, int tick)
    {
        if (side == Side.Allies) NextGasTickAllies = tick;
        else if (side == Side.Central) NextGasTickCentral = tick;
    }

    public int Remaining(Side side) => side switch
    {
        Side.Allies => TicketsAllies,
        Side.Central => TicketsCentral,
        _ => 0,
    };
}
