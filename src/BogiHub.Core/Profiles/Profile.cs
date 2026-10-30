using BogiHub.Core.Input;

namespace BogiHub.Core.Profiles;

/// <summary>
/// Declares which virtual devices a profile drives. Gamepad plugs in the virtual Xbox controller;
/// KeyboardMouse never does; Hybrid uses both (gamepad movement + mouse-style aiming).
/// </summary>
public enum OutputMode { Gamepad, KeyboardMouse, Hybrid }

public enum StickMode { None, LeftStick, RightStick, Keys, Mouse }

public enum TriggerMode { None, LeftTrigger, RightTrigger, Button }

public enum PadMode { None, Mouse, Directional, LeftStick, RightStick, Scroll }

public enum GyroMode { Off, Mouse, RightStick }

/// <summary>
/// Which rotation turns left and right. PlayerSpace follows the real world's up axis however the controller is held;
/// Yaw spins it flat like a steering wheel lying down; Roll tilts it like a steering wheel.
/// </summary>
public enum GyroTurnAxis { PlayerSpace, Yaw, Roll }

public sealed class StickConfig
{
    public StickMode Mode { get; set; } = StickMode.None;
    /// <summary>Inner radial deadzone, fraction of full deflection.</summary>
    public float Deadzone { get; set; } = 0.10f;
    /// <summary>Outer deadzone: deflection beyond (1 - this) counts as full.</summary>
    public float OuterDeadzone { get; set; } = 0.03f;
    public float Sensitivity { get; set; } = 1.0f;
    public bool InvertY { get; set; }

    /// <summary>Keys mode: bindings for each direction and the deflection needed to press them.</summary>
    public Binding Up { get; set; } = Binding.None;
    public Binding Down { get; set; } = Binding.None;
    public Binding Left { get; set; } = Binding.None;
    public Binding Right { get; set; } = Binding.None;
    public float KeyThreshold { get; set; } = 0.5f;

    /// <summary>Mouse mode: cursor speed in pixels per second at full deflection.</summary>
    public float MouseSpeed { get; set; } = 1200f;
}

public sealed class TriggerConfig
{
    public TriggerMode Mode { get; set; } = TriggerMode.None;
    /// <summary>Analog modes: pulls below this are ignored.</summary>
    public float Deadzone { get; set; } = 0.02f;
    /// <summary>Button mode: binding fired once the pull passes <see cref="Threshold"/>.</summary>
    public Binding Binding { get; set; } = Binding.None;
    public float Threshold { get; set; } = 0.5f;
}

public sealed class TrackpadConfig
{
    public PadMode Mode { get; set; } = PadMode.None;
    public float Sensitivity { get; set; } = 1.0f;

    /// <summary>Mouse mode: exponential smoothing 0 (off) .. 0.9 (heavy).</summary>
    public float Smoothing { get; set; } = 0.2f;
    /// <summary>Mouse mode: keep gliding after a flick, like a trackball.</summary>
    public bool Inertia { get; set; } = true;
    /// <summary>Mouse mode: how quickly a trackball glide slows down (per second, 0..1 of speed lost).</summary>
    public float Friction { get; set; } = 0.92f;

    /// <summary>Directional / stick modes: centre area that produces nothing.</summary>
    public float Deadzone { get; set; } = 0.20f;
    /// <summary>Directional / stick modes: only active while the pad is clicked in.</summary>
    public bool RequireClick { get; set; } = true;
    public Binding Up { get; set; } = Binding.None;
    public Binding Down { get; set; } = Binding.None;
    public Binding Left { get; set; } = Binding.None;
    public Binding Right { get; set; } = Binding.None;
    /// <summary>Little ticks under the thumb as the cursor rolls or the wheel notches, a click on a new direction.</summary>
    public bool Haptics { get; set; } = true;
}

public sealed class GyroConfig
{
    public GyroMode Mode { get; set; } = GyroMode.Off;
    public float Sensitivity { get; set; } = 1.0f;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    /// <summary>When set, gyro only acts while this control is held or touched (e.g. right grip touch).</summary>
    public PhysicalInput? EnableWhile { get; set; }
    /// <summary>When set, gyro pauses while this control is held or touched (e.g. lift the thumb off the stick to re-centre).</summary>
    public PhysicalInput? DisableWhile { get; set; }
    public GyroTurnAxis TurnAxis { get; set; } = GyroTurnAxis.PlayerSpace;
    /// <summary>Up and down speed relative to left and right.</summary>
    public float VerticalScale { get; set; } = 1.0f;
    /// <summary>0..1: calms hand shake at very low speeds; never delays real movement.</summary>
    public float Smoothing { get; set; } = 0.25f;
    /// <summary>0..0.9: how much slower slow movements are, for fine aim. 0 = the same speed everywhere.</summary>
    public float Precision { get; set; }
}

/// <summary>A complete, data-only layout. Saved as versioned JSON; never contains executable scripts.</summary>
public sealed class Profile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public string? Description { get; set; }
    public OutputMode Output { get; set; } = OutputMode.Gamepad;

    /// <summary>Executable file names (e.g. "FortniteClient-Win64-Shipping.exe") that select this profile automatically.</summary>
    public List<string> Executables { get; set; } = [];

    public Dictionary<PhysicalInput, Binding> Buttons { get; set; } = [];
    public StickConfig LeftStick { get; set; } = new();
    public StickConfig RightStick { get; set; } = new();
    public TriggerConfig LeftTrigger { get; set; } = new();
    public TriggerConfig RightTrigger { get; set; } = new();
    public TrackpadConfig LeftPad { get; set; } = new();
    public TrackpadConfig RightPad { get; set; } = new();
    public GyroConfig Gyro { get; set; } = new();

    public Binding GetBinding(PhysicalInput input) => Buttons.TryGetValue(input, out var b) ? b : Binding.None;

    public Profile Clone() => ProfileJson.Deserialize(ProfileJson.Serialize(this));
}
