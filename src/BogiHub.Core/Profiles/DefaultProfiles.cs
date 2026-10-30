using BogiHub.Core.Input;
using BogiHub.Core.Output;

namespace BogiHub.Core.Profiles;

/// <summary>Built-in presets. They are regenerated on demand, so "restore defaults" always has a clean source.</summary>
public static class DefaultProfiles
{
    public const string ControllerId = "default-controller";
    public const string KeyboardMouseId = "default-keyboard-mouse";

    public static IReadOnlyList<Profile> All() => [Controller(), KeyboardMouse()];

    public static Profile? ById(string id) => All().FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Plain Xbox controller: what games such as Fortnite expect from a gamepad.
    /// Paddles duplicate common actions so neither thumb has to leave a stick; trackpads stay off the right thumb's
    /// stick path (left pad is a clickable D-pad, right pad is unused) so no default needs a stick and a pad at once.
    /// </summary>
    public static Profile Controller() => new()
    {
        Id = ControllerId,
        Name = "Controller",
        Description = "Acts as a standard Xbox controller. Best default for games with controller support.",
        Output = OutputMode.Gamepad,
        Buttons = new()
        {
            [PhysicalInput.A] = Binding.Pad(GamepadButton.A),
            [PhysicalInput.B] = Binding.Pad(GamepadButton.B),
            [PhysicalInput.X] = Binding.Pad(GamepadButton.X),
            [PhysicalInput.Y] = Binding.Pad(GamepadButton.Y),
            [PhysicalInput.LB] = Binding.Pad(GamepadButton.LB),
            [PhysicalInput.RB] = Binding.Pad(GamepadButton.RB),
            [PhysicalInput.View] = Binding.Pad(GamepadButton.Back),
            [PhysicalInput.Menu] = Binding.Pad(GamepadButton.Start),
            [PhysicalInput.Steam] = Binding.Pad(GamepadButton.Guide),
            [PhysicalInput.LeftStickClick] = Binding.Pad(GamepadButton.LeftStick),
            [PhysicalInput.RightStickClick] = Binding.Pad(GamepadButton.RightStick),
            [PhysicalInput.DpadUp] = Binding.Pad(GamepadButton.DpadUp),
            [PhysicalInput.DpadDown] = Binding.Pad(GamepadButton.DpadDown),
            [PhysicalInput.DpadLeft] = Binding.Pad(GamepadButton.DpadLeft),
            [PhysicalInput.DpadRight] = Binding.Pad(GamepadButton.DpadRight),
            [PhysicalInput.LeftPaddle1] = Binding.Pad(GamepadButton.LeftStick),
            [PhysicalInput.RightPaddle1] = Binding.Pad(GamepadButton.A),
            [PhysicalInput.LeftPaddle2] = Binding.Pad(GamepadButton.X),
            [PhysicalInput.RightPaddle2] = Binding.Pad(GamepadButton.B),
        },
        LeftStick = new StickConfig { Mode = StickMode.LeftStick },
        RightStick = new StickConfig { Mode = StickMode.RightStick },
        LeftTrigger = new TriggerConfig { Mode = TriggerMode.LeftTrigger },
        RightTrigger = new TriggerConfig { Mode = TriggerMode.RightTrigger },
        LeftPad = new TrackpadConfig
        {
            Mode = PadMode.Directional,
            RequireClick = true,
            Up = Binding.Pad(GamepadButton.DpadUp),
            Down = Binding.Pad(GamepadButton.DpadDown),
            Left = Binding.Pad(GamepadButton.DpadLeft),
            Right = Binding.Pad(GamepadButton.DpadRight),
        },
        RightPad = new TrackpadConfig { Mode = PadMode.None },
        Gyro = new GyroConfig { Mode = GyroMode.Off },
    };

    /// <summary>Keyboard and mouse for games without controller support. Right pad and right stick both aim.</summary>
    public static Profile KeyboardMouse() => new()
    {
        Id = KeyboardMouseId,
        Name = "Keyboard & Mouse",
        Description = "WASD movement, trackpad/stick mouse aiming. For games without controller support.",
        Output = OutputMode.KeyboardMouse,
        Buttons = new()
        {
            [PhysicalInput.A] = Binding.Keyboard(Key.Space),
            [PhysicalInput.B] = Binding.Keyboard(Key.LeftCtrl),
            [PhysicalInput.X] = Binding.Keyboard(Key.R),
            [PhysicalInput.Y] = Binding.Keyboard(Key.F),
            [PhysicalInput.LB] = Binding.Keyboard(Key.Q),
            [PhysicalInput.RB] = Binding.Keyboard(Key.E),
            [PhysicalInput.View] = Binding.Keyboard(Key.Tab),
            [PhysicalInput.Menu] = Binding.Keyboard(Key.Escape),
            [PhysicalInput.LeftStickClick] = Binding.Keyboard(Key.LeftShift),
            [PhysicalInput.RightStickClick] = Binding.Keyboard(Key.V),
            [PhysicalInput.DpadUp] = Binding.Keyboard(Key.D1),
            [PhysicalInput.DpadRight] = Binding.Keyboard(Key.D2),
            [PhysicalInput.DpadDown] = Binding.Keyboard(Key.D3),
            [PhysicalInput.DpadLeft] = Binding.Keyboard(Key.D4),
            [PhysicalInput.LeftPaddle1] = Binding.Keyboard(Key.LeftShift),
            [PhysicalInput.RightPaddle1] = Binding.Keyboard(Key.Space),
            [PhysicalInput.LeftPaddle2] = Binding.Keyboard(Key.C),
            [PhysicalInput.RightPaddle2] = Binding.Keyboard(Key.G),
            [PhysicalInput.RightPadClick] = Binding.MouseButton(MouseAction.Left),
        },
        LeftStick = new StickConfig
        {
            Mode = StickMode.Keys,
            Deadzone = 0.15f,
            Up = Binding.Keyboard(Key.W),
            Down = Binding.Keyboard(Key.S),
            Left = Binding.Keyboard(Key.A),
            Right = Binding.Keyboard(Key.D),
            KeyThreshold = 0.35f,
        },
        RightStick = new StickConfig { Mode = StickMode.Mouse, MouseSpeed = 1400f },
        LeftTrigger = new TriggerConfig { Mode = TriggerMode.Button, Binding = Binding.MouseButton(MouseAction.Right), Threshold = 0.4f },
        RightTrigger = new TriggerConfig { Mode = TriggerMode.Button, Binding = Binding.MouseButton(MouseAction.Left), Threshold = 0.4f },
        LeftPad = new TrackpadConfig { Mode = PadMode.Scroll },
        RightPad = new TrackpadConfig { Mode = PadMode.Mouse, Inertia = true },
        Gyro = new GyroConfig { Mode = GyroMode.Off },
    };
}
