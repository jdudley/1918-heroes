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

    // --- Artillery ---
    public const int ShellsPerBarrage = 8;
    public const int BarrageImpactIntervalTicks = 12;
    /// <summary>Meters between successive shells of a creeping barrage (start -> end walk).</summary>
    public static readonly Fixed CreepStepMeters = Fixed.FromRatio(3, 1);
    /// <summary>Impacts scatter uniformly within this radius of their scheduled point.</summary>
    public static readonly Fixed ScatterRadius = Fixed.FromInt(3);
    public static readonly Fixed BlastRadius = Fixed.FromInt(6);
    public static readonly Fixed ShellDamage = Fixed.FromInt(45);
    public static readonly Fixed ShellSuppression = Fixed.FromInt(35);
    public static readonly Fixed CraterRadius = Fixed.FromRatio(11, 5);
    public const int MaxDynamicCoverObjects = 240;
    /// <summary>Barrage cooldown per side, in ticks.</summary>
    public const int BarrageCooldownTicks = 30 * 45;

    // --- Gas ---
    public const int GasCooldownTicks = 30 * 75;
    public static readonly Fixed GasCloudRadius = Fixed.FromInt(7);
    public const int GasCloudLifetimeTicks = 30 * 22;
    /// <summary>Damage per second to anyone standing in the open cloud.</summary>
    public static readonly Fixed GasDamagePerSecond = Fixed.FromInt(5);
    /// <summary>Suppression gained per second in the cloud: gas is terrifying.</summary>
    public static readonly Fixed GasSuppressionPerSecond = Fixed.FromInt(14);

    // --- Director / flanks ---
    public const int DirectorIntervalTicks = 30 * 30;   // assess momentum every 30 s
    public const int FlankWarningSeconds = 20;          // flare-to-impact telegraph

    // --- AI tactics ---
    /// <summary>Squads below this hp fraction near enemies fall back instead of dying.</summary>
    public static readonly Fixed RetreatHpFraction = Fixed.FromRatio(35, 100);
    public static readonly Fixed RetreatEnemyProximity = Fixed.FromInt(25);
    /// <summary>Enemies within this range of an owned point trigger a defender assignment.</summary>
    public static readonly Fixed DefendTriggerRadius = Fixed.FromInt(22);

    // --- Digging ---
    /// <summary>Seconds of uninterrupted digging to complete one trench segment.</summary>
    public static readonly Fixed DigSeconds = Fixed.FromInt(10);
    public static readonly Fixed TrenchRadius = Fixed.FromRatio(5, 2);

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
