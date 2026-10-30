using BogiHub.Core.Control;
using BogiHub.Core.Profiles;

namespace BogiHub.Core.Tests;

public class ControlPolicyTests
{
    private static readonly Profile Fortnite = MakeFortnite();

    private static Profile MakeFortnite()
    {
        var p = DefaultProfiles.Controller();
        p.Id = "fortnite";
        p.Name = "Fortnite";
        p.Executables.Add("FortniteClient-Win64-Shipping.exe");
        return p;
    }

    private static ControlInputs Inputs(
        ControlSwitch sw = ControlSwitch.Auto, ManualMode manual = ManualMode.Gamepad, bool paused = false, bool steam = false,
        string? exe = "explorer.exe", bool fullscreen = false, bool desktop = true, bool self = false, string? pinned = null,
        bool fullscreenIsGame = true) =>
        new(sw, manual, paused, steam, exe, fullscreen, desktop, self, pinned, DefaultProfiles.ControllerId, fullscreenIsGame,
            [.. DefaultProfiles.All(), Fortnite]);

    // ---- Auto -------------------------------------------------------------------------------------

    [Fact]
    public void Auto_desktop_without_steam_is_the_controllers_own_mouse_and_keyboard()
    {
        var d = ControlPolicy.Decide(Inputs());
        Assert.Equal(ControlOwner.BuiltIn, d.Owner);
        Assert.True(d.IsMouseKeyboard);
        Assert.Null(d.Profile);
    }

    [Fact]
    public void Auto_desktop_with_steam_is_steam()
    {
        var d = ControlPolicy.Decide(Inputs(steam: true));
        Assert.Equal(ControlOwner.Steam, d.Owner);
        Assert.True(d.IsMouseKeyboard);
    }

    [Fact]
    public void Auto_bogihub_window_counts_as_desktop_so_the_cursor_works_there()
    {
        // The bug: looking at BogiHub kept the gamepad (and the live preview held the controller), so no cursor.
        var d = ControlPolicy.Decide(Inputs(exe: "BogiHub.exe", self: true, desktop: false));
        Assert.Equal(ControlOwner.BuiltIn, d.Owner);
        Assert.False(d.HubActive);
    }

    [Fact]
    public void Auto_matching_executable_selects_its_profile_case_insensitively()
    {
        var d = ControlPolicy.Decide(Inputs(exe: "fortniteclient-win64-shipping.EXE", desktop: false));
        Assert.Equal(ControlOwner.Hub, d.Owner);
        Assert.Equal("fortnite", d.Profile!.Id);
        Assert.False(d.IsMouseKeyboard);
    }

    [Fact]
    public void Auto_unknown_fullscreen_game_uses_default_profile()
    {
        var d = ControlPolicy.Decide(Inputs(exe: "somegame.exe", fullscreen: true, desktop: false));
        Assert.Equal(ControlOwner.Hub, d.Owner);
        Assert.Equal(DefaultProfiles.ControllerId, d.Profile!.Id);
    }

    [Fact]
    public void Auto_game_then_desktop_switches_back_to_mouse_and_keyboard()
    {
        Assert.True(ControlPolicy.Decide(Inputs(exe: "FortniteClient-Win64-Shipping.exe", desktop: false)).HubActive);
        Assert.False(ControlPolicy.Decide(Inputs(exe: "notepad.exe", desktop: false)).HubActive);
    }

    [Theory]
    [InlineData("chrome.exe")]
    [InlineData("VLC.exe")]
    [InlineData("steamwebhelper.exe")]
    public void Fullscreen_browsers_and_players_are_not_games(string exe)
    {
        var d = ControlPolicy.Decide(Inputs(exe: exe, fullscreen: true, desktop: false));
        Assert.Equal(ControlOwner.BuiltIn, d.Owner);
    }

    [Fact]
    public void Explicit_profile_beats_non_game_list()
    {
        var p = DefaultProfiles.KeyboardMouse();
        p.Id = "browser";
        p.Executables.Add("chrome.exe");
        var inputs = Inputs(exe: "chrome.exe", fullscreen: true, desktop: false) with { Profiles = [p] };
        Assert.Equal("browser", ControlPolicy.Decide(inputs).Profile!.Id);
    }

    [Fact]
    public void Fullscreen_rule_can_be_turned_off()
    {
        var d = ControlPolicy.Decide(Inputs(exe: "somegame.exe", fullscreen: true, desktop: false, fullscreenIsGame: false));
        Assert.Equal(ControlOwner.BuiltIn, d.Owner);
    }

    [Fact]
    public void Pinned_profile_overrides_executable_match()
    {
        var d = ControlPolicy.Decide(Inputs(exe: "FortniteClient-Win64-Shipping.exe", desktop: false, pinned: DefaultProfiles.KeyboardMouseId));
        Assert.Equal(DefaultProfiles.KeyboardMouseId, d.Profile!.Id);
        Assert.True(d.IsMouseKeyboard); // a BogiHub keyboard & mouse profile also counts as mouse & keyboard
    }

    // ---- Manual -----------------------------------------------------------------------------------

    [Fact]
    public void Manual_gamepad_takes_control_everywhere_and_is_gamepad_only()
    {
        var d = ControlPolicy.Decide(Inputs(sw: ControlSwitch.Manual, manual: ManualMode.Gamepad));
        Assert.Equal(ControlOwner.Hub, d.Owner);
        Assert.True(d.GamepadOnly);
        Assert.Equal(DefaultProfiles.ControllerId, d.Profile!.Id);
    }

    [Fact]
    public void Manual_mouse_keyboard_stays_mouse_even_in_a_game()
    {
        var d = ControlPolicy.Decide(Inputs(sw: ControlSwitch.Manual, manual: ManualMode.MouseKeyboard,
            exe: "FortniteClient-Win64-Shipping.exe", fullscreen: true, desktop: false));
        Assert.Equal(ControlOwner.BuiltIn, d.Owner);
        Assert.True(d.IsMouseKeyboard);
    }

    [Fact]
    public void Manual_mouse_keyboard_with_steam_running_is_steam()
    {
        var d = ControlPolicy.Decide(Inputs(sw: ControlSwitch.Manual, manual: ManualMode.MouseKeyboard, steam: true));
        Assert.Equal(ControlOwner.Steam, d.Owner);
    }

    [Fact]
    public void Manual_gamepad_skips_keyboard_mouse_profiles()
    {
        var d = ControlPolicy.Decide(Inputs(sw: ControlSwitch.Manual, pinned: DefaultProfiles.KeyboardMouseId));
        Assert.Equal(DefaultProfiles.ControllerId, d.Profile!.Id);
    }

    [Fact]
    public void Manual_gamepad_still_uses_matching_gamepad_profile()
    {
        var d = ControlPolicy.Decide(Inputs(sw: ControlSwitch.Manual, exe: "FortniteClient-Win64-Shipping.exe", desktop: false));
        Assert.Equal("fortnite", d.Profile!.Id);
    }

    [Fact]
    public void Auto_game_is_not_gamepad_only()
    {
        Assert.False(ControlPolicy.Decide(Inputs(exe: "FortniteClient-Win64-Shipping.exe", desktop: false)).GamepadOnly);
    }

    [Fact]
    public void Missing_default_profile_falls_back_to_builtin_controller_layout()
    {
        var inputs = Inputs(sw: ControlSwitch.Manual) with { DefaultProfileId = "deleted", Profiles = [] };
        Assert.Equal(DefaultProfiles.ControllerId, ControlPolicy.Decide(inputs).Profile!.Id);
    }

    // ---- Off / pause ------------------------------------------------------------------------------

    [Fact]
    public void Switch_off_always_stands_aside_even_in_a_matched_game()
    {
        var d = ControlPolicy.Decide(Inputs(sw: ControlSwitch.Off, exe: "FortniteClient-Win64-Shipping.exe", desktop: false, steam: true));
        Assert.Equal(ControlOwner.Steam, d.Owner);
    }

    [Fact]
    public void Emergency_pause_beats_everything()
    {
        var d = ControlPolicy.Decide(Inputs(sw: ControlSwitch.Manual, paused: true, pinned: "fortnite"));
        Assert.Equal(ControlOwner.Paused, d.Owner);
        Assert.False(d.HubActive);
    }

    // ---- Settings ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("Hub")]
    [InlineData("Gamepad")]
    public void Old_always_gamepad_settings_migrate_to_manual_gamepad(string old)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, $"{{ \"schemaVersion\": 1, \"switch\": \"{old}\", \"rumbleStrength\": 0.5, \"desktop\": \"BuiltIn\", \"livePreview\": true }}");
        try
        {
            var s = AppSettings.Load(path);
            Assert.Equal(ControlSwitch.Manual, s.Switch);
            Assert.Equal(ManualMode.Gamepad, s.ManualMode);
            Assert.Equal(0.5f, s.RumbleStrength); // the rest of the settings survive (unknown old keys are ignored)
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Corrupted_settings_file_loads_defaults()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var s = AppSettings.Load(path);
            Assert.Equal(ControlSwitch.Auto, s.Switch);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
