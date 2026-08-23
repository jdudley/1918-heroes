using Godot;

namespace Heroes1918;

/// <summary>
/// Company-of-Heroes-style RTS camera:
/// - Wheel zoom sweeps from strategic height down to behind your troops,
///   flattening the pitch as you close in, and pulls toward the cursor.
/// - WASD/arrows pan relative to view; screen-edge scrolling works too.
/// - Q/E rotate, middle-mouse drag grabs the ground.
/// Not thread-safe trivia aside, everything is frame-rate independent.
/// </summary>
public partial class RtsCamera : Camera3D
{
    private const float PitchFarDeg = 64f;   // strategic view: mostly top-down
    private const float PitchNearDeg = 34f;  // zoomed in: behind your troops
    private const float MinDist = 13f;
    private const float MaxDist = 135f;
    private const float EdgeMarginPx = 26f;
    private const float BaseFovDeg = 75f;

    private Vector2 _center;
    private float _yaw;            // radians around vertical, 0 = camera south of center
    private float _distTarget = 42f;
    private float _dist = 42f;
    private float _mapW = 1000f;
    private float _mapH = 1000f;

    private bool _grabbing;
    private Vector2 _grabLast;
    private Vector2 _grabCenterAtStart;
    /// <summary>Wheel notches accumulated; consumed at a capped per-second rate.</summary>
    private float _pendingZoomSteps;
    private const float MaxZoomNotchesPerSecond = 11f;
    /// <summary>Pan-gesture units per zoom notch (tuned on-device).</summary>
    private const float PanGestureToNotches = 2.2f;
    /// <summary>Toggled with N when system natural-scrolling feels backwards.</summary>
    private bool _zoomInvert;

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

        if (Mathf.Abs(_pendingZoomSteps) > 0.01f)
        {
            // Consume at most MaxZoomNotchesPerSecond notches: real wheels land
            // instantly, trackpad floods become smooth continuous zoom.
            float maxSteps = MaxZoomNotchesPerSecond * dt;
            float used = Mathf.Clamp(_pendingZoomSteps, -maxSteps, maxSteps);
            _pendingZoomSteps -= used;

            float factor = Mathf.Pow(0.88f, used);
            _distTarget = Mathf.Clamp(_distTarget * factor, MinDist, MaxDist);
        }

        HandleKeyboardPan(dt);
        HandleEdgeScroll(dt);
        SmoothZoom(dt);
        ClampCenter();
        Apply();
    }

    private static readonly bool _inputDebug =
        System.Environment.GetEnvironmentVariable("INPUT_DEBUG") == "1";

    private static string Describe(InputEvent e) => e switch
    {
        InputEventMouseButton mb => $"btn={mb.ButtonIndex} pressed={mb.Pressed} factor={mb.Factor}",
        InputEventPanGesture pg => $"pan delta={pg.Delta}",
        InputEventMagnifyGesture mg => $"magnify factor={mg.Factor}",
        InputEventKey k => $"key={k.Keycode} pressed={k.Pressed}",
        _ => "",
    };

    // ------------------------------------------------------------ input events

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_inputDebug && @event is InputEventMouseButton or InputEventPanGesture or InputEventMagnifyGesture or InputEventKey)
            GD.Print($"[input] {@event.GetClass()} {Describe(@event)}");

        switch (@event)
        {
            // macOS reports Factor=0 for trackpad scrolls, so ignore it: every
            // event counts one notch, and the per-frame rate cap below turns a
            // two-finger flood into smooth continuous zoom.
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _pendingZoomSteps -= 1f;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _pendingZoomSteps += 1f;
                break;

            // macOS delivers two-finger trackpad scroll as a PAN GESTURE,
            // never as wheel buttons (Godot issue #105925).
            case InputEventPanGesture pan:
                float steps = pan.Delta.Y * PanGestureToNotches * (_zoomInvert ? -1f : 1f);
                _pendingZoomSteps += steps;
                break;

            // Bonus: pinch to zoom, where supported.
            case InputEventMagnifyGesture mag when mag.Factor > 0.05f:
                float f = Mathf.Clamp(mag.Factor, 0.85f, 1.18f);
                _distTarget = Mathf.Clamp(_distTarget / f, MinDist, MaxDist);
                break;

            case InputEventKey { Pressed: true, Keycode: Key.N }:
                _zoomInvert = !_zoomInvert;
                GD.Print($"zoom invert: {_zoomInvert}");
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Equal }:
                _pendingZoomSteps -= 1f;
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Minus }:
                _pendingZoomSteps += 1f;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mmb:
                _grabbing = mmb.Pressed;
                _grabLast = mmb.Position;
                _grabCenterAtStart = _center;
                break;
            case InputEventMouseMotion mm when _grabbing || Input.IsKeyPressed(Key.Space):
                // Middle-drag or Space+drag: the trackpad-friendly grab pan.
                GrabPan(mm.Relative);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Q }:
                _yaw += 0.06f;
                break;
            case InputEventKey { Pressed: true, Keycode: Key.E }:
                _yaw -= 0.06f;
                break;
        }
    }

    private void GrabPan(Vector2 relative)
    {
        float mpp = MetersPerPixel();
        var right = ScreenRight();
        var up = ScreenForward();
        _center -= right * (relative.X * mpp);
        _center -= up * (-relative.Y * mpp);
        ClampCenter();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Ground direction that appears "up" on screen.</summary>
    private Vector2 ScreenForward() => new(-Mathf.Sin(_yaw), -Mathf.Cos(_yaw));

    /// <summary>Ground direction that appears "right" on screen.</summary>
    private Vector2 ScreenRight() => new(Mathf.Cos(_yaw), -Mathf.Sin(_yaw));

    private float MetersPerPixel()
    {
        float viewportH = GetViewport().GetVisibleRect().Size.Y;
        if (viewportH < 1) viewportH = 900f;
        return 2f * _dist * Mathf.Tan(Mathf.DegToRad(BaseFovDeg) / 2f) / viewportH;
    }

    private void HandleKeyboardPan(float dt)
    {
        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move += ScreenForward();
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move -= ScreenForward();
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move += ScreenRight();
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move -= ScreenRight();

        if (move != Vector2.Zero)
        {
            _center += move.Normalized() * dt * PanSpeed();
            ClampCenter();
        }
    }

    private void HandleEdgeScroll(float dt)
    {
        // No edge scrolling without a real pointer (headless runs, unfocused window).
        if (DisplayServer.GetName() == "headless")
            return;
        if (DisplayServer.GetName().Contains("headless", StringComparison.OrdinalIgnoreCase))
            return;
        if (!GetWindow().HasFocus())
            return;

        var size = GetViewport().GetVisibleRect().Size;
        var mouse = GetViewport().GetMousePosition();

        float push = 0f;
        var dir = Vector2.Zero;

        if (mouse.X < EdgeMarginPx) { dir.X -= 1f; push = 1f - mouse.X / EdgeMarginPx; }
        else if (mouse.X > size.X - EdgeMarginPx) { dir.X += 1f; push = 1f - (size.X - mouse.X) / EdgeMarginPx; }
        if (mouse.Y < EdgeMarginPx) { dir.Y -= 1f; push = Mathf.Max(push, 1f - mouse.Y / EdgeMarginPx); }
        else if (mouse.Y > size.Y - EdgeMarginPx) { dir.Y += 1f; push = Mathf.Max(push, 1f - (size.Y - mouse.Y) / EdgeMarginPx); }

        if (push <= 0f || dir == Vector2.Zero)
            return;

        // Screen directions are already view-relative; normalize the diagonal.
        var world = (dir.Normalized().X * ScreenRight()) + (dir.Normalized().Y * ScreenForward());
        _center += world * dt * PanSpeed() * push;
        ClampCenter();
    }

    private float PanSpeed() => _dist * 1.15f;

    private void SmoothZoom(double dt)
    {
        // Exponential approach: fast when far off, settles gently.
        float blend = 1f - Mathf.Exp(-12f * (float)dt);
        _dist = Mathf.Lerp(_dist, _distTarget, blend);
    }

    private void ClampCenter()
    {
        // CoH rule: the camera focus point stays on the map, period - you cannot
        // scroll past the edge at any zoom level.
        const float inset = 2f;
        _center.X = Mathf.Clamp(_center.X, inset, _mapW - inset);
        _center.Y = Mathf.Clamp(_center.Y, inset, _mapH - inset);
    }

    private void Apply()
    {
        // Pitch flattens as you close in: strategic overhead vs over-the-shoulder.
        float zoomFrac = Mathf.Clamp(
            (_dist - MinDist) / (MaxDist - MinDist), 0f, 1f);
        float pitch = Mathf.DegToRad(Mathf.Lerp(PitchNearDeg, PitchFarDeg, zoomFrac));

        float horiz = Mathf.Cos(pitch) * _dist;
        float vert = Mathf.Sin(pitch) * _dist;

        var offset = new Vector3(
            Mathf.Sin(_yaw) * horiz,
            vert,
            Mathf.Cos(_yaw) * horiz);

        Position = new Vector3(_center.X, 0, _center.Y) + offset;
        LookAt(new Vector3(_center.X, 0, _center.Y), Vector3.Up);
    }

}
