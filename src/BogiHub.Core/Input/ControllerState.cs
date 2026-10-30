using System.Numerics;

namespace BogiHub.Core.Input;

/// <summary>A single trackpad contact. X and Y run 0..1 with (0,0) at the top-left.</summary>
public readonly record struct TouchPoint(bool Touching, float X, float Y, float Pressure)
{
    /// <summary>Position relative to the pad centre, -1..1, with +Y pointing up.</summary>
    public Vector2 Centered => new((X - 0.5f) * 2f, (0.5f - Y) * 2f);
}

/// <summary>What the device can actually report. Anything not listed must be shown as unavailable, never invented.</summary>
[Flags]
public enum Capabilities
{
    None = 0,
    Buttons = 1 << 0,
    Sticks = 1 << 1,
    AnalogTriggers = 1 << 2,
    Trackpads = 1 << 3,
    TrackpadPressure = 1 << 4,
    Gyro = 1 << 5,
    Accelerometer = 1 << 6,
    Battery = 1 << 7,
    Rumble = 1 << 8,
    CapacitiveTouch = 1 << 9,
}

/// <summary>
/// Normalised, timestamped controller snapshot. Value type so it can be copied between threads without allocation.
/// Sticks: -1..1 with +Y up. Triggers: 0..1. Gyro: rad/s (SDL axes: X pitch, Y yaw, Z roll). Accel: m/s².
/// </summary>
public struct ControllerState
{
    public long TimestampNs;
    public ulong Buttons;
    public Vector2 LeftStick;
    public Vector2 RightStick;
    public float LeftTrigger;
    public float RightTrigger;
    public TouchPoint LeftPad;
    public TouchPoint RightPad;
    public Vector3 Gyro;
    public Vector3 Accel;
    public bool MotionValid;

    public readonly bool IsPressed(PhysicalInput input) => (Buttons & (1UL << (int)input)) != 0;

    public void Set(PhysicalInput input, bool pressed)
    {
        ulong bit = 1UL << (int)input;
        Buttons = pressed ? Buttons | bit : Buttons & ~bit;
    }
}

/// <summary>Device-level facts that change rarely (shown on the dashboard).</summary>
public sealed record DeviceInfo(
    string Name,
    ushort VendorId,
    ushort ProductId,
    string Transport,
    string? Serial,
    Capabilities Capabilities);

public enum PowerStatus { Unknown, OnBattery, NoBattery, Charging, Charged }

public readonly record struct BatteryInfo(PowerStatus Status, int Percent)
{
    public bool HasPercent => Percent >= 0;
    public static readonly BatteryInfo Unknown = new(PowerStatus.Unknown, -1);
}
