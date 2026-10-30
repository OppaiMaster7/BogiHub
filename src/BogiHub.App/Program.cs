using Avalonia;

namespace BogiHub.App;

internal static class Program
{
    private const string MutexName = @"Local\BogiHub.SingleInstance";
    private const string ShowEventName = @"Local\BogiHub.Show";
    private const string ExitEventName = @"Local\BogiHub.Exit";

    public static bool StartMinimized { get; private set; }
    public static EventWaitHandle? ShowSignal { get; private set; }
    public static EventWaitHandle? ExitSignal { get; private set; }

    public static bool IsAssetTool =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BOGIHUB_ICON_SHEET")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BOGIHUB_WRITE_ICO")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BOGIHUB_INSTALLER_ART")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BOGIHUB_NOTICE_SHEET"));

    [STAThread]
    public static int Main(string[] args)
    {
        // Asset tools (see App): no window, no controller, so they may run beside a normal BogiHub.
        if (IsAssetTool) return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // One BogiHub at a time: two copies would fight over the controller and double every output.
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool first);

        // "BogiHub.exe --exit" (used by the installer): ask the running copy to quit the normal way, so every held
        // key and button is released and the virtual pad unplugged, then wait until it has gone. Exit code 0 = gone.
        if (args.Contains("--exit"))
        {
            if (first) return 0;
            try
            {
                using var exit = EventWaitHandle.OpenExisting(ExitEventName);
                exit.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            try { return mutex.WaitOne(TimeSpan.FromSeconds(15)) ? 0 : 1; }
            catch (AbandonedMutexException) { return 0; } // it exited without releasing: still gone
        }

        if (!first)
        {
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }

        ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ExitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        StartMinimized = args.Contains("--minimized");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
