using Godot;
using Lockstep;
using Sim;

using Side = Sim.Side;

namespace Heroes1918;

/// <summary>
/// Orchestrates everything: main menu, the three ways to start a match (solo vs AI,
/// host co-op, join co-op over LAN/Tailscale TCP), fixed-timestep pacing, view
/// syncing, and the headless smoke mode used by agents and CI.
/// </summary>
public partial class Main : Node3D
{
    private const string MapPath = "res://resources/maps/first_skirmish.json";
    private const int DefaultPort = 19180;

    private enum AppMode { Menu, HostWaiting, Joining, Match }

    private AppMode _mode = AppMode.Menu;

    // --- match state ---
    private World _world = null!;
    private LockstepSession? _session;      // null in solo mode
    private TcpTransport? _tcp;
    private RudimentaryAi? _centralAi;      // solo + host only
    private RudimentaryAi? _alliesAi;       // test modes: stands in for the human
    private Side _mySide = Side.Allies;
    private int _lastAiTick;
    private double _accumulator;
    private float _alpha;
    private bool _soloMode;

    // --- scene wiring ---
    private RtsCamera _camera = null!;
    private SelectionController _selection = null!;
    private Hud _hud = null!;
    private List<CapturePointView> _pointViews = new();

    // --- barrage / gas targeting ---
    public bool BarrageArmed { get; private set; }
    public bool GasArmed { get; private set; }
    private Fixed2? _barrageStart;
    private MeshInstance3D? _barrageMarker;
    private MeshInstance3D? _gasMarker;
    private readonly Dictionary<int, MeshInstance3D> _cloudMeshes = new();

    private readonly List<(MeshInstance3D Node, float Ttl)> _flashes = new();
    private int _seenDynamicCover;
    private readonly List<MeshInstance3D> _blockerMeshes = new();

    // --- headless test modes (--selfplay, --inputtest) ---
    private string? _testMode;
    private int _frame;
    private bool _testSettled;
    private int _itPhase;
    private int _itPhaseFrame;
    private Fixed2 _itExpectedGoal;
    private Fixed2 _itStartPos;

    // --- menu widgets ---
    private CanvasLayer _menuLayer = null!;
    private LineEdit _portEdit = null!;
    private LineEdit _ipEdit = null!;
    private Label _menuStatus = null!;

    public World World => _session?.World ?? _world;
    public Side MySide => _mySide;
    public Dictionary<int, UnitView> UnitViews { get; private set; } = new();

    private const float TickSeconds = 1f / 30f;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        foreach (var a in args)
        {
            switch (a)
            {
                case "--smoke":
                    RunSmoke();
                    return;
                case "--selfplay":
                    _testMode = "selfplay";
                    break;
                case "--inputtest":
                    _testMode = "inputtest";
                    break;
            }
        }

        if (_testMode is not null)
        {
            StartMatch(networked: false, hosting: false);
            return;
        }
        BuildMenu();
    }

    // ------------------------------------------------------------------ menu

    private void BuildMenu()
    {
        _menuLayer = new CanvasLayer();
        AddChild(_menuLayer);

        var center = new CenterContainer
        {
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0, AnchorBottom = 1,
        };
        _menuLayer.AddChild(center);

        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        center.AddChild(panel);

        var title = new Label
        {
            Text = "1918 HEROES",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 52);
        panel.AddChild(title);

        var subtitle = new Label
        {
            Text = "Western Front · first playable",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1, 1, 1, 0.6f),
        };
        subtitle.AddThemeFontSizeOverride("font_size", 16);
        panel.AddChild(subtitle);

        panel.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        panel.AddChild(MenuButton("Solo vs AI", () => StartMatch(networked: false, hosting: false)));
        panel.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var portRow = new HBoxContainer();
        portRow.AddChild(new Label { Text = "Port ", Modulate = new Color(1, 1, 1, 0.7f) });
        _portEdit = new LineEdit { Text = DefaultPort.ToString(), CustomMinimumSize = new Vector2(120, 0) };
        portRow.AddChild(_portEdit);
        panel.AddChild(portRow);

        panel.AddChild(MenuButton("Host co-op (you are Allies)", DoHost));
        panel.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        var ipRow = new HBoxContainer();
        ipRow.AddChild(new Label { Text = "Host IP ", Modulate = new Color(1, 1, 1, 0.7f) });
        _ipEdit = new LineEdit { Text = "127.0.0.1", CustomMinimumSize = new Vector2(220, 0) };
        ipRow.AddChild(_ipEdit);
        panel.AddChild(ipRow);

        panel.AddChild(MenuButton("Join co-op (you are Central)", DoJoin));

        _menuStatus = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1f, 0.9f, 0.5f),
        };
        panel.AddChild(_menuStatus);
    }

    private Button MenuButton(string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += onPressed;
        return b;
    }

    private int ReadPort()
    {
        if (!int.TryParse(_portEdit.Text.Trim(), out int port) || port is < 1024 or > 65535)
            return DefaultPort;
        return port;
    }

    private void DoHost()
    {
        int port = ReadPort();
        try
        {
            _tcp = TcpTransport.Listen(port);
        }
        catch (Exception e)
        {
            _menuStatus.Text = $"cannot listen on {port}: {e.Message}";
            return;
        }
        _mode = AppMode.HostWaiting;
        _menuStatus.Text = $"waiting for your son on port {port}...";
    }

    private void DoJoin()
    {
        string ip = _ipEdit.Text.Trim();
        if (ip.Length == 0)
        {
            _menuStatus.Text = "enter the host's IP (or Tailscale name)";
            return;
        }
        _tcp = TcpTransport.Connect(ip, ReadPort());
        _mode = AppMode.Joining;
        _menuStatus.Text = $"connecting to {ip}...";
    }

    public override void _Process(double delta)
    {
        switch (_mode)
        {
            case AppMode.HostWaiting:
                _tcp!.Pump();
                if (_tcp.Connected) StartMatch(networked: true, hosting: true);
                else if (_tcp.Failed) ReturnToMenu($"listen failed: {_tcp.LastError}");
                break;

            case AppMode.Joining:
                _tcp!.Pump();
                if (_tcp.Connected) StartMatch(networked: true, hosting: false);
                else if (_tcp.Failed) ReturnToMenu($"connect failed: {_tcp.LastError}");
                break;

            case AppMode.Match:
                TickMatch(delta);
                if (_testMode is not null)
                    RunTestChecks();
                break;
        }
    }

    /// <summary>Faster drain so automated matches actually reach a verdict.</summary>
    internal static MatchOptions TestOptions() => new()
    {
        StartingTickets = 120,
        TicketDrainPerVpPerSecond = Fixed.FromInt(2),
    };

    private void ReturnToMenu(string reason)
    {
        _mode = AppMode.Menu;
        _tcp?.Dispose();
        _tcp = null;
        _menuStatus.Text = reason;
    }

    // ------------------------------------------------------------------ match

    private void StartMatch(bool networked, bool hosting, MatchOptions? options = null)
    {
        if (_menuLayer is not null)
        {
            _menuLayer.QueueFree();
            _menuLayer = null;
        }

        ulong seed = hosting || !networked ? RandomSeed() : 0; // joiner adopts host seed anyway

        var map = JsonMapLoader.Load(MapPath);
        _world = new World(map, seed, options ?? TestOptions());

        _mySide = networked && !hosting ? Side.Central : Side.Allies;
        _soloMode = !networked;

        if (!networked)
        {
            _centralAi = new RudimentaryAi(Side.Central);
            if (_testMode == "selfplay")
                _alliesAi = new RudimentaryAi(Side.Allies); // inputtest stays AI-free so orders aren't overwritten
        }
        else
        {
            _session = new LockstepSession(_world, _tcp!, inputDelayTicks: 4);
            if (!hosting)
            {
                _session.AdoptPeerSeed = true;
                _world = _session.World; // will be replaced again during handshake
            }
            _session.WorldReplaced += OnWorldReplaced;
            _session.Start();
            _centralAi = hosting ? new RudimentaryAi(Side.Central) : null;
        }

        BuildScene(map);
        RebuildViews();

        _mode = AppMode.Match;
    }

    private static ulong RandomSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        new Random().NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private void BuildScene(MapDef map)
    {
        float w = (float)map.Width.Raw / Fixed.OneRaw;
        float h = (float)map.Height.Raw / Fixed.OneRaw;

        // Ground.
        var groundMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.30f, 0.33f, 0.22f),
            Roughness = 1f,
        };
        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(w + 40, h + 40) },
            MaterialOverride = groundMat,
            Position = new Vector3(w / 2, -0.02f, h / 2),
        };
        AddChild(ground);

        // Sight blockers as crude buildings.
        var buildingMat = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.38f, 0.32f) };
        foreach (var ob in map.SightBlockers)
        {
            float r = (float)ob.Radius.Raw / Fixed.OneRaw;
            var mesh = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = r * 0.8f, BottomRadius = r, Height = 4f },
                MaterialOverride = buildingMat,
                Position = UnitView.ToGodot(ob.Pos) with { Y = 2f },
            };
            AddChild(mesh);
        }

        // Cover as dark patches.
        var craterMat = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.15f, 0.12f), Roughness = 1f };
        foreach (var c in map.Cover)
        {
            float r = (float)c.Radius.Raw / Fixed.OneRaw;
            var patch = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = r * 0.95f, BottomRadius = r * 0.95f, Height = 0.06f },
                MaterialOverride = craterMat,
                Position = UnitView.ToGodot(c.Pos) with { Y = 0.02f },
            };
            AddChild(patch);
        }

        _camera = new RtsCamera();
        AddChild(_camera);
        _camera.Setup(w, h);

        foreach (var p in _world.Points)
        {
            var view = new CapturePointView();
            AddChild(view);
            view.Init(p.Radius);
            view.Position = UnitView.ToGodot(p.Pos);
            view.Sync(p);
            _pointViews.Add(view);
        }

        _hud = new Hud();
        AddChild(_hud);

        _selection = new SelectionController();
        AddChild(_selection);
        _selection.Init(this);
    }

    private void RebuildViews()
    {
        foreach (var kv in UnitViews)
            kv.Value.QueueFree();
        UnitViews.Clear();

        for (int i = 0; i < _world.Units.Count; i++)
        {
            var u = _world.Units[i];
            var view = new UnitView();
            AddChild(view);
            view.UnitId = i;
            view.Init(u.Side);
            view.Rebind(_world);
            UnitViews[i] = view;
        }
    }

    private void OnWorldReplaced(World newWorld)
    {
        _world = newWorld;
        RebuildViews(); // identical ids: same map spawns
    }

    // ------------------------------------------------------------------ barrage targeting

    public void ToggleBarrageMode()
    {
        if (BarrageArmed) DisarmBarrage();
        else { DisarmGas(); BarrageArmed = true; }
    }

    public void ToggleGasMode()
    {
        if (GasArmed) DisarmGas();
        else { DisarmBarrage(); GasArmed = true; }
    }

    public void DisarmBarrage()
    {
        BarrageArmed = false;
        _barrageStart = null;
        if (_barrageMarker is not null)
            _barrageMarker.Visible = false;
    }

    public void DisarmGas()
    {
        GasArmed = false;
        if (_gasMarker is not null)
            _gasMarker.Visible = false;
    }

    public void HandleBarrageClick(Fixed2 ground)
    {
        if (GasArmed)
        {
            int caller = FirstAliveOwnedUnit();
            if (caller >= 0)
                IssueOrder(new Command(caller, CommandType.Gas, ground, default));
            DisarmGas();
            return;
        }

        if (!BarrageArmed) return;

        if (_barrageStart is null)
        {
            _barrageStart = ground;
            EnsureBarrageMarker();
            _barrageMarker!.Position = UnitView.ToGodot(ground) with { Y = 0.15f };
            _barrageMarker.Visible = true;
            return;
        }

        int caller2 = FirstAliveOwnedUnit();
        if (caller2 >= 0)
            IssueOrder(new Command(caller2, CommandType.Barrage, _barrageStart.Value, ground));
        DisarmBarrage();
    }

    private int FirstAliveOwnedUnit()
    {
        var units = World.Units;
        for (int i = 0; i < units.Count; i++)
            if (units[i].Alive && units[i].Side == MySide)
                return i;
        return -1;
    }

    private void EnsureBarrageMarker()
    {
        if (_barrageMarker is not null) return;
        _barrageMarker = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.8f, OuterRadius = 1.1f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.85f, 0.3f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Scale = new Vector3(1, 0.15f, 1),
            Visible = false,
        };
        AddChild(_barrageMarker);
    }

    private void EnsureGasMarker()
    {
        if (_gasMarker is not null) return;
        _gasMarker = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.8f, OuterRadius = 1.1f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.5f, 0.9f, 0.4f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Scale = new Vector3(1, 0.15f, 1),
            Visible = false,
        };
        AddChild(_gasMarker);
    }

    private void SyncGasClouds(World world)
    {
        if (GasArmed)
        {
            EnsureGasMarker();
            _gasMarker!.Visible = true;
        }
        else if (_gasMarker is not null)
        {
            _gasMarker.Visible = false;
        }

        // Reconcile drifting clouds by id.
        for (int i = 0; i < world.Clouds.Count; i++)
        {
            var cloud = world.Clouds[i];
            if (!_cloudMeshes.TryGetValue(cloud.Id, out var mesh))
            {
                float r = cloud.Radius.Raw / Fixed.OneRaw;
                mesh = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r * 1.15f, Height = 2.6f },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(0.55f, 0.85f, 0.35f, 0.35f),
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    },
                    Position = UnitView.ToGodot(cloud.Pos) with { Y = 1.3f },
                };
                AddChild(mesh);
                _cloudMeshes[cloud.Id] = mesh;
            }

            mesh.Position = UnitView.ToGodot(cloud.Pos) with { Y = 1.3f };
        }

        // Expired clouds: drop meshes whose ids vanished.
        List<int>? dead = null;
        foreach (var id in _cloudMeshes.Keys)
            if (!world.Clouds.Any(c => c.Id == id))
                (dead ??= new List<int>()).Add(id);
        if (dead is not null)
            foreach (int id in dead)
            {
                _cloudMeshes[id].QueueFree();
                _cloudMeshes.Remove(id);
            }
    }

    // ------------------------------------------------------------------ battlefield FX

    private void SyncBattlefield(World world)
    {
        // Shell flashes.
        foreach (var pos in world.Explosions)
            SpawnFlash(pos);

        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var (node, ttl) = _flashes[i];
            ttl -= 1f / 60f;
            if (ttl <= 0)
            {
                node.QueueFree();
                _flashes.RemoveAt(i);
            }
            else
            {
                float growth = 1f + (0.35f - ttl) * 5f;
                node.Scale = new Vector3(growth, growth * 0.6f, growth);
                node.MaterialOverride!.Set("albedo_color",
                    new Color(1f, 0.7f - 0.4f * (0.35f - ttl), 0.25f));
            }
        }

        // New craters / trenches / rubble.
        var cover = world.DynamicCover;
        while (_seenDynamicCover < cover.Count)
        {
            AddChild(MakeCoverPatch(cover[_seenDynamicCover]));
            _seenDynamicCover++;
        }

        // Buildings destroyed -> rebuild blocker meshes.
        if (_blockerMeshes.Count != world.Blockers.Count)
        {
            foreach (var m in _blockerMeshes)
                m.QueueFree();
            _blockerMeshes.Clear();

            var buildingMat = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.38f, 0.32f) };
            foreach (var ob in world.Blockers)
            {
                float r = ob.Radius.Raw / Fixed.OneRaw;
                var mesh = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = r * 0.8f, BottomRadius = r, Height = 4f },
                    MaterialOverride = buildingMat,
                    Position = UnitView.ToGodot(ob.Pos) with { Y = 2f },
                };
                AddChild(mesh);
                _blockerMeshes.Add(mesh);
            }
        }
    }

    private static Node3D MakeCoverPatch(in CoverObject c)
    {
        float r = c.Radius.Raw / Fixed.OneRaw;
        Color color = c.Kind switch
        {
            CoverKind.Crater => new Color(0.16f, 0.13f, 0.10f),
            CoverKind.Trench => new Color(0.24f, 0.27f, 0.16f),
            _ => new Color(0.40f, 0.36f, 0.31f),
        };
        return new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.06f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f },
            Position = UnitView.ToGodot(c.Pos) with { Y = 0.03f },
        };
    }

    private void SpawnFlash(Fixed2 simPos)
    {
        var node = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1.1f, Height = 2.2f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.75f, 0.3f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
            Position = UnitView.ToGodot(simPos) with { Y = 0.6f },
        };
        AddChild(node);
        _flashes.Add((node, 0.35f));
    }

    /// <summary>Called by the selection controller when the local player issues an order.</summary>
    public void IssueOrder(Command command)
    {
        if (_session is not null)
            _session.EnqueueLocal(new[] { command });
        else
            _pendingLocal.Add(command);
    }

    private readonly List<Command> _pendingLocal = new();

    private string BarrageStatusText(World world)
    {
        int next = world.Match.NextBarrageTick(MySide);
        if (world.Tick >= next) return "READY";
        return $"{(int)MathF.Ceiling((next - world.Tick) / 30f)}s";
    }

    private string GasStatusText(World world)
    {
        int next = world.Match.NextGasTick(MySide);
        if (world.Tick >= next) return "READY";
        return $"{(int)MathF.Ceiling((next - world.Tick) / 30f)}s";
    }

    private void TickMatch(double delta)
    {
        bool worldChanged = false;

        if (_session is not null)
        {
            _tcp!.Pump();
            int before = _session.ExecutedTick;
            _session.Update();
            worldChanged = _session.ExecutedTick != before;

            // The AI (host/solo side) issues orders once per executed tick.
            if (_centralAi is not null && _session.ExecutedTick > _lastAiTick && !_session.World.Match.Finished)
            {
                _lastAiTick = _session.ExecutedTick;
                var cmds = _centralAi.Think(_session.World);
                if (cmds.Count > 0)
                    _session.EnqueueLocal(cmds);
            }

            if (_session.Status == SessionStatus.Desynced)
            {
                _hud.SetHint($"DESYNC at tick {_session.Desync?.Tick} — this is a bug, report it!");
            }
            else if (_session.Status == SessionStatus.Faulted)
            {
                _hud.SetHint($"connection lost: {_session.FaultReason}");
            }
        }
        else
        {
            // Test modes fast-forward: same tick pipeline, several ticks per frame.
            float scaledDelta = _testMode is null ? (float)delta : (float)delta * 8f;
            _accumulator += scaledDelta;
            while (_accumulator >= TickSeconds && !_world.Match.Finished)
            {
                _accumulator -= TickSeconds;

                var commands = new List<Command>(_pendingLocal);
                _pendingLocal.Clear();
                if (!_world.Match.Finished)
                {
                    if (_alliesAi is not null)
                        commands.AddRange(_alliesAi.Think(_world));
                    commands.AddRange(_centralAi!.Think(_world));
                }
                _world.Step(commands);
                worldChanged = true;
            }
            _alpha = Mathf.Clamp((float)(_accumulator / TickSeconds), 0f, 1f);
        }

        if (worldChanged)
        {
            foreach (var kv in UnitViews)
            {
                var u = _world.Units[kv.Key];
                kv.Value.PrevPos = kv.Value.CurPos;
                kv.Value.CurPos = UnitView.ToGodot(u.Pos);
                kv.Value.PrevYaw = kv.Value.CurYaw;
                kv.Value.CurYaw = UnitView.YawOf(u.Facing);
            }
            _alpha = 0f;
        }
        else if (_session is not null)
        {
            // Session mode has no accumulator; approximate alpha from frame timing.
            _alpha = Mathf.Clamp(_alpha + (float)(delta / TickSeconds), 0f, 1f);
        }

        foreach (var kv in UnitViews)
            kv.Value.Sync(_world, _alpha);

        for (int i = 0; i < _pointViews.Count && i < _world.Points.Count; i++)
            _pointViews[i].Sync(_world.Points[i]);

        SyncBattlefield(World);
        SyncGasClouds(World);

        string barrageStatus = BarrageStatusText(World);
        string gasStatus = GasStatusText(World);
        string hintPrefix = BarrageArmed ? "BARRAGE: click start then end · "
                          : GasArmed ? "GAS: click drop point · " : "";
        _hud.SetHint($"{hintPrefix}LMB select · RMB attack-move · B barrage [{barrageStatus}] · G gas [{gasStatus}] · WASD pan · wheel zoom");

        _hud.Sync(_world, _mySide);

        if (_world.Match.Finished && Input.IsKeyPressed(Key.R))
            GetTree().ReloadCurrentScene();
    }

    // ------------------------------------------------------------------ headless tests

    private void Fail(string mode, string why)
    {
        GD.Print($"{mode.ToUpper()} FAIL: {why}");
        if (_testSettled) return;
        _testSettled = true;
        GetTree().Quit(1);
    }

    private void Pass(string mode, string detail)
    {
        GD.Print($"{mode.ToUpper()} OK: {detail}");
        if (_testSettled) return;
        _testSettled = true;
        GetTree().Quit(0);
    }

    /// <summary>
    /// --selfplay: the real game scene, real frame loop, real views and HUD —
    /// both sides driven by AI. Verifies scene wiring, view tracking, and that
    /// a match reaches a verdict with the banner showing.
    /// </summary>
    private void RunSelfPlayChecks()
    {
        _frame++;

        if (_frame == 1)
        {
            if (UnitViews.Count != _world.Units.Count)
                Fail("selfplay", $"views {UnitViews.Count} != units {_world.Units.Count}");
            else if (_pointViews.Count != _world.Points.Count)
                Fail("selfplay", $"point views {_pointViews.Count} != points {_world.Points.Count}");
            return;
        }

        // Sample view sync every few seconds.
        if (_frame % 180 == 0 && !_testSettled)
        {
            foreach (var kv in UnitViews)
            {
                var u = _world.Units[kv.Key];
                if (!u.Alive) continue;
                float err = kv.Value.CurPos.DistanceTo(UnitView.ToGodot(u.Pos));
                if (err > 0.5f)
                {
                    Fail("selfplay", $"view {kv.Key} drifted {err:0.###} m from sim position");
                    return;
                }
                break;
            }
        }

        if (_world.Match.Finished && !_testSettled)
        {
            if (_world.Match.Winner is not (Side.Allies or Side.Central))
            {
                Fail("selfplay", $"bad winner {_world.Match.Winner}");
                return;
            }
            string banner = _hud.BannerText;
            if (banner is not ("VICTORY" or "DEFEAT"))
            {
                Fail("selfplay", $"banner shows '{banner}' after finish");
                return;
            }
            Pass("selfplay",
                $"winner={_world.Match.Winner} tick={_world.Tick} " +
                $"casualties={_world.Units.Count(u => !u.Alive)}");
        }
        else if (_world.Tick > 7200 && !_testSettled)
        {
            Fail("selfplay", $"no verdict within 7200 ticks (tickets {_world.Match.TicketsAllies}/{_world.Match.TicketsCentral})");
        }
    }

    /// <summary>
    /// --inputtest: synthesizes mouse events through the real input pipeline.
    /// Click-selects an allied squad, right-clicks the center victory point,
    /// then verifies the order landed in the sim and the squad marches.
    /// </summary>
    private void RunInputTestChecks()
    {
        _frame++;

        const int setupFrames = 10;
        if (_itPhase == 0 && _frame >= setupFrames)
        {
            var u = _world.Units[0];
            if (!u.Alive) { Fail("inputtest", "unit 0 died before the test started"); return; }

            var screen = _camera.UnprojectPosition(UnitView.ToGodot(u.Pos));
            PushClick(MouseButton.Left, screen);
            _itPhase = 1;
            _itPhaseFrame = _frame;
            return;
        }

        if (_itPhase == 1 && _frame >= _itPhaseFrame + 3)
        {
            if (!_selection.IsSelected(0))
            {
                Fail("inputtest", "click on squad did not select it");
                return;
            }

            // Right-click the center victory point.
            var vp = _world.Points[0].Pos;
            _itExpectedGoal = vp;
            var vpScreen = _camera.UnprojectPosition(UnitView.ToGodot(vp));
            PushClick(MouseButton.Right, vpScreen);
            _itPhase = 2;
            _itPhaseFrame = _frame;
            return;
        }

        if (_itPhase == 2 && _frame >= _itPhaseFrame + 6)
        {
            var u = _world.Units[0];
            if (u.Order != OrderKind.AttackMove)
            {
                Fail("inputtest", $"order not received (order={u.Order})");
                return;
            }
            float goalErr = UnitView.ToGodot(u.Goal).DistanceTo(UnitView.ToGodot(_itExpectedGoal));
            if (goalErr > 1.5f)
            {
                Fail("inputtest", $"goal {u.Goal} not near ordered point (err {goalErr:0.##} m)");
                return;
            }
            _itStartPos = u.Pos;
            _itPhase = 3;
            _itPhaseFrame = _frame;
            return;
        }

        if (_itPhase == 3 && _frame >= _itPhaseFrame + 90)
        {
            var u = _world.Units[0];
            float moved = UnitView.ToGodot(u.Pos).DistanceTo(UnitView.ToGodot(_itStartPos));
            if (moved < 1f)
            {
                Fail("inputtest", $"squad did not march (moved {moved:0.##} m in 3 s)");
                return;
            }
            Pass("inputtest", $"selected, ordered, marched {moved:0.#} m");
        }
    }

    private void PushClick(MouseButton button, Vector2 pos)
    {
        var press = new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = true,
            Position = pos,
            GlobalPosition = pos,
        };
        GetViewport().PushInput(press);

        var release = new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = false,
            Position = pos,
            GlobalPosition = pos,
        };
        GetViewport().PushInput(release);
    }

    private void RunTestChecks()
    {
        if (_testSettled) return;
        switch (_testMode)
        {
            case "selfplay": RunSelfPlayChecks(); break;
            case "inputtest": RunInputTestChecks(); break;
        }
    }

    // ------------------------------------------------------------------ smoke

    /// <summary>
    /// Headless end-to-end check: real map file, real world, real AIs, no rendering.
    /// godot --headless --path game -- --smoke
    /// </summary>
    private void RunSmoke()
    {
        var map = JsonMapLoader.Load(MapPath);
        var world = new World(map, seed: 20260822);
        var allies = new RudimentaryAi(Side.Allies);
        var central = new RudimentaryAi(Side.Central);

        int shots = 0;
        while (!world.Match.Finished && world.Tick < 7200)
        {
            var commands = new List<Command>();
            commands.AddRange(allies.Think(world));
            commands.AddRange(central.Think(world));
            world.Step(commands);
            shots += world.Events.Count;
        }

        int casualties = world.Units.Count(u => !u.Alive);
        GD.Print($"SMOKE OK: ticks={world.Tick} shots={shots} casualties={casualties} " +
                 $"ticketsA={world.Match.TicketsAllies} ticketsC={world.Match.TicketsCentral} " +
                 $"finished={world.Match.Finished} winner={world.Match.Winner}");
        GetTree().Quit(0);
    }
}
