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

    public int Remaining(Side side) => side switch
    {
        Side.Allies => TicketsAllies,
        Side.Central => TicketsCentral,
        _ => 0,
    };
}
