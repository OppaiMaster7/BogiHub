using BogiHub.Core.Input;
using static BogiHub.Devices.Sdl.Native.Sdl3;

namespace BogiHub.Probe;

/// <summary>
/// `--passive`: reads the controller's HID reports WITHOUT opening it through SDL (which would switch its built-in
/// mouse/keyboard mode off), decodes them with BogiHub's own parser, then sends "built-in mode ON".
/// Proves BogiHub can see "…" and drive the hologram while the controller acts as a mouse.
/// </summary>
internal static unsafe class PassiveCheck
{
    public static int Run()
    {
        SDL_SetHint(HintJoystickHidapiSteam, "1");
        if (!SDL_Init(InitGamepad))
        {
            Console.WriteLine($"SDL_Init failed: {LastError()}");
            return 1;
        }
        try
        {
            // Let SDL enumerate the puck and notice the connected controller (it only creates the joystick then).
            for (int i = 0; i < 40; i++) { SDL_PumpEvents(); Thread.Sleep(25); }

            uint id = GetJoystickIds().FirstOrDefault(j => SDL_GetJoystickVendorForID(j) == VendorValve && SDL_IsGamepad(j));
            if (id == 0)
            {
                Console.WriteLine("No connected Steam Controller (is it asleep? press the Steam button).");
                return 1;
            }
            string? path = Str(SDL_GetJoystickPathForID(id));
            IntPtr hid = path is null ? IntPtr.Zero : SDL_hid_open_path(path);
            if (hid == IntPtr.Zero)
            {
                Console.WriteLine($"Could not open {path}: {LastError()}");
                return 1;
            }
            Console.WriteLine($"Reading {path} passively (gamepad NOT opened) for 3 s...");

            var counts = new Dictionary<byte, int>();
            var state = new ControllerState();
            BatteryInfo battery = BatteryInfo.Unknown;
            int parsed = 0, motionSamples = 0;
            float accelSum = 0, accelMin = float.MaxValue, accelMax = 0, gyroSum = 0;
            byte* buf = stackalloc byte[64];
            long end = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < end)
            {
                int n = SDL_hid_read_timeout(hid, buf, 64, 50);
                if (n <= 0) continue;
                var report = new ReadOnlySpan<byte>(buf, n);
                counts[report[0]] = counts.GetValueOrDefault(report[0]) + 1;
                if (TritonReport.TryParseState(report, ref state))
                {
                    parsed++;
                    if (state.MotionValid)
                    {
                        float a = state.Accel.Length();
                        accelSum += a;
                        accelMin = Math.Min(accelMin, a);
                        accelMax = Math.Max(accelMax, a);
                        gyroSum += state.Gyro.Length();
                        motionSamples++;
                    }
                }
                if (TritonReport.TryParseBattery(report, out var b)) battery = b;
            }

            Console.WriteLine("Report IDs seen: " + (counts.Count == 0 ? "none" : string.Join(", ", counts.Select(kv => $"0x{kv.Key:X2}×{kv.Value}"))));
            Console.WriteLine($"State reports decoded: {parsed} ({parsed / 3.0:0}/s)");
            if (parsed > 0)
            {
                Console.WriteLine($"  buttons=0x{state.Buttons:X}  LT={state.LeftTrigger:0.00} RT={state.RightTrigger:0.00}  " +
                                  $"L=({state.LeftStick.X:0.00},{state.LeftStick.Y:0.00}) R=({state.RightStick.X:0.00},{state.RightStick.Y:0.00})");
                Console.WriteLine(state.MotionValid
                    ? $"  motion: |accel|={state.Accel.Length():0.00} m/s² (≈9.81 means the decoding is right), gyro=({state.Gyro.X:0.000},{state.Gyro.Y:0.000},{state.Gyro.Z:0.000})"
                    : "  motion: not streaming in this mode (accelerometer reads zero)");
            }
            if (motionSamples > 0)
                Console.WriteLine($"  over {motionSamples} samples: |accel| avg {accelSum / motionSamples:0.00} (min {accelMin:0.00}, max {accelMax:0.00}) m/s², |gyro| avg {gyroSum / motionSamples:0.000} rad/s");
            Console.WriteLine($"Battery report: {(battery.HasPercent ? $"{battery.Percent}% {battery.Status}" : "none in this window")}");

            // Built-in mode ON: same command SDL uses for OFF, with value 1 (LIZARD_MODE_ON).
            byte* rep = stackalloc byte[64];
            new Span<byte>(rep, 64).Clear();
            rep[0] = 0x01; rep[1] = 0x87; rep[2] = 0x03; rep[3] = 0x09; rep[4] = 0x01;
            int ok = SDL_hid_send_feature_report(hid, rep, 64);
            Console.WriteLine($"Built-in mode ON command: {(ok == 64 ? "accepted" : $"failed ({ok}) {LastError()}")}");
            SDL_hid_close(hid);
            return parsed > 0 ? 0 : 1;
        }
        finally
        {
            SDL_Quit();
        }
    }
}
