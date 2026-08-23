namespace Sim;

/// <summary>Immutable weapon stats. Balance tuning happens here; behavior never special-cases types.</summary>
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
/// Immutable unit type definition. Slice 1 keeps two generic line-infantry archetypes;
/// faction kits arrive in build step 5.
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

    public override string ToString() => Name;
}

public static class UnitTypes
{
    public static readonly UnitType RifleSquad = new()
    {
        Id = 0,
        Name = "Rifle Squad",
        MaxHp = Fixed.FromInt(600), // ~6 men at 100 hp each
        Speed = Fixed.FromRatio(34, 10),
        Sight = Fixed.FromInt(36),
        Weapon = new Weapon
        {
            Range = Fixed.FromInt(28),
            CooldownTicks = 30, // one volley per second
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

    public static readonly UnitType[] All = { RifleSquad, MachineGunSection };

    public static UnitType Get(int id) => All[id];
}
