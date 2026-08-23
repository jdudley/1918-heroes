using Sim;

namespace Sim.Tests;

/// <summary>Shared maps and force layouts for tests.</summary>
public static class TestWorlds
{
    public static Fixed M(int v) => Fixed.FromInt(v);

    /// <summary>
    /// 96 x 64 m test map: a center victory point, two flank points,
    /// one crater, one sight-blocking obstacle, no baked-in armies.
    /// </summary>
    public static MapDef SmallMap() => new()
    {
        Name = "test-small",
        Width = M(96),
        Height = M(64),
        CapturePoints = new[]
        {
            new CapturePointSpec(new Fixed2(M(48), M(32)), M(6), IsVictoryPoint: true),
            new CapturePointSpec(new Fixed2(M(24), M(16)), M(6), IsVictoryPoint: false),
            new CapturePointSpec(new Fixed2(M(72), M(48)), M(6), IsVictoryPoint: false),
        },
        Cover = new[]
        {
            new CoverObject(new Fixed2(M(40), M(32)), M(3), CoverKind.Crater),
            new CoverObject(new Fixed2(M(56), M(32)), M(4), CoverKind.Trench),
        },
        SightBlockers = new[]
        {
            new Obstacle(new Fixed2(M(48), M(22)), M(4)),
        },
    };

    public static World Create(ulong seed = 12345, MatchOptions? options = null) =>
        new(SmallMap(), seed, options);

    public const int AlliesRifles = 0;
}
