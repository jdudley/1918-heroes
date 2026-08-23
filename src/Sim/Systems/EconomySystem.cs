namespace Sim;

/// <summary>
/// Manpower drips in over time, faster the more territory you hold. Accumulators
/// keep fractional income exact under fixed-point arithmetic.
/// </summary>
public static class EconomySystem
{
    public static void Step(World world)
    {
        if (world.Match.Finished)
            return;

        Accumulate(world, Side.Allies);
        Accumulate(world, Side.Central);
    }

    private static void Accumulate(World world, Side side)
    {
        int owned = 0;
        for (int i = 0; i < world.Points.Count; i++)
        {
            var p = world.Points[i];
            if (p.Owner != side)
                continue;
            owned += p.IsVictoryPoint ? 2 : 1; // VPs are worth double the supply line
        }

        Fixed perSecond = SimConfig.BaseIncomePerSecond +
                          SimConfig.IncomePerOwnedPointPerSecond * Fixed.FromInt(owned);

        ref MatchState match = ref world.Match;
        Fixed accum = match.IncomeAccum(side) + perSecond * SimConfig.Dt;

        int whole = accum.ToInt();
        if (whole > 0)
        {
            accum -= Fixed.FromInt(whole);
            match.AddManpower(side, whole);
        }
        match.SetIncomeAccum(side, accum);
    }
}
