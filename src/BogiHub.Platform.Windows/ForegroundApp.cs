namespace BogiHub.Platform.Windows;

/// <summary>The window the user is currently interacting with.</summary>
public sealed record ForegroundApp(
    int ProcessId,
    string? ExeName,
    string? ExePath,
    string WindowTitle,
    string WindowClass,
    bool IsFullscreen,
    bool IsDesktopShell,
    bool IsSelf)
{
    public static readonly ForegroundApp None = new(0, null, null, "", "", false, true, false);

    /// <summary>Human label: "Fortnite (FortniteClient-Win64-Shipping.exe)" or the window class when the process is protected.</summary>
    public string Label =>
        IsDesktopShell ? "Windows desktop" :
        ExeName is null ? $"{(WindowTitle.Length > 0 ? WindowTitle : WindowClass)} (protected process)" :
        WindowTitle.Length > 0 ? $"{WindowTitle} ({ExeName})" : ExeName;
}
