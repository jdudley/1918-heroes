using Godot;
using Sim;

using Side = Sim.Side;

namespace Heroes1918;

/// <summary>Ground ring + disc for one capture point, tinted by owner.</summary>
public partial class CapturePointView : Node3D
{
    private static readonly Color Neutral = new(0.85f, 0.85f, 0.82f);
    private static readonly Color Allies = new(0.55f, 0.70f, 0.40f);
    private static readonly Color Central = new(0.50f, 0.58f, 0.75f);

    private readonly StandardMaterial3D _ringMat = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
    private readonly StandardMaterial3D _discMat = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    public void Init(Fixed radius)
    {
        float r = (float)radius.Raw / Fixed.OneRaw;

        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = r * 0.92f, OuterRadius = r * 1.04f },
            MaterialOverride = _ringMat,
            Position = new Vector3(0, 0.06f, 0),
            Scale = new Vector3(1, 0.12f, 1),
        };
        AddChild(ring);

        var disc = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r * 0.88f, BottomRadius = r * 0.88f, Height = 0.05f },
            MaterialOverride = _discMat,
            Position = new Vector3(0, 0.03f, 0),
        };
        AddChild(disc);
    }

    public void Sync(in CapturePoint point)
    {
        Color owner = point.Owner switch
        {
            Side.Allies => Allies,
            Side.Central => Central,
            _ => Neutral,
        };

        // Brightness rises with capture progress toward the flip.
        float progress = (float)Fixed.Clamp(point.Progress / Fixed.FromInt(100), Fixed.Zero, Fixed.One).Raw / Fixed.OneRaw;
        _ringMat.AlbedoColor = owner;
        var fill = owner;
        fill.A = 0.10f + 0.35f * progress;
        _discMat.AlbedoColor = fill;
    }
}
