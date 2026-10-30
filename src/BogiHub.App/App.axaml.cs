using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BogiHub.Core.Notifications;
using BogiHub.Runtime;

namespace BogiHub.App;

public partial class App : Application
{
    private HubRuntime? _runtime;
    private MainWindow? _window;
    private TrayController? _tray;
    private NotificationOverlay? _overlay;
    private bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Asset tools (no window, no controller): BOGIHUB_ICON_SHEET=png renders every icon, BOGIHUB_WRITE_ICO=ico writes the
            // app icon, BOGIHUB_INSTALLER_ART=folder writes the installer artwork, BOGIHUB_NOTICE_SHEET=png renders sample notices.
            if (Environment.GetEnvironmentVariable("BOGIHUB_ICON_SHEET") is { Length: > 0 } sheet) Theme.Icons.WriteSheet(sheet);
            if (Environment.GetEnvironmentVariable("BOGIHUB_WRITE_ICO") is { Length: > 0 } ico) Theme.Icons.WriteIco(ico);
            if (Environment.GetEnvironmentVariable("BOGIHUB_INSTALLER_ART") is { Length: > 0 } art) Theme.Icons.WriteInstallerArt(art);
            if (Environment.GetEnvironmentVariable("BOGIHUB_NOTICE_SHEET") is { Length: > 0 } notices) NoticeCard.WriteSheet(notices, NoticeSamples);
            if (Program.IsAssetTool)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
                base.OnFrameworkInitializationCompleted();
                return;
            }

            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BogiHub");
            _runtime = new HubRuntime(data);
            _runtime.Start();
            InstallCrashGuards(data);

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // ShutdownRequested only comes from the OS (log-off); Exit covers every way out, including our own Shutdown().
            desktop.ShutdownRequested += (_, _) => Shutdown();
            desktop.Exit += (_, _) => Shutdown();

            _window = new MainWindow(_runtime);
            _window.Closing += (_, e) =>
            {
                if (_exiting || !_runtime.Settings.CloseToTray) return;
                e.Cancel = true; // keep running in the tray; mapping continues
                HideWindow();
            };
            _window.Closed += (_, _) =>
            {
                if (!_exiting) ExitApp();
            };

            _tray = new TrayController(this, _runtime);

            _overlay = new NotificationOverlay();
            _overlay.ActionInvoked += n =>
            {
                ShowWindow();
                _window?.Navigate(n.Key == "driver" ? "settings" : "overview");
            };
            var runtime = _runtime;
            runtime.Notify += n => Dispatcher.UIThread.Post(() =>
            {
                if (!_exiting) _overlay?.Show(n, runtime.Settings.NotificationPosition);
            });
            // Screenshot aid: BOGIHUB_NOTICE_DEMO=1 shows sample notices, then exits after 10 s.
            if (Environment.GetEnvironmentVariable("BOGIHUB_NOTICE_DEMO") == "1") RunNoticeDemo();

            if (Program.StartMinimized || _runtime.Settings.StartMinimized) Theme.Glass.SetPaused(true);
            else ShowWindow();

            ListenForSecondInstance();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// A UI bug must never take the controller down with it. UI-thread exceptions are logged and survived;
    /// if the process is dying anyway, every held key/button is released first (injected keys do not
    /// release themselves when a process exits).
    /// </summary>
    private void InstallCrashGuards(string dataDir)
    {
        void Record(Exception ex, string where)
        {
            try
            {
                File.AppendAllText(Path.Combine(dataDir, "crash.log"), $"{DateTime.Now:O} [{where}] {ex}\n\n");
                _runtime?.Log.Error($"{where}: {ex.GetType().Name}: {ex.Message} (details in crash.log)");
            }
            catch { /* never throw from the crash handler */ }
        }

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Record(e.Exception, "UI");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Record(ex, "fatal");
            try { _runtime?.Dispose(); } catch { }
        };
    }

    private static readonly Notice[] NoticeSamples =
    [
        new("mode", NoticeKind.Mode, NoticeTone.Info, NoticeIcon.Gamepad, "Gamepad", "BogiHub in control · Controller", 64),
        new("steam", NoticeKind.Steam, NoticeTone.Warn, NoticeIcon.Steam, "Close Steam for the best result",
            "Steam still adds its own keyboard and shortcuts on top of BogiHub.", Action: NoticeAction.OpenApp, DurationMs: 8000),
        new("battery", NoticeKind.Battery, NoticeTone.Warn, NoticeIcon.Battery, "Battery low · 18 %", "Time to charge the controller soon.", 18, DurationMs: 8000),
    ];

    private void RunNoticeDemo()
    {
        var samples = NoticeSamples;
        for (int i = 0; i < samples.Length; i++)
        {
            var n = samples[i];
            DispatcherTimer.RunOnce(() => _overlay?.Show(n, _runtime!.Settings.NotificationPosition), TimeSpan.FromMilliseconds(800 + i * 350));
        }
        // The mode notice morphs in place, as it does when alt-tabbing out of a game.
        DispatcherTimer.RunOnce(() => _overlay?.Show(samples[0] with { Icon = NoticeIcon.Mouse, Title = "Mouse & keyboard", Detail = "The controller's own mouse mode" },
            _runtime!.Settings.NotificationPosition), TimeSpan.FromMilliseconds(2600));
        DispatcherTimer.RunOnce(ExitApp, TimeSpan.FromSeconds(10));
    }

    public void ShowWindow()
    {
        if (_window is null || _runtime is null) return;
        Theme.Glass.SetPaused(false);
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void HideWindow()
    {
        _window?.Hide();
        Theme.Glass.SetPaused(true); // nothing visible: stop every animation loop
    }

    public void ExitApp()
    {
        Shutdown(); // release every generated input before the process goes
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        else Shutdown();
    }

    private void Shutdown()
    {
        _exiting = true;
        _tray?.Dispose();
        _tray = null;
        _overlay?.Close();
        _overlay = null;
        _runtime?.Dispose(); // releases every generated input and unplugs the virtual pad
        _runtime = null;
    }

    private void ListenForSecondInstance()
    {
        if (Program.ShowSignal is not { } show || Program.ExitSignal is not { } exit) return;
        var t = new Thread(() =>
        {
            while (!_exiting)
            {
                switch (WaitHandle.WaitAny([show, exit], 500))
                {
                    case 0: Dispatcher.UIThread.Post(ShowWindow); break;
                    case 1: Dispatcher.UIThread.Post(ExitApp); return; // installer / "BogiHub.exe --exit"
                }
            }
        }) { IsBackground = true, Name = "BogiHub single-instance" };
        t.Start();
    }
}
