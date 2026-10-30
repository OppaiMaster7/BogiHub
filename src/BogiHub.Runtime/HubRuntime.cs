using System.Diagnostics;
using BogiHub.Core.Control;
using BogiHub.Core.Input;
using BogiHub.Core.Mapping;
using BogiHub.Core.Notifications;
using BogiHub.Core.Output;
using BogiHub.Core.Profiles;
using BogiHub.Devices.Sdl;
using BogiHub.Output;
using BogiHub.Platform.Windows;

namespace BogiHub.Runtime;

/// <summary>Processing-time numbers measured inside BogiHub only (not full game latency).</summary>
public readonly record struct Timings(double AvgMapMicros, double MaxMapMicros, int SamplesPerSecond);

/// <summary>Everything the UI shows, captured at one moment.</summary>
public sealed record RuntimeStatus(
    ControlDecision Decision,
    ControllerStatus Controller,
    bool SteamRunning,
    bool SteamConflict,
    ForegroundApp Foreground,
    OutputStatus Gamepad,
    OutputStatus KeyboardMouse,
    bool DriverInstalled,
    bool HotkeyRegistered,
    string HotkeyLabel,
    Timings Timings,
    string SdlVersion,
    bool SteamGuardActive,
    long SteamInputsBlocked,
    InputSources Sources);

/// <summary>
/// Where keyboard/mouse input is coming from right now (events in the last second), and the built-in-mode keeper's
/// track record. <see cref="BuiltInLeaking"/> means the controller's own mouse/keyboard mode is active while BogiHub
/// holds the controller, i.e. its default inputs are mixing into games.
/// </summary>
public readonly record struct InputSources(
    bool Monitoring,
    long ControllerMousePerSec,
    long ControllerKeyboardPerSec,
    long InjectedPerSec,
    bool BuiltInLeaking,
    long LizardOffSent,
    long LizardOffFailed,
    bool LizardKeeperActive,
    long StuckKeysReleased);

/// <summary>
/// Owns the whole pipeline: controller reader → mapping engine → outputs.
/// Input-path work happens on the SDL input thread (<see cref="IControllerSink"/>); everything else
/// (policy, Steam polling, UI) happens elsewhere and only flips volatile flags or swaps profiles.
/// </summary>
public sealed class HubRuntime : IControllerSink, IDisposable
{
    private readonly SdlControllerService _controller;
    private readonly WindowsEvents _windows;
    private readonly ViGEmGamepadSink _gamepad = new();
    private readonly SendInputSink _kbm = new();
    private readonly MappingEngine _engine;
    private readonly OutputFrame _frame = new();
    private readonly Timer _policyTimer;
    private readonly Timer _updateTimer;
    private string? _updateNoticeShown; // tag already announced this run
    private readonly object _policyLock = new();

    private volatile bool _outputsActive;
    private volatile bool _keepPadPlugged;
    private string _requestedProfileId;
    private bool _outputsWereActive;
    private ControlDecision _decision;
    private List<Profile> _profiles;
    private volatile bool _emergencyPaused;
    private bool _steamRunning;
    private bool _driverInstalled;
    private bool _conflict;
    private long _lastControllerActivity;

    // Timing (input thread writes, UI reads approximately).
    private double _mapMicrosSum, _mapMicrosMax;
    private int _mapCount;
    private Timings _timings;
    private long _timingWindowStart = Environment.TickCount64;

    private readonly object _previewLock = new();
    private GamepadReport _previewPad;
    private string _previewKbm = "";

    public HubRuntime(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        Log = new DiagnosticsLog(500, Path.Combine(dataDirectory, "logs", "bogihub.log"));
        Store = new ProfileStore(Path.Combine(dataDirectory, "profiles"));
        SettingsPath = Path.Combine(dataDirectory, "settings.json");
        Settings = AppSettings.Load(SettingsPath);

        var (profiles, errors) = Store.LoadAll();
        _profiles = profiles;
        foreach (var e in errors) Log.Warn($"Profile skipped ({Path.GetFileName(e.FilePath)}): {e.Message}");

        var initial = _profiles.FirstOrDefault(p => p.Id == Settings.DefaultProfileId) ?? DefaultProfiles.Controller();
        _engine = new MappingEngine(initial);
        _requestedProfileId = initial.Id;
        _decision = new ControlDecision(ControlOwner.BuiltIn, null, "Starting…");

        _controller = new SdlControllerService(this);
        _windows = new WindowsEvents(SendInputSink.Signature);
        _windows.ForegroundChanged += _ => Recompute();
        _windows.EmergencyStopPressed += () => SetEmergencyPause(true);
        _windows.GamepadTogglePressed += ToggleControllerMode;
        _windows.Info += Log.Info;

        _gamepad.Rumble += OnGameRumble;
        _policyTimer = new Timer(_ => PollSystem(), null, Timeout.Infinite, Timeout.Infinite);
        _updateTimer = new Timer(_ => _ = CheckForUpdatesAsync(scheduled: true), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string DataDirectory { get; }
    public string SettingsPath { get; }
    public ProfileStore Store { get; }
    public AppSettings Settings { get; private set; }
    public DiagnosticsLog Log { get; }
    public UpdateChecker Updates { get; } = new();

    public event Action? StatusChanged;
    public event Action? ProfilesChanged;

    /// <summary>An on-screen notice to show (any thread). Already filtered by the notification settings.</summary>
    public event Action<Notice>? Notify;

    public IReadOnlyList<Profile> Profiles
    {
        get { lock (_policyLock) return [.. _profiles]; }
    }

    public ControllerState ControllerSnapshot => _controller.Snapshot;

    /// <summary>Cheap check for per-frame callers (the hologram), instead of building a full status snapshot.</summary>
    public bool ControllerConnected => _controller.Status.Connected;

    /// <summary>Whether the current decision is Gamepad mode (keyboard & mouse output suppressed).</summary>
    public bool GamepadOnly => _engine.GamepadOnly;

    /// <summary>What the mapping engine would output right now (even when outputs are not active).</summary>
    public (GamepadReport Pad, string KeyboardMouse) OutputPreview
    {
        get { lock (_previewLock) return (_previewPad, _previewKbm); }
    }

    public bool EmergencyPaused => _emergencyPaused;

    public void Start()
    {
        Log.Info("BogiHub starting");
        // Decide about the built-in mouse before the reader's first loop: with Steam running it must stay untouched.
        _steamRunning = Steam.IsRunning();
        _controller.BuiltInModeWanted = !_steamRunning;
        _controller.Start();
        _windows.Start();
        if (!_windows.HotkeyRegistered) Log.Warn($"Emergency hotkey {_windows.HotkeyLabel} is used by another app; use the tray menu instead.");
        _driverInstalled = ViGEmGamepadSink.ProbeDriver().Readiness == OutputReadiness.Ready;
        Log.Info(_driverInstalled ? "ViGEmBus driver found" : "ViGEmBus driver not installed: controller output unavailable until it is installed");
        PollSystem();
        _policyTimer.Change(1000, 1000);
        // Hourly look at the clock; the check itself runs at most once a day, and only with the setting on.
        _updateTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));
    }

    /// <summary>
    /// Asks GitHub for a newer release. Scheduled checks need the opt-in setting and a day since the last success;
    /// "Check now" in Settings is the user asking, so it always runs. A background find is announced once per run.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool scheduled = false)
    {
        if (scheduled && (!Settings.CheckForUpdates ||
            Updates.LastSuccessUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(23))) return;
        var found = await Updates.CheckAsync();
        if (Updates.LastError is { } error) Log.Warn($"Update check failed: {error}");
        else Log.Info(found is { } f ? $"Update check: {f.Tag} is available" : $"Update check: {UpdateChecker.Current} is the newest version");
        // "Check now" answers on the Settings page itself; only the background check needs a notice.
        if (scheduled && found is { } r && r.Tag != _updateNoticeShown)
        {
            _updateNoticeShown = r.Tag;
            Emit(new Notice("update", NoticeKind.Update, NoticeTone.Info, NoticeIcon.Download,
                $"BogiHub {r.Tag.TrimStart('v', 'V')} is out", "Click to open Settings and download it.",
                Action: NoticeAction.OpenApp, DurationMs: 8000));
        }
    }

    public void SetEmergencyPause(bool paused)
    {
        if (_emergencyPaused == paused) return;
        _emergencyPaused = paused;
        if (paused)
        {
            _outputsActive = false; // input thread releases within one tick
            // Also free anything Steam or the controller's built-in mode left held (a stuck Windows/Alt/Esc key).
            // The short delay lets the user let go of the Ctrl+Shift+F12 hotkey first.
            _windows.ReleaseAllStuckKeys(delayMs: 400);
            Log.Warn("Emergency stop: all generated input released");
        }
        else
        {
            Log.Info("Resumed after emergency stop");
        }
        Recompute();
    }

    public void UpdateSettings(Action<AppSettings> change)
    {
        lock (_policyLock)
        {
            change(Settings);
            Settings.Save(SettingsPath);
        }
        Recompute();
    }

    public void SaveProfile(Profile profile)
    {
        Store.Save(profile);
        lock (_policyLock)
        {
            int i = _profiles.FindIndex(p => p.Id == profile.Id);
            if (i >= 0) _profiles[i] = profile.Clone(); else _profiles.Add(profile.Clone());
        }
        Log.Info($"Profile saved: {profile.Name}");
        ProfilesChanged?.Invoke();
        Recompute(reloadProfileId: profile.Id);
    }

    public void DeleteProfile(string id)
    {
        Store.Delete(id);
        lock (_policyLock)
        {
            _profiles.RemoveAll(p => p.Id == id);
            var builtIn = DefaultProfiles.ById(id);
            if (builtIn is not null) _profiles.Insert(0, builtIn); // deleting a built-in resets it
            if (Settings.PinnedProfileId == id) Settings.PinnedProfileId = null;
        }
        ProfilesChanged?.Invoke();
        Recompute(reloadProfileId: id);
    }

    public (Profile? Profile, List<string> Errors) ImportProfile(string path)
    {
        var result = Store.Import(path, Profiles.Select(p => p.Id));
        if (result.Profile is not null)
        {
            lock (_policyLock) _profiles.Add(result.Profile);
            Log.Info($"Imported profile {result.Profile.Name}");
            ProfilesChanged?.Invoke();
        }
        else
        {
            Log.Warn($"Import rejected: {string.Join(" ", result.Errors)}");
        }
        return result;
    }

    /// <summary>Short buzz; works whether the controller is a gamepad or its own mouse &amp; keyboard.</summary>
    public void TestRumble() => _controller.Buzz((300, 0));

    public string GamepadHotkeyLabel => _windows.GamepadHotkeyLabel;
    public bool GamepadHotkeyRegistered => _windows.GamepadHotkeyRegistered;

    /// <summary>Two short pulses: the controller is now a gamepad.</summary>
    private static readonly (int, int)[] GamepadBuzz = [(90, 140), (90, 0)];
    /// <summary>One long pulse: the controller is now a mouse &amp; keyboard.</summary>
    private static readonly (int, int)[] MouseBuzz = [(280, 0)];

    /// <summary>
    /// Holding "…" (or Ctrl+Shift+F11): flip what the controller is right now, gamepad ↔ mouse &amp; keyboard.
    /// From Auto this switches to Manual so the choice sticks. Ignored when the switch is Off. Thread-safe.
    /// </summary>
    public void ToggleControllerMode()
    {
        bool toGamepad = false, ignored = false;
        UpdateSettings(s =>
        {
            if (s.Switch == ControlSwitch.Off)
            {
                ignored = true;
                return;
            }
            toGamepad = !_decision.HubActive; // flip what the controller is doing at this moment
            s.Switch = ControlSwitch.Manual;
            s.ManualMode = toGamepad ? ManualMode.Gamepad : ManualMode.MouseKeyboard;
        });
        if (ignored)
        {
            Log.Info("Mode shortcut ignored: the switch is Off");
            return;
        }
        Log.Info(toGamepad ? "Shortcut: controller is now a gamepad (Manual)" : "Shortcut: controller is now a mouse & keyboard (Manual)");
        _controller.Buzz(toGamepad ? GamepadBuzz : MouseBuzz);
    }

    public bool RequestSteamExit()
    {
        bool ok = Steam.RequestCleanExit();
        Log.Info(ok ? "Asked Steam to exit cleanly (steam.exe -shutdown)" : "Could not find steam.exe to request a clean exit");
        return ok;
    }

    public void RefreshDriverStatus()
    {
        _driverInstalled = ViGEmGamepadSink.ProbeDriver().Readiness == OutputReadiness.Ready;
        Recompute();
    }

    public RuntimeStatus GetStatus()
    {
        ControlDecision d;
        lock (_policyLock) d = _decision;
        return new RuntimeStatus(
            d, _controller.Status, _steamRunning, _conflict, _windows.Foreground,
            _gamepad.Status, _kbm.Status, _driverInstalled, _windows.HotkeyRegistered, _windows.HotkeyLabel,
            _timings, _controller.SdlVersion, WindowsEvents.BlockForeignWhileControllerActive, WindowsEvents.BlockedCount,
            _sources);
    }

    private InputSources _sources;
    private long _hubActiveSince;
    private (long Mouse, long Keyboard, long Injected) _lastSourceCounts;
    private bool _leakReported;

    /// <summary>Once a second: how much input each source produced, and whether the built-in mode is leaking.</summary>
    private void UpdateSources()
    {
        // Always on: the stuck-key cleaner needs to know what the controller's built-in mode pressed even before
        // BogiHub takes over, so it can release those keys at the hand-over.
        bool monitoring = true;
        _windows.SetSourceMonitoring(monitoring);

        var c = WindowsEvents.SourceCounts;
        long mouse = c.ControllerMouse - _lastSourceCounts.Mouse;
        long keys = c.ControllerKeyboard - _lastSourceCounts.Keyboard;
        long injected = c.Injected - _lastSourceCounts.Injected;
        _lastSourceCounts = (c.ControllerMouse, c.ControllerKeyboard, c.Injected);

        // Only a problem while BogiHub holds the controller: otherwise built-in mode is supposed to be on.
        // A switch to gamepad takes a moment to land; events in that window are the hand-over, not a leak.
        bool settled = Environment.TickCount64 - Interlocked.Read(ref _hubActiveSince) > 1500;
        bool leaking = _outputsActive && settled && _controller.Status.Connected && (mouse + keys) > 0;
        if (leaking && !_leakReported)
            Log.Warn($"The controller's built-in mouse/keyboard is still active while BogiHub is in control ({mouse} mouse, {keys} key events in 1 s). Re-sending the off command every 0.5 s.");
        if (!leaking && _leakReported) Log.Info("Built-in mouse/keyboard leak stopped");
        _leakReported = leaking;

        var (sent, failed) = _controller.LizardStats;
        _sources = new InputSources(monitoring, mouse, keys, injected, leaking, sent, failed, _controller.LizardKeeperActive, WindowsEvents.ReleasedCount);
    }

    // ---- Policy (timer / events / UI threads) -------------------------------------------------

    private void PollSystem()
    {
        bool steam = Steam.IsRunning();
        if (steam != _steamRunning)
        {
            _steamRunning = steam;
            Log.Info(steam ? "Steam started" : "Steam closed");
        }

        // Conflict: someone else injected keyboard/mouse input while the controller was being used and BogiHub was in control.
        // With the Steam guard on, that input is dropped instead, so it is not a conflict any more.
        bool hubActive = _outputsActive;
        UpdateSteamGuard();
        UpdateSources();
        long? foreignAgo = WindowsEvents.MsSinceForeignInjected;
        bool controllerBusy = Environment.TickCount64 - Interlocked.Read(ref _lastControllerActivity) < 1500;
        bool conflict = hubActive && steam && !WindowsEvents.BlockForeignWhileControllerActive && controllerBusy && foreignAgo is < 1500;
        if (conflict != _conflict)
        {
            _conflict = conflict;
            if (conflict) Log.Warn("Possible double input: another program (likely Steam) is also sending keyboard/mouse input");
        }

        Recompute();
    }

    /// <param name="reloadProfileId">A profile whose contents changed; re-sent to the engine only if it is the active one.</param>
    private void Recompute(string? reloadProfileId = null)
    {
        ControlDecision next, prev;
        lock (_policyLock)
        {
            var fg = _windows.Foreground;
            prev = _decision;
            next = ControlPolicy.Decide(new ControlInputs(
                Settings.Switch, Settings.ManualMode, _emergencyPaused, _steamRunning, fg.ExeName, fg.IsFullscreen, fg.IsDesktopShell,
                fg.IsSelf, Settings.PinnedProfileId, Settings.DefaultProfileId, Settings.FullscreenIsGame, _profiles));
            _decision = next;

            // Compare against what we last *asked* for, not what the engine has applied yet, so a profile is never
            // sent twice (each send releases held outputs for one sample).
            if (next.Profile is not null && (next.Profile.Id != _requestedProfileId || next.Profile.Id == reloadProfileId))
            {
                _requestedProfileId = next.Profile.Id;
                _engine.SetProfile(next.Profile);
            }

            // The virtual pad stays plugged in while BogiHub merely stands aside (e.g. alt-tab to the desktop),
            // so games never see a "controller disconnected" blip. It is unplugged only when switched off or stopped.
            _keepPadPlugged = Settings.Switch != ControlSwitch.Off && !_emergencyPaused;

            // Applied under the same lock as the decision: Recompute runs on several threads (timer, focus changes,
            // UI, hotkeys) and an older decision must never overwrite a newer one.
            _engine.GamepadOnly = next.GamepadOnly;
            _outputsActive = next.HubActive;
            WindowsEvents.HubInControl = next.HubActive;
            UpdateAcquire();
            UpdateSteamGuard();

            // Control changed hands: free every key/button Steam or the controller's built-in mode still holds.
            // This is the moment a press can lose its release (built-in mode switched off mid-press).
            if (next.HubActive != prev.HubActive || next.GamepadOnly != prev.GamepadOnly)
                _windows.ReleaseStrayInput();
            if (next.HubActive && !prev.HubActive) Interlocked.Exchange(ref _hubActiveSince, Environment.TickCount64);

            // Under the lock too, so notices see decisions in the order they were made.
            UpdateNotices(next);
        }

        if (next.Owner != prev.Owner || next.Profile?.Id != prev.Profile?.Id || next.GamepadOnly != prev.GamepadOnly)
            Log.Info($"{next.Owner}{(next.Profile is null ? "" : $" · {next.Profile.Name}")} — {next.Reason}");

        if (next.HubActive && !prev.HubActive) MaybeAutoExitSteam();

        StatusChanged?.Invoke();
    }

    // ---- Notices ------------------------------------------------------------------------------

    private readonly NoticeTracker _notices = new();

    /// <summary>Compares the live state with the last call and emits notices for what changed. Called under the policy lock.</summary>
    private void UpdateNotices(ControlDecision d)
    {
        var c = _controller.Status;
        var fg = _windows.Foreground;
        var inputs = new NoticeInputs(
            Environment.TickCount64, d.Owner, d.IsMouseKeyboard, d.Profile?.Name, fg.IsSelf,
            c.Connected, c.Device?.Transport, c.Battery, _steamRunning, Settings.AutoExitSteam,
            _sources.BuiltInLeaking, _conflict,
            DriverMissing: d.HubActive && d.Profile?.Output != OutputMode.KeyboardMouse && !_driverInstalled,
            WindowsEvents.ReleasedCount);
        foreach (var n in _notices.Update(inputs)) Emit(n);
    }

    private void Emit(Notice n)
    {
        Log.Info($"Notice: {n.Title}{(n.Detail is null ? "" : $" — {n.Detail}")}");
        if (!Settings.ShowNotifications && n.Kind != NoticeKind.Test) return;
        if (n.Kind == NoticeKind.Mode && !Settings.NotifyModeChanges && n.Icon != NoticeIcon.Pause) return;
        Notify?.Invoke(n);
    }

    /// <summary>Settings → "Show an example": a notice built from the current state.</summary>
    /// <summary>Settings → "Feel it": one click and a short run of ticks on each pad at the chosen strength.</summary>
    public void TestPadHaptics()
    {
        float strength = Math.Max(Settings.PadHapticStrength, 0.05f);
        foreach (bool left in new[] { true, false })
        {
            _controller.PadHaptic(left, click: true, strength);
            for (int i = 0; i < 6; i++)
            {
                int delay = 250 + i * 60 + (left ? 0 : 700);
                bool side = left;
                _ = Task.Delay(delay).ContinueWith(_ => _controller.PadHaptic(side, click: false, strength));
            }
        }
    }

    public void ShowTestNotice()
    {
        var s = GetStatus();
        var b = s.Controller.Battery;
        Emit(new Notice("test", NoticeKind.Test, NoticeTone.Info, s.Decision.IsMouseKeyboard ? NoticeIcon.Mouse : NoticeIcon.Gamepad,
            "Notifications look like this",
            s.Controller.Connected ? $"Controller: {(s.Decision.IsMouseKeyboard ? "mouse & keyboard" : "gamepad")}" : "No controller connected",
            b.HasPercent && s.Controller.Connected ? b.Percent : null));
    }

    private long _lastAutoSteamExit = -60_000;

    /// <summary>Optional: Steam keeps applying its own layout to the controller, so close it when BogiHub takes over.</summary>
    private void MaybeAutoExitSteam()
    {
        if (!Settings.AutoExitSteam || !_steamRunning) return;
        long now = Environment.TickCount64;
        if (now - _lastAutoSteamExit < 60_000) return; // never nag Steam in a loop
        _lastAutoSteamExit = now;
        Log.Info("BogiHub took the controller and \"Close Steam automatically\" is on: asking Steam to exit");
        Emit(new Notice("steam", NoticeKind.Steam, NoticeTone.Info, NoticeIcon.Steam, "Closing Steam",
            "So it stops adding its own keyboard and shortcuts on top of BogiHub."));
        Task.Run(RequestSteamExit);
    }

    /// <summary>Manual clean-up for the tray / UI: releases modifiers, Esc, Tab, Enter, Space and mouse buttons left held.</summary>
    public void ReleaseStuckKeys() => _windows.ReleaseAllStuckKeys();

    /// <summary>
    /// Hooks are only installed while they can matter (BogiHub in control and Steam running); the guard then drops
    /// Steam's injected desktop-layout input during controller use, if the user left it enabled.
    /// </summary>
    private void UpdateSteamGuard()
    {
        lock (_policyLock) ApplySteamGuard(); // re-entrant: also called from inside Recompute's lock
    }

    private void ApplySteamGuard()
    {
        bool relevant = _outputsActive && _steamRunning;
        bool guard = relevant && Settings.BlockSteamInput;
        if (guard != WindowsEvents.BlockForeignWhileControllerActive)
            Log.Info(guard ? "Steam guard on: Steam's controller mouse/keyboard input is blocked while BogiHub is in control"
                           : "Steam guard off");
        WindowsEvents.BlockForeignWhileControllerActive = guard;
        _windows.SetConflictMonitoring(relevant);
    }

    // Capacitive sensors (resting a thumb or holding the grips) are not "using" the controller.
    private const ulong TouchSensorMask =
        (1UL << (int)PhysicalInput.RightStickTouch) | (1UL << (int)PhysicalInput.LeftStickTouch) |
        (1UL << (int)PhysicalInput.RightGripTouch) | (1UL << (int)PhysicalInput.LeftGripTouch);

    private void UpdateAcquire()
    {
        // Hold the device when BogiHub controls it, or for the live preview while the window is open.
        // Otherwise close it so the controller's firmware returns to its built-in desktop mode.
        // Hold the controller only while it is a gamepad. Otherwise hand it back so its own mouse & keyboard work;
        // the reader keeps decoding it passively, so the hologram and "…" still work. With Steam running, Steam keeps
        // the built-in mode off and drives the controller itself, so BogiHub must not switch it on (double input).
        _controller.BuiltInModeWanted = !_steamRunning;
        _controller.Acquire = _outputsActive;
    }

    // ---- Input thread -------------------------------------------------------------------------

    public void OnSample(in ControllerState state, float dt)
    {
        long t0 = Stopwatch.GetTimestamp();

        if ((state.Buttons & ~TouchSensorMask) != 0 || state.LeftPad.Touching || state.RightPad.Touching ||
            state.LeftStick.LengthSquared() > 0.04f || state.RightStick.LengthSquared() > 0.04f ||
            state.LeftTrigger > 0.1f || state.RightTrigger > 0.1f)
        {
            Interlocked.Exchange(ref _lastControllerActivity, Environment.TickCount64);
            WindowsEvents.NoteControllerActivity();
        }

        DetectGamepadShortcut(in state);

        _engine.Map(in state, dt, _frame);
        bool active = _outputsActive;

        if (_engine.ProfileJustChanged || (_outputsWereActive && !active))
            ReleaseAll(disconnectPad: !active && !_keepPadPlugged);
        else if (!active && !_keepPadPlugged && _gamepad.IsConnected)
            _gamepad.Disconnect();

        if (active)
        {
            if (_frame.UsesGamepad)
            {
                if (!_gamepad.IsConnected && _driverInstalled && !_gamepad.Connect())
                    Log.Error(_gamepad.Status.Message);
                _gamepad.Submit(_frame.Pad);
            }
            else if (_gamepad.IsConnected)
            {
                _gamepad.Disconnect();
            }

            if (_frame.UsesKeyboardMouse) _kbm.Submit(_frame);
            else if (_kbm.IsHoldingAnything) _kbm.Neutralize();

            float haptic = Settings.PadHapticStrength;
            if (haptic > 0)
            {
                if (_frame.TicksLeft > 0 || _frame.ClickLeft) _controller.PadHaptic(left: true, _frame.ClickLeft, haptic);
                if (_frame.TicksRight > 0 || _frame.ClickRight) _controller.PadHaptic(left: false, _frame.ClickRight, haptic);
            }
        }
        _outputsWereActive = active;

        PublishPreview();
        RecordTiming(t0);
    }

    public const int GamepadShortcutHoldMs = 1500;
    private long _qamDownTick = -1;
    private bool _qamFired;

    /// <summary>Holding "…" (Quick Access) for 1.5 s toggles Gamepad mode. Fires once per hold.</summary>
    private void DetectGamepadShortcut(in ControllerState state)
    {
        if (!Settings.HoldQamForGamepad) return;
        if (!state.IsPressed(PhysicalInput.Qam))
        {
            _qamDownTick = -1;
            _qamFired = false;
            return;
        }
        long now = Environment.TickCount64;
        if (_qamDownTick < 0) _qamDownTick = now;
        if (!_qamFired && now - _qamDownTick >= GamepadShortcutHoldMs)
        {
            _qamFired = true;
            Task.Run(ToggleControllerMode); // settings I/O stays off the input thread
        }
    }

    public void OnDeviceLost()
    {
        _qamDownTick = -1;
        ReleaseAll(disconnectPad: !_keepPadPlugged);
        _outputsWereActive = false;
        Log.Info("Controller released or disconnected: outputs neutralised");
    }

    private void ReleaseAll(bool disconnectPad)
    {
        _kbm.Neutralize();
        if (disconnectPad) _gamepad.Disconnect();
        else _gamepad.Neutralize();
        _controller.Rumble(0, 0, 0);
    }

    private void OnGameRumble(byte large, byte small)
    {
        if (!Settings.RumblePassthrough || !_outputsActive) return;
        float k = Settings.RumbleStrength * 257f;
        // XInput rumble lasts until changed; SDL needs a duration, so use its maximum and rely on the game's stop message.
        _controller.Rumble((ushort)(large * k), (ushort)(small * k), large == 0 && small == 0 ? 0u : 0xFFFFu);
    }

    private void PublishPreview()
    {
        string kbm = "";
        if (_frame.UsesKeyboardMouse)
        {
            var parts = new List<string>();
            for (int vk = 1; vk < 256; vk++)
                if (_frame.Keys[vk]) parts.Add(Enum.IsDefined((Key)vk) ? ((Key)vk).ToString() : $"VK{vk:X2}");
            if (_frame.Mouse != MouseButtons.None) parts.Add($"Mouse {_frame.Mouse}");
            if (_frame.MouseDx != 0 || _frame.MouseDy != 0) parts.Add("Cursor moving");
            if (_frame.WheelNotches != 0) parts.Add($"Wheel {_frame.WheelNotches:+0;-0}");
            kbm = string.Join(" · ", parts);
        }
        lock (_previewLock)
        {
            _previewPad = _frame.UsesGamepad ? _frame.Pad : GamepadReport.Neutral;
            if (kbm.Length > 0 || !_frame.UsesKeyboardMouse) _previewKbm = kbm;
        }
    }

    private void RecordTiming(long t0)
    {
        double us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
        _mapMicrosSum += us;
        _mapCount++;
        if (us > _mapMicrosMax) _mapMicrosMax = us;

        long now = Environment.TickCount64;
        if (now - _timingWindowStart >= 1000)
        {
            _timings = new Timings(_mapCount == 0 ? 0 : _mapMicrosSum / _mapCount, _mapMicrosMax, _mapCount);
            if (_mapMicrosMax > 4000) Log.Warn($"Timing spike: one sample took {_mapMicrosMax / 1000:0.0} ms inside BogiHub");
            _mapMicrosSum = _mapMicrosMax = 0;
            _mapCount = 0;
            _timingWindowStart = now;
        }
    }

    public void Dispose()
    {
        _policyTimer.Dispose();
        _updateTimer.Dispose();
        WindowsEvents.BlockForeignWhileControllerActive = false;
        _outputsActive = false;
        _controller.Acquire = false;
        _controller.Dispose(); // closes the device → OnDeviceLost releases outputs
        _gamepad.Dispose();
        _kbm.Neutralize();
        WindowsEvents.HubInControl = false;
        _windows.ReleaseStrayInput(); // queued before the events thread's quit message, so it runs first
        _windows.Dispose();
        Log.Info("BogiHub stopped; all outputs released");
    }
}
