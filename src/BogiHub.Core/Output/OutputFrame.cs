namespace BogiHub.Core.Output;

/// <summary>Xbox 360 / XInput button bits (same values as XINPUT_GAMEPAD_* and ViGEm's Xbox360Button).</summary>
[Flags]
public enum XButtons : ushort
{
    None = 0,
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

/// <summary>Virtual Xbox controller report. Stick Y is positive up, as in XInput.</summary>
public struct GamepadReport : IEquatable<GamepadReport>
{
    public XButtons Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short LeftX;
    public short LeftY;
    public short RightX;
    public short RightY;

    public static readonly GamepadReport Neutral = default;

    public readonly bool Equals(GamepadReport o) =>
        Buttons == o.Buttons && LeftTrigger == o.LeftTrigger && RightTrigger == o.RightTrigger &&
        LeftX == o.LeftX && LeftY == o.LeftY && RightX == o.RightX && RightY == o.RightY;

    public override readonly bool Equals(object? obj) => obj is GamepadReport r && Equals(r);
    public override readonly int GetHashCode() => HashCode.Combine(Buttons, LeftTrigger, RightTrigger, LeftX, LeftY, RightX, RightY);
}

[Flags]
public enum MouseButtons : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
    X1 = 8,
    X2 = 16,
}

/// <summary>
/// Everything the mapping engine wants the outputs to do for one input sample.
/// Reused between samples to avoid allocation on the input thread.
/// </summary>
public sealed class OutputFrame
{
    public GamepadReport Pad;
    public readonly bool[] Keys = new bool[256];
    public MouseButtons Mouse;
    /// <summary>Relative mouse movement in pixels for this sample (fractional; the sink keeps the remainder).</summary>
    public float MouseDx, MouseDy;
    /// <summary>Wheel movement in notches (positive = up/away).</summary>
    public int WheelNotches;

    public bool UsesGamepad;
    public bool UsesKeyboardMouse;

    public void Clear()
    {
        Pad = GamepadReport.Neutral;
        Array.Clear(Keys);
        Mouse = MouseButtons.None;
        MouseDx = MouseDy = 0;
        WheelNotches = 0;
    }

    public bool AnyKeyDown()
    {
        foreach (bool k in Keys) if (k) return true;
        return false;
    }
}
