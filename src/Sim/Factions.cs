namespace Sim;

public enum FactionId : byte
{
    BEF = 0,
    AEF = 1,
    GermanEmpire = 2,
}

/// <summary>
/// A playable army. Factions differ by roster (what they can requisition, in
/// hotkey order), starting ranks, and artillery character — never by special-cased
/// code paths. Default co-op: players share an Allied-side kit vs the German Empire.
/// </summary>
public sealed record FactionDef
{
    public required FactionId Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Requisitionable unit types, in hotkey order (keys 1..N).</summary>
    public required int[] Roster { get; init; }
    /// <summary>Starting veterancy per roster index.</summary>
    public int[] StartRanks { get; init; } = Array.Empty<int>();
    /// <summary>Barrage cooldown multiplier: BEF runs superior creeping barrages.</summary>
    public Fixed BarrageCooldownMultiplier { get; init; } = Fixed.One;
}

public static class Factions
{
    public static readonly FactionDef BEF = new()
    {
        Id = FactionId.BEF,
        Name = "BEF",
        Roster = new[]
        {
            UnitTypes.RifleSquad.Id,
            UnitTypes.LewisGunTeam.Id,
            UnitTypes.MachineGunSection.Id,
            UnitTypes.Engineers.Id,
            UnitTypes.MarkVTank.Id,
        },
        BarrageCooldownMultiplier = Fixed.FromRatio(80, 100),
    };

    public static readonly FactionDef AEF = new()
    {
        Id = FactionId.AEF,
        Name = "AEF",
        Roster = new[]
        {
            UnitTypes.AefRiflePlatoon.Id,
            UnitTypes.MachineGunSection.Id,
            UnitTypes.Engineers.Id,
            UnitTypes.Ft17Tank.Id,
        },
    };

    public static readonly FactionDef GermanEmpire = new()
    {
        Id = FactionId.GermanEmpire,
        Name = "German Empire",
        Roster = new[]
        {
            UnitTypes.Stormtroopers.Id,
            UnitTypes.RifleSquad.Id,
            UnitTypes.MachineGunSection.Id,
            UnitTypes.FlamethrowerTeam.Id,
            UnitTypes.Engineers.Id,
            UnitTypes.A7VTank.Id,
        },
        StartRanks = new[] { 1, 0, 0, 0, 0, 0 }, // stormtroopers land at rank 1
        BarrageCooldownMultiplier = Fixed.FromRatio(90, 100),
    };

    public static readonly FactionDef[] All = { BEF, AEF, GermanEmpire };

    public static FactionDef Get(FactionId id) => All[(int)id];
}
