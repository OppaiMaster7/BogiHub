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
}
