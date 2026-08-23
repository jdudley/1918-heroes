namespace Sim;

public static class VictorySystem
{
    /// <summary>
    /// Ticket drain: each victory point a side owns drains the enemy's tickets.
    /// Losing position costs income and map control; the drain does the pacing,
    /// and comebacks stay possible while tickets remain.
    /// </summary>
    public static void Step(World world)
    {
        if (world.Match.Finished) return;

        ref MatchState match = ref world.Match;

        for (int i = 0; i < world.Points.Count; i++)
        {
            var point = world.Points[i];
            if (!point.IsVictoryPoint || point.Owner == Side.Neutral)
                continue;

            switch (point.Owner)
            {
                case Side.Allies:
                    match.AccumCentral += world.Options.TicketDrainPerVpPerSecond * SimConfig.Dt;
                    break;
                case Side.Central:
                    match.AccumAllies += world.Options.TicketDrainPerVpPerSecond * SimConfig.Dt;
                    break;
            }
        }

        Drain(ref match, Side.Allies);
        Drain(ref match, Side.Central);

        if (match.TicketsAllies <= 0 || match.TicketsCentral <= 0)
        {
            match.Finished = true;
            match.Winner = match.TicketsAllies <= 0 ? Side.Central : Side.Allies;
        }
    }

    private static void Drain(ref MatchState match, Side side)
    {
        Fixed accum = side == Side.Allies ? match.AccumAllies : match.AccumCentral;
        int whole = accum.ToInt();
        if (whole <= 0) return;

        accum -= Fixed.FromInt(whole);
        if (side == Side.Allies)
        {
            match.AccumAllies = accum;
            match.TicketsAllies -= whole;
        }
        else
        {
            match.AccumCentral = accum;
            match.TicketsCentral -= whole;
        }
    }
}
