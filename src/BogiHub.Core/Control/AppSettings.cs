using System.Text.Json;
using BogiHub.Core.Notifications;
using BogiHub.Core.Profiles;

namespace BogiHub.Core.Control;

/// <summary>App-wide preferences, saved as JSON next to the profiles folder.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public ControlSwitch Switch { get; set; } = ControlSwitch.Auto;
    /// <summary>What Manual mode makes the controller. Holding "…" flips it.</summary>
    public ManualMode ManualMode { get; set; } = ManualMode.Gamepad;
    public string DefaultProfileId { get; set; } = DefaultProfiles.ControllerId;
    /// <summary>Manual override: always use this profile while BogiHub is in control.</summary>
    public string? PinnedProfileId { get; set; }
    public bool FullscreenIsGame { get; set; } = true;
    /// <summary>
    /// Steam guard: while BogiHub is in control and Steam runs, drop synthetic mouse/keyboard input from other programs
    /// (Steam's desktop layout) during controller use. Real mouse and keyboard input is never touched.
    /// </summary>
    public bool BlockSteamInput { get; set; } = true;
    /// <summary>
    /// Ask Steam to exit (cleanly) whenever BogiHub takes the controller. Steam applies its own desktop layout and
    /// Steam-button shortcuts to the controller during non-Steam games; only closing Steam stops its pop-ups.
    /// </summary>
    public bool AutoExitSteam { get; set; }
    /// <summary>Holding the "…" (Quick Access) button for 1.5 s switches between gamepad and mouse &amp; keyboard.</summary>
    public bool HoldQamForGamepad { get; set; } = true;
    public bool RumblePassthrough { get; set; } = true;
    /// <summary>0..1 strength of trackpad ticks and clicks (each profile turns them on or off per pad). 0 = off.</summary>
    public float PadHapticStrength { get; set; } = 0.6f;
    /// <summary>0..1 multiplier applied to game rumble.</summary>
    public float RumbleStrength { get; set; } = 1.0f;
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    /// <summary>Show the controller's charge on the tray icon (a level bar) and in its tooltip.</summary>
    public bool TrayBattery { get; set; } = true;
    /// <summary>Visual: faint colour in the moving background behind the glass (false = greyscale).</summary>
    public bool AmbientColor { get; set; } = true;
    /// <summary>On-screen notices (over games too): who controls the controller, Steam, battery, problems.</summary>
    public bool ShowNotifications { get; set; } = true;
    /// <summary>Also notify every time the controller switches between gamepad and mouse &amp; keyboard.</summary>
    public bool NotifyModeChanges { get; set; } = true;
    public NoticePosition NotificationPosition { get; set; } = NoticePosition.TopCenter;
    /// <summary>Opt-in: once a day, ask GitHub whether a newer BogiHub is out. Off by default; nothing else is sent.</summary>
    public bool CheckForUpdates { get; set; }

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            // Earlier versions called the always-gamepad switch "Hub" and then "Gamepad". Both are now Manual with
            // ManualMode.Gamepad (the default), so the user's choice carries over instead of resetting every setting.
            string json = File.ReadAllText(path)
                .Replace("\"switch\": \"Hub\"", "\"switch\": \"Manual\"")
                .Replace("\"switch\": \"Gamepad\"", "\"switch\": \"Manual\"");
            var s = JsonSerializer.Deserialize<AppSettings>(json, ProfileJson.Options) ?? new AppSettings();
            s.RumbleStrength = Math.Clamp(float.IsNaN(s.RumbleStrength) ? 1f : s.RumbleStrength, 0f, 1f);
            s.PadHapticStrength = Math.Clamp(float.IsNaN(s.PadHapticStrength) ? 0.6f : s.PadHapticStrength, 0f, 1f);
            if (!Enum.IsDefined(s.Switch)) s.Switch = ControlSwitch.Auto;
            if (!Enum.IsDefined(s.ManualMode)) s.ManualMode = ManualMode.Gamepad;
            if (!Enum.IsDefined(s.NotificationPosition)) s.NotificationPosition = NoticePosition.TopCenter;
            if (string.IsNullOrWhiteSpace(s.DefaultProfileId)) s.DefaultProfileId = DefaultProfiles.ControllerId;
            return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings(); // corrupted settings never stop the app; defaults are safe
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, ProfileJson.Options));
        File.Move(temp, path, overwrite: true);
    }
}
