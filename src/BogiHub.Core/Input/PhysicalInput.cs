namespace BogiHub.Core.Input;

/// <summary>
/// Every digital control on the Steam Controller (2026).
/// Values 0..21 equal SDL's raw joystick button indices for the Triton driver; the D-pad comes from SDL's hat.
/// </summary>
public enum PhysicalInput
{
    A = 0,
    B = 1,
    X = 2,
    Y = 3,
    View = 4,
    Steam = 5,
    Menu = 6,
    LeftStickClick = 7,
    RightStickClick = 8,
    LB = 9,
    RB = 10,
    Qam = 11,
    RightPaddle1 = 12,
    LeftPaddle1 = 13,
    RightPaddle2 = 14,
    LeftPaddle2 = 15,
    RightPadClick = 16,
    LeftPadClick = 17,
    RightStickTouch = 18,
    LeftStickTouch = 19,
    RightGripTouch = 20,
    LeftGripTouch = 21,
    DpadUp = 22,
    DpadDown = 23,
    DpadLeft = 24,
    DpadRight = 25,
}

public static class PhysicalInputs
{
    public const int Count = 26;

    /// <summary>Number of leading inputs that map 1:1 to SDL raw joystick buttons.</summary>
    public const int SdlButtonCount = 22;

    public static readonly IReadOnlyList<PhysicalInput> All = Enum.GetValues<PhysicalInput>();

    /// <summary>Capacitive sensors: useful as modifiers (e.g. gyro enable), but not offered as default bindings.</summary>
    public static bool IsTouchSensor(PhysicalInput input) =>
        input is PhysicalInput.RightStickTouch or PhysicalInput.LeftStickTouch
            or PhysicalInput.RightGripTouch or PhysicalInput.LeftGripTouch;

    public static string DisplayName(PhysicalInput input) => input switch
    {
        PhysicalInput.LeftStickClick => "L3 (left stick click)",
        PhysicalInput.RightStickClick => "R3 (right stick click)",
        PhysicalInput.Qam => "Quick Access (…)",
        PhysicalInput.RightPaddle1 => "Right paddle 1",
        PhysicalInput.LeftPaddle1 => "Left paddle 1",
        PhysicalInput.RightPaddle2 => "Right paddle 2",
        PhysicalInput.LeftPaddle2 => "Left paddle 2",
        PhysicalInput.RightPadClick => "Right trackpad click",
        PhysicalInput.LeftPadClick => "Left trackpad click",
        PhysicalInput.RightStickTouch => "Right stick touch",
        PhysicalInput.LeftStickTouch => "Left stick touch",
        PhysicalInput.RightGripTouch => "Right grip touch",
        PhysicalInput.LeftGripTouch => "Left grip touch",
        PhysicalInput.DpadUp => "D-pad up",
        PhysicalInput.DpadDown => "D-pad down",
        PhysicalInput.DpadLeft => "D-pad left",
        PhysicalInput.DpadRight => "D-pad right",
        _ => input.ToString(),
    };
}
