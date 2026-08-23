using Godot;
using Sim;

using Side = Sim.Side;

namespace Heroes1918;

/// <summary>
/// Placeholder capsule puppet for one sim squad. The renderer knows things the sim
/// does not (individual bodies, tints); the sim knows things the renderer reads.
/// Interpolates between the last two executed ticks for smooth motion at 30 Hz logic.
/// </summary>
public partial class UnitView : Node3D
{
    private static readonly Color AlliesBase = new(0.66f, 0.60f, 0.36f);
    private static readonly Color CentralBase = new(0.40f, 0.44f, 0.50f);
    private static readonly Color DeadTint = new(0.16f, 0.15f, 0.14f);
    private static readonly Color PinTint = new(0.85f, 0.25f, 0.20f);

    public int UnitId { get; set; }
    public Vector3 PrevPos { get; set; }
    public Vector3 CurPos { get; set; }
    public float PrevYaw { get; set; }
    public float CurYaw { get; set; }

    private MeshInstance3D _body = null!;
    private MeshInstance3D _ring = null!;
    private StandardMaterial3D _material = null!;
    private Color _baseColor;
    private bool _corpse;

    public void Init(Sim.Side side)
    {
        _baseColor = side == Sim.Side.Allies ? AlliesBase : CentralBase;

        _material = new StandardMaterial3D { AlbedoColor = _baseColor, Roughness = 0.9f };

        _body = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.55f, Height = 1.9f },
            MaterialOverride = _material,
            Position = new Vector3(0, 0.95f, 0),
        };
        AddChild(_body);

        var ringMesh = new TorusMesh
        {
            InnerRadius = 0.85f,
            OuterRadius = 1.05f,
        };
        var ringMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 0.7f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _ring = new MeshInstance3D
        {
            Mesh = ringMesh,
            MaterialOverride = ringMat,
            Position = new Vector3(0, 0.08f, 0),
            Scale = new Vector3(1, 0.15f, 1),
            Visible = false,
        };
        AddChild(_ring);
    }

    public void SetSelected(bool selected) => _ring.Visible = selected && !_corpse;

    /// <summary>alpha in [0,1] between the previous and current executed tick.</summary>
    public void Sync(World world, float alpha)
    {
        var u = world.Units[UnitId];

        if (!u.Alive)
        {
            if (!_corpse)
            {
                _corpse = true;
                _ring.Visible = false;
                _body.RotationDegrees = new Vector3(90, 0, 0);
                _body.Position = new Vector3(0, 0.4f, 0);
                _material.AlbedoColor = DeadTint;
            }
            return;
        }

        Position = PrevPos.Lerp(CurPos, Mathf.Clamp(alpha, 0f, 1f));
        Rotation = new Vector3(0, Mathf.LerpAngle(PrevYaw, CurYaw, Mathf.Clamp(alpha, 0f, 1f)), 0);

        // Squad strength and morale show on the body: shorter when bloodied,
        // darker when suppressed, reddened when pinned.
        float hpFrac = (float)Fixed.Clamp(u.Hp / u.Type.MaxHp, Fixed.FromRatio(15, 100), Fixed.One).Raw / Fixed.OneRaw;
        _body.Scale = new Vector3(1, hpFrac, 1);

        float supFrac = (float)Fixed.Clamp(u.Suppression / SimConfig.MaxSuppression, Fixed.Zero, Fixed.One).Raw / Fixed.OneRaw;
        var tinted = _baseColor.Lerp(new Color(0.25f, 0.25f, 0.28f), supFrac * 0.65f);
        if (SimConfig.IsPinned(u.Suppression))
            tinted = tinted.Lerp(PinTint, 0.45f);
        _material.AlbedoColor = tinted;
    }

    public void Rebind(World world)
    {
        var u = world.Units[UnitId];
        var p = ToGodot(u.Pos);
        PrevPos = p;
        CurPos = p;
        _corpse = false;
        _body.RotationDegrees = Vector3.Zero;
        _body.Position = new Vector3(0, 0.95f, 0);
        _material.AlbedoColor = _baseColor;
        Sync(world, 1f);
    }

    public static Vector3 ToGodot(Fixed2 sim) => new((float)sim.X.Raw / Fixed.OneRaw, 0f, (float)sim.Y.Raw / Fixed.OneRaw);

    public static float YawOf(Fixed2 facing) => Mathf.Atan2((float)facing.Y.Raw / Fixed.OneRaw, (float)facing.X.Raw / Fixed.OneRaw);
}
