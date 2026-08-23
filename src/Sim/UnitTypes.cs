namespace Sim;

/// <summary>
/// The armies of 1918. ~15 unit types across three factions; balance numbers are
/// first-pass guesses to be tuned by self-play sweeps.
/// </summary>
public sealed record Weapon
{
    public required Fixed Range { get; init; }
    public int CooldownTicks { get; init; }
    /// <summary>Base hit probability against a target in the open.</summary>
    public Fixed Accuracy { get; init; }
    public Fixed Damage { get; init; }
    public Fixed SuppressionPerHit { get; init; }
    /// <summary>Suppression inflicted even when the shot misses: being shot at shakes men.</summary>
    public Fixed SuppressionPerNearMiss { get; init; }
}

/// <summary>
/// Immutable unit type definition. Faction flavor lives in modifiers
/// (<see cref="SuppressionTakenMultiplier"/>, <see cref="VetXpPerKill"/>,
/// <see cref="DigSpeedMultiplier"/>) rather than special-cased behavior.
/// </summary>
public sealed record UnitType
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required Fixed MaxHp { get; init; }
    /// <summary>Meters per second.</summary>
    public required Fixed Speed { get; init; }
    /// <summary>Vision range in meters.</summary>
    public required Fixed Sight { get; init; }
    public required Weapon Weapon { get; init; }
    /// <summary>Manpower cost to requisition this squad.</summary>
    public int ManpowerCost { get; init; } = 100;
    /// <summary>Digging speed relative to a rifle squad (engineers dig fastest, tanks not at all).</summary>
    public Fixed DigSpeedMultiplier { get; init; } = Fixed.One;
    /// <summary>Multiplicative modifier on suppression gained (stormtroopers shrug, green troops crumble).</summary>
    public Fixed SuppressionTakenMultiplier { get; init; } = Fixed.One;
    /// <summary>Veterancy experience earned per kill (AEF earns fastest).</summary>
    public Fixed VetXpPerKill { get; init; } = Fixed.One;
    /// <summary>Ranks granted at spawn (German stormtroopers land at rank 1).</summary>
    public int StartingRank { get; init; }

    public override string ToString() => Name;
}

public static class UnitTypes
{
    // ---- Shared line units -------------------------------------------------

    public static readonly UnitType RifleSquad = new()
    {
        Id = 0,
        Name = "Rifle Squad",
        MaxHp = Fixed.FromInt(600),
        Speed = Fixed.FromRatio(34, 10),
        Sight = Fixed.FromInt(36),
        ManpowerCost = 90,
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(28),
            CooldownTicks = 30,
            Accuracy = Fixed.FromRatio(35, 100),
            Damage = Fixed.FromInt(25),
            SuppressionPerHit = Fixed.FromInt(12),
            SuppressionPerNearMiss = Fixed.FromRatio(4, 1),
        },
    };

    public static readonly UnitType MachineGunSection = new()
    {
        Id = 1,
        Name = "Machine Gun Section",
        MaxHp = Fixed.FromInt(300),
        Speed = Fixed.FromRatio(26, 10),
        Sight = Fixed.FromInt(44),
        ManpowerCost = 140,
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(38),
            CooldownTicks = 48,
            Accuracy = Fixed.FromRatio(45, 100),
            Damage = Fixed.FromInt(40),
            SuppressionPerHit = Fixed.FromInt(25),
            SuppressionPerNearMiss = Fixed.FromInt(10),
        },
    };

    public static readonly UnitType Engineers = new()
    {
        Id = 2,
        Name = "Engineers",
        MaxHp = Fixed.FromInt(400),
        Speed = Fixed.FromRatio(30, 10),
        Sight = Fixed.FromInt(32),
        ManpowerCost = 80,
        DigSpeedMultiplier = Fixed.FromInt(3),
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(20),
            CooldownTicks = 36,
            Accuracy = Fixed.FromRatio(25, 100),
            Damage = Fixed.FromInt(15),
            SuppressionPerHit = Fixed.FromInt(6),
            SuppressionPerNearMiss = Fixed.FromRatio(2, 1),
        },
    };

    // ---- BEF: veterans, best artillery, Mark V tanks ------------------------

    public static readonly UnitType LewisGunTeam = new()
    {
        Id = 3,
        Name = "Lewis Gun Team",
        MaxHp = Fixed.FromInt(300),
        Speed = Fixed.FromRatio(28, 10),
        Sight = Fixed.FromInt(40),
        ManpowerCost = 150,
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(34),
            CooldownTicks = 22,
            Accuracy = Fixed.FromRatio(38, 100),
            Damage = Fixed.FromInt(18),
            SuppressionPerHit = Fixed.FromInt(14),
            SuppressionPerNearMiss = Fixed.FromInt(6),
        },
    };

    public static readonly UnitType MarkVTank = new()
    {
        Id = 4,
        Name = "Mark V Tank",
        MaxHp = Fixed.FromInt(2200),
        Speed = Fixed.FromRatio(12, 10),
        Sight = Fixed.FromInt(30),
        ManpowerCost = 420,
        DigSpeedMultiplier = Fixed.Zero,
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(34),
            CooldownTicks = 60,
            Accuracy = Fixed.FromRatio(50, 100),
            Damage = Fixed.FromInt(90),
            SuppressionPerHit = Fixed.FromInt(25),
            SuppressionPerNearMiss = Fixed.FromInt(10),
        },
    };

    // ---- AEF: fresh and numerous, borrowed French kit -----------------------

    public static readonly UnitType AefRiflePlatoon = new()
    {
        Id = 5,
        Name = "Rifle Platoon (AEF)",
        MaxHp = Fixed.FromInt(750), // bigger platoon
        Speed = Fixed.FromRatio(34, 10),
        Sight = Fixed.FromInt(36),
        ManpowerCost = 75,          // cheap replacements
        SuppressionTakenMultiplier = Fixed.FromRatio(125, 100), // green: suppresses easily...
        VetXpPerKill = Fixed.FromInt(2),                        // ...but earns fastest
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(28),
            CooldownTicks = 30,
            Accuracy = Fixed.FromRatio(32, 100),
            Damage = Fixed.FromInt(25),
            SuppressionPerHit = Fixed.FromInt(12),
            SuppressionPerNearMiss = Fixed.FromRatio(4, 1),
        },
    };

    public static readonly UnitType Ft17Tank = new()
    {
        Id = 6,
        Name = "FT-17 Light Tank",
        MaxHp = Fixed.FromInt(700),
        Speed = Fixed.FromRatio(16, 10),
        Sight = Fixed.FromInt(28),
        ManpowerCost = 280,
        DigSpeedMultiplier = Fixed.Zero,
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(28),
            CooldownTicks = 55,
            Accuracy = Fixed.FromRatio(45, 100),
            Damage = Fixed.FromInt(55),
            SuppressionPerHit = Fixed.FromInt(18),
            SuppressionPerNearMiss = Fixed.FromInt(8),
        },
    };

    // ---- German Empire: stormtroopers, flamethrowers, the A7V ---------------

    public static readonly UnitType Stormtroopers = new()
    {
        Id = 7,
        Name = "Stormtroopers",
        MaxHp = Fixed.FromInt(500),
        Speed = Fixed.FromRatio(38, 10),
        Sight = Fixed.FromInt(36),
        ManpowerCost = 135,
        SuppressionTakenMultiplier = Fixed.FromRatio(55, 100), // infiltration training
        StartingRank = 1,                                      // land at rank 1
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(26),
            CooldownTicks = 26,
            Accuracy = Fixed.FromRatio(40, 100),
            Damage = Fixed.FromInt(30),
            SuppressionPerHit = Fixed.FromInt(12),
            SuppressionPerNearMiss = Fixed.FromInt(5),
        },
    };

    public static readonly UnitType FlamethrowerTeam = new()
    {
        Id = 8,
        Name = "Flamethrower Team",
        MaxHp = Fixed.FromInt(250),
        Speed = Fixed.FromRatio(32, 10),
        Sight = Fixed.FromInt(30),
        ManpowerCost = 120,
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(9),
            CooldownTicks = 40,
            Accuracy = Fixed.FromRatio(85, 100),
            Damage = Fixed.FromInt(70),
            SuppressionPerHit = Fixed.FromInt(30),
            SuppressionPerNearMiss = Fixed.FromInt(12),
        },
    };

    public static readonly UnitType A7VTank = new()
    {
        Id = 9,
        Name = "A7V",
        MaxHp = Fixed.FromInt(2600),
        Speed = Fixed.FromRatio(10, 10),
        Sight = Fixed.FromInt(30),
        ManpowerCost = 470,
        DigSpeedMultiplier = Fixed.Zero,
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(32),
            CooldownTicks = 65,
            Accuracy = Fixed.FromRatio(50, 100),
            Damage = Fixed.FromInt(95),
            SuppressionPerHit = Fixed.FromInt(25),
            SuppressionPerNearMiss = Fixed.FromInt(10),
        },
    };

    public static readonly UnitType[] All =
    {
        RifleSquad, MachineGunSection, Engineers, LewisGunTeam, MarkVTank,
        AefRiflePlatoon, Ft17Tank, Stormtroopers, FlamethrowerTeam, A7VTank,
    };

    public static UnitType Get(int id) => All[id];
}
