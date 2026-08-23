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

/// <summary>
/// A handcrafted map: terrain extent plus point layouts. Maps carry no scripting —
/// everything in a match emerges from play.
/// </summary>
public sealed record MapDef(
    string Name,
    Fixed Width,
    Fixed Height,
    IReadOnlyList<CapturePointSpec> CapturePoints,
    IReadOnlyList<CoverObject> Cover,
    IReadOnlyList<Obstacle> SightBlockers);
