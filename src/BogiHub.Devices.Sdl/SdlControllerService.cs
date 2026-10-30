using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using BogiHub.Core.Input;
using BogiHub.Devices.Sdl.Native;
using static BogiHub.Devices.Sdl.Native.Sdl3;

namespace BogiHub.Devices.Sdl;

/// <summary>Live facts about the controller connection, safe to read from any thread.</summary>
public sealed record ControllerStatus(
    bool Connected,
    DeviceInfo? Device,
    BatteryInfo Battery,
    int ReportsPerSecond,
    string? LastError)
{
    public static readonly ControllerStatus Disconnected = new(false, null, BatteryInfo.Unknown, 0, null);
}

/// <summary>Receives every fresh controller sample on the input thread. Must return quickly.</summary>
public interface IControllerSink
{
    void OnSample(in ControllerState state, float dtSeconds);

    /// <summary>The device went away (or was released). Outputs must be neutralised now.</summary>
    void OnDeviceLost();
}

/// <summary>
/// Owns SDL on one dedicated, high-priority thread (SDL requires all calls on the thread that initialised it).
/// Two ways of reading the controller:
/// <list type="bullet">
/// <item><b>Held</b> (<see cref="Acquire"/> = true): opened through SDL for gamepad use. The controller's built-in
/// mouse/keyboard ("lizard") mode is kept off.</item>
/// <item><b>Passive</b> (<see cref="Acquire"/> = false): handed back with an explicit "built-in mode ON" so it is a mouse
/// and keyboard right away, while BogiHub keeps decoding its HID reports through a side channel. That is how "…"
/// and the hologram keep working in mouse &amp; keyboard mode. Nothing is sent that would switch the mouse off.</item>
/// </list>
/// Opens only Valve devices, so BogiHub can never read its own virtual Xbox controller back (no feedback loop).
/// </summary>
public sealed unsafe partial class SdlControllerService : IDisposable
{
    private const long FallbackTickMs = 8;

    private readonly IControllerSink _sink;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly Thread _thread;
    private volatile bool _stop;
    private volatile bool _acquire;

    private IntPtr _gamepad, _joystick;
    private uint _instanceId;
    private ControllerState _state;
    private long _lastMapTicks;
    private int _updatesThisSecond;
    private long _rateWindowStart;
    private long _lastOpenAttempt;

    private readonly object _snapshotLock = new();
    private ControllerState _snapshot;
    private ControllerStatus _status = ControllerStatus.Disconnected;

    public SdlControllerService(IControllerSink sink)
    {
        _sink = sink;
        _thread = new Thread(Run) { Name = "BogiHub input", IsBackground = true, Priority = ThreadPriority.Highest };
    }

    public string SdlVersion { get; private set; } = "?";

    /// <summary>True = hold the controller for gamepad use. False = hand it back to its built-in mouse &amp; keyboard mode.</summary>
    public bool Acquire
    {
        get => _acquire;
        set => _acquire = value;
    }

    public ControllerStatus Status
    {
        get { lock (_snapshotLock) return _status; }
    }

    /// <summary>Most recent controller state, for the UI (copied, never shared). Live in both held and passive mode.</summary>
    public ControllerState Snapshot
    {
        get { lock (_snapshotLock) return _snapshot; }
    }

    /// <summary>True while the controller is read passively (it is acting as its own mouse &amp; keyboard).</summary>
    public bool IsPassive => _gamepad == IntPtr.Zero && _hid != IntPtr.Zero;

    private volatile bool _builtInWanted = true;
    private bool _builtInSent;

    /// <summary>
    /// Whether the controller's built-in mouse &amp; keyboard should be switched on while it is not held. False while
    /// Steam runs: Steam keeps the built-in mode off and drives the controller itself, and turning it on would give
    /// double input. When true, "built-in mode ON" is sent once per passive period (also after a start or after Steam
    /// closes), so the cursor works even if a previous session was killed while holding the controller.
    /// </summary>
    public bool BuiltInModeWanted
    {
        get => _builtInWanted;
        set
        {
            if (_builtInWanted == value) return;
            _builtInWanted = value;
            _commands.Enqueue(() => _builtInSent = false);
        }
    }

    public void Start() => _thread.Start();

    /// <summary>Game rumble (held mode only). Values 0..65535; SDL keeps it alive until the duration ends.</summary>
    public void Rumble(ushort low, ushort high, uint durationMs) =>
        _commands.Enqueue(() =>
        {
            if (_gamepad != IntPtr.Zero) SDL_RumbleGamepad(_gamepad, low, high, durationMs);
        });

    /// <summary>
    /// Haptic pattern of (on, off) segments in milliseconds. Works in held and passive mode (sent through the side
    /// channel), so mode-switch confirmations can always be felt.
    /// </summary>
    public void Buzz(params (int OnMs, int OffMs)[] pattern) =>
        _commands.Enqueue(() =>
        {
            _buzz.Clear();
            foreach (var p in pattern) _buzz.Enqueue(p);
            _buzzOn = false;
            _buzzNextStart = 0;
        });

    /// <summary>
    /// A trackpad haptic: a light tick (cursor rolling, wheel notch) or a firmer click (new direction).
    /// <paramref name="strength"/> 0..1. Ticks closer than <see cref="TickSpacingMs"/> on one side merge into one.
    /// </summary>
    public void PadHaptic(bool left, bool click, float strength) =>
        _commands.Enqueue(() => WritePadHaptic(left, click, strength));

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive) _thread.Join(2000);
    }

    // ---- Side channel: our own HID handle to the controller ------------------------------------
    // Used to keep built-in mode off while held, to switch it back on when handing over, to read reports passively,
    // and for haptics. Windows delivers every input report to every open handle, so SDL is unaffected.
    // Report bytes are SDL 3.4.18's own (SDL_hidapi_steam_triton.c):
    //   built-in mode: feature 01 87 03 09 VV 00 … (64 bytes), VV = 00 off / 01 on  (SETTING_LIZARD_MODE = 9)
    //   rumble:        output  80 00 0000 LLLL 00 RRRR 00                (ID_OUT_REPORT_HAPTIC_RUMBLE, 10 bytes)

    private const long LizardResendMs = 500;
    private const long SideRetryMs = 1000;
    private IntPtr _hid;
    private uint _sideId;
    private long _lastSideAttempt = -SideRetryMs;
    private long _lastLizardSend;
    private long _lizardSent, _lizardFailed;

    /// <summary>Successful / failed "built-in mode off" commands since start (diagnostics).</summary>
    public (long Sent, long Failed) LizardStats => (Interlocked.Read(ref _lizardSent), Interlocked.Read(ref _lizardFailed));

    /// <summary>True while BogiHub holds the controller and keeps its built-in mode off.</summary>
    public bool LizardKeeperActive => _gamepad != IntPtr.Zero && _hid != IntPtr.Zero;

    private void EnsureSideChannel()
    {
        if (_hid != IntPtr.Zero) return;
        long now = Environment.TickCount64;
        if (now - _lastSideAttempt < SideRetryMs) return;
        _lastSideAttempt = now;
        foreach (uint id in ValveGamepads())
            if (OpenSideChannel(id)) return;
    }

    /// <summary>
    /// Valve gamepads, best link first: a USB-C cable, then Bluetooth, then the puck. A controller charging on a cable
    /// while its puck is also plugged in may show up twice; the cable is the direct, lowest-latency path.
    /// </summary>
    private static IEnumerable<uint> ValveGamepads() =>
        GetJoystickIds()
            .Where(id => SDL_GetJoystickVendorForID(id) == VendorValve && SDL_IsGamepad(id))
            .OrderBy(id => SDL_GetJoystickProductForID(id) switch { ProductTritonUsb => 0, ProductTritonBle => 1, _ => 2 });

    private bool OpenSideChannel(uint id)
    {
        string? path = Str(SDL_GetJoystickPathForID(id));
        IntPtr h = path is null ? IntPtr.Zero : SDL_hid_open_path(path);
        if (h == IntPtr.Zero) return false;
        _hid = h;
        _sideId = id;
        _builtInSent = false;
        _imuClock = default;
        _passive = default;
        _lastPassiveTicks = Stopwatch.GetTimestamp();
        if (_gamepad == IntPtr.Zero) SetStatus(PassiveStatus());
        return true;
    }

    private void CloseSideChannel()
    {
        if (_hid == IntPtr.Zero) return;
        SDL_hid_close(_hid);
        _hid = IntPtr.Zero;
        _sideId = 0;
    }

    private bool SendLizard(bool on)
    {
        if (_hid == IntPtr.Zero) return false;
        byte* report = stackalloc byte[64];
        new Span<byte>(report, 64).Clear();
        report[0] = 0x01;
        report[1] = 0x87;
        report[2] = 0x03;
        report[3] = 0x09;
        report[4] = on ? (byte)1 : (byte)0;
        return SDL_hid_send_feature_report(_hid, report, 64) == 64;
    }

    private void KeepLizardOff()
    {
        if (_hid == IntPtr.Zero) return;
        long now = Environment.TickCount64;
        if (now - _lastLizardSend < LizardResendMs) return;
        _lastLizardSend = now;
        if (SendLizard(on: false)) Interlocked.Increment(ref _lizardSent);
        else Interlocked.Increment(ref _lizardFailed);
    }

    private void WriteRumble(ushort low, ushort high)
    {
        if (_hid != IntPtr.Zero)
        {
            byte* r = stackalloc byte[10];
            new Span<byte>(r, 10).Clear();
            r[0] = 0x80;
            r[4] = (byte)low;
            r[5] = (byte)(low >> 8);
            r[7] = (byte)high;
            r[8] = (byte)(high >> 8);
            if (SDL_hid_write(_hid, r, 10) == 10) return;
        }
        if (_gamepad != IntPtr.Zero) SDL_RumbleGamepad(_gamepad, low, high, 60);
    }

    // ---- Trackpad haptics ----------------------------------------------------------------------
    // SDL 3.4.18's controller_structs.h: output report 82 = haptic command { side, command, gain dB },
    // command 1 = tick, 2 = click. Side 0 = left pad, 1 = right pad (Steamworks' ESteamControllerPad order;
    // check with `BogiHub.Probe --haptics`).

    public const int TickSpacingMs = 14;
    public const byte HapticSideLeft = 0, HapticSideRight = 1;
    private long _lastTickLeft, _lastTickRight;

    private void WritePadHaptic(bool left, bool click, float strength)
    {
        if (_hid == IntPtr.Zero || strength <= 0) return;
        long now = Environment.TickCount64;
        ref long last = ref left ? ref _lastTickLeft : ref _lastTickRight;
        if (!click && now - last < TickSpacingMs) return;
        last = now;
        byte* r = stackalloc byte[4];
        r[0] = 0x82;
        r[1] = left ? HapticSideLeft : HapticSideRight;
        r[2] = click ? (byte)2 : (byte)1;
        r[3] = (byte)GainDb(strength);
        SDL_hid_write(_hid, r, 4);
    }

    /// <summary>0..1 strength as the firmware's gain in dB: 100 % = 0 dB, 50 % ≈ -6 dB, quietest -30 dB.</summary>
    public static sbyte GainDb(float strength) =>
        (sbyte)Math.Clamp(MathF.Round(20f * MathF.Log10(Math.Max(strength, 0.03f))), -30f, 0f);

    // ---- Haptic patterns ---------------------------------------------------------------------------
    // The controller stops rumbling ~50 ms after the last command, so an "on" segment is refreshed every 35 ms.

    private const ushort BuzzLevel = 0x9000;
    private readonly Queue<(int OnMs, int OffMs)> _buzz = new();
    private bool _buzzOn;
    private long _buzzOnUntil, _buzzNextStart, _lastBuzzWrite;

    private void RunBuzz()
    {
        if (_buzz.Count == 0 && !_buzzOn) return;
        long now = Environment.TickCount64;
        if (!_buzzOn)
        {
            if (_buzz.Count == 0 || now < _buzzNextStart) return;
            _buzzOn = true;
            _buzzOnUntil = now + _buzz.Peek().OnMs;
            _lastBuzzWrite = 0;
        }
        if (now < _buzzOnUntil)
        {
            if (now - _lastBuzzWrite >= 35)
            {
                WriteRumble(BuzzLevel, BuzzLevel);
                _lastBuzzWrite = now;
            }
            return;
        }
        WriteRumble(0, 0);
        _buzzOn = false;
        _buzzNextStart = now + _buzz.Dequeue().OffMs;
    }

    // ---- Passive reading -----------------------------------------------------------------------

    private ControllerState _passive;
    private TritonReport.ImuClock _imuClock;
    private long _lastPassiveTicks;
    private int _passiveReports;
    private BatteryInfo _passiveBattery = BatteryInfo.Unknown;

    private void PollPassive()
    {
        byte* buf = stackalloc byte[64];
        bool fresh = false;
        for (int i = 0; i < 64; i++)
        {
            int n = SDL_hid_read_timeout(_hid, buf, 64, 0);
            if (n == 0) break;
            if (n < 0)
            {
                Close("Controller disconnected");
                return;
            }
            var report = new ReadOnlySpan<byte>(buf, n);
            if (TritonReport.TryParseState(report, ref _passive, ref _imuClock))
            {
                fresh = true;
                _passiveReports++;
            }
            else if (TritonReport.TryParseBattery(report, out var battery))
            {
                _passiveBattery = battery;
                SetStatus(PassiveStatus());
            }
        }

        long now = Stopwatch.GetTimestamp();
        if (fresh)
        {
            _passive.TimestampNs = (long)(now * (1_000_000_000.0 / Stopwatch.Frequency));
            float dt = (float)Stopwatch.GetElapsedTime(_lastPassiveTicks, now).TotalSeconds;
            _lastPassiveTicks = now;
            lock (_snapshotLock) _snapshot = _passive;
            _sink.OnSample(in _passive, dt);
        }

        long ms = Environment.TickCount64;
        if (ms - _rateWindowStart >= 1000)
        {
            int rate = _passiveReports;
            _passiveReports = 0;
            _rateWindowStart = ms;
            lock (_snapshotLock) _status = _status with { ReportsPerSecond = rate };
        }
    }

    private ControllerStatus PassiveStatus()
    {
        ushort pid = SDL_GetJoystickProductForID(_sideId);
        var caps = Capabilities.Buttons | Capabilities.Sticks | Capabilities.AnalogTriggers | Capabilities.Trackpads |
                   Capabilities.TrackpadPressure | Capabilities.CapacitiveTouch | Capabilities.Rumble;
        if (_passiveBattery.HasPercent) caps |= Capabilities.Battery;
        var device = new DeviceInfo(
            Str(SDL_GetJoystickNameForID(_sideId)) ?? "Steam Controller",
            VendorValve,
            pid,
            Transport(pid, ConnectionState.Unknown),
            null,
            caps);
        int rate;
        lock (_snapshotLock) rate = _status.ReportsPerSecond;
        return new ControllerStatus(true, device, _passiveBattery, rate, null);
    }

    // ---- Input thread -------------------------------------------------------------------------

    private void Run()
    {
        timeBeginPeriod(1);
        try
        {
            SDL_SetHint(HintJoystickHidapiSteam, "1");
            SDL_SetHint(HintJoystickAllowBackgroundEvents, "1");
            if (!SDL_Init(InitGamepad | InitSensor))
            {
                SetStatus(ControllerStatus.Disconnected with { LastError = $"SDL could not start: {LastError()}" });
                return;
            }
            SDL_SetEventEnabled(SdlEventType.GamepadUpdateComplete, true);
            SdlVersion = VersionString();

            while (!_stop)
            {
                while (_commands.TryDequeue(out var cmd)) cmd();

                bool fresh = false;
                SdlEvent ev;
                while (SDL_PollEvent(&ev)) fresh |= Handle(in ev);

                EnsureSideChannel();
                if (_acquire)
                {
                    if (_gamepad == IntPtr.Zero && Environment.TickCount64 - _lastOpenAttempt > 1000) TryOpen();
                }
                else if (_gamepad != IntPtr.Zero)
                {
                    Release();
                }

                if (_gamepad != IntPtr.Zero)
                {
                    long now = Stopwatch.GetTimestamp();
                    bool fallback = Stopwatch.GetElapsedTime(_lastMapTicks, now).TotalMilliseconds >= FallbackTickMs;
                    if (fresh || fallback) Sample(now);
                    KeepLizardOff();
                    UpdateRate();
                }
                else if (_hid != IntPtr.Zero)
                {
                    if (_builtInWanted && !_builtInSent) _builtInSent = SendLizard(on: true);
                    PollPassive();
                }
                RunBuzz();

                // 1 ms polling only matters while there is a controller to read; otherwise just watch for hot-plug.
                Thread.Sleep(_gamepad != IntPtr.Zero || _hid != IntPtr.Zero ? 1 : 15);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ControllerStatus.Disconnected with { LastError = $"Input thread crashed: {ex.Message}" });
        }
        finally
        {
            // Leaving: the controller must end up as a working mouse & keyboard again.
            if (_gamepad != IntPtr.Zero) Release();
            CloseSideChannel();
            SDL_Quit();
            timeEndPeriod(1);
        }
    }

    /// <summary>Returns true when the opened controller produced a new report.</summary>
    private bool Handle(in SdlEvent ev)
    {
        switch (ev.Type)
        {
            case SdlEventType.GamepadAdded:
                _lastSideAttempt = -SideRetryMs; // look for the side channel right away
                if (_acquire && _gamepad == IntPtr.Zero) TryOpen();
                return false;
            case SdlEventType.GamepadRemoved when ev.Which == _instanceId || ev.Which == _sideId:
                Close("Controller disconnected");
                return false;
            case SdlEventType.GamepadUpdateComplete when ev.Which == _instanceId:
                _updatesThisSecond++;
                return true;
            case SdlEventType.JoystickBatteryUpdated when ev.Which == _instanceId:
                RefreshBattery();
                return false;
            default:
                return false;
        }
    }

    private void TryOpen()
    {
        _lastOpenAttempt = Environment.TickCount64;
        foreach (uint id in ValveGamepads())
        {
            IntPtr gp = SDL_OpenGamepad(id);
            if (gp == IntPtr.Zero)
            {
                SetStatus(Status with { LastError = $"Could not open the controller: {LastError()}" });
                continue;
            }

            _gamepad = gp;
            _joystick = SDL_GetGamepadJoystick(gp);
            _instanceId = id;
            if (_hid == IntPtr.Zero || _sideId != id)
            {
                CloseSideChannel();
                OpenSideChannel(id);
            }
            _lastLizardSend = 0;
            _state = default;
            _lastMapTicks = Stopwatch.GetTimestamp();

            bool gyro = SDL_GamepadHasSensor(gp, SensorType.Gyro) && SDL_SetGamepadSensorEnabled(gp, SensorType.Gyro, true);
            bool accel = SDL_GamepadHasSensor(gp, SensorType.Accel) && SDL_SetGamepadSensorEnabled(gp, SensorType.Accel, true);

            var caps = Capabilities.Buttons | Capabilities.Sticks | Capabilities.AnalogTriggers | Capabilities.Rumble | Capabilities.CapacitiveTouch;
            if (SDL_GetNumGamepadTouchpads(gp) >= 2) caps |= Capabilities.Trackpads | Capabilities.TrackpadPressure;
            if (gyro) caps |= Capabilities.Gyro;
            if (accel) caps |= Capabilities.Accelerometer;

            ushort pid = SDL_GetJoystickProductForID(id);
            var device = new DeviceInfo(
                Str(SDL_GetJoystickNameForID(id)) ?? "Steam Controller",
                VendorValve,
                pid,
                Transport(pid, SDL_GetJoystickConnectionState(_joystick)),
                Str(SDL_GetJoystickSerial(_joystick)),
                caps);
            SetStatus(new ControllerStatus(true, device, BatteryInfo.Unknown, 0, null));
            RefreshBattery();
            return;
        }
    }

    private static string Transport(ushort pid, ConnectionState state) => pid switch
    {
        // SDL reports the puck's USB link as "wired"; the controller itself is wireless.
        ProductProteusDongle or ProductNereidDongle => "Wireless (Puck)",
        // Known by product id, so passive mode (no SDL connection state) names them too.
        ProductTritonUsb => "USB cable",
        ProductTritonBle => "Bluetooth",
        _ => state switch
        {
            ConnectionState.Wired => "USB cable",
            ConnectionState.Wireless => "Bluetooth",
            _ => "Connected",
        },
    };

    /// <summary>
    /// Hand the controller back: close it in SDL; the passive loop then switches its built-in mouse &amp; keyboard on
    /// right away (unless Steam runs) instead of waiting for the firmware watchdog. Keeps reading it passively.
    /// </summary>
    private void Release()
    {
        SDL_CloseGamepad(_gamepad);
        _gamepad = _joystick = IntPtr.Zero;
        _instanceId = 0;
        _state = default;
        _builtInSent = false;
        if (_builtInWanted) _builtInSent = SendLizard(on: true); // immediately, even when Release runs at shutdown
        _imuClock = default;
        _lastPassiveTicks = Stopwatch.GetTimestamp();
        SetStatus(_hid != IntPtr.Zero ? PassiveStatus() : ControllerStatus.Disconnected);
        _sink.OnDeviceLost();
    }

    /// <summary>The controller is gone (or BogiHub stops): close everything.</summary>
    private void Close(string reason)
    {
        bool hadSomething = _gamepad != IntPtr.Zero || _hid != IntPtr.Zero;
        if (_gamepad != IntPtr.Zero) SDL_CloseGamepad(_gamepad);
        _gamepad = _joystick = IntPtr.Zero;
        _instanceId = 0;
        _state = default;
        CloseSideChannel();
        _passive = default;
        lock (_snapshotLock)
        {
            _snapshot = default;
            _status = ControllerStatus.Disconnected with { LastError = reason == "Controller disconnected" ? reason : null };
        }
        if (hadSomething) _sink.OnDeviceLost();
    }

    private void Sample(long nowTicks)
    {
        ref var s = ref _state;
        s.TimestampNs = (long)(nowTicks * (1_000_000_000.0 / Stopwatch.Frequency));

        ulong buttons = 0;
        for (int i = 0; i < PhysicalInputs.SdlButtonCount; i++)
            if (SDL_GetJoystickButton(_joystick, i)) buttons |= 1UL << i;
        byte hat = SDL_GetJoystickHat(_joystick, 0);
        if ((hat & 1) != 0) buttons |= 1UL << (int)PhysicalInput.DpadUp;
        if ((hat & 2) != 0) buttons |= 1UL << (int)PhysicalInput.DpadRight;
        if ((hat & 4) != 0) buttons |= 1UL << (int)PhysicalInput.DpadDown;
        if ((hat & 8) != 0) buttons |= 1UL << (int)PhysicalInput.DpadLeft;
        s.Buttons = buttons;

        // SDL: Y axis is positive down. BogiHub: positive up.
        s.LeftStick = new Vector2(Axis(0), -Axis(1));
        s.RightStick = new Vector2(Axis(2), -Axis(3));
        s.LeftTrigger = (SDL_GetJoystickAxis(_joystick, 4) + 32768) / 65535f;
        s.RightTrigger = (SDL_GetJoystickAxis(_joystick, 5) + 32768) / 65535f;

        s.LeftPad = Touch(0);
        s.RightPad = Touch(1);

        float* g = stackalloc float[3];
        float* a = stackalloc float[3];
        bool gyroOk = SDL_GetGamepadSensorData(_gamepad, SensorType.Gyro, g, 3);
        bool accelOk = SDL_GetGamepadSensorData(_gamepad, SensorType.Accel, a, 3);
        s.MotionValid = gyroOk;
        s.Gyro = gyroOk ? new Vector3(g[0], g[1], g[2]) : Vector3.Zero;
        s.Accel = accelOk ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;

        float dt = (float)Stopwatch.GetElapsedTime(_lastMapTicks, nowTicks).TotalSeconds;
        _lastMapTicks = nowTicks;

        lock (_snapshotLock) _snapshot = s;
        _sink.OnSample(in s, dt);
    }

    private float Axis(int i) => Math.Max(SDL_GetJoystickAxis(_joystick, i) / 32767f, -1f);

    private TouchPoint Touch(int pad)
    {
        byte down;
        float x, y, p;
        return SDL_GetGamepadTouchpadFinger(_gamepad, pad, 0, &down, &x, &y, &p)
            ? new TouchPoint(down != 0, x, y, p)
            : default;
    }

    private void RefreshBattery()
    {
        if (_joystick == IntPtr.Zero) return;
        int pct;
        var state = SDL_GetJoystickPowerInfo(_joystick, &pct);
        var battery = new BatteryInfo(state switch
        {
            PowerState.OnBattery => PowerStatus.OnBattery,
            PowerState.NoBattery => PowerStatus.NoBattery,
            PowerState.Charging => PowerStatus.Charging,
            PowerState.Charged => PowerStatus.Charged,
            _ => PowerStatus.Unknown,
        }, pct);
        if (battery.HasPercent) _passiveBattery = battery; // remembered for passive mode too
        lock (_snapshotLock)
        {
            if (_status.Connected && _status.Device is { } d)
                _status = _status with { Battery = battery, Device = d with { Capabilities = battery.HasPercent ? d.Capabilities | Capabilities.Battery : d.Capabilities } };
        }
    }

    private void UpdateRate()
    {
        long now = Environment.TickCount64;
        if (now - _rateWindowStart < 1000) return;
        int rate = _updatesThisSecond;
        _updatesThisSecond = 0;
        _rateWindowStart = now;
        lock (_snapshotLock) _status = _status with { ReportsPerSecond = rate };
        RefreshBattery();
    }

    private void SetStatus(ControllerStatus s)
    {
        lock (_snapshotLock) _status = s;
    }

    [LibraryImport("winmm.dll")] private static partial uint timeBeginPeriod(uint ms);
    [LibraryImport("winmm.dll")] private static partial uint timeEndPeriod(uint ms);
}
