namespace Sim;

/// <summary>
/// The two belligerents. Allies maps to BEF + AEF co-op later; Central to the German Empire.
/// Neutral belongs to nobody (uncaptured points).
/// </summary>
public enum Side : byte
{
    Neutral = 0,
    Allies = 1,
    Central = 2,
}

public static class SideExtensions
{
    public static Side EnemyOf(this Side side) => side switch
    {
        Side.Allies => Side.Central,
        Side.Central => Side.Allies,
        _ => Side.Neutral,
    };
}
