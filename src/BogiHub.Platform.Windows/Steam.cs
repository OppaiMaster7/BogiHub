using System.Diagnostics;
using Microsoft.Win32;

namespace BogiHub.Platform.Windows;

/// <summary>Read-only Steam facts plus one polite action: asking Steam to exit through its own command line.</summary>
public static class Steam
{
    public static bool IsRunning()
    {
        var procs = Process.GetProcessesByName("steam");
        bool running = procs.Length > 0;
        foreach (var p in procs) p.Dispose();
        return running;
    }

    public static string? ExePath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamExe") is string s && File.Exists(s)) return s;
        }
        catch { /* registry unavailable */ }
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    /// <summary>
    /// Asks Steam to shut down cleanly (same as Steam menu → Exit). Never kills the process.
    /// Returns false if Steam's executable could not be found.
    /// </summary>
    public static bool RequestCleanExit()
    {
        string? exe = ExePath();
        if (exe is null) return false;
        Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false })?.Dispose();
        return true;
    }
}

/// <summary>Per-user "start with Windows" via the HKCU Run key. No admin rights needed. Off by default.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BogiHub";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{exePath}\" --minimized");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// A portable update is extracted into a new folder, which leaves the Run key starting the old copy (or nothing,
    /// once that folder is deleted). On launch, take the key over when the copy it names is gone or older than this one.
    /// Returns true when the key was moved.
    /// </summary>
    public static bool FollowMove(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is not string command) return false;
        string? stored = command.StartsWith('"') && command.IndexOf('"', 1) is var end and > 0 ? command[1..end] : null;
        if (stored is not null && string.Equals(Path.GetFullPath(stored), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
            return false;
        if (stored is not null && File.Exists(stored) && FileVersion(stored) >= FileVersion(exePath)) return false;
        key.SetValue(ValueName, $"\"{exePath}\" --minimized");
        return true;
    }

    private static Version FileVersion(string path) =>
        Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out var v) ? v : new Version(0, 0);
}
