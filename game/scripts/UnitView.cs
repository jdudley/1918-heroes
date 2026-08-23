using Godot;
using Sim;
using Side = Sim.Side;

namespace Heroes1918;

/// <summary>
/// Squad puppet renderer: the sim sees one entity, you see a squad of men.
/// Builds distinct low-poly silhouettes per unit type (infantry wedges, MG crews
/// behind their gun, tank hulls with turrets), tinted by faction uniform.
/// Soldiers crouch under suppression and fall when the squad breaks.
/// </summary>
public partial class UnitView : Node3D
{
    // Faction uniforms.
    private static readonly System.Collections.Generic.Dictionary<Sim.FactionId, Color> Uniform = new()
    {
        [Sim.FactionId.BEF] = new Color(0.66f, 0.58f, 0.34f),          // khaki
        [Sim.FactionId.AEF] = new Color(0.47f, 0.53f, 0.33f),          // olive drab
        [Sim.FactionId.GermanEmpire] = new Color(0.42f, 0.46f, 0.40f), // feldgrau
    };
    private static readonly Color HelmetTint = new(0.7f, 0.7f, 0.7f);
    private static readonly Color WeaponColor = new(0.12f, 0.11f, 0.10f);
    private static readonly Color DeadTint = new(0.16f, 0.15f, 0.14f);
    private static readonly Color PinTint = new(1.0f, 0.45f, 0.30f);

    public int UnitId { get; set; }
    public Vector3 PrevPos { get; set; }
    public Vector3 CurPos { get; set; }
    public float PrevYaw { get; set; }
    public float CurYaw { get; set; }

    private readonly List<Node3D> _soldiers = new();
    private readonly List<StandardMaterial3D> _uniformMaterials = new();
    private MeshInstance3D? _hpBar;
    private StandardMaterial3D? _hpBarMaterial;
    private Color _baseColor;
    private bool _corpse;
    private float _uniformHueShift;

    /// <summary>Build the formation for this squad's type.</summary>
    public void Init(Side side, int typeId, Sim.FactionId faction)
    {
        _baseColor = Uniform[faction];
        var type = UnitTypes.Get(typeId);
        bool german = faction == Sim.FactionId.GermanEmpire;

        switch (type.Name)
        {
            case "Mark V Tank": BuildTank(3.6f, 1.25f); break;
            case "FT-17 Light Tank": BuildTank(2.4f, 0.95f); break;
            case "A7V": BuildTank(4.0f, 1.5f); break;
            case "Machine Gun Section": BuildCrew(3, bigGun: true); break;
            case "Lewis Gun Team": BuildCrew(2, bigGun: true); break;
            case "Flamethrower Team": BuildSoldiers(2, flame: true); break;
            default:
                int men = type.MaxHp.Raw > Fixed.FromInt(700).Raw ? 6 : type == UnitTypes.Engineers ? 4 : 5;
                BuildSoldiers(men, stormtrooper: german && type == UnitTypes.Stormtroopers);
                break;
        }

        BuildHpBar();
    }

    private StandardMaterial3D NewUniformMat()
    {
        var m = new StandardMaterial3D { AlbedoColor = _baseColor, Roughness = 0.9f };
        _uniformMaterials.Add(m);
        return m;
    }

    private static StandardMaterial3D FlatMat(Color c) =>
        new() { AlbedoColor = c, Roughness = 0.85f };

    /// <summary>One little man: body capsule, head sphere, rifle stick.</summary>
    private Node3D MakeSoldier(bool prone = false, bool flame = false, bool stormtrooper = false)
    {
        var soldier = new Node3D();
        var uniform = NewUniformMat();
        var skin = FlatMat(new Color(0.78f, 0.62f, 0.50f));
        var helmet = FlatMat(stormtrooper ? new Color(0.28f, 0.28f, 0.27f) : _baseColor * HelmetTint);

        var body = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.22f, Height = prone ? 0.8f : 1.05f },
            MaterialOverride = uniform,
            Position = new Vector3(0, prone ? 0.35f : 0.55f, 0),
        };
        soldier.AddChild(body);

        var head = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.17f, Height = 0.34f },
            MaterialOverride = skin,
            Position = new Vector3(0, prone ? 0.75f : 1.18f, 0),
        };
        soldier.AddChild(head);

        var helmetMesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.19f, Height = 0.24f },
            MaterialOverride = helmet,
            Position = new Vector3(0, prone ? 0.80f : 1.26f, 0),
        };
        soldier.AddChild(helmetMesh);

        if (!prone)
        {
            var rifle = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.85f) },
                MaterialOverride = FlatMat(WeaponColor),
                Position = new Vector3(0.16f, 0.85f, -0.25f),
                RotationDegrees = new Vector3(-8, 0, 0),
            };
            soldier.AddChild(rifle);

            if (flame)
            {
                var tank = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.14f, Height = 0.55f },
                    MaterialOverride = FlatMat(new Color(0.65f, 0.20f, 0.15f)),
                    Position = new Vector3(-0.22f, 0.85f, 0.08f),
                };
                soldier.AddChild(tank);
            }
        }

        AddChild(soldier);
        return soldier;
    }

    private void AddGun(Vector3 pos, Vector3 size)
    {
        var gun = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = FlatMat(WeaponColor),
            Position = pos,
        };
        AddChild(gun);
    }

    private void BuildSoldiers(int count, bool flame = false, bool stormtrooper = false)
    {
        for (int i = 0; i < count; i++)
        {
            int row = i / 3, col = i % 3;
            var s = MakeSoldier(flame: flame, stormtrooper: stormtrooper);
            s.Position = FormationSlot(i, count);
            _soldiers.Add(s);
        }
    }

    private void BuildCrew(int count, bool bigGun)
    {
        for (int i = 0; i < count; i++)
        {
            var s = MakeSoldier(prone: true);
            s.Position = FormationSlot(i, count) + new Vector3((i - 1) * 0.2f, 0, 0.5f);
            _soldiers.Add(s);
        }
        if (bigGun)
            AddGun(new Vector3(0, 0.42f, -0.55f), new Vector3(0.28f, 0.30f, 1.15f));
    }

    private void BuildTank(float length, float height)
    {
        var hullMat = NewUniformMat();

        var hull = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(length * 0.55f, height, length) },
            MaterialOverride = hullMat,
            Position = new Vector3(0, height * 0.65f, 0),
        };
        AddChild(hull);

        var tracks = FlatMat(new Color(0.18f, 0.17f, 0.15f));
        foreach (var sideX in new[] { -length * 0.36f, length * 0.36f })
        {
            var track = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(length * 0.16f, height * 0.55f, length * 0.95f) },
                MaterialOverride = tracks,
                Position = new Vector3(sideX, height * 0.32f, 0),
            };
            AddChild(track);
        }

        var turret = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = height * 0.38f, BottomRadius = height * 0.45f, Height = height * 0.55f },
            MaterialOverride = hullMat,
            Position = new Vector3(0, height * 1.2f, length * 0.08f),
        };
        AddChild(turret);

        var barrel = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.14f, length * 0.55f) },
            MaterialOverride = FlatMat(WeaponColor),
            Position = new Vector3(0, height * 1.2f, -length * 0.28f),
        };
        AddChild(barrel);
    }

    private static Vector3 FormationSlot(int index, int total)
    {
        int row = index / 3, col = index % 3;
        float spread = total <= 4 ? 0.75f : 0.95f;
        return new Vector3((col - 1) * spread - row * 0.35f, 0, row * spread);
    }

    private void BuildHpBar()
    {
        _hpBarMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.9f, 0.35f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        _hpBar = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.4f, 0.13f, 0.02f) },
            MaterialOverride = _hpBarMaterial,
            Position = new Vector3(0, 2.1f, 0),
        };
        AddChild(_hpBar);
    }

    public void SetSelected(bool selected)
    {
        if (_selectionRing is not null)
            _selectionRing.Visible = selected && !_corpse;
    }

    private MeshInstance3D? _selectionRing;

    // Selection ring is built lazily by Main via EnsureSelectionRing.
    public void EnsureSelectionRing()
    {
        if (_selectionRing is not null) return;
        _selectionRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 1.15f, OuterRadius = 1.35f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 1f, 0.7f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Position = new Vector3(0, 0.08f, 0),
            Scale = new Vector3(1, 0.12f, 1),
            Visible = false,
        };
        AddChild(_selectionRing);
    }

    /// <summary>alpha in [0,1] between the previous and current executed tick.</summary>
    public void Sync(World world, float alpha)
    {
        var u = world.Units[UnitId];

        if (!u.Alive)
        {
            if (!_corpse)
            {
                _corpse = true;
                if (_selectionRing is not null) _selectionRing.Visible = false;
                if (_hpBar is not null) _hpBar.Visible = false;

                // The formation falls.
                foreach (var s in _soldiers)
                {
                    s.RotationDegrees = new Vector3(-90, 0, 0);
                    s.Position += new Vector3(0, -0.15f, 0);
                    s.Scale = new Vector3(1, 1, 1);
                }
                foreach (var m in _uniformMaterials)
                    m.AlbedoColor = DeadTint;
            }
            return;
        }

        float a = Mathf.Clamp(alpha, 0f, 1f);
        Position = PrevPos.Lerp(CurPos, a);
        Rotation = new Vector3(0, Mathf.LerpAngle(PrevYaw, CurYaw, a), 0);

        // Suppression: soldiers crouch; pinned squads flatten and redden at the edges.
        float supFrac = Fixed.Clamp(u.Suppression / SimConfig.MaxSuppression, Fixed.Zero, Fixed.One).ToFloat01();
        float crouch = 1f - 0.38f * supFrac;
        foreach (var s in _soldiers)
            s.Scale = new Vector3(1, crouch, 1);

        var tinted = _baseColor;
        if (supFrac > 0.55f)
            tinted = tinted.Lerp(PinTint, (supFrac - 0.55f) * 1.2f);
        foreach (var m in _uniformMaterials)
            m.AlbedoColor = tinted;

        if (_hpBar is not null && _hpBarMaterial is not null)
        {
            float hpFrac = Fixed.Clamp(u.Hp / u.Type.MaxHp, Fixed.Zero, Fixed.One).ToFloat01();
            _hpBar.Scale = new Vector3(Mathf.Max(hpFrac, 0.03f), 1, 1);
            _hpBarMaterial.AlbedoColor = hpFrac > 0.5f
                ? new Color(0.35f, 0.9f, 0.35f)
                : hpFrac > 0.25f ? new Color(0.95f, 0.8f, 0.25f) : new Color(0.95f, 0.3f, 0.2f);
        }
    }

    public void Rebind(World world)
    {
        var u = world.Units[UnitId];
        var p = ToGodot(u.Pos);
        PrevPos = p;
        CurPos = p;
        Sync(world, 1f);
    }

    public static Vector3 ToGodot(Fixed2 sim) =>
        new((float)sim.X.Raw / Fixed.OneRaw, 0f, (float)sim.Y.Raw / Fixed.OneRaw);

    public static float YawOf(Fixed2 facing) =>
        Mathf.Atan2(facing.Y.Raw / (float)Fixed.OneRaw, facing.X.Raw / (float)Fixed.OneRaw);
}

file static class FixedViewExt
{
    public static float ToFloat01(this Fixed f) => f.Raw / (float)Fixed.OneRaw;
}
