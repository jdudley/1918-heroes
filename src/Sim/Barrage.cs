namespace Sim;

/// <summary>
/// An active artillery strike. Walks from Start toward End (Cursor advances per shell),
/// dropping <see cref="SimConfig.ShellsPerBarrage"/> shells at fixed intervals.
/// Pure state: scheduling is tick-exact, scatter rolls come from World.Rng.
/// </summary>
public struct Barrage
{
    public Side Side;
    public Fixed2 Cursor;
    /// <summary>Per-shell walk offset; Zero for stationary strikes.</summary>
    public Fixed2 Step;
    public int Remaining;
    public int NextTick;
}
