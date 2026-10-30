// BogiHub Probe — Phase 0 hardware proof.
// Reads the Steam Controller (2026) through SDL3's HIDAPI Triton driver and shows every input live,
// plus a coverage checklist so a tester can confirm each physical control really reaches software.
// Keys: R = short rumble test, G = toggle gyro/accel, S = save report, Q / Esc = quit (also saves).

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BogiHub.Devices.Sdl.Native;
using static BogiHub.Devices.Sdl.Native.Sdl3;

namespace BogiHub.Probe;

internal static unsafe partial class Program
{
    // Raw joystick button indices reported by SDL_hidapi_steam_triton.c (SDL 3.4.18).
    // Paddle "1/2" naming follows SDL; the tester confirms which physical paddle is which.
    private static readonly string[] ButtonNames =
    [
        "A", "B", "X", "Y", "View", "Steam", "Menu", "L3 click", "R3 click", "LB", "RB",
        "QAM (...)", "R paddle 1", "L paddle 1", "R paddle 2", "L paddle 2",
        "R pad click", "L pad click", "R stick touch", "L stick touch", "R grip touch", "L grip touch",
    ];

    private static readonly string[] AxisNames = ["Left X", "Left Y", "Right X", "Right Y", "LT", "RT"];

    private static IntPtr _gamepad;
    private static IntPtr _joystick;
    private static uint _instanceId;
    private static bool _sensorsOn;
    private static readonly Coverage Cov = new();
    private static readonly List<string> Log = [];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static int _updateEvents, _sensorEvents, _updateRate, _sensorRate;
    private static long _rateWindowStart;
    private static double _maxLoopMs, _maxLoopMsShown;
    private static string _steamState = "?";

    private static readonly string ReportPath = Path.Combine(Environment.CurrentDirectory, "probe-reports", $"probe-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
    private static readonly object ReportLock = new();

    [LibraryImport("winmm.dll")] private static partial uint timeBeginPeriod(uint ms);
    [LibraryImport("winmm.dll")] private static partial uint timeEndPeriod(uint ms);

    private delegate bool ConsoleCtrlHandler(int ctrlType);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    // Kept in a field so the GC never collects the delegate while Windows still holds it.
    private static readonly ConsoleCtrlHandler OnConsoleClose = _ =>
    {
        // Close button, Ctrl+C, logoff or shutdown: Windows gives us a few seconds to save.
        try { SaveReport(); } catch { }
        return false;
    };

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (Environment.GetCommandLineArgs().Contains("--vigem")) return VigemCheck.Run();
        if (Environment.GetCommandLineArgs().Contains("--guard")) return GuardCheck.Run();
        if (Environment.GetCommandLineArgs().Contains("--passive")) return PassiveCheck.Run();
        if (Environment.GetCommandLineArgs().Contains("--sources"))
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "--sources");
            return SourcesCheck.Run(i + 1 < a.Length && int.TryParse(a[i + 1], out int sec) ? sec : 15);
        }
        Console.Title = "BogiHub Probe";

        SDL_SetHint(HintJoystickHidapiSteam, "1");
        SDL_SetHint(HintJoystickAllowBackgroundEvents, "1");

        if (!SDL_Init(InitGamepad | InitSensor))
        {
            Console.WriteLine($"SDL_Init failed: {LastError()}");
            return 1;
        }

        SDL_SetEventEnabled(SdlEventType.GamepadUpdateComplete, true);

        _steamState = SteamState();
        Note($"BogiHub Probe started. SDL {VersionString()} ({Str(SDL_GetRevision())}). Steam at start: {_steamState}");
        ListAllJoysticks();
        TryOpenController();

        if (Environment.GetCommandLineArgs().Contains("--list")) return RunListMode();

        SetConsoleCtrlHandler(OnConsoleClose, true);
        timeBeginPeriod(1);
        Console.CursorVisible = false;
        Console.Clear();

        long lastRender = 0, lastSteamCheck = 0, lastSave = 0;
        bool running = true;
        try
        {
            while (running)
            {
                long loopStart = Clock.ElapsedTicks;

                SdlEvent ev;
                while (SDL_PollEvent(&ev)) HandleEvent(in ev, ref running);

                if (_gamepad != IntPtr.Zero) SampleState();

                long nowMs = Clock.ElapsedMilliseconds;
                if (nowMs - _rateWindowStart >= 1000)
                {
                    _updateRate = _updateEvents; _sensorRate = _sensorEvents;
                    _updateEvents = _sensorEvents = 0;
                    _maxLoopMsShown = _maxLoopMs; _maxLoopMs = 0;
                    _rateWindowStart = nowMs;
                }
                if (nowMs - lastSteamCheck >= 1000)
                {
                    string s = SteamState();
                    if (s != _steamState) Note($"Steam changed: {_steamState} -> {s}");
                    _steamState = s;
                    lastSteamCheck = nowMs;
                }
                if (nowMs - lastRender >= 50)
                {
                    Render();
                    lastRender = nowMs;
                }
                if (nowMs - lastSave >= 2000)
                {
                    SaveReport();
                    lastSave = nowMs;
                }

                while (Console.KeyAvailable) running &= HandleKey(Console.ReadKey(true).Key);

                double loopMs = (Clock.ElapsedTicks - loopStart) * 1000.0 / Stopwatch.Frequency;
                if (loopMs > _maxLoopMs) _maxLoopMs = loopMs;
                Thread.Sleep(1);
            }
        }
        finally
        {
            Console.CursorVisible = true;
            timeEndPeriod(1);
            string path = SaveReport();
            CloseController();
            SDL_Quit();
            Console.Clear();
            Console.WriteLine($"Report saved: {path}");
        }
        return 0;
    }

    /// <summary>Non-interactive: pump for ~3 s, then print the log and a coverage summary. Works with redirected output.</summary>
    private static int RunListMode()
    {
        ListValveHidInterfaces();
        if (_gamepad != IntPtr.Zero) TestLizardOffCommand();
        bool running = true;
        long end = Clock.ElapsedMilliseconds + 3000, windowStart = Clock.ElapsedMilliseconds;
        while (running && Clock.ElapsedMilliseconds < end)
        {
            SdlEvent ev;
            while (SDL_PollEvent(&ev)) HandleEvent(in ev, ref running);
            if (_gamepad != IntPtr.Zero) SampleState();
            Thread.Sleep(1);
        }
        double secs = (Clock.ElapsedMilliseconds - windowStart) / 1000.0;
        Note($"Over {secs:0.0} s: {_updateEvents / secs:0} reports/s, {_sensorEvents / secs:0} sensor updates/s");
        if (_gamepad != IntPtr.Zero)
            Note($"Sample: LX={Axes[0]} LY={Axes[1]} RX={Axes[2]} RY={Axes[3]} LT={Axes[4]} RT={Axes[5]} " +
                 $"gyro=({Gyro[0]:0.000},{Gyro[1]:0.000},{Gyro[2]:0.000}) accel=({Accel[0]:0.00},{Accel[1]:0.00},{Accel[2]:0.00}) " +
                 $"battery={(_batteryPct < 0 ? "n/a" : _batteryPct + "%")} {_power}");
        foreach (string line in Log) Console.WriteLine(line);
        CloseController();
        SDL_Quit();
        return 0;
    }

    private static void HandleEvent(in SdlEvent ev, ref bool running)
    {
        switch (ev.Type)
        {
            case SdlEventType.Quit:
                running = false;
                break;
            case SdlEventType.GamepadAdded:
                Note($"Gamepad added: id={ev.Which} {Describe(ev.Which)}");
                if (_gamepad == IntPtr.Zero) TryOpenController();
                break;
            case SdlEventType.GamepadRemoved:
                Note($"Gamepad removed: id={ev.Which}");
                if (ev.Which == _instanceId)
                {
                    CloseController();
                    TryOpenController();
                }
                break;
            case SdlEventType.GamepadUpdateComplete when ev.Which == _instanceId:
                _updateEvents++;
                break;
            case SdlEventType.GamepadSensorUpdate when ev.Which == _instanceId:
                _sensorEvents++;
                break;
        }
    }

    private static bool HandleKey(ConsoleKey key)
    {
        switch (key)
        {
            case ConsoleKey.Q or ConsoleKey.Escape:
                return false;
            case ConsoleKey.R when _gamepad != IntPtr.Zero:
                bool ok = SDL_RumbleGamepad(_gamepad, 0x6000, 0x6000, 300);
                Note(ok ? "Rumble test sent (300 ms, ~37%)" : $"Rumble failed: {LastError()}");
                if (ok) Cov.RumbleSent = true;
                break;
            case ConsoleKey.G when _gamepad != IntPtr.Zero:
                SetSensors(!_sensorsOn);
                break;
            case ConsoleKey.S:
                Note($"Report saved: {SaveReport()}");
                break;
        }
        return true;
    }

    private static void ListAllJoysticks()
    {
        var ids = GetJoystickIds();
        Note($"SDL sees {ids.Length} joystick(s):");
        foreach (uint id in ids) Note($"  id={id} {Describe(id)}");
    }

    /// <summary>Sends SDL's exact "built-in (lizard) mode off" feature report through a second HID handle, as BogiHub does.</summary>
    private static void TestLizardOffCommand()
    {
        string? path = Str(SDL_GetJoystickPathForID(_instanceId));
        IntPtr hid = path is null ? IntPtr.Zero : SDL_hid_open_path(path);
        if (hid == IntPtr.Zero)
        {
            Note($"Lizard-off test: could not open a second HID handle ({LastError()})");
            return;
        }
        byte* report = stackalloc byte[64];
        new Span<byte>(report, 64).Clear();
        report[0] = 0x01; report[1] = 0x87; report[2] = 0x03; report[3] = 0x09;
        int ok = 0;
        for (int i = 0; i < 5; i++) if (SDL_hid_send_feature_report(hid, report, 64) == 64) ok++;
        SDL_hid_close(hid);
        Note($"Lizard-off test: {ok}/5 feature reports accepted by the controller");
    }

    private static void ListValveHidInterfaces()
    {
        SDL_hid_init();
        HidDeviceInfo* list = SDL_hid_enumerate(VendorValve, 0);
        int n = 0;
        for (HidDeviceInfo* d = list; d != null; d = d->Next, n++)
        {
            Note($"  HID PID={d->ProductId:X4} if={d->InterfaceNumber} usage={d->UsagePage:X4}:{d->Usage:X4} bus={d->BusType} " +
                 $"product=\"{Marshal.PtrToStringUni(d->ProductString)}\"");
        }
        SDL_hid_free_enumeration(list);
        Note($"Valve HID interfaces visible to SDL: {n}");
    }

    private static string Describe(uint id) =>
        $"\"{Str(SDL_GetJoystickNameForID(id))}\" VID={SDL_GetJoystickVendorForID(id):X4} PID={SDL_GetJoystickProductForID(id):X4} " +
        $"gamepad={SDL_IsGamepad(id)} path={Str(SDL_GetJoystickPathForID(id))}";

    private static void TryOpenController()
    {
        foreach (uint id in GetJoystickIds())
        {
            // Only ever open Valve devices: this is what keeps BogiHub from reading its own virtual Xbox pad later.
            if (SDL_GetJoystickVendorForID(id) != VendorValve || !SDL_IsGamepad(id)) continue;

            IntPtr gp = SDL_OpenGamepad(id);
            if (gp == IntPtr.Zero)
            {
                Note($"Open failed for id={id}: {LastError()}");
                continue;
            }

            _gamepad = gp;
            _joystick = SDL_GetGamepadJoystick(gp);
            _instanceId = id;
            int pct;
            var power = SDL_GetJoystickPowerInfo(_joystick, &pct);
            Note($"Opened id={id}: {Describe(id)}");
            Note($"  connection={SDL_GetJoystickConnectionState(_joystick)} serial={Str(SDL_GetJoystickSerial(_joystick)) ?? "n/a"} " +
                 $"firmware=0x{SDL_GetJoystickFirmwareVersion(_joystick):X4}");
            Note($"  buttons={SDL_GetNumJoystickButtons(_joystick)} axes={SDL_GetNumJoystickAxes(_joystick)} hats={SDL_GetNumJoystickHats(_joystick)} " +
                 $"touchpads={SDL_GetNumGamepadTouchpads(gp)} gyro={SDL_GamepadHasSensor(gp, SensorType.Gyro)} accel={SDL_GamepadHasSensor(gp, SensorType.Accel)}");
            Note($"  power={power} percent={(pct < 0 ? "unknown" : pct + "%")}");
            Cov.Connection = SDL_GetJoystickConnectionState(_joystick).ToString();
            SetSensors(true);
            return;
        }
    }

    private static void CloseController()
    {
        if (_gamepad == IntPtr.Zero) return;
        SDL_CloseGamepad(_gamepad);
        _gamepad = _joystick = IntPtr.Zero;
        _instanceId = 0;
        _sensorsOn = false;
        Note("Controller closed (firmware watchdog should restore built-in desktop mode within a few seconds).");
    }

    private static void SetSensors(bool on)
    {
        bool g = SDL_SetGamepadSensorEnabled(_gamepad, SensorType.Gyro, on);
        bool a = SDL_SetGamepadSensorEnabled(_gamepad, SensorType.Accel, on);
        _sensorsOn = on && (g || a);
        Note($"Sensors {(on ? "enable" : "disable")}: gyro={g} accel={a}{(g && a ? "" : " err=" + LastError())}");
    }

    // ---- Live state ---------------------------------------------------------------------------

    private static readonly bool[] Buttons = new bool[32];
    private static readonly short[] Axes = new short[8];
    private static byte _hat;
    private static readonly TouchState[] Pads = new TouchState[2];
    private static readonly float[] Gyro = new float[3];
    private static readonly float[] Accel = new float[3];
    private static PowerState _power;
    private static int _batteryPct = -1;

    private struct TouchState
    {
        public bool Down;
        public float X, Y, Pressure;
    }

    private static void SampleState()
    {
        int nb = Math.Min(SDL_GetNumJoystickButtons(_joystick), Buttons.Length);
        for (int i = 0; i < nb; i++)
        {
            Buttons[i] = SDL_GetJoystickButton(_joystick, i);
            if (Buttons[i]) Cov.Buttons.Add(i);
        }

        int na = Math.Min(SDL_GetNumJoystickAxes(_joystick), Axes.Length);
        for (int i = 0; i < na; i++)
        {
            Axes[i] = SDL_GetJoystickAxis(_joystick, i);
            Cov.SeeAxis(i, Axes[i]);
        }

        _hat = SDL_GetNumJoystickHats(_joystick) > 0 ? SDL_GetJoystickHat(_joystick, 0) : (byte)0;
        Cov.HatBits |= _hat;

        for (int p = 0; p < 2 && p < SDL_GetNumGamepadTouchpads(_gamepad); p++)
        {
            byte down; float x, y, pr;
            if (SDL_GetGamepadTouchpadFinger(_gamepad, p, 0, &down, &x, &y, &pr))
            {
                Pads[p] = new TouchState { Down = down != 0, X = x, Y = y, Pressure = pr };
                if (down != 0) Cov.SeePad(p, x, y, pr);
            }
        }

        if (_sensorsOn)
        {
            fixed (float* g = Gyro) SDL_GetGamepadSensorData(_gamepad, SensorType.Gyro, g, 3);
            fixed (float* a = Accel) SDL_GetGamepadSensorData(_gamepad, SensorType.Accel, a, 3);
            float gMag = MathF.Sqrt(Gyro[0] * Gyro[0] + Gyro[1] * Gyro[1] + Gyro[2] * Gyro[2]);
            float aMag = MathF.Sqrt(Accel[0] * Accel[0] + Accel[1] * Accel[1] + Accel[2] * Accel[2]);
            if (gMag > 1.0f) Cov.GyroMoved = true;
            if (aMag > 5.0f) Cov.AccelSeen = true;
        }

        int pct;
        _power = SDL_GetJoystickPowerInfo(_joystick, &pct);
        _batteryPct = pct;
        if (pct >= 0) Cov.BatterySeen = true;
    }

    // ---- Rendering ----------------------------------------------------------------------------

    private static void Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine($" BogiHub Probe   SDL {VersionString()}   Steam: {_steamState}   [R] rumble  [G] gyro {(_sensorsOn ? "ON" : "off")}  [S] save  [Q] quit");
        sb.AppendLine(new string('─', 100));

        if (_gamepad == IntPtr.Zero)
        {
            sb.AppendLine(" No Steam Controller opened. Turn the controller on (puck plugged in), or plug it in by USB-C.");
            sb.AppendLine(" If it stays missing: note whether Steam is running and save the report with [S].");
        }
        else
        {
            sb.AppendLine($" Connection: {Cov.Connection,-9} Reports/s: {_updateRate,4}   Sensor updates/s: {_sensorRate,4}   " +
                          $"Worst probe loop: {_maxLoopMsShown,5:0.0} ms   Battery: {(_batteryPct < 0 ? "not reported" : _batteryPct + "%")} ({_power})");
            sb.AppendLine();
            sb.AppendLine(" BUTTONS");
            for (int row = 0; row < ButtonNames.Length; row += 4)
            {
                sb.Append("  ");
                for (int i = row; i < Math.Min(row + 4, ButtonNames.Length); i++)
                    sb.Append($"{(Buttons[i] ? "●" : "○")} {ButtonNames[i],-14}{(Cov.Buttons.Contains(i) ? "✓" : " ")}    ");
                sb.AppendLine();
            }
            string up = (_hat & 1) != 0 ? "UP " : "   ", down = (_hat & 4) != 0 ? "DOWN " : "     ";
            string left = (_hat & 8) != 0 ? "LEFT " : "     ", right = (_hat & 2) != 0 ? "RIGHT" : "     ";
            sb.AppendLine($"  D-pad: {up}{down}{left}{right}   seen: {HatSeen()}");
            sb.AppendLine();
            sb.AppendLine(" STICKS & TRIGGERS");
            for (int i = 0; i < 4; i++)
                sb.AppendLine($"  {AxisNames[i],-8} {CenterBar(Axes[i] / 32767f, 41)} {Axes[i],7}   range seen {Cov.AxisMin[i],7} .. {Cov.AxisMax[i],6}");
            for (int i = 4; i < 6; i++)
            {
                float t = (Axes[i] + 32768) / 65535f;
                sb.AppendLine($"  {AxisNames[i],-8} {FillBar(t, 41)} {t * 100,6:0.0}%  full press seen {(Cov.AxisMax[i] > 32000 ? "✓" : "✗")}");
            }
            sb.AppendLine();
            sb.AppendLine(" TRACKPADS (x, y 0..1, pressure)");
            for (int p = 0; p < 2; p++)
            {
                var t = Pads[p];
                sb.AppendLine($"  {(p == 0 ? "Left " : "Right")} {(t.Down ? "TOUCH" : "  -  ")}  x={t.X:0.000} y={t.Y:0.000} pressure={t.Pressure:0.000}   " +
                              $"seen x {Cov.PadMinX[p]:0.00}..{Cov.PadMaxX[p]:0.00} y {Cov.PadMinY[p]:0.00}..{Cov.PadMaxY[p]:0.00} max pressure {Cov.PadMaxPressure[p]:0.00}");
            }
            sb.AppendLine();
            sb.AppendLine(" MOTION");
            sb.AppendLine(_sensorsOn
                ? $"  Gyro  (rad/s): {Gyro[0],8:0.000} {Gyro[1],8:0.000} {Gyro[2],8:0.000}   moved {(Cov.GyroMoved ? "✓" : "✗")}"
                : "  Gyro: off");
            sb.AppendLine(_sensorsOn
                ? $"  Accel (m/s²):  {Accel[0],8:0.000} {Accel[1],8:0.000} {Accel[2],8:0.000}   seen {(Cov.AccelSeen ? "✓" : "✗")}"
                : "  Accel: off");
            sb.AppendLine();
            sb.AppendLine($" COVERAGE {Cov.Seen(ButtonNames.Length)}/{Cov.Total(ButtonNames.Length)}   still missing: {Cov.Missing(ButtonNames)}");
        }

        sb.AppendLine(new string('─', 100));
        foreach (string line in Log.TakeLast(8)) sb.AppendLine(" " + Truncate(line, 98));

        Console.SetCursorPosition(0, 0);
        int width = Math.Max(Console.WindowWidth - 1, 40);
        foreach (string line in sb.ToString().Split('\n'))
            Console.WriteLine(Truncate(line.TrimEnd('\r'), width).PadRight(width));
        for (int i = 0; i < 3; i++) Console.WriteLine(new string(' ', width));
    }

    private static string HatSeen()
    {
        var parts = new List<string>();
        if ((Cov.HatBits & 1) != 0) parts.Add("up");
        if ((Cov.HatBits & 4) != 0) parts.Add("down");
        if ((Cov.HatBits & 8) != 0) parts.Add("left");
        if ((Cov.HatBits & 2) != 0) parts.Add("right");
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }

    private static string CenterBar(float v, int width)
    {
        v = Math.Clamp(v, -1f, 1f);
        int mid = width / 2;
        int pos = mid + (int)MathF.Round(v * mid);
        var c = new char[width];
        for (int i = 0; i < width; i++) c[i] = i == mid ? '┼' : '·';
        int from = Math.Min(mid, pos), to = Math.Max(mid, pos);
        for (int i = from; i <= to; i++) if (i != mid) c[i] = '█';
        return "[" + new string(c) + "]";
    }

    private static string FillBar(float v, int width)
    {
        int n = (int)MathF.Round(Math.Clamp(v, 0f, 1f) * width);
        return "[" + new string('█', n) + new string('·', width - n) + "]";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // ---- Reporting ----------------------------------------------------------------------------

    private static void Note(string message) => Log.Add($"{DateTime.Now:HH:mm:ss} {message}");

    private static string SteamState()
    {
        var procs = Process.GetProcessesByName("steam");
        string s = procs.Length > 0 ? "RUNNING" : "closed";
        foreach (var p in procs) p.Dispose();
        return s;
    }

    /// <summary>Writes (overwrites) this session's report file. Called every 2 s, on [S], on quit and on window close.</summary>
    private static string SaveReport()
    {
        lock (ReportLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
            File.WriteAllText(ReportPath, BuildReport());
            return ReportPath;
        }
    }

    private static string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("BogiHub Probe report (real hardware, not simulated)");
        sb.AppendLine($"Time: {DateTime.Now:O}");
        sb.AppendLine($"OS: {Environment.OSVersion}  SDL: {VersionString()} ({Str(SDL_GetRevision())})");
        sb.AppendLine($"Steam now: {SteamState()}");
        sb.AppendLine($"Connection: {Cov.Connection ?? "not opened"}  Last reports/s: {_updateRate}  sensor updates/s: {_sensorRate}");
        sb.AppendLine($"Battery: {(_batteryPct < 0 ? "not reported" : _batteryPct + "%")} ({_power})  Rumble test sent: {Cov.RumbleSent}");
        sb.AppendLine();
        sb.AppendLine("Coverage:");
        for (int i = 0; i < ButtonNames.Length; i++) sb.AppendLine($"  [{(Cov.Buttons.Contains(i) ? "x" : " ")}] button {i,2} {ButtonNames[i]}");
        sb.AppendLine($"  D-pad seen: {HatSeen()}");
        for (int i = 0; i < 6; i++) sb.AppendLine($"  axis {AxisNames[i],-8} min {Cov.AxisMin[i],7} max {Cov.AxisMax[i],7}");
        for (int p = 0; p < 2; p++)
            sb.AppendLine($"  {(p == 0 ? "left" : "right")} pad touched={Cov.PadTouched[p]} x {Cov.PadMinX[p]:0.000}..{Cov.PadMaxX[p]:0.000} y {Cov.PadMinY[p]:0.000}..{Cov.PadMaxY[p]:0.000} maxPressure {Cov.PadMaxPressure[p]:0.000}");
        sb.AppendLine($"  gyro moved={Cov.GyroMoved} accel seen={Cov.AccelSeen} battery seen={Cov.BatterySeen}");
        sb.AppendLine();
        sb.AppendLine("Log:");
        foreach (string line in Log.ToArray()) sb.AppendLine("  " + line);
        return sb.ToString();
    }

    private sealed class Coverage
    {
        public readonly HashSet<int> Buttons = [];
        public readonly int[] AxisMin = new int[8];
        public readonly int[] AxisMax = new int[8];
        public int HatBits;
        public readonly bool[] PadTouched = new bool[2];
        public readonly float[] PadMinX = [1, 1], PadMaxX = [0, 0], PadMinY = [1, 1], PadMaxY = [0, 0];
        public readonly float[] PadMaxPressure = new float[2];
        public bool GyroMoved, AccelSeen, BatterySeen, RumbleSent;
        public string? Connection;

        public Coverage()
        {
            Array.Fill(AxisMin, int.MaxValue);
            Array.Fill(AxisMax, int.MinValue);
        }

        public void SeeAxis(int i, short v)
        {
            if (v < AxisMin[i]) AxisMin[i] = v;
            if (v > AxisMax[i]) AxisMax[i] = v;
        }

        public void SeePad(int p, float x, float y, float pressure)
        {
            PadTouched[p] = true;
            PadMinX[p] = Math.Min(PadMinX[p], x); PadMaxX[p] = Math.Max(PadMaxX[p], x);
            PadMinY[p] = Math.Min(PadMinY[p], y); PadMaxY[p] = Math.Max(PadMaxY[p], y);
            PadMaxPressure[p] = Math.Max(PadMaxPressure[p], pressure);
        }

        private IEnumerable<(string Name, bool Ok)> Checks(IReadOnlyList<string> names)
        {
            for (int i = 0; i < names.Count; i++) yield return (names[i], Buttons.Contains(i));
            yield return ("D-pad up", (HatBits & 1) != 0);
            yield return ("D-pad right", (HatBits & 2) != 0);
            yield return ("D-pad down", (HatBits & 4) != 0);
            yield return ("D-pad left", (HatBits & 8) != 0);
            for (int i = 0; i < 4; i++) yield return ($"{AxisNames[i]} full range", AxisMin[i] < -30000 && AxisMax[i] > 30000);
            yield return ("LT full", AxisMax[4] > 32000);
            yield return ("RT full", AxisMax[5] > 32000);
            yield return ("L pad touch", PadTouched[0]);
            yield return ("R pad touch", PadTouched[1]);
            yield return ("Gyro", GyroMoved);
            yield return ("Accel", AccelSeen);
        }

        public int Seen(int n) => Checks(ButtonNames).Count(c => c.Ok);
        public int Total(int n) => Checks(ButtonNames).Count();
        public string Missing(IReadOnlyList<string> names)
        {
            var missing = Checks(names).Where(c => !c.Ok).Select(c => c.Name).ToList();
            return missing.Count == 0 ? "nothing — every input reached software ✓" : string.Join(", ", missing);
        }
    }
}
