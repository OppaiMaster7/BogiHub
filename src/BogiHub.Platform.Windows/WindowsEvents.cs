using System.Diagnostics;
using System.Runtime.InteropServices;
using BogiHub.Core.Output;

namespace BogiHub.Platform.Windows;

/// <summary>
/// One background thread with a Win32 message loop that hosts everything needing one:
/// foreground-window tracking, the global emergency-stop hotkey, and (only while needed) low-level
/// keyboard/mouse hooks that count injected input coming from other programs such as Steam.
/// Events are raised on that thread; subscribers must marshal to their own thread.
/// </summary>
public sealed unsafe partial class WindowsEvents : IDisposable
{
    private const uint WM_QUIT = 0x0012, WM_TIMER = 0x0113, WM_HOTKEY = 0x0312, WM_APP = 0x8000;
    private const uint CmdHooksOn = WM_APP + 1, CmdHooksOff = WM_APP + 2;
    private const int HotkeyId = 0xB061, GamepadHotkeyId = 0xB062;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private nint _winEventHook, _mouseHook, _keyboardHook;
    private ForegroundApp _lastForeground = ForegroundApp.None;
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private static nuint _ownSignature;

    // Counters written by the hook callbacks (same thread) and read from anywhere.
    private static long _foreignInjectedCount;
    private static long _foreignInjectedLastTick;

    public WindowsEvents(nuint ownInputSignature)
    {
        _ownSignature = ownInputSignature;
        _thread = new Thread(Run) { Name = "BogiHub Windows events", IsBackground = true };
    }

    public event Action<ForegroundApp>? ForegroundChanged;
    public event Action? EmergencyStopPressed;
    public event Action? GamepadTogglePressed;

    public bool HotkeyRegistered { get; private set; }
    public string HotkeyLabel => "Ctrl+Shift+F12";
    public bool GamepadHotkeyRegistered { get; private set; }
    public string GamepadHotkeyLabel => "Ctrl+Shift+F11";
    public ForegroundApp Foreground => Volatile.Read(ref _lastForeground);

    /// <summary>Injected keyboard/mouse events from programs other than BogiHub, since start.</summary>
    public static long ForeignInjectedCount => Interlocked.Read(ref _foreignInjectedCount);

    // ---- Input source monitor (raw input) ------------------------------------------------------
    // Raw input tells which physical device produced each mouse/keyboard event. Events from a Valve (VID 28DE) device
    // are the controller's own built-in mouse/keyboard mode leaking through; hDevice 0 means injected (SendInput).

    private const uint CmdRawOn = WM_APP + 3, CmdRawOff = WM_APP + 4, WM_INPUT = 0x00FF;
    private nint _rawHwnd;
    private bool _rawRegistered;
    private readonly Dictionary<nint, bool> _valveDevice = [];
    private static long _ctrlMouse, _ctrlKeyboard, _injectedRaw, _otherRaw;

    /// <summary>Counts since start: controller built-in mouse / keyboard events, injected events, other devices.</summary>
    public static (long ControllerMouse, long ControllerKeyboard, long Injected, long OtherDevices) SourceCounts =>
        (Interlocked.Read(ref _ctrlMouse), Interlocked.Read(ref _ctrlKeyboard), Interlocked.Read(ref _injectedRaw), Interlocked.Read(ref _otherRaw));

    /// <summary>Starts or stops counting mouse/keyboard events by source device.</summary>
    public void SetSourceMonitoring(bool on)
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, on ? CmdRawOn : CmdRawOff, 0, 0);
    }

    private void RegisterRaw(bool on)
    {
        if (on == _rawRegistered || (on && _rawHwnd == 0)) return;
        var devs = stackalloc RAWINPUTDEVICE[2];
        devs[0] = new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = on ? RIDEV_INPUTSINK : RIDEV_REMOVE, Target = on ? _rawHwnd : 0 };
        devs[1] = new RAWINPUTDEVICE { UsagePage = 1, Usage = 6, Flags = on ? RIDEV_INPUTSINK : RIDEV_REMOVE, Target = on ? _rawHwnd : 0 };
        if (RegisterRawInputDevices(devs, 2, (uint)sizeof(RAWINPUTDEVICE))) _rawRegistered = on;
    }

    private void HandleRawInput(nint hRaw)
    {
        // Mouse and keyboard RAWINPUT fit in 64 bytes (header 24 + RAWMOUSE 24 / RAWKEYBOARD 16).
        byte* buf = stackalloc byte[64];
        uint size = 64;
        if (GetRawInputData(hRaw, RID_INPUT, buf, &size, (uint)sizeof(RAWINPUTHEADER)) == uint.MaxValue) return;
        var h = (RAWINPUTHEADER*)buf;
        byte* data = buf + sizeof(RAWINPUTHEADER);
        if (h->Device == 0)
        {
            Interlocked.Increment(ref _injectedRaw);
            return;
        }
        if (!IsValveDevice(h->Device))
        {
            Interlocked.Increment(ref _otherRaw);
            return;
        }

        // The controller's own firmware keyboard/mouse: remember what it holds so it can be released later.
        long now = Environment.TickCount64;
        if (h->Type == 1)
        {
            Interlocked.Increment(ref _ctrlKeyboard);
            ushort flags = *(ushort*)(data + 2), vkey = *(ushort*)(data + 6);
            if (vkey is > 0 and < 0xFF) _stray.Note(vkey, (flags & RI_KEY_BREAK) == 0, StraySource.ControllerBuiltIn, now);
        }
        else if (h->Type == 0)
        {
            Interlocked.Increment(ref _ctrlMouse);
            ushort b = *(ushort*)(data + 4);
            void Button(int vk, ushort downBit, ushort upBit)
            {
                if ((b & downBit) != 0) _stray.Note(vk, true, StraySource.ControllerBuiltIn, now);
                if ((b & upBit) != 0) _stray.Note(vk, false, StraySource.ControllerBuiltIn, now);
            }
            Button(1, 0x0001, 0x0002);
            Button(2, 0x0004, 0x0008);
            Button(4, 0x0010, 0x0020);
            Button(5, 0x0040, 0x0080);
            Button(6, 0x0100, 0x0200);
        }
    }

    // ---- Stuck-key cleaner -----------------------------------------------------------------------
    // Keys/buttons pressed by Steam (injected) or by the controller's built-in mode are tracked. If their release never
    // arrives (a mode switch mid-press, another app eating the "up"), Windows keeps them held: a stuck Windows/Alt/Esc
    // key makes typing trigger shortcuts. While BogiHub controls the controller they are released after a short hold,
    // and whenever control changes hands everything still held is released. Only releases are ever sent.

    private const uint CmdReleaseStray = WM_APP + 5, CmdReleaseStuck = WM_APP + 6;
    private const int StrayHoldMs = 150;
    private readonly StrayInputTracker _stray = new();
    private nuint _slowTimer, _fastTimer;
    private static volatile bool _hubInControl;
    private static long _releasedCount;

    /// <summary>Whether BogiHub currently controls the controller (set by the runtime).</summary>
    public static bool HubInControl
    {
        get => _hubInControl;
        set => _hubInControl = value;
    }

    /// <summary>Stuck keys/buttons released since start.</summary>
    public static long ReleasedCount => Interlocked.Read(ref _releasedCount);

    /// <summary>Human-readable notes from this thread (e.g. which stuck keys were released), for the log.</summary>
    public event Action<string>? Info;

    /// <summary>Release everything Steam or the controller's built-in mode still holds. Call whenever control changes.</summary>
    public void ReleaseStrayInput()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, CmdReleaseStray, 0, 0);
    }

    /// <summary>
    /// Manual / emergency clean-up: release modifier keys, Esc/Tab/Enter/Space and mouse buttons that Windows thinks
    /// are held. <paramref name="delayMs"/> lets the user let go of a hotkey first.
    /// </summary>
    public void ReleaseAllStuckKeys(int delayMs = 0)
    {
        if (delayMs <= 0)
        {
            if (_threadId != 0) PostThreadMessageW(_threadId, CmdReleaseStuck, 0, 0);
            return;
        }
        _ = Task.Delay(delayMs).ContinueWith(_ => { if (_threadId != 0) PostThreadMessageW(_threadId, CmdReleaseStuck, 0, 0); });
    }

    private static readonly int[] UsuallyStuck =
        [0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x1B, 0x09, 0x0D, 0x20, 1, 2, 4];

    private void ReleaseNow(IEnumerable<int> vks, string why)
    {
        var released = InputReleaser.ReleaseHeld(vks, _ownSignature);
        if (released.Count == 0) return;
        Interlocked.Add(ref _releasedCount, released.Count);
        Info?.Invoke($"Released stuck {string.Join(", ", released.Select(InputReleaser.Name))} ({why})");
    }

    private void StrayWatchdog()
    {
        if (!_hubInControl || _stray.Count == 0) return;
        // Built-in-mode presses are never wanted while BogiHub is in control. Steam's injected presses only count as
        // strays while the Steam guard is on; otherwise they may be another tool holding a key on purpose.
        var due = _stray.TakeDue(Environment.TickCount64, StrayHoldMs, s => s == StraySource.ControllerBuiltIn || _blockForeign);
        if (due.Count > 0) ReleaseNow(due, "pressed by Steam or the controller's built-in mode while BogiHub is in control");
    }

    private bool IsValveDevice(nint device)
    {
        if (_valveDevice.TryGetValue(device, out bool valve)) return valve;
        uint chars = 0;
        GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, null, &chars);
        valve = false;
        if (chars is > 0 and < 1024)
        {
            char* name = stackalloc char[(int)chars];
            if (GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, name, &chars) > 0)
                valve = new string(name).Contains("VID_28DE", StringComparison.OrdinalIgnoreCase);
        }
        _valveDevice[device] = valve;
        return valve;
    }

    // ---- Steam guard ---------------------------------------------------------------------------
    // While set, injected input from other programs is dropped, but only within a short window after real
    // controller activity: that is when Steam's desktop layout injects its mouse/keyboard events.
    // Physical keyboard/mouse input is never injected, so it is never affected.

    private const long GuardWindowMs = 300;
    private static volatile bool _blockForeign;
    private static long _controllerActiveTick;
    private static long _blockedCount;

    public static bool BlockForeignWhileControllerActive
    {
        get => _blockForeign;
        set => _blockForeign = value;
    }

    /// <summary>Called by the input thread whenever the controller is being used.</summary>
    public static void NoteControllerActivity() => Interlocked.Exchange(ref _controllerActiveTick, Environment.TickCount64);

    /// <summary>Foreign injected events dropped by the Steam guard since start.</summary>
    public static long BlockedCount => Interlocked.Read(ref _blockedCount);

    private static bool ShouldBlock() =>
        _blockForeign && Environment.TickCount64 - Interlocked.Read(ref _controllerActiveTick) < GuardWindowMs;

    /// <summary>Milliseconds since the last foreign injected event, or null if none seen while monitoring.</summary>
    public static long? MsSinceForeignInjected
    {
        get
        {
            long t = Interlocked.Read(ref _foreignInjectedLastTick);
            return t == 0 ? null : Environment.TickCount64 - t;
        }
    }

    public void Start()
    {
        _thread.Start();
        _ready.Wait(3000);
    }

    /// <summary>Installs or removes the low-level hooks used for conflict detection. Cheap to call repeatedly.</summary>
    public void SetConflictMonitoring(bool on)
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, on ? CmdHooksOn : CmdHooksOff, 0, 0);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        if (_thread.IsAlive) _thread.Join(2000);
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        MSG msg;
        PeekMessageW(&msg, 0, 0, 0, 0); // creates the thread's message queue

        _winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, &OnWinEvent, 0, 0, WINEVENT_OUTOFCONTEXT);
        HotkeyRegistered = RegisterHotKey(0, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_F12);
        GamepadHotkeyRegistered = RegisterHotKey(0, GamepadHotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_F11);
        _slowTimer = SetTimer(0, 0, 1000, null); // re-check fullscreen state: games often go fullscreen after gaining focus
        _fastTimer = SetTimer(0, 0, 100, null);  // stuck-key watchdog
        _rawHwnd = CreateWindowExW(0, "STATIC", "BogiHub raw input", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
        s_instance = this;
        _ready.Set();
        Refresh();

        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            switch (msg.message)
            {
                case WM_HOTKEY when msg.wParam == HotkeyId:
                    EmergencyStopPressed?.Invoke();
                    break;
                case WM_HOTKEY when msg.wParam == GamepadHotkeyId:
                    GamepadTogglePressed?.Invoke();
                    break;
                case WM_TIMER when msg.wParam == (nint)_fastTimer:
                    StrayWatchdog();
                    break;
                case WM_TIMER:
                    Refresh();
                    break;
                case CmdReleaseStray:
                    ReleaseNow(_stray.TakeAll(), "control changed hands");
                    break;
                case CmdReleaseStuck:
                    ReleaseNow(_stray.TakeAll().Concat(UsuallyStuck), "manual release");
                    break;
                case CmdHooksOn:
                    if (_mouseHook == 0) _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, &OnMouseHook, GetModuleHandleW(null), 0);
                    if (_keyboardHook == 0) _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, &OnKeyboardHook, GetModuleHandleW(null), 0);
                    break;
                case CmdHooksOff:
                    RemoveInputHooks();
                    break;
                case CmdRawOn:
                    RegisterRaw(true);
                    break;
                case CmdRawOff:
                    RegisterRaw(false);
                    break;
                case WM_INPUT:
                    HandleRawInput(msg.lParam);
                    break;
            }
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }

        RemoveInputHooks();
        RegisterRaw(false);
        if (_rawHwnd != 0) DestroyWindow(_rawHwnd);
        if (_winEventHook != 0) UnhookWinEvent(_winEventHook);
        if (HotkeyRegistered) UnregisterHotKey(0, HotkeyId);
        if (GamepadHotkeyRegistered) UnregisterHotKey(0, GamepadHotkeyId);
        s_instance = null;
    }

    private void RemoveInputHooks()
    {
        if (_mouseHook != 0) UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != 0) UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = _keyboardHook = 0;
    }

    private void Refresh()
    {
        var app = Describe(GetForegroundWindow());
        if (app == _lastForeground) return;
        Volatile.Write(ref _lastForeground, app);
        ForegroundChanged?.Invoke(app);
    }

    private ForegroundApp Describe(nint hwnd)
    {
        if (hwnd == 0) return ForegroundApp.None;

        string cls = ClassName(hwnd);
        bool shell = hwnd == GetShellWindow() || cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

        uint pid;
        GetWindowThreadProcessId(hwnd, &pid);
        string? path = ProcessPath(pid);
        string? exe = path is not null ? Path.GetFileName(path) : ProcessNameFallback(pid);

        return new ForegroundApp((int)pid, exe, path, WindowTitle(hwnd), cls, !shell && IsFullscreen(hwnd), shell, pid == _ownPid);
    }

    private static string? ProcessPath(uint pid)
    {
        nint h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return null; // protected (e.g. anti-cheat) processes may refuse; caller falls back
        try
        {
            char* buf = stackalloc char[1024];
            uint len = 1024;
            return QueryFullProcessImageNameW(h, 0, buf, &len) ? new string(buf, 0, (int)len) : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    private static string? ProcessNameFallback(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName + ".exe";
        }
        catch
        {
            return null;
        }
    }

    private static bool IsFullscreen(nint hwnd)
    {
        RECT r;
        if (!GetWindowRect(hwnd, &r)) return false;
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!GetMonitorInfoW(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), &mi)) return false;
        var m = mi.rcMonitor;
        return r.left <= m.left && r.top <= m.top && r.right >= m.right && r.bottom >= m.bottom;
    }

    private static string ClassName(nint hwnd)
    {
        char* buf = stackalloc char[256];
        int n = GetClassNameW(hwnd, buf, 256);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    private static string WindowTitle(nint hwnd)
    {
        char* buf = stackalloc char[256];
        int n = GetWindowTextW(hwnd, buf, 256);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    // ---- Callbacks (run on this class's thread) ----------------------------------------------

    private static WindowsEvents? s_instance;

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { s_instance?.Refresh(); } catch { /* never throw across the native boundary */ }
    }

    [UnmanagedCallersOnly]
    private static nint OnMouseHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = (MSLLHOOKSTRUCT*)lParam;
            if ((info->flags & LLMHF_INJECTED) != 0 && info->dwExtraInfo != _ownSignature)
            {
                CountForeign();
                uint m = (uint)wParam;
                bool up = m is WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP or WM_XBUTTONUP;
                // A release always passes: blocking an "up" whose "down" got through is what leaves buttons stuck.
                if (!up && ShouldBlock())
                {
                    Interlocked.Increment(ref _blockedCount);
                    return 1; // swallow: never reaches any window
                }
                int vk = m switch
                {
                    WM_LBUTTONDOWN or WM_LBUTTONUP => 1,
                    WM_RBUTTONDOWN or WM_RBUTTONUP => 2,
                    WM_MBUTTONDOWN or WM_MBUTTONUP => 4,
                    WM_XBUTTONDOWN or WM_XBUTTONUP => (info->mouseData >> 16) == 1 ? 5 : 6,
                    _ => 0,
                };
                if (vk != 0) s_instance?._stray.Note(vk, !up, StraySource.Injected, Environment.TickCount64);
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static nint OnKeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam;
            if ((info->flags & LLKHF_INJECTED) != 0 && info->dwExtraInfo != _ownSignature)
            {
                CountForeign();
                bool up = (info->flags & LLKHF_UP) != 0;
                // A key-up always passes, so the guard can never leave a key stuck down.
                if (!up && ShouldBlock())
                {
                    Interlocked.Increment(ref _blockedCount);
                    return 1;
                }
                s_instance?._stray.Note((int)info->vkCode, !up, StraySource.Injected, Environment.TickCount64);
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private static void CountForeign()
    {
        Interlocked.Increment(ref _foreignInjectedCount);
        Interlocked.Exchange(ref _foreignInjectedLastTick, Environment.TickCount64);
    }

    // ---- Win32 -------------------------------------------------------------------------------

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    private const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000, VK_F11 = 0x7A, VK_F12 = 0x7B;
    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    private const uint LLMHF_INJECTED = 0x1, LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80;
    private const uint WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205,
        WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_XBUTTONDOWN = 0x20B, WM_XBUTTONUP = 0x20C;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public int ptX, ptY;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    private const uint RIDEV_REMOVE = 0x1, RIDEV_INPUTSINK = 0x100, RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007;
    private const ushort RI_KEY_BREAK = 0x1;
    private static readonly nint HWND_MESSAGE = -3;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage, Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint Type, Size;
        public nint Device;
        public nint WParam;
    }

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool RegisterRawInputDevices(RAWINPUTDEVICE* devices, uint count, uint size);
    [LibraryImport("user32.dll")] private static partial uint GetRawInputData(nint raw, uint command, void* data, uint* size, uint headerSize);
    [LibraryImport("user32.dll")] private static partial uint GetRawInputDeviceInfoW(nint device, uint command, void* data, uint* size);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int ptX, ptY;
        public uint mouseData, flags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public nuint dwExtraInfo;
    }

    [LibraryImport("user32.dll")] private static partial int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PeekMessageW(MSG* msg, nint hwnd, uint min, uint max, uint remove);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool TranslateMessage(MSG* msg);
    [LibraryImport("user32.dll")] private static partial nint DispatchMessageW(MSG* msg);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostThreadMessageW(uint thread, uint msg, nint w, nint l);
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
    [LibraryImport("user32.dll")] private static partial nuint SetTimer(nint hwnd, nuint id, uint ms, void* proc);

    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(uint min, uint max, nint mod, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> proc, uint pid, uint tid, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")] private static partial nint SetWindowsHookExW(int id, delegate* unmanaged<int, nint, nint, nint> proc, nint mod, uint tid);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] private static partial nint CallNextHookEx(nint hook, int code, nint w, nint l);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial nint GetShellWindow();
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, uint* pid);
    [LibraryImport("user32.dll")] private static partial int GetClassNameW(nint hwnd, char* buf, int max);
    [LibraryImport("user32.dll")] private static partial int GetWindowTextW(nint hwnd, char* buf, int max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowRect(nint hwnd, RECT* r);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfoW(nint monitor, MONITORINFO* info);

    [LibraryImport("kernel32.dll")] private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool QueryFullProcessImageNameW(nint process, uint flags, char* buf, uint* size);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint h);
}
