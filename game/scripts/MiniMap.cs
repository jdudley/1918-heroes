using Godot;
using Sim;
using Side = Sim.Side;

namespace Heroes1918;

/// <summary>
/// Bottom-left tactical map drawn from sim state every frame: capture points by
/// owner, living squads per side, and the camera's ground footprint. Click or
/// drag to jump the camera.
/// </summary>
public partial class MiniMap : Control
{
    private const float PanelPad = 6f;

    public World? World;
    /// <summary>Screen corners projected onto the ground, for the view polygon.</summary>
    public Func<Vector2[]?>? ViewFootprint;
    public Action<float, float>? JumpRequested;      // world x,z

    private Vector2 _mapSize = new(112, 72);
    public void SetMapSize(float w, float h) => _mapSize = new Vector2(w, h);

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(210, 210 * (_mapSize.Y / _mapSize.X));
        MouseFilter = MouseFilterEnum.Stop;
    }

    private Vector2 SimToPx(Fixed2 sim) => new(
        sim.X.Raw / (float)Fixed.OneRaw / _mapSize.X * Size.X,
        sim.Y.Raw / (float)Fixed.OneRaw / _mapSize.Y * Size.Y);

    public override void _Draw()
    {
        var bg = new Color(0.10f, 0.11f, 0.08f, 0.92f);
        DrawRect(new Rect2(Vector2.Zero, Size), bg);

        if (World is null)
            return;

        // Capture points.
        foreach (var p in World.Points)
        {
            var pos = SimToPx(p.Pos);
            var color = p.Owner switch
            {
                Side.Allies => new Color(0.55f, 0.70f, 0.40f),
                Side.Central => new Color(0.50f, 0.58f, 0.75f),
                _ => new Color(0.75f, 0.75f, 0.72f),
            };
            float rad = p.IsVictoryPoint ? 5f : 3.5f;
            DrawCircle(pos, rad, color);
            if (p.IsVictoryPoint)
                DrawArc(pos, rad + 1.5f, 0, Mathf.Tau, 24, color * new Color(1, 1, 1, 0.7f), 1.2f);
        }

        // Squads.
        foreach (var u in World.Units)
        {
            if (!u.Alive)
                continue;
            var color = u.Side switch
            {
                Side.Allies => new Color(0.85f, 0.80f, 0.45f),
                Side.Central => new Color(0.65f, 0.70f, 0.85f),
                 _ => new Color(1f, 1f, 1f),
            };
            DrawRect(new Rect2(SimToPx(u.Pos) - new Vector2(2, 2), new Vector2(4, 4)), color);
        }

        // Camera footprint.
        var poly = ViewFootprint?.Invoke();
        if (poly is { Length: 4 })
        {
            var pts = new Vector2[poly.Length];
            for (int i = 0; i < poly.Length; i++)
                pts[i] = PxOfWorld(poly[i]);
            DrawColoredPolygon(pts, new Color(1f, 1f, 1f, 0.14f));
            for (int i = 0; i < pts.Length; i++)
                DrawLine(pts[i], pts[(i + 1) % pts.Length], new Color(1f, 1f, 1f, 0.75f), 1.2f);
        }
    }

    private Vector2 PxOfWorld(Vector2 world) => new(
        world.X / _mapSize.X * Size.X,
        world.Y / _mapSize.Y * Size.Y);

    public override void _Process(double delta)
    {
        if (World is not null)
            QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } or
               InputEventMouseMotion when Input.IsMouseButtonPressed(MouseButton.Left):
                if (@event is InputEventMouseButton mb)
                    Jump(mb.Position);
                else if (@event is InputEventMouseMotion mm)
                    Jump(mm.Position);
                break;
        }
    }

    private void Jump(Vector2 local)
    {
        if (new Rect2(Vector2.Zero, Size).HasPoint(local))
            JumpRequested?.Invoke(local.X / Size.X * _mapSize.X, local.Y / Size.Y * _mapSize.Y);
    }
}
