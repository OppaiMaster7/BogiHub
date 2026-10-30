using System.Buffers.Binary;
using System.Numerics;

namespace BogiHub.Core.Input;

/// <summary>
/// Decodes the Steam Controller (2026, "Triton") HID input reports directly, mirroring SDL 3.4.18
/// (SDL_hidapi_steam_triton.c + steam/controller_structs.h). Used to keep reading the controller while its own
/// built-in mouse/keyboard mode is active: opening it through SDL would switch that mode off.
/// </summary>
public static class TritonReport
{
    public const byte IdState = 0x42, IdBattery = 0x43, IdStateBle = 0x45, IdStateTimestamp = 0x47;

    /// <summary>Report ID byte + 45-byte state payload (both state layouts are 45 bytes).</summary>
    public const int StateReportLength = 46;

    // Button bits (TritonButtons in SDL). SDL maps BACK to the bit named MENU and START to the bit named VIEW;
    // BogiHub follows SDL so both input paths agree.
    private static readonly (uint Bit, PhysicalInput Input)[] ButtonBits =
    [
        (0x00000001, PhysicalInput.A),
        (0x00000002, PhysicalInput.B),
        (0x00000004, PhysicalInput.X),
        (0x00000008, PhysicalInput.Y),
        (0x00000010, PhysicalInput.Qam),
        (0x00000020, PhysicalInput.RightStickClick),
        (0x00000040, PhysicalInput.Menu),
        (0x00000080, PhysicalInput.RightPaddle1),
        (0x00000100, PhysicalInput.RightPaddle2),
        (0x00000200, PhysicalInput.RB),
        (0x00000400, PhysicalInput.DpadDown),
        (0x00000800, PhysicalInput.DpadRight),
        (0x00001000, PhysicalInput.DpadLeft),
        (0x00002000, PhysicalInput.DpadUp),
        (0x00004000, PhysicalInput.View),
        (0x00008000, PhysicalInput.LeftStickClick),
        (0x00010000, PhysicalInput.Steam),
        (0x00020000, PhysicalInput.LeftPaddle1),
        (0x00040000, PhysicalInput.LeftPaddle2),
        (0x00080000, PhysicalInput.LB),
        (0x00100000, PhysicalInput.RightStickTouch),
        (0x00400000, PhysicalInput.RightPadClick),
        (0x01000000, PhysicalInput.LeftStickTouch),
        (0x04000000, PhysicalInput.LeftPadClick),
        (0x10000000, PhysicalInput.RightGripTouch),
        (0x20000000, PhysicalInput.LeftGripTouch),
    ];

    private const uint RightPadTouch = 0x00200000, LeftPadTouch = 0x02000000;
    private const float GyroRadPerCount = 2000f * (MathF.PI / 180f) / 32768f;
    private const float AccelPerCount = 2f * 9.80665f / 32768f;

    public static bool IsState(byte id) => id is IdState or IdStateBle or IdStateTimestamp;

    /// <summary>
    /// Tracks the IMU timestamp across reports. When the motion sensor is not streaming (the controller was not
    /// opened by a game API), reports repeat the last IMU sample forever; a timestamp that stops moving means
    /// "no motion data", not "the controller is perfectly still".
    /// </summary>
    public struct ImuClock
    {
        public uint LastTimestamp;
        public int Unchanged;
        public bool Seen;
    }

    /// <summary>Stateless convenience overload (motion is trusted if the accelerometer is non-zero).</summary>
    public static bool TryParseState(ReadOnlySpan<byte> report, ref ControllerState s)
    {
        var clock = new ImuClock();
        return TryParseState(report, ref s, ref clock);
    }

    /// <summary>
    /// Updates <paramref name="s"/> from one input report (including its leading report-ID byte).
    /// Returns false if it is not a state report or is too short. Trackpad positions are kept while not touched.
    /// </summary>
    public static bool TryParseState(ReadOnlySpan<byte> report, ref ControllerState s, ref ImuClock clock)
    {
        if (report.Length < StateReportLength || !IsState(report[0])) return false;
        var p = report[1..];
        bool timestamped = report[0] == IdStateTimestamp; // adds a 16-bit trackpad timestamp before the pads

        uint buttons = BinaryPrimitives.ReadUInt32LittleEndian(p[1..]);
        ulong mask = 0;
        foreach (var (bit, input) in ButtonBits)
            if ((buttons & bit) != 0) mask |= 1UL << (int)input;
        s.Buttons = mask;

        s.LeftTrigger = Math.Clamp(I16(p, 5) / 32767f, 0f, 1f);
        s.RightTrigger = Math.Clamp(I16(p, 7) / 32767f, 0f, 1f);
        s.LeftStick = new Vector2(Axis(I16(p, 9)), Axis(I16(p, 11)));
        s.RightStick = new Vector2(Axis(I16(p, 13)), Axis(I16(p, 15)));

        int pad = timestamped ? 19 : 17;
        s.LeftPad = Pad(p, pad, (buttons & LeftPadTouch) != 0, s.LeftPad);
        s.RightPad = Pad(p, pad + 6, (buttons & RightPadTouch) != 0, s.RightPad);

        // IMU block: accel XYZ then gyro XYZ, at the same offset in both layouts (the timestamped layout trades two
        // bytes of IMU timestamp for its trackpad timestamp). Converted to SDL's axis order and units.
        const int imu = 33;
        short ax = I16(p, imu), ay = I16(p, imu + 2), az = I16(p, imu + 4);
        short gx = I16(p, imu + 6), gy = I16(p, imu + 8), gz = I16(p, imu + 10);
        s.Accel = new Vector3(ax * AccelPerCount, az * AccelPerCount, -ay * AccelPerCount);
        s.Gyro = new Vector3(gx * GyroRadPerCount, gz * GyroRadPerCount, -gy * GyroRadPerCount);
        // The IMU only streams when enabled. An all-zero accelerometer, or a timestamp that has stopped advancing
        // (stale sample repeated in every report), means "no motion data".
        uint imuTime = timestamped ? BinaryPrimitives.ReadUInt16LittleEndian(p[31..]) : BinaryPrimitives.ReadUInt32LittleEndian(p[29..]);
        if (clock.Seen && imuTime == clock.LastTimestamp) clock.Unchanged++;
        else clock.Unchanged = 0;
        clock.LastTimestamp = imuTime;
        clock.Seen = true;
        s.MotionValid = (ax != 0 || ay != 0 || az != 0) && clock.Unchanged < StaleImuReports;
        return true;
    }

    /// <summary>Consecutive reports with the same IMU timestamp after which motion is considered not streaming.</summary>
    public const int StaleImuReports = 4;

    /// <summary>Battery report: (percent 0..100, status).</summary>
    public static bool TryParseBattery(ReadOnlySpan<byte> report, out BatteryInfo battery)
    {
        battery = BatteryInfo.Unknown;
        if (report.Length < 3 || report[0] != IdBattery) return false;
        var status = report[1] switch
        {
            1 => PowerStatus.OnBattery,
            2 => PowerStatus.Charging,
            4 => PowerStatus.Charged,
            _ => PowerStatus.Unknown,
        };
        battery = new BatteryInfo(status, Math.Min((int)report[2], 100));
        return true;
    }

    private static short I16(ReadOnlySpan<byte> p, int offset) => BinaryPrimitives.ReadInt16LittleEndian(p[offset..]);

    private static float Axis(short v) => Math.Max(v / 32767f, -1f);

    private static TouchPoint Pad(ReadOnlySpan<byte> p, int offset, bool touching, TouchPoint previous)
    {
        if (!touching) return previous with { Touching = false, Pressure = 0 };
        float x = I16(p, offset) / 65536f + 0.5f;
        float y = -I16(p, offset + 2) / 65536f + 0.5f;
        float pressure = BinaryPrimitives.ReadUInt16LittleEndian(p[(offset + 4)..]) / 32768f;
        return new TouchPoint(true, x, y, pressure);
    }
}
