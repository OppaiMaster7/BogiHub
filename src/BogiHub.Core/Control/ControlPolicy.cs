using BogiHub.Core.Profiles;

namespace BogiHub.Core.Control;

/// <summary>The big switch on the Overview page. The user always has the final say over auto-detection.</summary>
public enum ControlSwitch
{
    /// <summary>BogiHub decides: games → gamepad; desktop (and BogiHub itself) → mouse &amp; keyboard.</summary>
    Auto,
    /// <summary>The user decides with <see cref="ManualMode"/> (holding "…" on the controller flips it).</summary>
    Manual,
    /// <summary>BogiHub stands aside: Steam (if running) or the controller's built-in desktop mode.</summary>
    Off,
}

/// <summary>What the controller is in Manual mode.</summary>
public enum ManualMode
{
    /// <summary>Only an Xbox controller, everywhere. Its built-in mouse/keyboard stays off.</summary>
    Gamepad,
    /// <summary>The controller's own built-in mouse &amp; keyboard mode, everywhere.</summary>
    MouseKeyboard,
}

public enum ControlOwner
{
    /// <summary>BogiHub reads the controller and drives its virtual outputs.</summary>
    Hub,
    /// <summary>Steam is running and BogiHub stands aside; Steam's own layout applies.</summary>
    Steam,
    /// <summary>Nobody remaps: the controller acts as its firmware's built-in mouse/keyboard.</summary>
    BuiltIn,
    /// <summary>Emergency stop: all generated input released until the user resumes.</summary>
    Paused,
}

/// <summary>Facts the policy needs. Built by the runtime from live system state.</summary>
public sealed record ControlInputs(
    ControlSwitch Switch,
    ManualMode Manual,
    bool EmergencyPaused,
    bool SteamRunning,
    string? ForegroundExe,
    bool ForegroundIsFullscreen,
    bool ForegroundIsDesktop,
    bool ForegroundIsSelf,
    string? PinnedProfileId,
    string DefaultProfileId,
    bool FullscreenIsGame,
    IReadOnlyList<Profile> Profiles);

/// <param name="GamepadOnly">Keyboard and mouse bindings are ignored; only the virtual Xbox controller is driven.</param>
public sealed record ControlDecision(ControlOwner Owner, Profile? Profile, string Reason, bool GamepadOnly = false)
{
    public bool HubActive => Owner == ControlOwner.Hub;

    /// <summary>The controller is acting as a mouse &amp; keyboard (its built-in mode, Steam's desktop layout, or a BogiHub keyboard &amp; mouse profile).</summary>
    public bool IsMouseKeyboard => Owner is ControlOwner.BuiltIn or ControlOwner.Steam ||
                                   (Owner == ControlOwner.Hub && Profile?.Output == OutputMode.KeyboardMouse);
}

/// <summary>Pure decision function: same inputs, same answer. No OS calls, fully unit-tested.</summary>
public static class ControlPolicy
{
    public static ControlDecision Decide(ControlInputs i)
    {
        if (i.EmergencyPaused)
            return new(ControlOwner.Paused, null, "Emergency stop is on. All generated input is released.");

        var standAside = i.SteamRunning ? ControlOwner.Steam : ControlOwner.BuiltIn;
        string mouseKeyboard = i.SteamRunning
            ? "Steam is running, so Steam's desktop layout drives the controller."
            : "The controller is its own mouse & keyboard.";

        if (i.Switch == ControlSwitch.Off)
            return new(standAside, null, $"Switch is off. {mouseKeyboard}");

        var pinned = Find(i.Profiles, i.PinnedProfileId);
        var matched = MatchExecutable(i.Profiles, i.ForegroundExe);
        var fallback = Find(i.Profiles, i.DefaultProfileId) ?? DefaultProfiles.Controller();

        if (i.Switch == ControlSwitch.Manual)
        {
            if (i.Manual == ManualMode.MouseKeyboard)
                return new(standAside, null, $"Manual: mouse & keyboard. {mouseKeyboard} Hold \"…\" to switch to gamepad.");

            // Only profiles that drive a gamepad qualify; keyboard & mouse profiles are skipped, not half-applied.
            static bool Pad(Profile? p) => p is not null && p.Output != OutputMode.KeyboardMouse;
            var p = Pad(pinned) ? pinned! : Pad(matched) ? matched! : Pad(fallback) ? fallback : DefaultProfiles.Controller();
            string why = p == pinned ? $"pinned profile \"{p.Name}\"" :
                p == matched ? $"\"{p.Name}\" matches {i.ForegroundExe}" : $"\"{p.Name}\" layout";
            return new(ControlOwner.Hub, p,
                $"Manual: gamepad. The controller is only an Xbox controller everywhere ({why}). Hold \"…\" to switch to mouse & keyboard.",
                GamepadOnly: true);
        }

        // Auto: games get the gamepad, everything else gets mouse & keyboard.
        if (pinned is not null)
            return new(ControlOwner.Hub, pinned, $"Pinned profile \"{pinned.Name}\".");

        // Looking at BogiHub itself is desktop use: the controller should move the cursor here too.
        if (i.ForegroundIsSelf)
            return new(standAside, null, $"BogiHub window focused. {mouseKeyboard}");

        if (matched is not null)
            return new(ControlOwner.Hub, matched, $"\"{matched.Name}\" matches {i.ForegroundExe}: gamepad.");

        if (i.FullscreenIsGame && i.ForegroundIsFullscreen && !i.ForegroundIsDesktop && !IsKnownNonGame(i.ForegroundExe))
            return new(ControlOwner.Hub, fallback, $"Fullscreen game ({i.ForegroundExe ?? "unknown"}): gamepad with \"{fallback.Name}\".");

        return new(standAside, null, $"No game focused. {mouseKeyboard}");
    }

    /// <summary>
    /// Apps that often go fullscreen but are not games (browsers, video players, chat, Steam itself).
    /// The fullscreen rule ignores them; a profile that lists one explicitly still matches.
    /// </summary>
    public static readonly IReadOnlySet<string> NonGameApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "opera_gx.exe", "vivaldi.exe", "arc.exe",
        "vlc.exe", "mpv.exe", "mpc-hc64.exe", "mpc-be64.exe", "PotPlayerMini64.exe", "Video.UI.exe", "Microsoft.Media.Player.exe",
        "explorer.exe", "ApplicationFrameHost.exe", "POWERPNT.EXE", "Teams.exe", "ms-teams.exe", "Discord.exe", "obs64.exe",
        "steam.exe", "steamwebhelper.exe", "EpicGamesLauncher.exe", "Spotify.exe", "Netflix.exe", "WindowsTerminal.exe",
    };

    public static bool IsKnownNonGame(string? exe) => exe is not null && NonGameApps.Contains(exe);

    public static Profile? MatchExecutable(IReadOnlyList<Profile> profiles, string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        foreach (var p in profiles)
            foreach (string e in p.Executables)
                if (string.Equals(e, exe, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    private static Profile? Find(IReadOnlyList<Profile> profiles, string? id) =>
        id is null ? null : profiles.FirstOrDefault(p => p.Id == id);
}
