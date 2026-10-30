using System.Numerics;
using BogiHub.Core.Input;
using BogiHub.Core.Mapping;
using BogiHub.Core.Output;
using BogiHub.Core.Profiles;

namespace BogiHub.Core.Tests;

public class MappingEngineTests
{
    private const float Dt = 1f / 250f;

    private static OutputFrame Run(MappingEngine engine, ControllerState s)
    {
        var f = new OutputFrame();
        engine.Map(in s, Dt, f);
        return f;
    }

    private static ControllerState Pressed(params PhysicalInput[] inputs)
    {
        var s = new ControllerState();
        foreach (var i in inputs) s.Set(i, true);
        return s;
    }

    [Fact]
    public void Neutral_state_gives_neutral_gamepad()
    {
        var f = Run(new MappingEngine(DefaultProfiles.Controller()), new ControllerState());
        Assert.Equal(GamepadReport.Neutral, f.Pad);
        Assert.True(f.UsesGamepad);
        Assert.False(f.UsesKeyboardMouse);
    }

    [Fact]
    public void Controller_default_maps_face_buttons_and_dpad()
    {
        var f = Run(new MappingEngine(DefaultProfiles.Controller()), Pressed(PhysicalInput.A, PhysicalInput.Menu, PhysicalInput.DpadLeft));
        Assert.Equal(XButtons.A | XButtons.Start | XButtons.DpadLeft, f.Pad.Buttons);
    }

    [Fact]
    public void Two_inputs_sharing_one_output_are_combined_and_release_only_when_both_released()
    {
        var engine = new MappingEngine(DefaultProfiles.Controller()); // A and right paddle 1 both → A
        Assert.Equal(XButtons.A, Run(engine, Pressed(PhysicalInput.A, PhysicalInput.RightPaddle1)).Pad.Buttons);
        Assert.Equal(XButtons.A, Run(engine, Pressed(PhysicalInput.RightPaddle1)).Pad.Buttons);
        Assert.Equal(XButtons.None, Run(engine, new ControllerState()).Pad.Buttons);
    }

    [Theory]
    [InlineData(0.09f, 0)]       // inside 0.10 deadzone
    [InlineData(0.10f, 0)]       // exactly on the edge → still zero
    [InlineData(0.97f, 32767)]   // inside the 0.03 outer deadzone → full
    [InlineData(1.0f, 32767)]
    public void Stick_deadzone_boundaries(float deflection, short expected)
    {
        var s = new ControllerState { LeftStick = new Vector2(deflection, 0) };
        Assert.Equal(expected, Run(new MappingEngine(DefaultProfiles.Controller()), s).Pad.LeftX);
    }

    [Fact]
    public void Stick_just_past_deadzone_starts_near_zero_without_jump()
    {
        var s = new ControllerState { LeftStick = new Vector2(0.11f, 0) };
        short x = Run(new MappingEngine(DefaultProfiles.Controller()), s).Pad.LeftX;
        Assert.InRange(x, 1, 600);
    }

    [Fact]
    public void Stick_direction_is_preserved_and_up_is_positive()
    {
        var s = new ControllerState { RightStick = new Vector2(0, 1) };
        var f = Run(new MappingEngine(DefaultProfiles.Controller()), s);
        Assert.Equal(0, f.Pad.RightX);
        Assert.Equal(32767, f.Pad.RightY);
    }

    [Fact]
    public void Triggers_are_analog_with_deadzone()
    {
        var engine = new MappingEngine(DefaultProfiles.Controller());
        Assert.Equal(0, Run(engine, new ControllerState { LeftTrigger = 0.01f }).Pad.LeftTrigger);
        Assert.InRange(Run(engine, new ControllerState { LeftTrigger = 0.5f }).Pad.LeftTrigger, 120, 130);
        Assert.Equal(255, Run(engine, new ControllerState { RightTrigger = 1f }).Pad.RightTrigger);
    }

    [Fact]
    public void Trigger_threshold_for_digital_binding()
    {
        var engine = new MappingEngine(DefaultProfiles.KeyboardMouse()); // RT → left click at 0.4
        Assert.Equal(MouseButtons.None, Run(engine, new ControllerState { RightTrigger = 0.39f }).Mouse);
        Assert.Equal(MouseButtons.Left, Run(engine, new ControllerState { RightTrigger = 0.40f }).Mouse);
    }

    [Fact]
    public void Keyboard_profile_wasd_from_left_stick()
    {
        var engine = new MappingEngine(DefaultProfiles.KeyboardMouse());
        var f = Run(engine, new ControllerState { LeftStick = new Vector2(0.9f, 0.9f) });
        Assert.True(f.Keys[(byte)Key.W]);
        Assert.True(f.Keys[(byte)Key.D]);
        Assert.False(f.Keys[(byte)Key.S]);
        Assert.False(f.Keys[(byte)Key.A]);
        Assert.False(f.UsesGamepad);
    }

    [Fact]
    public void Profile_switch_while_button_held_drops_old_output_and_flags_change()
    {
        var engine = new MappingEngine(DefaultProfiles.KeyboardMouse());
        var held = Pressed(PhysicalInput.A);
        Assert.True(Run(engine, held).Keys[(byte)Key.Space]);

        engine.SetProfile(DefaultProfiles.Controller());
        var f = Run(engine, held);
        Assert.True(engine.ProfileJustChanged);
        Assert.False(f.Keys[(byte)Key.Space]);
        Assert.Equal(XButtons.A, f.Pad.Buttons);

        Run(engine, held);
        Assert.False(engine.ProfileJustChanged);
    }

    [Fact]
    public void Ui_edits_after_SetProfile_do_not_leak_into_engine()
    {
        var p = DefaultProfiles.Controller();
        var engine = new MappingEngine(p);
        p.Buttons[PhysicalInput.A] = Binding.Pad(GamepadButton.Y);
        Assert.Equal(XButtons.A, Run(engine, Pressed(PhysicalInput.A)).Pad.Buttons);
    }

    [Fact]
    public void Gamepad_only_drops_every_keyboard_and_mouse_output()
    {
        var p = DefaultProfiles.Controller();
        p.Buttons[PhysicalInput.Qam] = Binding.Keyboard(Key.F12);
        p.RightPad.Mode = PadMode.Mouse;
        var engine = new MappingEngine(p);

        var s = Pressed(PhysicalInput.Qam, PhysicalInput.A);
        Assert.True(Run(engine, s).Keys[(byte)Key.F12]);

        engine.GamepadOnly = true;
        var f = Run(engine, s);
        Assert.False(f.UsesKeyboardMouse);
        Assert.False(f.Keys[(byte)Key.F12]);
        Assert.True(f.UsesGamepad);
        Assert.Equal(XButtons.A, f.Pad.Buttons);
    }

    [Fact]
    public void Wheel_binding_fires_once_per_press()
    {
        var p = DefaultProfiles.KeyboardMouse();
        p.Buttons[PhysicalInput.Y] = Binding.MouseButton(MouseAction.WheelDown);
        var engine = new MappingEngine(p);
        Assert.Equal(-1, Run(engine, Pressed(PhysicalInput.Y)).WheelNotches);
        Assert.Equal(0, Run(engine, Pressed(PhysicalInput.Y)).WheelNotches);
        Run(engine, new ControllerState());
        Assert.Equal(-1, Run(engine, Pressed(PhysicalInput.Y)).WheelNotches);
    }

    [Fact]
    public void Trackpad_mouse_ignores_touch_down_and_tracks_movement()
    {
        var engine = new MappingEngine(DefaultProfiles.KeyboardMouse());
        var p = DefaultProfiles.KeyboardMouse().RightPad;

        var f1 = Run(engine, new ControllerState { RightPad = new TouchPoint(true, 0.5f, 0.5f, 0.1f) });
        Assert.Equal(0f, f1.MouseDx);

        var f2 = Run(engine, new ControllerState { RightPad = new TouchPoint(true, 0.6f, 0.5f, 0.1f) });
        float expected = 0.1f * MappingEngine.PadMousePixelsPerWidth * (1 - p.Smoothing);
        Assert.Equal(expected, f2.MouseDx, 2);
        Assert.Equal(0f, f2.MouseDy, 3);
    }

    [Fact]
    public void Trackpad_inertia_glides_and_stops()
    {
        var engine = new MappingEngine(DefaultProfiles.KeyboardMouse());
        Run(engine, new ControllerState { RightPad = new TouchPoint(true, 0.2f, 0.5f, 0) });
        for (int i = 1; i <= 5; i++) Run(engine, new ControllerState { RightPad = new TouchPoint(true, 0.2f + i * 0.05f, 0.5f, 0) });

        var glide = Run(engine, new ControllerState());
        Assert.True(glide.MouseDx > 0);

        float dx = 0;
        for (int i = 0; i < 2000; i++) dx = Run(engine, new ControllerState()).MouseDx;
        Assert.Equal(0f, dx);
    }

    [Fact]
    public void Clickable_dpad_pad_needs_click()
    {
        var engine = new MappingEngine(DefaultProfiles.Controller());
        var touchUp = new ControllerState { LeftPad = new TouchPoint(true, 0.5f, 0.05f, 0.5f) };
        Assert.Equal(XButtons.None, Run(engine, touchUp).Pad.Buttons);

        touchUp.Set(PhysicalInput.LeftPadClick, true);
        Assert.Equal(XButtons.DpadUp, Run(engine, touchUp).Pad.Buttons);
    }

    [Fact]
    public void Gyro_needs_enable_control_when_configured()
    {
        var p = DefaultProfiles.KeyboardMouse();
        p.Gyro = new GyroConfig { Mode = GyroMode.Mouse, EnableWhile = PhysicalInput.RightGripTouch };
        var engine = new MappingEngine(p);
        var s = new ControllerState { MotionValid = true, Gyro = new Vector3(0, 1f, 0) };
        Assert.Equal(0f, Run(engine, s).MouseDx);

        s.Set(PhysicalInput.RightGripTouch, true);
        Assert.True(Run(engine, s).MouseDx < 0); // turning left moves the cursor left
    }

    [Fact]
    public void Missing_motion_data_never_moves_anything()
    {
        var p = DefaultProfiles.KeyboardMouse();
        p.Gyro.Mode = GyroMode.Mouse;
        var s = new ControllerState { MotionValid = false, Gyro = new Vector3(5, 5, 5) };
        var f = Run(new MappingEngine(p), s);
        Assert.Equal(0f, f.MouseDx);
        Assert.Equal(0f, f.MouseDy);
    }
}
