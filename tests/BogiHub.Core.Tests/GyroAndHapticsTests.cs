using System.Numerics;
using BogiHub.Core.Input;
using BogiHub.Core.Mapping;
using BogiHub.Core.Output;
using BogiHub.Core.Profiles;

namespace BogiHub.Core.Tests;

public class GyroAndHapticsTests
{
    private const float Dt = 1f / 250f;
    private static readonly Vector3 Flat = new(0, 9.81f, 0); // resting face up: gravity reads along +Y

    private static Profile GyroMouse(Action<GyroConfig>? tweak = null)
    {
        var p = DefaultProfiles.KeyboardMouse();
        p.Gyro = new GyroConfig { Mode = GyroMode.Mouse, Smoothing = 0, Precision = 0 };
        tweak?.Invoke(p.Gyro);
        return p;
    }

    private static ControllerState Motion(Vector3 gyro, Vector3? accel = null) =>
        new() { MotionValid = true, Gyro = gyro, Accel = accel ?? Flat };

    private static OutputFrame Run(MappingEngine engine, ControllerState s)
    {
        var f = new OutputFrame();
        engine.Map(in s, Dt, f);
        return f;
    }

    // ---- Gyro ------------------------------------------------------------------------------------

    [Fact]
    public void Drift_on_a_resting_controller_is_learned_away()
    {
        var engine = new MappingEngine(GyroMouse());
        var drift = new Vector3(0.02f, 0.03f, -0.01f);
        Assert.NotEqual(0f, Run(engine, Motion(drift)).MouseDx);

        OutputFrame last = new();
        for (int i = 0; i < 250 * 4; i++) last = Run(engine, Motion(drift));
        Assert.InRange(last.MouseDx, -0.001f, 0.001f);
        Assert.InRange(last.MouseDy, -0.001f, 0.001f);
        Assert.True(engine.GyroState.Calibrated);
    }

    [Fact]
    public void Real_turning_is_never_calibrated_away()
    {
        var engine = new MappingEngine(GyroMouse());
        float expected = -1f * MappingEngine.GyroPixelsPerRadian * Dt; // turning left at 1 rad/s moves the cursor left
        OutputFrame last = new();
        for (int i = 0; i < 250 * 4; i++) last = Run(engine, Motion(new Vector3(0, 1f, 0)));
        Assert.Equal(expected, last.MouseDx, 3);
        Assert.False(engine.GyroState.Calibrated);
    }

    [Fact]
    public void Player_space_turns_about_the_world_up_axis_however_it_is_held()
    {
        var g = new GyroProcessor();
        var config = new GyroConfig { Mode = GyroMode.Mouse, Smoothing = 0 };
        var standingUp = new Vector3(0, 0, 9.81f); // held vertically: the roll axis now points at the sky
        for (int i = 0; i < 500; i++) g.Observe(new Vector3(0, 0, 1f), standingUp, Dt);

        Assert.Equal(-1f, g.Aim(new Vector3(0, 0, 1f), config, Dt).X, 2);
        config.TurnAxis = GyroTurnAxis.Yaw;
        Assert.Equal(0f, g.Aim(new Vector3(0, 0, 1f), config, Dt).X, 3);
    }

    [Fact]
    public void Player_space_on_a_flat_controller_is_plain_yaw()
    {
        var g = new GyroProcessor();
        var config = new GyroConfig { Mode = GyroMode.Mouse, Smoothing = 0 };
        for (int i = 0; i < 500; i++) g.Observe(new Vector3(0, -0.5f, 0), Flat, Dt);
        Assert.Equal(0.5f, g.Aim(new Vector3(0, -0.5f, 0), config, Dt).X, 3);
    }

    [Fact]
    public void Precision_slows_fine_movement_but_not_fast_turns()
    {
        var g = new GyroProcessor();
        var config = new GyroConfig { Mode = GyroMode.Mouse, Smoothing = 0, Precision = 0.5f, TurnAxis = GyroTurnAxis.Yaw };
        Assert.Equal(-0.05f * (1f - 0.5f * (1f - 0.05f / GyroProcessor.PrecisionFullSpeed)), g.Aim(new Vector3(0, 0.05f, 0), config, Dt).X, 4);
        Assert.Equal(-3f, g.Aim(new Vector3(0, 3f, 0), config, Dt).X, 4);
    }

    [Fact]
    public void Smoothing_never_delays_real_movement()
    {
        var g = new GyroProcessor();
        var config = new GyroConfig { Mode = GyroMode.Mouse, Smoothing = 1f, TurnAxis = GyroTurnAxis.Yaw };
        Assert.Equal(-2f, g.Aim(new Vector3(0, 2f, 0), config, Dt).X, 4); // first sample, far above the threshold
        Assert.True(MathF.Abs(g.Aim(new Vector3(0, 0.02f, 0), config, Dt).X) < 0.02f); // a tiny shake is calmed
    }

    [Fact]
    public void Vertical_speed_scales_only_up_and_down()
    {
        var g = new GyroProcessor();
        var config = new GyroConfig { Mode = GyroMode.Mouse, Smoothing = 0, VerticalScale = 0.5f, TurnAxis = GyroTurnAxis.Yaw };
        var aim = g.Aim(new Vector3(1f, 1f, 0), config, Dt);
        Assert.Equal(0.5f, aim.Y, 4);
        Assert.Equal(-1f, aim.X, 4);
    }

    [Fact]
    public void Pause_while_held_stops_gyro()
    {
        var engine = new MappingEngine(GyroMouse(g => g.DisableWhile = PhysicalInput.RightStickTouch));
        var turning = Motion(new Vector3(0, 1f, 0));
        Assert.NotEqual(0f, Run(engine, turning).MouseDx);
        turning.Set(PhysicalInput.RightStickTouch, true);
        Assert.Equal(0f, Run(engine, turning).MouseDx);
    }

    // ---- Trackpad haptics ------------------------------------------------------------------------

    private static ControllerState RightTouch(float x, float y = 0.5f) => new() { RightPad = new TouchPoint(true, x, y, 0.1f) };

    [Fact]
    public void Trackball_swipe_ticks_by_distance_whatever_the_sensitivity()
    {
        foreach (float sensitivity in new[] { 0.5f, 1f, 3f })
        {
            var p = DefaultProfiles.KeyboardMouse();
            p.RightPad.Mode = PadMode.Mouse;
            p.RightPad.Smoothing = 0;
            p.RightPad.Inertia = false;
            p.RightPad.Sensitivity = sensitivity;
            var engine = new MappingEngine(p);
            int ticks = 0;
            for (int i = 0; i <= 50; i++) ticks += Run(engine, RightTouch(0.2f + i * 0.01f)).TicksRight; // half a pad width
            Assert.InRange(ticks, 13, 15); // 0.5 / 0.035 ≈ 14
        }
    }

    [Fact]
    public void Scroll_wheel_ticks_once_per_notch()
    {
        var p = DefaultProfiles.KeyboardMouse();
        p.RightPad.Mode = PadMode.Scroll;
        p.RightPad.Sensitivity = 1;
        var engine = new MappingEngine(p);
        int ticks = 0, notches = 0;
        for (int i = 0; i <= 50; i++)
        {
            var f = Run(engine, RightTouch(0.5f, 0.9f - i * 0.016f));
            ticks += f.TicksRight;
            notches += Math.Abs(f.WheelNotches);
        }
        Assert.True(notches > 0);
        Assert.Equal(notches, ticks);
    }

    [Fact]
    public void Dpad_pad_clicks_on_each_new_direction_only()
    {
        var p = DefaultProfiles.KeyboardMouse();
        p.LeftPad = new TrackpadConfig { Mode = PadMode.Directional, RequireClick = false, Up = Binding.Pad(GamepadButton.DpadUp) };
        var engine = new MappingEngine(p);
        ControllerState Left(float x, float y) => new() { LeftPad = new TouchPoint(true, x, y, 0.1f) };

        Assert.True(Run(engine, Left(0.5f, 0.1f)).ClickLeft);   // up
        Assert.False(Run(engine, Left(0.52f, 0.1f)).ClickLeft); // still up
        Assert.True(Run(engine, Left(0.9f, 0.5f)).ClickLeft);   // right
        Assert.False(Run(engine, Left(0.5f, 0.5f)).ClickLeft);  // centre: nothing
    }

    [Fact]
    public void Haptics_off_on_a_pad_sends_nothing()
    {
        var p = DefaultProfiles.KeyboardMouse();
        p.RightPad.Mode = PadMode.Mouse;
        p.RightPad.Haptics = false;
        var engine = new MappingEngine(p);
        int ticks = 0;
        for (int i = 0; i <= 50; i++) ticks += Run(engine, RightTouch(0.2f + i * 0.01f)).TicksRight;
        Assert.Equal(0, ticks);
    }
}
