using BogiHub.Core.Input;

namespace BogiHub.Core.Profiles;

public static class ProfileValidator
{
    public const int MaxNameLength = 64;
    public const int MaxExecutables = 32;

    /// <summary>Returns human-readable problems. An empty list means the profile is safe to use.</summary>
    public static List<string> Validate(Profile? p)
    {
        var errors = new List<string>();
        if (p is null)
        {
            errors.Add("Profile is empty.");
            return errors;
        }

        if (p.SchemaVersion < 1 || p.SchemaVersion > Profile.CurrentSchemaVersion)
            errors.Add($"Unsupported schemaVersion {p.SchemaVersion} (this BogiHub understands 1..{Profile.CurrentSchemaVersion}).");
        if (string.IsNullOrWhiteSpace(p.Id) || p.Id.Length > 64 || p.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            errors.Add("Id must be 1-64 letters, digits, '-' or '_'.");
        if (string.IsNullOrWhiteSpace(p.Name)) errors.Add("Name is required.");
        else if (p.Name.Length > MaxNameLength) errors.Add($"Name is longer than {MaxNameLength} characters.");
        if (!Enum.IsDefined(p.Output)) errors.Add("Unknown output mode.");

        p.Executables ??= [];
        if (p.Executables.Count > MaxExecutables) errors.Add($"More than {MaxExecutables} executables.");
        foreach (string exe in p.Executables)
        {
            if (string.IsNullOrWhiteSpace(exe) || exe.Length > 260 || exe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                errors.Add($"\"{exe}\" is not a valid executable file name (use just the name, e.g. game.exe).");
        }

        p.Buttons ??= [];
        foreach (var input in p.Buttons.Keys)
            if (!Enum.IsDefined(input)) errors.Add($"Unknown control {(int)input}.");

        if (p.LeftStick is null || p.RightStick is null || p.LeftTrigger is null || p.RightTrigger is null ||
            p.LeftPad is null || p.RightPad is null || p.Gyro is null)
        {
            errors.Add("A stick, trigger, trackpad or gyro section is missing.");
            return errors;
        }

        Stick("leftStick", p.LeftStick, errors);
        Stick("rightStick", p.RightStick, errors);
        Trigger("leftTrigger", p.LeftTrigger, errors);
        Trigger("rightTrigger", p.RightTrigger, errors);
        Pad("leftPad", p.LeftPad, errors);
        Pad("rightPad", p.RightPad, errors);

        Range("gyro.sensitivity", p.Gyro.Sensitivity, 0.05f, 20f, errors);
        if (p.Gyro.EnableWhile is { } ew && !Enum.IsDefined(ew)) errors.Add("gyro.enableWhile is not a known control.");

        if (p.Output == OutputMode.KeyboardMouse && UsesGamepad(p))
            errors.Add("This keyboard & mouse profile binds Xbox controller outputs. Switch it to Gamepad or Hybrid, or rebind those controls.");

        return errors;
    }

    public static bool UsesGamepad(Profile p) =>
        p.Buttons.Values.Any(b => b.Kind == BindingKind.Gamepad) ||
        p.LeftStick.Mode is StickMode.LeftStick or StickMode.RightStick ||
        p.RightStick.Mode is StickMode.LeftStick or StickMode.RightStick ||
        p.LeftTrigger.Mode is TriggerMode.LeftTrigger or TriggerMode.RightTrigger ||
        p.RightTrigger.Mode is TriggerMode.LeftTrigger or TriggerMode.RightTrigger ||
        p.LeftPad.Mode is PadMode.LeftStick or PadMode.RightStick ||
        p.RightPad.Mode is PadMode.LeftStick or PadMode.RightStick ||
        p.Gyro.Mode == GyroMode.RightStick ||
        new[] { p.LeftTrigger.Binding, p.RightTrigger.Binding, p.LeftStick.Up, p.LeftStick.Down, p.LeftStick.Left, p.LeftStick.Right,
                p.RightStick.Up, p.RightStick.Down, p.RightStick.Left, p.RightStick.Right, p.LeftPad.Up, p.LeftPad.Down,
                p.LeftPad.Left, p.LeftPad.Right, p.RightPad.Up, p.RightPad.Down, p.RightPad.Left, p.RightPad.Right }
            .Any(b => b.Kind == BindingKind.Gamepad);

    private static void Stick(string name, StickConfig s, List<string> errors)
    {
        if (!Enum.IsDefined(s.Mode)) errors.Add($"{name}.mode is unknown.");
        Range($"{name}.deadzone", s.Deadzone, 0f, 0.9f, errors);
        Range($"{name}.outerDeadzone", s.OuterDeadzone, 0f, 0.5f, errors);
        if (s.Deadzone + s.OuterDeadzone >= 0.95f) errors.Add($"{name}: deadzone + outerDeadzone leave no usable range.");
        Range($"{name}.sensitivity", s.Sensitivity, 0.1f, 5f, errors);
        Range($"{name}.keyThreshold", s.KeyThreshold, 0.05f, 0.99f, errors);
        Range($"{name}.mouseSpeed", s.MouseSpeed, 10f, 20000f, errors);
    }

    private static void Trigger(string name, TriggerConfig t, List<string> errors)
    {
        if (!Enum.IsDefined(t.Mode)) errors.Add($"{name}.mode is unknown.");
        Range($"{name}.deadzone", t.Deadzone, 0f, 0.9f, errors);
        Range($"{name}.threshold", t.Threshold, 0.02f, 1f, errors);
    }

    private static void Pad(string name, TrackpadConfig t, List<string> errors)
    {
        if (!Enum.IsDefined(t.Mode)) errors.Add($"{name}.mode is unknown.");
        Range($"{name}.sensitivity", t.Sensitivity, 0.05f, 10f, errors);
        Range($"{name}.smoothing", t.Smoothing, 0f, 0.9f, errors);
        Range($"{name}.friction", t.Friction, 0f, 1f, errors);
        Range($"{name}.deadzone", t.Deadzone, 0f, 0.9f, errors);
    }

    private static void Range(string name, float v, float min, float max, List<string> errors)
    {
        if (float.IsNaN(v) || v < min || v > max) errors.Add($"{name} must be between {min} and {max} (was {v}).");
    }
}
