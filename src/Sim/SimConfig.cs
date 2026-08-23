namespace Sim;

/// <summary>
/// Central tuning constants. Values are first-pass guesses to be tuned by
/// AI-vs-AI self-play sweeps once the full game exists.
/// </summary>
public static class SimConfig
{
    public const int TicksPerSecond = 30;
    public static readonly Fixed Dt = Fixed.FromRatio(1, TicksPerSecond);

    // --- Suppression ---
    public static readonly Fixed SuppressedThreshold = Fixed.FromInt(25);
    public static readonly Fixed PinnedThreshold = Fixed.FromInt(70);
    public static readonly Fixed SuppressionDecayPerSecond = Fixed.FromInt(6);
    public static readonly Fixed MaxSuppression = Fixed.FromInt(100);

    // --- Veterancy (three ranks earned in combat) ---
    public static readonly int[] RankKillThresholds = { 4, 10, 18 };

    public static Fixed AccuracyBonusPerRank(int rank) => Fixed.FromRatio(5 * rank, 100);
    public static Fixed SuppressionResistPerRank(int rank) => Fixed.FromRatio(15 * rank, 100);

    /// <summary>Multiplicative accuracy modifier from suppression state.</summary>
    public static Fixed AccuracyMultiplier(Fixed suppression)
    {
        if (suppression >= PinnedThreshold) return Fixed.FromRatio(15, 100);
        if (suppression >= SuppressedThreshold) return Fixed.FromRatio(50, 100);
        return Fixed.One;
    }

    /// <summary>Multiplicative speed modifier from suppression state.</summary>
    public static Fixed SpeedMultiplier(Fixed suppression)
    {
        if (suppression >= PinnedThreshold) return Fixed.FromRatio(25, 100);
        if (suppression >= SuppressedThreshold) return Fixed.FromRatio(60, 100);
        return Fixed.One;
    }

    public static bool IsPinned(Fixed suppression) => suppression >= PinnedThreshold;

    // Pinned squads are combat-ineffective: no firing, no capturing, crawl-speed movement.

    // --- Capture ---
    /// <summary>Seconds for one squad to fully capture an uncontested point.</summary>
    public const int CaptureSecondsPerPointPerSquad = 6;

    // --- Targeting ---
    public const int RetargetIntervalTicks = 6;

    // --- Cover: hit-chance and suppression-gain multipliers when the target is in cover ---
    public static Fixed CoverHitMultiplier(CoverKind kind) => kind switch
    {
        CoverKind.Crater => Fixed.FromRatio(60, 100),
        CoverKind.Trench => Fixed.FromRatio(40, 100),
        CoverKind.Rubble => Fixed.FromRatio(55, 100),
        _ => Fixed.One,
    };

    public static Fixed CoverSuppressionMultiplier(CoverKind kind) => kind switch
    {
        CoverKind.Crater => Fixed.FromRatio(60, 100),
        CoverKind.Trench => Fixed.FromRatio(35, 100),
        CoverKind.Rubble => Fixed.FromRatio(50, 100),
        _ => Fixed.One,
    };
}
