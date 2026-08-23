using Godot;
using Sim;
using Side = Sim.Side;

namespace Heroes1918;

/// <summary>
/// Mouse-driven selection and orders for the local player's side.
/// Event-driven (not polled) so tests can synthesize input headlessly.
/// Left drag: box select. Left click: select single / clear. Shift: add.
/// Right click: attack-move everyone selected to the ground point.
/// </summary>
public partial class SelectionController : Node3D
{
    private const float BoxThresholdPx = 8f;
    private const float ClickForgivenessPx = 28f;

    private Main _main = null!;
    private Camera3D _camera = null!;
    private SelectionBox _box = null!;
    private readonly HashSet<int> _selected = new();

    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _dragCur;

    public int SelectedCount => _selected.Count;
    public bool IsSelected(int id) => _selected.Contains(id);

    public void Init(Main main)
    {
        _main = main;
        _camera = GetViewport().GetCamera3D();

        var layer = new CanvasLayer { Layer = 5 };
        _box = new SelectionBox { Visible = false };
        layer.AddChild(_box);
        AddChild(layer);
    }

    public void ClearSelection()
    {
        foreach (int id in _selected)
            if (id < _main.World.Units.Count)
                _main.UnitViews[id].SetSelected(false);
        _selected.Clear();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_main.World.Match.Finished)
            return;

        switch (@event)
        {
            case InputEventMouseButton mb:
                HandleMouseButton(mb);
                break;
            case InputEventMouseMotion motion when _dragging:
                _dragCur = motion.Position;
                if ((_dragCur - _dragStart).Length() > BoxThresholdPx)
                {
                    var topLeft = new Vector2(
                        Mathf.Min(_dragStart.X, _dragCur.X),
                        Mathf.Min(_dragStart.Y, _dragCur.Y));
                    _box.Visible = true;
                    _box.SetRect(new Rect2(topLeft, (_dragCur - _dragStart).Abs()));
                }
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                _main.DisarmBarrage();
                _main.DisarmGas();
                ClearSelection();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.B }:
                _main.ToggleBarrageMode();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.G }:
                _main.ToggleGasMode();
                break;
            case InputEventKey { Pressed: true, Keycode: var k }
                when k is Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4 or Key.Key5 or Key.Key6:
                _main.TryRequisitionHotkey((int)(k - Key.Key1));
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        switch (mb.ButtonIndex)
        {
            case MouseButton.Left when mb.Pressed && (_main.BarrageArmed || _main.GasArmed):
                // Barrage targeting consumes left clicks while armed.
                var target = GroundPointAt(mb.Position);
                if (target is not null)
                    _main.HandleBarrageClick(target.Value);
                break;

            case MouseButton.Left when mb.Pressed:
                _dragging = true;
                _dragStart = mb.Position;
                _dragCur = mb.Position;
                break;

            case MouseButton.Left when !mb.Pressed && _dragging:
                _dragging = false;
                _box.Visible = false;

                if (!Input.IsKeyPressed(Key.Shift))
                    ClearSelection();

                if ((mb.Position - _dragStart).Length() > BoxThresholdPx)
                    BoxSelect(_dragStart, mb.Position);
                else
                    ClickSelect(mb.Position);

                RefreshRings();
                break;

            case MouseButton.Right when mb.Pressed:
                IssueAttackMove(mb.Position);
                break;
        }
    }

    private void BoxSelect(Vector2 start, Vector2 end)
    {
        var min = new Vector2(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
        var max = new Vector2(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y));

        var units = _main.World.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.Side != _main.MySide)
                continue;
            var screen = _camera.UnprojectPosition(UnitView.ToGodot(u.Pos));
            if (screen.X >= min.X && screen.X <= max.X && screen.Y >= min.Y && screen.Y <= max.Y)
                _selected.Add(i);
        }
    }

    private void ClickSelect(Vector2 mouse)
    {
        int best = -1;
        float bestDist = ClickForgivenessPx;

        var units = _main.World.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.Side != _main.MySide)
                continue;
            float d = (_camera.UnprojectPosition(UnitView.ToGodot(u.Pos)) - mouse).Length();
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        if (best >= 0)
        {
            if (Input.IsKeyPressed(Key.Shift) && _selected.Contains(best))
                _selected.Remove(best);
            else
                _selected.Add(best);
        }
    }

    private void IssueAttackMove(Vector2 screenPos)
    {
        if (_selected.Count == 0)
            return;

        var ground = GroundPointAt(screenPos);
        if (ground is null)
            return;

        _main.SpawnOrderMarker(ground.Value);

        var world = _main.World;
        foreach (int id in _selected)
        {
            var u = world.Units[id];
            if (!u.Alive || u.Side != _main.MySide)
                continue;
            _main.IssueOrder(new Command(id, CommandType.AttackMove, ground.Value));
        }
    }

    /// <summary>Intersect a screen point's camera ray with the ground plane; returns sim coordinates.</summary>
    public Fixed2? GroundPointAt(Vector2 screen)
    {
        var origin = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);

        // Plane y=0: t = -origin.y / dir.y
        if (Mathf.Abs(dir.Y) < 1e-5f)
            return null;
        float t = -origin.Y / dir.Y;
        if (t < 0)
            return null;

        var hit = origin + dir * t;
        return new Fixed2(
            Fixed.FromRatio((long)(hit.X * 1000), 1000),
            Fixed.FromRatio((long)(hit.Z * 1000), 1000));
    }

    private void RefreshRings()
    {
        var units = _main.World.Units;
        for (int i = 0; i < units.Count; i++)
        {
            bool sel = _selected.Contains(i) && units[i].Alive && units[i].Side == _main.MySide;
            _main.UnitViews[i].SetSelected(sel);
        }
    }
}
