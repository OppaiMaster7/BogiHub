using BogiHub.Core.Control;
using BogiHub.Core.Input;

namespace BogiHub.Core.Notifications;

/// <summary>What a notice is about. The user can mute <see cref="Mode"/> on its own (it is the chattiest).</summary>
public enum NoticeKind { Mode, Controller, Battery, Steam, Problem, Test }

public enum NoticeTone { Info, Ok, Warn, Danger }

public enum NoticeIcon { Gamepad, Mouse, Steam, Pause, Controller, Unplugged, Battery, Charging, Warning, Keys }

/// <summary>Where on the main screen notices appear.</summary>
public enum NoticePosition { TopCenter, TopRight, BottomRight }

/// <summary>Clicking the notice does something (otherwise clicks go straight through it to the game).</summary>
public enum NoticeAction { None, OpenApp }

/// <summary>
/// One on-screen notification. A newer notice with the same <see cref="Key"/> replaces the one on screen in place
/// (e.g. quick alt-tabs morph one "mode" notice instead of stacking several).
/// </summary>
public sealed record Notice(
    string Key,
    NoticeKind Kind,
    NoticeTone Tone,
    NoticeIcon Icon,
    string Title,
    string? Detail,
    int? BatteryPercent = null,
    NoticeAction Action = NoticeAction.None,
    int DurationMs = 3200);

/// <summary>Live facts the tracker compares from one call to the next. Built by the runtime.</summary>
public readonly record struct NoticeInputs(
    long NowMs,
    ControlOwner Owner,
    bool MouseKeyboard,
    string? ProfileName,
    bool ForegroundIsSelf,
    bool ControllerConnected,
    string? Transport,
    BatteryInfo Battery,
    bool SteamRunning,
    bool SteamAutoExit,
    bool BuiltInLeaking,
    bool DoubleInput,
    bool DriverMissing,
    long StuckKeysReleased);

/// <summary>
/// Turns state changes into notices: who controls the controller, Steam getting in the way, battery, connection and
/// problems. Pure (time comes in through <see cref="NoticeInputs.NowMs"/>), so it is unit-tested without hardware.
/// Not thread-safe; the runtime calls it under a lock.
/// </summary>
public sealed class NoticeTracker
{
    /// <summary>Battery levels that warn once each on the way down (re-armed after charging or rising 5 % above).</summary>
    public static readonly int[] BatteryWarnLevels = [20, 10, 5];

    // A disconnect must last this long before it is reported: switching between held and passive reading
    // closes and reopens the device, which must never look like an unplug.
    public const long DisconnectSettleMs = 2500;
    public const long ConnectSettleMs = 600;

    private bool _started;
    private NoticeInputs _prev;
    private (ControlOwner Owner, bool MouseKeyboard, string? Profile) _shownMode;

    private bool _connectedShown;
    private bool _rawConnected;
    private long _rawConnectedSince;

    private int _batteryWarnedAt = int.MaxValue; // lowest level already warned about
    private PowerStatus _shownPower = PowerStatus.Unknown;

    private readonly Dictionary<string, long> _lastShown = [];

    public List<Notice> Update(in NoticeInputs i)
    {
        var list = new List<Notice>();
        if (!_started)
        {
            _started = true;
            _prev = i;
            _rawConnected = _connectedShown = i.ControllerConnected;
            _rawConnectedSince = i.NowMs;
            _shownMode = ModeOf(i);
            _shownPower = i.Battery.Status;
            if (i.ControllerConnected) list.Add(ModeNotice(i, "BogiHub is running"));
            CheckBattery(i, list, initial: true);
            return list;
        }

        UpdateConnection(i, list);
        UpdateMode(i, list);
        UpdateSteam(i, list);
        if (_connectedShown) CheckBattery(i, list, initial: false);
        UpdateProblems(i, list);

        _prev = i;
        return list;
    }

    // ---- Connection --------------------------------------------------------------------------

    private void UpdateConnection(in NoticeInputs i, List<Notice> list)
    {
        if (i.ControllerConnected != _rawConnected)
        {
            _rawConnected = i.ControllerConnected;
            _rawConnectedSince = i.NowMs;
        }
        if (_rawConnected == _connectedShown) return;

        long stable = i.NowMs - _rawConnectedSince;
        if (_rawConnected && stable >= ConnectSettleMs)
        {
            _connectedShown = true;
            _shownMode = ModeOf(i); // the connect notice already says what the controller is
            _batteryWarnedAt = int.MaxValue;
            _shownPower = i.Battery.Status;
            string detail = $"{ModeText(i).Title} · {i.Transport ?? "connected"}";
            list.Add(new Notice("controller", NoticeKind.Controller, NoticeTone.Ok, NoticeIcon.Controller, "Controller connected", detail,
                BatteryPercent: i.Battery.HasPercent ? i.Battery.Percent : null));
        }
        else if (!_rawConnected && stable >= DisconnectSettleMs)
        {
            _connectedShown = false;
            list.Add(new Notice("controller", NoticeKind.Controller, NoticeTone.Warn, NoticeIcon.Unplugged, "Controller disconnected",
                "Turned off, out of range or unplugged. Everything it was pressing has been released.", DurationMs: 4500));
        }
    }

    // ---- Who is in control ------------------------------------------------------------------

    private static (ControlOwner, bool, string?) ModeOf(in NoticeInputs i) =>
        (i.Owner, i.MouseKeyboard, i.Owner == ControlOwner.Hub ? i.ProfileName : null);

    private void UpdateMode(in NoticeInputs i, List<Notice> list)
    {
        var mode = ModeOf(i);
        if (mode == _shownMode) return;
        bool pauseChanged = (mode.Item1 == ControlOwner.Paused) != (_shownMode.Owner == ControlOwner.Paused);
        _shownMode = mode;

        // Emergency stop and resume always show. Other changes only matter with a controller in hand, and not while
        // the user is looking at BogiHub itself (the window already shows it).
        if (!pauseChanged && (!_connectedShown || i.ForegroundIsSelf)) return;
        list.Add(ModeNotice(i, null));
    }

    private static Notice ModeNotice(in NoticeInputs i, string? titleOverride)
    {
        var (title, detail, icon, tone) = ModeText(i);
        int? battery = titleOverride is not null && i.Battery.HasPercent ? i.Battery.Percent : null;
        if (titleOverride is not null) detail = $"{title} · {detail}";
        return new Notice("mode", NoticeKind.Mode, tone, icon, titleOverride ?? title, detail, battery,
            DurationMs: i.Owner == ControlOwner.Paused ? 5000 : 2600);
    }

    public static (string Title, string Detail, NoticeIcon Icon, NoticeTone Tone) ModeText(in NoticeInputs i) => i.Owner switch
    {
        ControlOwner.Paused => ("Emergency stop", "All input released. Resume from the tray or BogiHub.", NoticeIcon.Pause, NoticeTone.Danger),
        ControlOwner.Hub when i.MouseKeyboard => ("Mouse & keyboard", $"BogiHub in control · {i.ProfileName}", NoticeIcon.Mouse, NoticeTone.Info),
        ControlOwner.Hub => ("Gamepad", $"BogiHub in control · {i.ProfileName}", NoticeIcon.Gamepad, NoticeTone.Info),
        ControlOwner.Steam => ("Steam in control", "Steam's own layout drives the controller", NoticeIcon.Steam, NoticeTone.Info),
        _ => ("Mouse & keyboard", "The controller's own mouse mode", NoticeIcon.Mouse, NoticeTone.Info),
    };

    // ---- Steam --------------------------------------------------------------------------------

    private void UpdateSteam(in NoticeInputs i, List<Notice> list)
    {
        bool clash = i.Owner == ControlOwner.Hub && i.SteamRunning && _connectedShown;
        bool wasClash = _prev.Owner == ControlOwner.Hub && _prev.SteamRunning;
        if (clash && !wasClash && !i.SteamAutoExit && Due("steam", i.NowMs, 10 * 60_000))
        {
            list.Add(new Notice("steam", NoticeKind.Steam, NoticeTone.Warn, NoticeIcon.Steam, "Close Steam for the best result",
                "Steam still adds its own keyboard and shortcuts on top of BogiHub.",
                Action: NoticeAction.OpenApp, DurationMs: 6500));
        }
        else if (!i.SteamRunning && _prev.SteamRunning && _connectedShown)
        {
            list.Add(new Notice("steam", NoticeKind.Steam, NoticeTone.Ok, NoticeIcon.Steam, "Steam closed",
                i.Owner == ControlOwner.Hub ? "BogiHub has the controller to itself now." : "The controller is its own mouse & keyboard again."));
        }
    }

    // ---- Battery ------------------------------------------------------------------------------

    private void CheckBattery(in NoticeInputs i, List<Notice> list, bool initial)
    {
        var b = i.Battery;
        if (b.Status == PowerStatus.Unknown || !b.HasPercent) return;

        bool charging = b.Status is PowerStatus.Charging or PowerStatus.Charged;
        if (!initial && b.Status != _shownPower)
        {
            if (b.Status == PowerStatus.Charging && _shownPower == PowerStatus.OnBattery)
                list.Add(new Notice("battery", NoticeKind.Battery, NoticeTone.Ok, NoticeIcon.Charging, "Charging", $"Battery at {b.Percent} %", b.Percent));
            else if (b.Status == PowerStatus.Charged && _shownPower is PowerStatus.Charging or PowerStatus.OnBattery)
                list.Add(new Notice("battery", NoticeKind.Battery, NoticeTone.Ok, NoticeIcon.Charging, "Fully charged", "You can unplug the controller.", 100));
        }
        _shownPower = b.Status;

        if (charging)
        {
            _batteryWarnedAt = int.MaxValue;
            return;
        }
        // Re-arm once the level is clearly back above a warned threshold (noisy readings must not re-warn).
        if (_batteryWarnedAt != int.MaxValue && b.Percent >= _batteryWarnedAt + 5) _batteryWarnedAt = int.MaxValue;

        int? crossed = null;
        foreach (int level in BatteryWarnLevels)
            if (b.Percent <= level && level < _batteryWarnedAt) crossed = level;
        if (crossed is not { } at) return;
        _batteryWarnedAt = at;
        var tone = at <= 10 ? NoticeTone.Danger : NoticeTone.Warn;
        string detail = at <= 5 ? "Charge it now. The controller will switch off soon." : "Time to charge the controller soon.";
        list.Add(new Notice("battery", NoticeKind.Battery, tone, NoticeIcon.Battery, $"Battery low · {b.Percent} %", detail, b.Percent,
            DurationMs: at <= 10 ? 6000 : 4500));
    }

    // ---- Problems -----------------------------------------------------------------------------

    private void UpdateProblems(in NoticeInputs i, List<Notice> list)
    {
        if (i.DriverMissing && !_prev.DriverMissing && Due("driver", i.NowMs, 10 * 60_000))
            list.Add(new Notice("driver", NoticeKind.Problem, NoticeTone.Danger, NoticeIcon.Warning, "Virtual Xbox controller missing",
                "Games can't see the gamepad until the ViGEmBus driver is installed.",
                Action: NoticeAction.OpenApp, DurationMs: 7000));

        if (i.BuiltInLeaking && !_prev.BuiltInLeaking && Due("leak", i.NowMs, 2 * 60_000))
            list.Add(new Notice("leak", NoticeKind.Problem, NoticeTone.Warn, NoticeIcon.Mouse, "Controller's own mouse is still on",
                i.SteamRunning ? "Its default buttons mix into the game. Steam is probably turning it back on; closing Steam fixes it."
                               : "Its default buttons mix into the game. BogiHub keeps switching it off.",
                Action: i.SteamRunning ? NoticeAction.OpenApp : NoticeAction.None, DurationMs: 6000));

        if (i.DoubleInput && !_prev.DoubleInput && Due("double", i.NowMs, 2 * 60_000))
            list.Add(new Notice("double", NoticeKind.Problem, NoticeTone.Warn, NoticeIcon.Warning, "Double input",
                "Another program (likely Steam) is also typing for the controller.",
                Action: NoticeAction.OpenApp, DurationMs: 6000));

        if (i.StuckKeysReleased > _prev.StuckKeysReleased && Due("keys", i.NowMs, 60_000))
        {
            long n = i.StuckKeysReleased - _prev.StuckKeysReleased;
            list.Add(new Notice("keys", NoticeKind.Problem, NoticeTone.Info, NoticeIcon.Keys,
                n == 1 ? "Released a stuck key" : $"Released {n} stuck keys",
                "Something left a key held down. BogiHub let go of it for you."));
        }
    }

    /// <summary>True (and remembered) if <paramref name="key"/> was not shown within the last <paramref name="gapMs"/>.</summary>
    private bool Due(string key, long now, long gapMs)
    {
        if (_lastShown.TryGetValue(key, out long last) && now - last < gapMs) return false;
        _lastShown[key] = now;
        return true;
    }
}
