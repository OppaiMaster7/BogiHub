using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using BogiHub.Runtime;

namespace BogiHub.App;

/// <summary>
/// The "ViGEmBus driver" buttons. The portable ZIP ships the driver's own signed installer in drivers\ beside
/// BogiHub.exe, so run that one (Windows asks for admin); the installed app has no copy and opens the download page.
/// </summary>
internal static class DriverSetup
{
    private const string DownloadPage = "https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0";
    private static readonly string Bundled = Path.Combine(AppContext.BaseDirectory, "drivers", "ViGEmBus_1.22.0_x64_x86_arm64.exe");

    public static string ButtonLabel => File.Exists(Bundled) ? "Install ViGEmBus driver…" : "Get ViGEmBus driver…";

    public static async void Run(Visual from, HubRuntime runtime)
    {
        if (File.Exists(Bundled))
        {
            try
            {
                using var setup = Process.Start(new ProcessStartInfo(Bundled) { UseShellExecute = true });
                if (setup is null) return;
                runtime.Log.Info("Started the bundled ViGEmBus installer");
                await setup.WaitForExitAsync();
                runtime.RefreshDriverStatus();
            }
            catch (Win32Exception ex)
            {
                runtime.Log.Warn($"ViGEmBus installer did not start: {ex.Message}"); // usually the admin prompt was declined
            }
            return;
        }
        var top = TopLevel.GetTopLevel(from);
        if (top is not null) await top.Launcher.LaunchUriAsync(new Uri(DownloadPage));
    }
}
