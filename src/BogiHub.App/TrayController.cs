using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using BogiHub.App.Theme;
using BogiHub.Core.Control;
using BogiHub.Runtime;

namespace BogiHub.App;

/// <summary>
/// Tray icon: open, emergency stop / resume, control switch, profile pin, exit.
/// Works with the main window closed and without a working controller.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly App _app;
    private readonly HubRuntime _runtime;
    private readonly TrayIcon _tray;

    public TrayController(App app, HubRuntime runtime)
    {
        _app = app;
        _runtime = runtime;
        _tray = new TrayIcon { ToolTipText = "BogiHub", Icon = Icons.AppIcon() };
        _tray.Clicked += (_, _) => _app.ShowWindow();
        Rebuild();
        TrayIcon.SetIcons(app, [_tray]);

        runtime.ProfilesChanged += () => Dispatcher.UIThread.Post(Rebuild);
        runtime.StatusChanged += () => Dispatcher.UIThread.Post(() =>
        {
            // The mode can change from a shortcut; keep the menu's radio marks in sync.
            if (_runtime.Settings.Switch != _shownSwitch || _runtime.Settings.ManualMode != _shownManual || _runtime.EmergencyPaused != _shownPaused) Rebuild();
            else UpdateTooltip();
        });
    }

    private ControlSwitch _shownSwitch;
    private ManualMode _shownManual;
    private bool _shownPaused;

    private void Rebuild()
    {
        _shownSwitch = _runtime.Settings.Switch;
        _shownManual = _runtime.Settings.ManualMode;
        _shownPaused = _runtime.EmergencyPaused;
        var menu = new NativeMenu();
        menu.Items.Add(Item("Open BogiHub", _app.ShowWindow));
        menu.Items.Add(new NativeMenuItemSeparator());

        bool paused = _runtime.EmergencyPaused;
        menu.Items.Add(Item(paused ? "Resume" : "Emergency stop (release all input)", () =>
        {
            _runtime.SetEmergencyPause(!_runtime.EmergencyPaused);
            Rebuild();
        }));

        menu.Items.Add(Item("Release stuck keys", _runtime.ReleaseStuckKeys));

        var control = new NativeMenu();
        var current = (_runtime.Settings.Switch, _runtime.Settings.ManualMode);
        foreach (var (sw, manual, label) in new[]
        {
            (ControlSwitch.Auto, ManualMode.Gamepad, "Auto (games: gamepad, desktop: mouse)"),
            (ControlSwitch.Manual, ManualMode.Gamepad, "Manual: gamepad"),
            (ControlSwitch.Manual, ManualMode.MouseKeyboard, "Manual: mouse & keyboard"),
            (ControlSwitch.Off, ManualMode.Gamepad, "Off (Steam / built-in)"),
        })
        {
            bool isChecked = current.Switch == sw && (sw != ControlSwitch.Manual || current.ManualMode == manual);
            var mi = new NativeMenuItem(label) { ToggleType = MenuItemToggleType.Radio, IsChecked = isChecked };
            mi.Click += (_, _) =>
            {
                _runtime.UpdateSettings(s =>
                {
                    s.Switch = sw;
                    if (sw == ControlSwitch.Manual) s.ManualMode = manual;
                });
                Rebuild();
            };
            control.Items.Add(mi);
        }
        menu.Items.Add(new NativeMenuItem("Control") { Menu = control });

        var profiles = new NativeMenu();
        var auto = new NativeMenuItem("Automatic (per game)") { ToggleType = MenuItemToggleType.Radio, IsChecked = _runtime.Settings.PinnedProfileId is null };
        auto.Click += (_, _) =>
        {
            _runtime.UpdateSettings(s => s.PinnedProfileId = null);
            Rebuild();
        };
        profiles.Items.Add(auto);
        profiles.Items.Add(new NativeMenuItemSeparator());
        foreach (var p in _runtime.Profiles)
        {
            string id = p.Id;
            var mi = new NativeMenuItem(p.Name) { ToggleType = MenuItemToggleType.Radio, IsChecked = _runtime.Settings.PinnedProfileId == id };
            mi.Click += (_, _) =>
            {
                _runtime.UpdateSettings(s => s.PinnedProfileId = id);
                Rebuild();
            };
            profiles.Items.Add(mi);
        }
        menu.Items.Add(new NativeMenuItem("Profile") { Menu = profiles });

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Exit BogiHub", _app.ExitApp));
        _tray.Menu = menu;
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        var s = _runtime.GetStatus();
        string who = s.Decision.Owner switch
        {
            ControlOwner.Hub when s.Decision.IsMouseKeyboard => $"mouse & keyboard · {s.Decision.Profile?.Name}",
            ControlOwner.Hub => $"gamepad · {s.Decision.Profile?.Name}",
            ControlOwner.Steam => "Steam in control",
            ControlOwner.Paused => "Paused (emergency stop)",
            _ => "mouse & keyboard (controller's own)",
        };
        _tray.ToolTipText = $"BogiHub — {who}";
    }

    private static NativeMenuItem Item(string text, Action action)
    {
        var mi = new NativeMenuItem(text);
        mi.Click += (_, _) => action();
        return mi;
    }

    public void Dispose()
    {
        _tray.IsVisible = false;
        _tray.Dispose();
    }
}
