using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BogiHub.App.Theme;
using BogiHub.Core.Control;
using BogiHub.Core.Notifications;
using BogiHub.Platform.Windows;
using BogiHub.Runtime;

namespace BogiHub.App.Pages;

public sealed class SettingsPage : UserControl, IPage
{
    private readonly HubRuntime _runtime;
    private readonly TextBlock _sdl = Ui.Text("", "mono");
    private readonly StatusPill _driver = new();
    private readonly TextBlock _timings = Ui.Text("", "mono");
    private readonly TextBlock _hotkey = Ui.Text("", "muted");
    private readonly TextBlock _log = new() { FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 11.5, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Palette.MutedBrush };
    private readonly TextBlock _note = Ui.Text("", "muted");
    private readonly TextBlock _update = Ui.Text("", "muted");
    private readonly Button _openUpdate;
    private int _logCount = -1;

    public SettingsPage(HubRuntime runtime, MainWindow window)
    {
        _runtime = runtime;
        var s = runtime.Settings;
        void Set(Action<AppSettings> a) => runtime.UpdateSettings(a);

        var control = Ui.Card(Ui.Stack(12,
            Ui.SettingRow("Treat fullscreen apps as games", "Auto mode: fullscreen apps without a profile of their own get the gamepad (with the default profile).",
                Ui.Toggle(s.FullscreenIsGame, v => Set(x => x.FullscreenIsGame = v))),
            Ui.SettingRow("Hold \"…\" to switch gamepad / mouse & keyboard",
                $"Hold the controller's \"…\" button for 1.5 s: two buzzes = gamepad, one long buzz = mouse & keyboard. Switches to Manual so the choice sticks. {runtime.GamepadHotkeyLabel} does the same from the keyboard.",
                Ui.Toggle(s.HoldQamForGamepad, v => Set(x => x.HoldQamForGamepad = v))),
            Ui.SettingRow("Close Steam automatically", "Ask Steam to exit whenever BogiHub takes the controller. Steam otherwise applies its own desktop layout (X opens its keyboard, Steam-button shortcuts) during non-Steam games.",
                Ui.Toggle(s.AutoExitSteam, v => Set(x => x.AutoExitSteam = v))),
            Ui.SettingRow("Steam guard", "While BogiHub is in control and Steam is open, block the mouse & keyboard input Steam generates from the controller. Your real mouse and keyboard are never affected.",
                Ui.Toggle(s.BlockSteamInput, v => Set(x => x.BlockSteamInput = v)))), "Control");

        var rumble = Ui.Card(Ui.Stack(12,
            Ui.SettingRow("Pass game rumble to the controller", "Games that rumble an Xbox controller will vibrate the Steam Controller.",
                Ui.Toggle(s.RumblePassthrough, v => Set(x => x.RumblePassthrough = v))),
            Ui.SettingRow("Strength", null, Ui.Slider(0, 1, s.RumbleStrength, v => $"{v * 100:0}%", v => Set(x => x.RumbleStrength = (float)v), 160)),
            Ui.Row(8, Ui.Button("Test rumble", runtime.TestRumble)),
            Ui.SettingRow("Trackpad haptics", "Small ticks under your thumb as the cursor rolls or the scroll wheel turns, and a click on each new direction. Each profile can turn them off per pad in Controls.",
                Ui.Slider(0, 1, s.PadHapticStrength, v => v <= 0.001 ? "Off" : $"{v * 100:0}%", v => Set(x => x.PadHapticStrength = (float)v), 160)),
            Ui.Row(8, Ui.Button("Feel trackpad haptics", runtime.TestPadHaptics))), "Rumble & haptics");

        var notices = Ui.Card(Ui.Stack(12,
            Ui.SettingRow("On-screen notifications", "Small notices that slide in over games too: who controls the controller, when Steam gets in the way, battery, connection and problems. Clicks pass straight through them.",
                Ui.Toggle(s.ShowNotifications, v => Set(x => x.ShowNotifications = v))),
            Ui.SettingRow("Notify on every mode switch", "Gamepad ↔ mouse & keyboard, each time it changes (alt-tabbing in and out of games, holding \"…\"). Emergency stop always shows.",
                Ui.Toggle(s.NotifyModeChanges, v => Set(x => x.NotifyModeChanges = v))),
            Ui.SettingRow("Position", null,
                Ui.Combo([NoticePosition.TopCenter, NoticePosition.TopRight, NoticePosition.BottomRight], s.NotificationPosition,
                    p => p switch { NoticePosition.TopRight => "Top right", NoticePosition.BottomRight => "Bottom right", _ => "Top centre" },
                    p => { Set(x => x.NotificationPosition = p); runtime.ShowTestNotice(); })),
            Ui.Row(8, Ui.Button("Show an example", runtime.ShowTestNotice))), "Notifications");

        var looks = Ui.Card(Ui.Stack(12,
            Ui.SettingRow("Colour behind the glass", "Faint moving colour in the background. Off keeps everything greyscale.",
                Ui.Toggle(s.AmbientColor, v => { Set(x => x.AmbientColor = v); window.SetAmbient(v); }))), "Appearance");

        var startup = Ui.Card(Ui.Stack(12,
            Ui.SettingRow("Start with Windows", "Off by default. Starts minimised to the tray.",
                Ui.Toggle(SafeAutostart(), v => { try { Autostart.Set(v, Environment.ProcessPath!); } catch (Exception ex) { _note.Text = ex.Message; } })),
            Ui.SettingRow("Start minimised", null, Ui.Toggle(s.StartMinimized, v => Set(x => x.StartMinimized = v))),
            Ui.SettingRow("Battery on the tray icon", "A level bar on BogiHub's tray icon shows the controller's charge: amber at 20 %, red at 10 %. Hover it for the exact number.",
                Ui.Toggle(s.TrayBattery, v => Set(x => x.TrayBattery = v))),
            Ui.SettingRow("Closing the window keeps BogiHub in the tray", "Mapping keeps working with the window closed. Exit from the tray menu.",
                Ui.Toggle(s.CloseToTray, v => Set(x => x.CloseToTray = v))),
            _hotkey), "Startup & tray");

        _openUpdate = Ui.Button("Open download page", () => { if (runtime.Updates.Available is { } r) Open(r.Url); }, "primary");
        var updates = Ui.Card(Ui.Stack(12,
            Ui.SettingRow("Check for new versions", "Off by default. Once a day, BogiHub asks GitHub whether a newer version is out and shows a notice if one is. Only that question is sent: no settings, logs or controller data.",
                Ui.Toggle(s.CheckForUpdates, v => { Set(x => x.CheckForUpdates = v); if (v) _ = runtime.CheckForUpdatesAsync(); })),
            _update,
            Ui.Row(8, _openUpdate, Ui.Button("Check now", () => _ = runtime.CheckForUpdatesAsync(), "ghost"))), "Updates");

        var deps = Ui.Card(Ui.Stack(10,
            Ui.SettingRow("Controller input", "SDL3 HIDAPI driver for the Steam Controller (2026), bundled.", _sdl),
            Ui.SettingRow("Virtual Xbox controller", "ViGEmBus 1.22 by Nefarius. The project is retired upstream but the signed driver still works on Windows 10/11. It is the only dependency that needs admin rights, once, to install.", _driver),
            Ui.Row(8,
                Ui.Button(DriverSetup.ButtonLabel, () => DriverSetup.Run(this, runtime)),
                Ui.Button("Check again", runtime.RefreshDriverStatus, "ghost"))), "Dependencies");

        var diag = Ui.Card(Ui.Stack(10,
            Ui.Text("Times below are measured inside BogiHub only (read → map → send). They are not your full game latency.", "muted"),
            _timings,
            Ui.Row(8,
                Ui.Button("Export log…", ExportLog),
                Ui.Button("Open data folder", () => Process.Start(new ProcessStartInfo(runtime.DataDirectory) { UseShellExecute = true })?.Dispose(), "ghost")),
            new Border
            {
                Background = Palette.Brush(Avalonia.Media.Colors.Black, 0.28),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                MaxHeight = 280,
                Child = new ScrollViewer { Content = _log },
            }), "Diagnostics");

        Content = new ScrollViewer
        {
            Padding = new Thickness(0, 0, 6, 0),
            Content = new StackPanel
            {
                Spacing = 14,
                MaxWidth = 820,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Children = { Ui.Text("Settings", "h1"), _note, control, notices, rumble, looks, startup, updates, deps, diag },
            },
        };
    }

    private static bool SafeAutostart()
    {
        try { return Autostart.IsEnabled(); } catch { return false; }
    }

    private async void Open(string url)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is not null) await top.Launcher.LaunchUriAsync(new Uri(url));
    }

    private async void ExportLog()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export BogiHub log",
            SuggestedFileName = $"bogihub-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var st = _runtime.GetStatus();
        string header = $"BogiHub {typeof(SettingsPage).Assembly.GetName().Version} · {Environment.OSVersion} · SDL {st.SdlVersion}\n" +
                        $"Controller: {st.Controller.Device?.Name ?? "not connected"} {st.Controller.Device?.Transport}\n" +
                        $"Steam running: {st.SteamRunning} · ViGEmBus: {(st.DriverInstalled ? "installed" : "missing")} · Owner: {st.Decision.Owner}";
        File.WriteAllText(path, _runtime.Log.Export(header));
        _note.Text = $"Log exported to {path}";
    }

    public void Refresh(RuntimeStatus s)
    {
        _sdl.Text = $"SDL {s.SdlVersion}";
        _driver.Set(s.DriverInstalled ? "Installed" : "Not installed", s.DriverInstalled ? Palette.Ok : Palette.Warn);
        var u = _runtime.Updates;
        _update.Text = u.Checking ? "Asking GitHub…"
            : u.Available is { } r ? $"BogiHub {r.Tag.TrimStart('v', 'V')} is out. You have {UpdateChecker.Current}."
            : u.LastError is { } err ? $"Couldn't reach GitHub: {err}"
            : u.LastSuccessUtc is { } at ? $"You have the newest version, {UpdateChecker.Current} (checked at {at.ToLocalTime():HH:mm})."
            : $"You have BogiHub {UpdateChecker.Current}.";
        _openUpdate.IsVisible = u.Available is not null;
        _hotkey.Text = s.HotkeyRegistered
            ? $"Emergency stop hotkey: {s.HotkeyLabel} (works even if the controller doesn't)."
            : $"Emergency stop hotkey {s.HotkeyLabel} is taken by another app. Use the tray menu or the button in the sidebar.";
        _timings.Text =
            $"Samples processed: {s.Timings.SamplesPerSecond}/s   Controller reports: {s.Controller.ReportsPerSecond}/s\n" +
            $"Processing time: avg {s.Timings.AvgMapMicros:0} µs   worst {s.Timings.MaxMapMicros:0} µs (last second)\n" +
            $"Steam running: {(s.SteamRunning ? "yes" : "no")}   Foreign injected input seen: {WindowsEvents.ForeignInjectedCount}";

        var entries = _runtime.Log.Snapshot();
        if (entries.Count != _logCount)
        {
            _logCount = entries.Count;
            _log.Text = string.Join("\n", entries.TakeLast(150).Reverse().Select(e => e.ToString()));
        }
    }
}
