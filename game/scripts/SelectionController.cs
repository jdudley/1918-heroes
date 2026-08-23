using Godot;
using Sim;

using Side = Sim.Side;

namespace Heroes1918;

/// <summary>
/// Mouse-driven selection and orders for the local player's side.
/// Left drag: box select. Left click: select single / clear. Shift: add.
/// Right click: attack-move everyone selected to the ground point.
/// </summary>
public partial class SelectionController : Node3D
{
    private Main _main = null!;
    private Camera3D _camera = null!;
    private SelectionBox _box = null!;
    private readonly HashSet<int> _selected = new();

    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _dragCur;

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

    public override void _Process(double delta)
    {
        var world = _main.World;
        if (world.Match.Finished)
            return;

        bool lmb = Input.IsMouseButtonPressed(MouseButton.Left);
        bool rmbJust = Input.IsMouseButtonPressed(MouseButton.Right) && !_prevRmb;
        bool lmbJust = lmb && !_prevLmb;
        bool lmbReleased = !lmb && _prevLmb;
        _prevLmb = lmb;
        _prevRmb = Input.IsMouseButtonPressed(MouseButton.Right);

        if (lmbJust)
        {
            _dragging = true;
            _dragStart = GetViewport().GetMousePosition();
            _dragCur = _dragStart;
        }

        if (_dragging && lmb)
        {
            _dragCur = GetViewport().GetMousePosition();
            if ((_dragCur - _dragStart).Length() > 8f)
            {
                _box.Visible = true;
                var topLeft = new Vector2(Mathf.Min(_dragStart.X, _dragCur.X), Mathf.Min(_dragStart.Y, _dragCur.Y));
                var size = (_dragCur - _dragStart).Abs();
                _box.SetRect(new Rect2(topLeft, size));
            }
        }

        if (lmbReleased)
        {
            _dragging = false;
            _box.Visible = false;
            bool additive = Input.IsKeyPressed(Key.Shift);
            if (!additive) ClearSelection();

            if ((_dragCur - _dragStart).Length() > 8f)
                BoxSelect(world);
            else
                ClickSelect(world);

            RefreshRings();
        }

        if (rmbJust)
            IssueAttackMove(world);

        if (Input.IsKeyPressed(Key.Escape))
            ClearSelection();
    }

    private bool _prevLmb;
    private bool _prevRmb;

    private void BoxSelect(World world)
    {
        var min = new Vector2(Mathf.Min(_dragStart.X, _dragCur.X), Mathf.Min(_dragStart.Y, _dragCur.Y));
        var max = new Vector2(Mathf.Max(_dragStart.X, _dragCur.X), Mathf.Max(_dragStart.Y, _dragCur.Y));

        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.Side != _main.MySide) continue;
            var screen = _camera.UnprojectPosition(UnitView.ToGodot(u.Pos));
            if (screen.X >= min.X && screen.X <= max.X && screen.Y >= min.Y && screen.Y <= max.Y)
                _selected.Add(i);
        }
    }

    private void ClickSelect(World world)
    {
        var mouse = GetViewport().GetMousePosition();
        int best = -1;
        float bestDist = 28f; // pixels of forgiveness

        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.Side != _main.MySide) continue;
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

    private void IssueAttackMove(World world)
    {
        if (_selected.Count == 0)
            return;

        var ground = GroundPointUnderMouse();
        if (ground is null)
            return;

        foreach (int id in _selected)
        {
            var u = world.Units[id];
            if (!u.Alive || u.Side != _main.MySide)
                continue;
            _main.IssueOrder(new Command(id, CommandType.AttackMove, ground.Value));
        }
    }

    /// <summary>Intersect the mouse ray with the ground plane; returns sim coordinates.</summary>
    private Fixed2? GroundPointUnderMouse()
    {
        var mouse = GetViewport().GetMousePosition();
        var origin = _camera.ProjectRayOrigin(mouse);
        var dir = _camera.ProjectRayNormal(mouse);

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
        var world = _main.World;
        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            bool sel = _selected.Contains(i) && units[i].Alive && units[i].Side == _main.MySide;
            _main.UnitViews[i].SetSelected(sel);
        }
    }
}
