namespace Sim;

/// <summary>Static map data for ground-level cover objects (craters, trenches, rubble).</summary>
public sealed record CoverObject(Fixed2 Pos, Fixed Radius, CoverKind Kind);

public enum CoverKind : byte
{
    Crater = 0,
    Trench = 1,
    Rubble = 2,
}

/// <summary>Sight-blocking obstacle (building, wood). Blocks line of sight but not movement in slice 1.</summary>
public sealed record Obstacle(Fixed2 Pos, Fixed Radius);

/// <summary>Immutable spawn-time description of a capture point on the map.</summary>
public sealed record CapturePointSpec(Fixed2 Pos, Fixed Radius, bool IsVictoryPoint);

/// <summary>A starting unit baked into the map: both peers spawn identical armies from it.</summary>
public sealed record SpawnSpec(Side Side, int TypeId, Fixed2 Pos);

/// <summary>
/// A handcrafted map: terrain extent plus point layouts and starting forces.
/// Maps carry no scripting — everything in a match emerges from play.
/// </summary>
public sealed record MapDef
{
    public required string Name { get; init; }
    public required Fixed Width { get; init; }
    public required Fixed Height { get; init; }
    public IReadOnlyList<CapturePointSpec> CapturePoints { get; init; } = Array.Empty<CapturePointSpec>();
    public IReadOnlyList<CoverObject> Cover { get; init; } = Array.Empty<CoverObject>();
    public IReadOnlyList<Obstacle> SightBlockers { get; init; } = Array.Empty<Obstacle>();
    public IReadOnlyList<SpawnSpec> Spawns { get; init; } = Array.Empty<SpawnSpec>();
}
