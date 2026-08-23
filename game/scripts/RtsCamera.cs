using Godot;

namespace Heroes1918;

/// <summary>
/// RTS camera: WASD/arrows to pan (scaled by zoom), wheel to zoom, clamped to the map.
/// The view direction is fixed; north stays up.
/// </summary>
public partial class RtsCamera : Camera3D
{
    private const float PitchDeg = 56f;
    private const float MinDist = 22f;
    private const float MaxDist = 95f;

    private Vector2 _center;
    private float _dist = 55f;
    private float _mapW = 1000f;
    private float _mapH = 1000f;

    public void Setup(float mapWidth, float mapHeight)
    {
        _mapW = mapWidth;
        _mapH = mapHeight;
        _center = new Vector2(mapWidth / 2f, mapHeight / 2f);
        Apply();
        MakeCurrent();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var move = Vector2.Zero;

        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;

        if (move != Vector2.Zero)
        {
            move = move.Normalized() * dt * _dist * 0.9f;
            _center += move;
            ClampCenter();
            Apply();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } mb)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.WheelUp:
                    Zoom(0.88f);
                    break;
                case MouseButton.WheelDown:
                    Zoom(1.14f);
                    break;
            }
        }
    }

    private void Zoom(float factor)
    {
        _dist = Mathf.Clamp(_dist * factor, MinDist, MaxDist);
        Apply();
    }

    private void ClampCenter()
    {
        float marginX = _dist * 0.35f;
        float marginY = _dist * 0.35f;
        _center.X = Mathf.Clamp(_center.X, -marginX, _mapW + marginX);
        _center.Y = Mathf.Clamp(_center.Y, -marginY, _mapH + marginY);
    }

    private void Apply()
    {
        float pitch = Mathf.DegToRad(PitchDeg);
        var offset = new Vector3(0, Mathf.Sin(pitch), Mathf.Cos(pitch)) * _dist;
        Position = new Vector3(_center.X, 0, _center.Y) + offset;
        LookAt(new Vector3(_center.X, 0, _center.Y), Vector3.Up);
    }
}
