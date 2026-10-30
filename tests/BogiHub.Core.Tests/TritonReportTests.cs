using System.Buffers.Binary;
using BogiHub.Core.Input;

namespace BogiHub.Core.Tests;

public class TritonReportTests
{
    /// <summary>Builds a 46-byte state report the way the controller lays it out (TritonMTUNoQuat_t).</summary>
    private static byte[] State(uint buttons = 0, short lt = 0, short rt = 0, short lx = 0, short ly = 0, short rx = 0, short ry = 0,
        short lpx = 0, short lpy = 0, ushort lpp = 0, short rpx = 0, short rpy = 0, ushort rpp = 0,
        short ax = 0, short ay = 0, short az = 0, short gx = 0, short gy = 0, short gz = 0, byte id = TritonReport.IdState)
    {
        var r = new byte[64];
        r[0] = id;
        var p = r.AsSpan(1);
        p[0] = 7; // sequence
        BinaryPrimitives.WriteUInt32LittleEndian(p[1..], buttons);
        BinaryPrimitives.WriteInt16LittleEndian(p[5..], lt);
        BinaryPrimitives.WriteInt16LittleEndian(p[7..], rt);
        BinaryPrimitives.WriteInt16LittleEndian(p[9..], lx);
        BinaryPrimitives.WriteInt16LittleEndian(p[11..], ly);
        BinaryPrimitives.WriteInt16LittleEndian(p[13..], rx);
        BinaryPrimitives.WriteInt16LittleEndian(p[15..], ry);
        int pad = id == TritonReport.IdStateTimestamp ? 19 : 17;
        BinaryPrimitives.WriteInt16LittleEndian(p[pad..], lpx);
        BinaryPrimitives.WriteInt16LittleEndian(p[(pad + 2)..], lpy);
        BinaryPrimitives.WriteUInt16LittleEndian(p[(pad + 4)..], lpp);
        BinaryPrimitives.WriteInt16LittleEndian(p[(pad + 6)..], rpx);
        BinaryPrimitives.WriteInt16LittleEndian(p[(pad + 8)..], rpy);
        BinaryPrimitives.WriteUInt16LittleEndian(p[(pad + 10)..], rpp);
        BinaryPrimitives.WriteInt16LittleEndian(p[33..], ax);
        BinaryPrimitives.WriteInt16LittleEndian(p[35..], ay);
        BinaryPrimitives.WriteInt16LittleEndian(p[37..], az);
        BinaryPrimitives.WriteInt16LittleEndian(p[39..], gx);
        BinaryPrimitives.WriteInt16LittleEndian(p[41..], gy);
        BinaryPrimitives.WriteInt16LittleEndian(p[43..], gz);
        return r;
    }

    [Fact]
    public void Quick_access_button_is_decoded()
    {
        var s = new ControllerState();
        Assert.True(TritonReport.TryParseState(State(buttons: 0x10), ref s));
        Assert.True(s.IsPressed(PhysicalInput.Qam));
        Assert.False(s.IsPressed(PhysicalInput.A));
    }

    [Theory]
    [InlineData(0x00000001u, PhysicalInput.A)]
    [InlineData(0x00000004u, PhysicalInput.X)]
    [InlineData(0x00010000u, PhysicalInput.Steam)]
    [InlineData(0x00080000u, PhysicalInput.LB)]
    [InlineData(0x00000200u, PhysicalInput.RB)]
    [InlineData(0x00020000u, PhysicalInput.LeftPaddle1)]
    [InlineData(0x00000100u, PhysicalInput.RightPaddle2)]
    [InlineData(0x00002000u, PhysicalInput.DpadUp)]
    [InlineData(0x00004000u, PhysicalInput.View)] // SDL maps BACK to the bit named MENU
    [InlineData(0x00000040u, PhysicalInput.Menu)] // and START to the bit named VIEW
    [InlineData(0x10000000u, PhysicalInput.RightGripTouch)]
    public void Each_button_bit_maps_like_sdl(uint bit, PhysicalInput expected)
    {
        var s = new ControllerState();
        TritonReport.TryParseState(State(buttons: bit), ref s);
        Assert.True(s.IsPressed(expected));
        Assert.Equal(1UL << (int)expected, s.Buttons);
    }

    [Fact]
    public void Sticks_and_triggers_are_normalised_with_up_positive()
    {
        var s = new ControllerState();
        TritonReport.TryParseState(State(lt: 32767, rt: 16384, lx: 32767, ly: 32767, rx: -32768, ry: -16384), ref s);
        Assert.Equal(1f, s.LeftTrigger, 3);
        Assert.Equal(0.5f, s.RightTrigger, 2);
        Assert.Equal(1f, s.LeftStick.X, 3);
        Assert.Equal(1f, s.LeftStick.Y, 3);   // raw +Y is up, as in SDL (which negates it for its own Y-down axis)
        Assert.Equal(-1f, s.RightStick.X, 3);
        Assert.Equal(-0.5f, s.RightStick.Y, 2);
    }

    [Fact]
    public void Trackpad_touch_position_and_pressure()
    {
        var s = new ControllerState();
        TritonReport.TryParseState(State(buttons: 0x00200000, rpx: 16384, rpy: 16384, rpp: 16384), ref s);
        Assert.True(s.RightPad.Touching);
        Assert.Equal(0.75f, s.RightPad.X, 3);  // sX / 65536 + 0.5
        Assert.Equal(0.25f, s.RightPad.Y, 3);  // -sY / 65536 + 0.5 (0 = top)
        Assert.Equal(0.5f, s.RightPad.Pressure, 3);
        Assert.False(s.LeftPad.Touching);

        // Finger lifted: keeps the last position, not touching, no pressure.
        TritonReport.TryParseState(State(), ref s);
        Assert.False(s.RightPad.Touching);
        Assert.Equal(0.75f, s.RightPad.X, 3);
        Assert.Equal(0f, s.RightPad.Pressure);
    }

    [Fact]
    public void Timestamped_layout_reads_pads_two_bytes_later()
    {
        var s = new ControllerState();
        TritonReport.TryParseState(State(buttons: 0x02000000, lpx: -16384, id: TritonReport.IdStateTimestamp), ref s);
        Assert.True(s.LeftPad.Touching);
        Assert.Equal(0.25f, s.LeftPad.X, 3);
    }

    [Fact]
    public void Gravity_comes_out_as_about_one_g_in_sdl_axes()
    {
        var s = new ControllerState();
        // 1 g = 16384 counts on a ±2 g scale; lying flat, gravity is on the device's Z, which SDL reports as Y.
        TritonReport.TryParseState(State(az: 16384), ref s);
        Assert.True(s.MotionValid);
        Assert.Equal(9.80665f, s.Accel.Y, 2);
        Assert.Equal(9.80665f, s.Accel.Length(), 2);
    }

    [Fact]
    public void Frozen_imu_timestamp_means_motion_is_not_streaming()
    {
        // Seen on real hardware: with the controller not opened by a game API, every report repeats one IMU sample.
        var s = new ControllerState();
        var clock = new TritonReport.ImuClock();
        var report = State(az: 16384, gy: 500);
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(1 + 29), 123456); // same IMU timestamp every time
        for (int i = 0; i < TritonReport.StaleImuReports; i++) TritonReport.TryParseState(report, ref s, ref clock);
        Assert.True(s.MotionValid); // a few repeats are normal (reports can outpace the IMU)
        TritonReport.TryParseState(report, ref s, ref clock);
        Assert.False(s.MotionValid);

        // Streaming again: the timestamp advances.
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(1 + 29), 123460);
        TritonReport.TryParseState(report, ref s, ref clock);
        Assert.True(s.MotionValid);
    }

    [Fact]
    public void All_zero_accelerometer_means_no_motion_data()
    {
        var s = new ControllerState();
        TritonReport.TryParseState(State(), ref s);
        Assert.False(s.MotionValid);
    }

    [Fact]
    public void Non_state_or_short_reports_are_rejected()
    {
        var s = new ControllerState();
        Assert.False(TritonReport.TryParseState(new byte[] { 0x42, 1, 2 }, ref s));
        Assert.False(TritonReport.TryParseState(State(id: 0x43), ref s));
    }

    [Fact]
    public void Battery_report()
    {
        Assert.True(TritonReport.TryParseBattery(new byte[] { 0x43, 1, 87, 0, 0 }, out var b));
        Assert.Equal(87, b.Percent);
        Assert.Equal(PowerStatus.OnBattery, b.Status);
        Assert.False(TritonReport.TryParseBattery(new byte[] { 0x42, 1, 87 }, out _));
    }
}
