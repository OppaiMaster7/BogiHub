using System.Numerics;
using BogiHub.Core.Input;
using BogiHub.Core.Output;
using BogiHub.Core.Profiles;

namespace BogiHub.Core.Mapping;

/// <summary>
/// Turns one <see cref="ControllerState"/> into one <see cref="OutputFrame"/>. Runs on the input thread only.
/// Deterministic: the same states, timings and profile always give the same output.
/// The UI swaps profiles with <see cref="SetProfile"/>; the switch takes effect on the next sample.
/// </summary>
public sealed class MappingEngine
{
    /// <summary>Pixels the cursor travels for one full trackpad width at sensitivity 1.</summary>
    public const float PadMousePixelsPerWidth = 1600f;
    /// <summary>Wheel notches for one full trackpad height at sensitivity 1.</summary>
    public const float PadScrollNotchesPerHeight = 12f;
    /// <summary>Pixels per radian of controller rotation at gyro sensitivity 1.</summary>
    public const float GyroPixelsPerRadian = 1000f;
    private const float GyroNoiseFloor = 0.01f;
    private const float DiagonalThreshold = 0.38f; // sin(22.5°): 8-way directions

    // Source ids for edge detection of wheel bindings.
    private const int SrcTriggerL = PhysicalInputs.Count, SrcTriggerR = SrcTriggerL + 1;
    private const int SrcLeftStick = SrcTriggerR + 1, SrcRightStick = SrcLeftStick + 4;
    private const int SrcLeftPad = SrcRightStick + 4, SrcRightPad = SrcLeftPad + 4;

    private Profile? _pending;
    private Profile _profile = null!;
    private readonly Binding[] _buttonMap = new Binding[PhysicalInputs.Count];
    private bool _usesGamepad, _usesKbm;
    private bool _padNow, _kbmNow; // effective for the current sample (Gamepad mode can override)

    private PadMotion _leftPad, _rightPad;
    private ulong _wheelSourcesHeld, _wheelSourcesNow;

    // Analog accumulators for the current sample.
    private Vector2 _outLeft, _outRight;
    private float _outLt, _outRt;
    private XButtons _outButtons;

    public MappingEngine(Profile profile) => Apply(profile.Clone());

    /// <summary>The profile currently in effect on the input thread.</summary>
    public Profile Profile => _profile;

    /// <summary>True for exactly one sample after a profile switch; outputs should be neutralised first.</summary>
    public bool ProfileJustChanged { get; private set; }

    private volatile bool _gamepadOnly;
    /// <summary>Gamepad mode: ignore every keyboard/mouse binding and drive only the virtual Xbox controller. Thread-safe.</summary>
    public bool GamepadOnly
    {
        get => _gamepadOnly;
        set => _gamepadOnly = value;
    }

    /// <summary>Thread-safe. The engine takes a private copy, so later UI edits to <paramref name="profile"/> do not leak in.</summary>
    public void SetProfile(Profile profile) => Volatile.Write(ref _pending, profile.Clone());

    public void Map(in ControllerState s, float dt, OutputFrame f)
    {
        var pending = Interlocked.Exchange(ref _pending, null);
        ProfileJustChanged = pending is not null;
        if (pending is not null) Apply(pending);

        dt = Math.Clamp(dt, 0f, 0.1f);
        bool gamepadOnly = _gamepadOnly;
        _kbmNow = _usesKbm && !gamepadOnly;
        _padNow = _usesGamepad || gamepadOnly;
        f.Clear();
        f.UsesGamepad = _padNow;
        f.UsesKeyboardMouse = _kbmNow;
        _outLeft = _outRight = Vector2.Zero;
        _outLt = _outRt = 0;
        _outButtons = XButtons.None;
        _wheelSourcesNow = 0;

        for (int i = 0; i < PhysicalInputs.Count; i++)
            Digital(i, _buttonMap[i], s.IsPressed((PhysicalInput)i), f);

        Stick(SrcLeftStick, s.LeftStick, _profile.LeftStick, dt, f);
        Stick(SrcRightStick, s.RightStick, _profile.RightStick, dt, f);
        Trigger(SrcTriggerL, s.LeftTrigger, _profile.LeftTrigger, f);
        Trigger(SrcTriggerR, s.RightTrigger, _profile.RightTrigger, f);
        Pad(SrcLeftPad, s.LeftPad, s.IsPressed(PhysicalInput.LeftPadClick), _profile.LeftPad, ref _leftPad, dt, f);
        Pad(SrcRightPad, s.RightPad, s.IsPressed(PhysicalInput.RightPadClick), _profile.RightPad, ref _rightPad, dt, f);
        Gyro(in s, dt, f);

        _wheelSourcesHeld = _wheelSourcesNow;

        if (_padNow)
        {
            f.Pad.Buttons = _outButtons;
            f.Pad.LeftTrigger = ToByte(_outLt);
            f.Pad.RightTrigger = ToByte(_outRt);
            var l = Deadzone.ClampUnit(_outLeft);
            var r = Deadzone.ClampUnit(_outRight);
            f.Pad.LeftX = ToShort(l.X);
            f.Pad.LeftY = ToShort(l.Y);
            f.Pad.RightX = ToShort(r.X);
            f.Pad.RightY = ToShort(r.Y);
        }
    }

    private void Apply(Profile p)
    {
        _profile = p;
        for (int i = 0; i < _buttonMap.Length; i++) _buttonMap[i] = p.GetBinding((PhysicalInput)i);
        _usesGamepad = p.Output != OutputMode.KeyboardMouse;
        _usesKbm = p.Output != OutputMode.Gamepad || UsesKeyboardMouse(p);
        _leftPad = _rightPad = default;
        _wheelSourcesHeld = 0;
    }

    private static bool UsesKeyboardMouse(Profile p) =>
        p.Buttons.Values.Any(b => b.Kind is BindingKind.Key or BindingKind.Mouse) ||
        p.LeftStick.Mode is StickMode.Keys or StickMode.Mouse || p.RightStick.Mode is StickMode.Keys or StickMode.Mouse ||
        p.LeftTrigger.Binding.Kind is BindingKind.Key or BindingKind.Mouse ||
        p.RightTrigger.Binding.Kind is BindingKind.Key or BindingKind.Mouse ||
        p.LeftPad.Mode is PadMode.Mouse or PadMode.Scroll || p.RightPad.Mode is PadMode.Mouse or PadMode.Scroll ||
        new[] { p.LeftPad.Up, p.LeftPad.Down, p.LeftPad.Left, p.LeftPad.Right, p.RightPad.Up, p.RightPad.Down, p.RightPad.Left, p.RightPad.Right }
            .Any(b => b.Kind is BindingKind.Key or BindingKind.Mouse) ||
        p.Gyro.Mode == GyroMode.Mouse;

    /// <summary>Several sources may share one output; outputs are OR-ed (buttons) or max/summed (analog).</summary>
    private void Digital(int source, Binding b, bool active, OutputFrame f)
    {
        if (!active) return;
        switch (b.Kind)
        {
            case BindingKind.Gamepad when _padNow:
                switch (b.Gamepad)
                {
                    case GamepadButton.LT: _outLt = 1f; break;
                    case GamepadButton.RT: _outRt = 1f; break;
                    default: _outButtons |= ToXButton(b.Gamepad); break;
                }
                break;
            case BindingKind.Key when _kbmNow:
                f.Keys[(byte)b.Key] = true;
                break;
            case BindingKind.Mouse when _kbmNow:
                switch (b.Mouse)
                {
                    case MouseAction.WheelUp or MouseAction.WheelDown:
                        // One notch per press, not per sample.
                        ulong bit = 1UL << source;
                        _wheelSourcesNow |= bit;
                        if ((_wheelSourcesHeld & bit) == 0) f.WheelNotches += b.Mouse == MouseAction.WheelUp ? 1 : -1;
                        break;
                    default:
                        f.Mouse |= b.Mouse switch
                        {
                            MouseAction.Left => MouseButtons.Left,
                            MouseAction.Right => MouseButtons.Right,
                            MouseAction.Middle => MouseButtons.Middle,
                            MouseAction.X1 => MouseButtons.X1,
                            _ => MouseButtons.X2,
                        };
                        break;
                }
                break;
        }
    }

    private void Stick(int src, Vector2 raw, StickConfig c, float dt, OutputFrame f)
    {
        if (c.Mode == StickMode.None) return;
        var v = Deadzone.Radial(raw, c.Deadzone, c.OuterDeadzone);
        if (c.InvertY) v.Y = -v.Y;

        switch (c.Mode)
        {
            case StickMode.LeftStick: _outLeft += v * c.Sensitivity; break;
            case StickMode.RightStick: _outRight += v * c.Sensitivity; break;
            case StickMode.Keys:
                Directions(src, v, c.KeyThreshold, c.Up, c.Down, c.Left, c.Right, f);
                break;
            case StickMode.Mouse:
                // Squared response: fine control near the centre, full speed at the edge.
                float speed = c.MouseSpeed * c.Sensitivity * v.Length();
                f.MouseDx += v.X * speed * dt;
                f.MouseDy -= v.Y * speed * dt;
                break;
        }
    }

    private void Trigger(int src, float raw, TriggerConfig c, OutputFrame f)
    {
        switch (c.Mode)
        {
            case TriggerMode.LeftTrigger: _outLt = Math.Max(_outLt, Deadzone.Linear(raw, c.Deadzone)); break;
            case TriggerMode.RightTrigger: _outRt = Math.Max(_outRt, Deadzone.Linear(raw, c.Deadzone)); break;
            case TriggerMode.Button: Digital(src, c.Binding, raw >= c.Threshold, f); break;
        }
    }

    private void Pad(int src, TouchPoint t, bool clicked, TrackpadConfig c, ref PadMotion m, float dt, OutputFrame f)
    {
        var pos = new Vector2(t.X, t.Y);
        switch (c.Mode)
        {
            case PadMode.Mouse:
                if (t.Touching)
                {
                    if (m.WasTouching)
                    {
                        var delta = (pos - m.Last) * PadMousePixelsPerWidth * c.Sensitivity;
                        m.Smoothed = Vector2.Lerp(delta, m.Smoothed, c.Smoothing);
                        f.MouseDx += m.Smoothed.X;
                        f.MouseDy += m.Smoothed.Y;
                        m.Velocity = dt > 0 ? m.Smoothed / dt : Vector2.Zero;
                    }
                    else
                    {
                        m.Smoothed = Vector2.Zero;
                        m.Velocity = Vector2.Zero;
                    }
                }
                else if (c.Inertia && m.Velocity.LengthSquared() > 40f * 40f)
                {
                    f.MouseDx += m.Velocity.X * dt;
                    f.MouseDy += m.Velocity.Y * dt;
                    m.Velocity *= MathF.Pow(1f - Math.Min(c.Friction, 0.999f), dt);
                }
                else
                {
                    m.Velocity = Vector2.Zero;
                }
                break;

            case PadMode.Scroll:
                if (t.Touching && m.WasTouching)
                {
                    m.ScrollRemainder += (m.Last.Y - pos.Y) * PadScrollNotchesPerHeight * c.Sensitivity;
                    int notches = (int)m.ScrollRemainder;
                    f.WheelNotches += notches;
                    m.ScrollRemainder -= notches;
                }
                else if (!t.Touching)
                {
                    m.ScrollRemainder = 0;
                }
                break;

            case PadMode.Directional:
                if (t.Touching && (clicked || !c.RequireClick))
                {
                    var v = t.Centered;
                    if (v.Length() > c.Deadzone) Directions(src, Vector2.Normalize(v), DiagonalThreshold, c.Up, c.Down, c.Left, c.Right, f);
                }
                break;

            case PadMode.LeftStick or PadMode.RightStick:
                if (t.Touching && (clicked || !c.RequireClick))
                {
                    var v = Deadzone.Radial(Deadzone.ClampUnit(t.Centered), c.Deadzone, 0.1f) * c.Sensitivity;
                    if (c.Mode == PadMode.LeftStick) _outLeft += v; else _outRight += v;
                }
                break;
        }

        m.WasTouching = t.Touching;
        if (t.Touching) m.Last = pos;
    }

    private void Gyro(in ControllerState s, float dt, OutputFrame f)
    {
        var g = _profile.Gyro;
        if (g.Mode == GyroMode.Off || !s.MotionValid) return;
        if (g.EnableWhile is { } hold && !s.IsPressed(hold)) return;

        // SDL gyro axes: X = pitch (nose up positive), Y = yaw (turn left positive).
        float yaw = Math.Abs(s.Gyro.Y) < GyroNoiseFloor ? 0 : s.Gyro.Y;
        float pitch = Math.Abs(s.Gyro.X) < GyroNoiseFloor ? 0 : s.Gyro.X;
        float x = -yaw * (g.InvertX ? -1 : 1);
        float y = pitch * (g.InvertY ? -1 : 1);

        switch (g.Mode)
        {
            case GyroMode.Mouse:
                f.MouseDx += x * GyroPixelsPerRadian * g.Sensitivity * dt;
                f.MouseDy -= y * GyroPixelsPerRadian * g.Sensitivity * dt;
                break;
            case GyroMode.RightStick:
                // About 2 rad/s of turning maps to a full stick deflection at sensitivity 1.
                _outRight += new Vector2(x, y) * 0.5f * g.Sensitivity;
                break;
        }
    }

    private void Directions(int src, Vector2 v, float threshold, Binding up, Binding down, Binding left, Binding right, OutputFrame f)
    {
        Digital(src, up, v.Y > threshold, f);
        Digital(src + 1, down, v.Y < -threshold, f);
        Digital(src + 2, left, v.X < -threshold, f);
        Digital(src + 3, right, v.X > threshold, f);
    }

    internal static XButtons ToXButton(GamepadButton b) => b switch
    {
        GamepadButton.A => XButtons.A,
        GamepadButton.B => XButtons.B,
        GamepadButton.X => XButtons.X,
        GamepadButton.Y => XButtons.Y,
        GamepadButton.LB => XButtons.LeftShoulder,
        GamepadButton.RB => XButtons.RightShoulder,
        GamepadButton.Back => XButtons.Back,
        GamepadButton.Start => XButtons.Start,
        GamepadButton.Guide => XButtons.Guide,
        GamepadButton.LeftStick => XButtons.LeftThumb,
        GamepadButton.RightStick => XButtons.RightThumb,
        GamepadButton.DpadUp => XButtons.DpadUp,
        GamepadButton.DpadDown => XButtons.DpadDown,
        GamepadButton.DpadLeft => XButtons.DpadLeft,
        GamepadButton.DpadRight => XButtons.DpadRight,
        _ => XButtons.None,
    };

    private static short ToShort(float v) => (short)Math.Round(Math.Clamp(v, -1f, 1f) * 32767f);
    private static byte ToByte(float v) => (byte)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);

    private struct PadMotion
    {
        public bool WasTouching;
        public Vector2 Last;
        public Vector2 Smoothed;
        public Vector2 Velocity;
        public float ScrollRemainder;
    }
}
