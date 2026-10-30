using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BogiHub.App.Controls;
using BogiHub.App.Theme;
using BogiHub.Core.Control;
using BogiHub.Core.Input;
using BogiHub.Output;
using BogiHub.Runtime;

namespace BogiHub.App.Pages;

public sealed class OverviewPage : UserControl, IPage
{
    private readonly HubRuntime _runtime;
    private readonly HologramView _holo;
    private readonly TextBlock _deviceTitle = Ui.Text("Steam Controller", "h1");
    private readonly TextBlock _deviceSub = Ui.Text("", "muted");

    private readonly StatusPill _ownerPill = new();
    private readonly TextBlock _reason = Ui.Text("", "muted");
    private readonly SegmentedSwitch _switch;
    private readonly SegmentedSwitch _manualSwitch;
    private readonly StackPanel _manualRow;
    private readonly Border _conflictBox;
    private readonly TextBlock _guardLine = Ui.Text("", "muted");
    private readonly StatusPill _builtInPill = new();
    private readonly TextBlock _builtInText = Ui.Text("", "muted");
    private readonly StackPanel _builtInRow;
    private readonly TextBlock _steamGuide;
    private readonly Button _exitSteam;
    private bool _exitArmed;

    private readonly TextBlock _profileName = Ui.Text("—", "h2");
    private readonly TextBlock _foreground = Ui.Text("", "muted");
    private readonly StatusPill _padPill = new(), _kbmPill = new();
    private readonly TextBlock _padMsg = Ui.Text("", "muted"), _kbmMsg = Ui.Text("", "muted");
    private readonly StackPanel _driverActions;

    private readonly TextBlock _model = Ui.Text("—"), _transport = Ui.Text("—"), _battery = Ui.Text("—"), _rate = Ui.Text("—");
    private readonly WrapPanel _caps = new() { Orientation = Orientation.Horizontal };
    private Capabilities _shownCaps = (Capabilities)(-1);

    public OverviewPage(HubRuntime runtime)
    {
        _runtime = runtime;

        _holo = new HologramView
        {
            StateSource = () => runtime.ControllerSnapshot,
            ConnectedSource = () => runtime.ControllerConnected,
        };

        // ---- Left: hologram -----------------------------------------------------------------
        var rumble = Ui.Button("Test rumble", runtime.TestRumble, "ghost");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(Ui.Stack(4, _deviceTitle, _deviceSub));
        Grid.SetColumn(rumble, 1);
        header.Children.Add(rumble);

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        left.Children.Add(header);
        var holoHost = new Border { Child = _holo, Margin = new Thickness(0, 10, 0, 0), ClipToBounds = false };
        Grid.SetRow(holoHost, 1);
        left.Children.Add(holoHost);

        // ---- Right: control card ------------------------------------------------------------
        _switch = new SegmentedSwitch(["Auto", "Manual", "Off"], (int)runtime.Settings.Switch, i => runtime.UpdateSettings(s => s.Switch = (ControlSwitch)i));
        ToolTip.SetTip(_switch,
            "Auto: games get the gamepad; the desktop (and BogiHub) get mouse & keyboard.\n" +
            "Manual: you choose gamepad or mouse & keyboard, everywhere.\n" +
            "Off: hand the controller to Steam or its built-in mode.\n" +
            "Hold \"…\" on the controller for 1.5 s (or Ctrl+Shift+F11) to flip gamepad / mouse & keyboard.");

        _manualSwitch = new SegmentedSwitch(["Gamepad", "Mouse & keyboard"], (int)runtime.Settings.ManualMode,
            i => runtime.UpdateSettings(s => s.ManualMode = (ManualMode)i));
        _manualRow = Ui.Stack(6, _manualSwitch, Ui.Text("Hold \"…\" on the controller for 1.5 s to flip between them: two buzzes = gamepad, one long buzz = mouse & keyboard.", "muted"));
        _manualRow.IsVisible = runtime.Settings.Switch == ControlSwitch.Manual;

        _steamGuide = Ui.Text(
            "Without closing Steam: open Steam → Settings → Controller. Edit the \"Desktop Layout\" and pick a blank template (or remove its " +
            "bindings), then do the same for the \"Guide Button Chord Layout\". Menu names differ a little between Steam versions. " +
            "BogiHub's own buttons keep working either way.", "muted");
        _steamGuide.IsVisible = false;

        _exitSteam = Ui.Button("Exit Steam", OnExitSteam, "primary");
        var guideBtn = Ui.Button("Fix without closing Steam", () => _steamGuide.IsVisible = !_steamGuide.IsVisible, "ghost");
        var releaseBtn = Ui.Button("Release stuck keys", runtime.ReleaseStuckKeys, "ghost");
        ToolTip.SetTip(releaseBtn, "Lets go of any Windows/Alt/Ctrl/Shift/Esc key or mouse button that Windows thinks is still held.");
        var autoExit = Ui.SettingRow("Close Steam automatically", "Every time BogiHub takes the controller.",
            Ui.Toggle(runtime.Settings.AutoExitSteam, v => runtime.UpdateSettings(s => s.AutoExitSteam = v)));
        _conflictBox = new Border
        {
            Background = Palette.Brush(Palette.Warn, 0.08),
            BorderBrush = Palette.Brush(Palette.Warn, 0.35),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            IsVisible = false,
            Child = Ui.Stack(8,
                new TextBlock { Text = "Steam is also using your controller", Foreground = Palette.WarnBrush, FontWeight = FontWeight.SemiBold },
                Ui.Text("In games that aren't Steam games, Steam applies its own desktop layout on top of BogiHub: X opens Steam's on-screen " +
                        "keyboard, A/B/Y press Enter/Esc/Space, and Steam-button shortcuts stay live (Steam+B quits the game, Steam+Y turns " +
                        "the controller off). BogiHub blocks Steam's key presses and releases anything left stuck, but Steam's own pop-ups " +
                        "only stop when Steam stops handling the controller.", "muted"),
                new WrapPanel { Children = { _exitSteam, guideBtn, releaseBtn } },
                _steamGuide,
                autoExit),
        };
        foreach (var b in new Control[] { _exitSteam, guideBtn, releaseBtn }) b.Margin = new Thickness(0, 0, 8, 6);

        _guardLine.IsVisible = false;
        _builtInRow = Ui.Stack(4, Ui.Row(8, Ui.Text("Controller's own mouse & keyboard"), _builtInPill), _builtInText);
        _builtInRow.IsVisible = false;
        var controlCard = Ui.Card(Ui.Stack(12, _ownerPill, _reason, _switch, _manualRow, _builtInRow, _guardLine, _conflictBox), "Who's in control");

        // ---- Right: profile & outputs ------------------------------------------------------
        var profileCard = Ui.Card(Ui.Stack(6, _profileName, _foreground), "Active profile");

        _driverActions = Ui.Row(8,
            Ui.Button(DriverSetup.ButtonLabel, () => DriverSetup.Run(this, runtime), "primary"),
            Ui.Button("Check again", runtime.RefreshDriverStatus, "ghost"));
        _driverActions.IsVisible = false;

        var outputsCard = Ui.Card(Ui.Stack(10,
            Ui.Stack(4, Ui.Row(8, Ui.Text("Xbox controller"), _padPill), _padMsg, _driverActions),
            Ui.Divider(),
            Ui.Stack(4, Ui.Row(8, Ui.Text("Keyboard & mouse"), _kbmPill), _kbmMsg)), "Output");

        // ---- Right: device -----------------------------------------------------------------
        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        AddFact(facts, 0, "Model", _model);
        AddFact(facts, 1, "Connection", _transport);
        AddFact(facts, 2, "Battery", _battery);
        AddFact(facts, 3, "Update rate", _rate);
        var deviceCard = Ui.Card(Ui.Stack(12, facts, _caps), "Device");

        var right = new ScrollViewer
        {
            Content = Ui.Stack(14, controlCard, profileCard, outputsCard, deviceCard),
            Padding = new Thickness(0, 0, 6, 0),
        };

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,380") };
        root.Children.Add(left);
        Grid.SetColumn(right, 2);
        root.Children.Add(right);
        Content = root;
    }

    private static void AddFact(Grid g, int row, string label, TextBlock value)
    {
        var l = Ui.Text(label, "muted");
        l.Margin = new Thickness(0, 3, 18, 3);
        Grid.SetRow(l, row);
        value.Margin = new Thickness(0, 3);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        g.Children.Add(l);
        g.Children.Add(value);
    }

    private void OnExitSteam()
    {
        // Two-step confirm: closing Steam affects other games and downloads, so never one click.
        if (!_exitArmed)
        {
            _exitArmed = true;
            _exitSteam.Content = "Confirm: exit Steam";
            _exitSteam.Classes.Remove("primary");
            _exitSteam.Classes.Add("danger");
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                _exitArmed = false;
                _exitSteam.Content = "Exit Steam";
                _exitSteam.Classes.Remove("danger");
                _exitSteam.Classes.Add("primary");
            }, TimeSpan.FromSeconds(4));
            return;
        }
        _exitArmed = false;
        _runtime.RequestSteamExit();
        _exitSteam.Content = "Asked Steam to exit";
    }

    public void Refresh(RuntimeStatus s)
    {
        _switch.Select((int)_runtime.Settings.Switch);
        _manualRow.IsVisible = _runtime.Settings.Switch == ControlSwitch.Manual;
        _manualSwitch.Select((int)_runtime.Settings.ManualMode);

        var d = s.Decision;
        var (text, color) = d.Owner switch
        {
            ControlOwner.Hub when d.IsMouseKeyboard => ("Mouse & keyboard (BogiHub layout)", Palette.Accent),
            ControlOwner.Hub => ("Gamepad", Palette.Accent),
            ControlOwner.Steam => ("Steam is in control", Palette.Muted),
            ControlOwner.Paused => ("Paused — emergency stop", Palette.Danger),
            _ => ("Mouse & keyboard", Palette.Ok),
        };
        _ownerPill.Set(text, color);
        _reason.Text = d.Reason + (s.SteamRunning ? "" : "  Steam is closed.");
        // Steam reads the controller whenever it runs, so say so whenever BogiHub is in control alongside it,
        // not only when a conflict happened to be detected.
        _conflictBox.IsVisible = d.HubActive && s.SteamRunning;
        _guardLine.IsVisible = s.SteamGuardActive;
        // Live check that the controller's firmware mouse/keyboard really is off while BogiHub is in charge.
        var src = s.Sources;
        _builtInRow.IsVisible = d.HubActive && s.Controller.Connected && src.Monitoring;
        if (src.BuiltInLeaking)
        {
            _builtInPill.Set("Leaking", Palette.Warn);
            _builtInText.Text = $"{src.ControllerMousePerSec} mouse and {src.ControllerKeyboardPerSec} key events from the controller's built-in mode in the last second. " +
                                "BogiHub keeps re-sending the off command; if this stays, Steam is probably turning it back on (try Off in Steam, or exit Steam).";
        }
        else
        {
            _builtInPill.Set("Off ✓", Palette.Ok);
            _builtInText.Text = src.LizardKeeperActive
                ? $"Verified: no default mouse/keyboard input from the controller. Off command sent {src.LizardOffSent:N0}× ({src.LizardOffFailed} failed)."
                : "No default mouse/keyboard input from the controller.";
        }

        _guardLine.Text = s.SteamInputsBlocked == 0
            ? "Steam guard is on: Steam's controller mouse & keyboard are blocked while you play."
            : $"Steam guard is on: blocked {s.SteamInputsBlocked:N0} Steam mouse/keyboard inputs so far.";

        _profileName.Text = d.Profile?.Name ?? (d.Owner == ControlOwner.Steam
            ? "None — Steam's layout applies"
            : "None — the controller is its own mouse & keyboard");
        _foreground.Text = $"Focused: {s.Foreground.Label}";

        SetOutput(_padPill, _padMsg, s.Gamepad, s.DriverInstalled, d.Profile?.Output != Core.Profiles.OutputMode.KeyboardMouse && d.HubActive);
        _driverActions.IsVisible = !s.DriverInstalled;
        if (d.GamepadOnly)
        {
            _kbmPill.Set("Off", Palette.Muted);
            _kbmMsg.Text = "Manual gamepad: the controller never sends keyboard or mouse input.";
        }
        else
        {
            SetOutput(_kbmPill, _kbmMsg, s.KeyboardMouse, true, d.HubActive);
        }

        var c = s.Controller;
        if (c.Connected && c.Device is { } dev)
        {
            _deviceTitle.Text = dev.Name;
            _deviceSub.Text = $"{dev.Transport}{(c.Battery.HasPercent ? $" · {c.Battery.Percent}% battery" : "")}";
            _model.Text = $"{dev.Name} (USB {dev.VendorId:X4}:{dev.ProductId:X4})";
            _transport.Text = dev.Transport;
            _battery.Text = c.Battery.HasPercent ? $"{c.Battery.Percent}% · {BatteryWord(c.Battery.Status)}" : "Not reported by the device";
            _rate.Text = $"{c.ReportsPerSecond} reports/s";
            ShowCaps(dev.Capabilities);
        }
        else
        {
            _deviceTitle.Text = "Steam Controller";
            _deviceSub.Text = c.LastError ?? "Not connected";
            _model.Text = _transport.Text = _battery.Text = _rate.Text = "—";
            ShowCaps(Capabilities.None);
        }
    }

    private static void SetOutput(StatusPill pill, TextBlock msg, OutputStatus status, bool driverOk, bool wanted)
    {
        if (!driverOk)
        {
            pill.Set("Driver missing", Palette.Warn);
            msg.Text = "Games can't see a controller until the ViGEmBus driver is installed (one-time, needs admin).";
            return;
        }
        // A connected virtual pad stays plugged in while the controller is a mouse (so games never see it disconnect),
        // but it is idle then: say so instead of "Active".
        if (status.Readiness == OutputReadiness.Ready && !wanted)
        {
            pill.Set("Standby", Palette.Muted);
            msg.Text = "Plugged in but idle while the controller is a mouse & keyboard.";
            return;
        }
        var (t, c) = status.Readiness switch
        {
            OutputReadiness.Ready => ("Active", Palette.Ok),
            OutputReadiness.Blocked => ("Blocked", Palette.Warn),
            OutputReadiness.Error => ("Error", Palette.Danger),
            OutputReadiness.DriverMissing => ("Driver missing", Palette.Warn),
            _ => (wanted ? "Ready" : "Idle", Palette.Muted),
        };
        pill.Set(t, c);
        msg.Text = status.Readiness == OutputReadiness.Idle ? (wanted ? "Waiting for input" : "Not used right now") : status.Message;
    }

    private static string BatteryWord(PowerStatus p) => p switch
    {
        PowerStatus.Charging => "charging",
        PowerStatus.Charged => "charged",
        PowerStatus.OnBattery => "on battery",
        _ => "unknown",
    };

    private void ShowCaps(Capabilities caps)
    {
        if (caps == _shownCaps) return;
        _shownCaps = caps;
        _caps.Children.Clear();
        foreach (var (cap, label, why) in new[]
        {
            (Capabilities.Trackpads, "Trackpads", "No trackpad data from this device"),
            (Capabilities.TrackpadPressure, "Pad pressure", "Pressure not reported"),
            (Capabilities.Gyro, "Gyro", "Motion sensors unavailable"),
            (Capabilities.CapacitiveTouch, "Touch sensing", "Capacitive touch not reported"),
            (Capabilities.Rumble, "Rumble", "Rumble unsupported"),
            (Capabilities.Battery, "Battery", "Battery level not reported by the device"),
        })
        {
            bool has = (caps & cap) != 0;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(99),
                Padding = new Thickness(9, 3),
                Margin = new Thickness(0, 0, 6, 6),
                Background = has ? Palette.Brush(Palette.Accent, 0.10) : Palette.Surface2Brush,
                Child = new TextBlock { Text = label, FontSize = 11.5, Foreground = has ? Palette.AccentBrush : Palette.FaintBrush },
            };
            if (!has) ToolTip.SetTip(chip, caps == Capabilities.None ? "Connect the controller to check" : why);
            _caps.Children.Add(chip);
        }
    }
}
