using BogiHub.Core.Control;
using BogiHub.Core.Input;
using BogiHub.Core.Notifications;

namespace BogiHub.Core.Tests;

public class NoticeTrackerTests
{
    private static NoticeInputs Desktop(long now, int battery = 80) => new(
        now, ControlOwner.BuiltIn, MouseKeyboard: true, ProfileName: null, ForegroundIsSelf: false,
        ControllerConnected: true, Transport: "Wireless", new BatteryInfo(PowerStatus.OnBattery, battery),
        SteamRunning: false, SteamAutoExit: false, BuiltInLeaking: false, DoubleInput: false, DriverMissing: false, StuckKeysReleased: 0);

    private static NoticeInputs Game(long now, int battery = 80) =>
        Desktop(now, battery) with { Owner = ControlOwner.Hub, MouseKeyboard = false, ProfileName = "Controller" };

    private static List<Notice> Started(NoticeTracker t, NoticeInputs first)
    {
        var list = t.Update(first);
        return list;
    }

    [Fact]
    public void Start_with_controller_says_what_it_is()
    {
        var n = Assert.Single(Started(new NoticeTracker(), Desktop(0)));
        Assert.Equal("mode", n.Key);
        Assert.Equal("BogiHub is running", n.Title);
        Assert.Contains("Mouse & keyboard", n.Detail);
        Assert.Equal(80, n.BatteryPercent);
    }

    [Fact]
    public void Switching_to_a_game_announces_gamepad_once()
    {
        var t = new NoticeTracker();
        Started(t, Desktop(0));
        var n = Assert.Single(t.Update(Game(1000)));
        Assert.Equal(NoticeIcon.Gamepad, n.Icon);
        Assert.Contains("BogiHub in control", n.Detail);
        Assert.Empty(t.Update(Game(2000)));
    }

    [Fact]
    public void Mode_changes_while_looking_at_BogiHub_stay_quiet()
    {
        var t = new NoticeTracker();
        Started(t, Game(0));
        Assert.Empty(t.Update(Desktop(1000) with { ForegroundIsSelf = true }));
    }

    [Fact]
    public void Emergency_stop_always_shows()
    {
        var t = new NoticeTracker();
        Started(t, Desktop(0) with { ControllerConnected = false });
        var n = Assert.Single(t.Update(Desktop(1000) with { ControllerConnected = false, Owner = ControlOwner.Paused, ForegroundIsSelf = true }));
        Assert.Equal(NoticeTone.Danger, n.Tone);
        Assert.Equal(NoticeIcon.Pause, n.Icon);
    }

    [Fact]
    public void Short_disconnects_are_not_reported()
    {
        var t = new NoticeTracker();
        Started(t, Game(0));
        Assert.Empty(t.Update(Game(1000) with { ControllerConnected = false }));
        Assert.Empty(t.Update(Game(2000) with { ControllerConnected = false }));
        Assert.Empty(t.Update(Game(2500)));
        Assert.Empty(t.Update(Game(4000)));
    }

    [Fact]
    public void Real_disconnect_and_reconnect_are_reported()
    {
        var t = new NoticeTracker();
        Started(t, Game(0));
        t.Update(Game(1000) with { ControllerConnected = false });
        var gone = Assert.Single(t.Update(Game(1000 + NoticeTracker.DisconnectSettleMs) with { ControllerConnected = false }));
        Assert.Equal(NoticeIcon.Unplugged, gone.Icon);

        Assert.Empty(t.Update(Game(5000)));
        var back = Assert.Single(t.Update(Game(5000 + NoticeTracker.ConnectSettleMs)));
        Assert.Equal("Controller connected", back.Title);
        Assert.Contains("Gamepad", back.Detail);
        Assert.Equal(80, back.BatteryPercent);
    }

    [Fact]
    public void Steam_clash_warns_once_with_an_action()
    {
        var t = new NoticeTracker();
        Started(t, Desktop(0) with { SteamRunning = true, Owner = ControlOwner.Steam });
        var list = t.Update(Game(1000) with { SteamRunning = true });
        var steam = Assert.Single(list, n => n.Key == "steam");
        Assert.Equal(NoticeAction.OpenApp, steam.Action);
        Assert.Equal(NoticeTone.Warn, steam.Tone);

        // Back to the desktop and into a game again within 10 minutes: no second nag.
        t.Update(Desktop(2000) with { SteamRunning = true, Owner = ControlOwner.Steam });
        Assert.DoesNotContain(t.Update(Game(3000) with { SteamRunning = true }), n => n.Key == "steam");
    }

    [Fact]
    public void Steam_clash_is_silent_when_auto_exit_handles_it()
    {
        var t = new NoticeTracker();
        Started(t, Desktop(0) with { SteamRunning = true, Owner = ControlOwner.Steam });
        Assert.DoesNotContain(t.Update(Game(1000) with { SteamRunning = true, SteamAutoExit = true }), n => n.Key == "steam");
    }

    [Fact]
    public void Steam_closing_is_good_news()
    {
        var t = new NoticeTracker();
        Started(t, Game(0) with { SteamRunning = true });
        var n = Assert.Single(t.Update(Game(1000)));
        Assert.Equal("Steam closed", n.Title);
        Assert.Equal(NoticeTone.Ok, n.Tone);
    }

    [Fact]
    public void Battery_warns_at_each_level_once()
    {
        var t = new NoticeTracker();
        Started(t, Game(0, battery: 25));
        Assert.Empty(t.Update(Game(1000, battery: 21)));
        var at20 = Assert.Single(t.Update(Game(2000, battery: 20)));
        Assert.Equal(NoticeTone.Warn, at20.Tone);
        Assert.Empty(t.Update(Game(3000, battery: 19)));
        Assert.Empty(t.Update(Game(4000, battery: 21))); // noisy reading back up: no re-warn
        Assert.Empty(t.Update(Game(5000, battery: 20)));
        var at10 = Assert.Single(t.Update(Game(6000, battery: 9)));
        Assert.Equal(NoticeTone.Danger, at10.Tone);
        Assert.Single(t.Update(Game(7000, battery: 5)));
        Assert.Empty(t.Update(Game(8000, battery: 4)));
    }

    [Fact]
    public void Big_drop_warns_only_for_the_lowest_level_crossed()
    {
        var t = new NoticeTracker();
        Started(t, Game(0, battery: 50));
        var n = Assert.Single(t.Update(Game(1000, battery: 8)));
        Assert.Contains("8 %", n.Title);
        Assert.Empty(t.Update(Game(2000, battery: 7)));
    }

    [Fact]
    public void Low_battery_at_start_warns_right_away()
    {
        var list = Started(new NoticeTracker(), Game(0, battery: 12));
        Assert.Contains(list, n => n.Key == "battery" && n.Title.Contains("12 %"));
    }

    [Fact]
    public void Charging_and_full_are_announced_and_rearm_warnings()
    {
        var t = new NoticeTracker();
        Started(t, Game(0, battery: 30));
        t.Update(Game(1000, battery: 15));
        var charging = Assert.Single(t.Update(Game(2000, battery: 15) with { Battery = new BatteryInfo(PowerStatus.Charging, 15) }));
        Assert.Equal("Charging", charging.Title);
        var full = Assert.Single(t.Update(Game(3000) with { Battery = new BatteryInfo(PowerStatus.Charged, 100) }));
        Assert.Equal("Fully charged", full.Title);
        Assert.Single(t.Update(Game(4000, battery: 18))); // unplugged again later: warnings work again
    }

    [Fact]
    public void Unknown_battery_never_warns()
    {
        var t = new NoticeTracker();
        Started(t, Game(0) with { Battery = BatteryInfo.Unknown });
        Assert.Empty(t.Update(Game(1000) with { Battery = new BatteryInfo(PowerStatus.Unknown, 3) }));
    }

    [Fact]
    public void Problems_are_reported_on_their_rising_edge_and_rate_limited()
    {
        var t = new NoticeTracker();
        Started(t, Game(0));
        Assert.Single(t.Update(Game(1000) with { BuiltInLeaking = true }), n => n.Key == "leak");
        Assert.Empty(t.Update(Game(2000) with { BuiltInLeaking = true }));
        Assert.Empty(t.Update(Game(3000)));
        Assert.Empty(t.Update(Game(4000) with { BuiltInLeaking = true })); // again within 2 minutes
        t.Update(Game(130_000));
        Assert.Single(t.Update(Game(131_000) with { BuiltInLeaking = true }));
    }

    [Fact]
    public void Missing_driver_points_to_the_app()
    {
        var t = new NoticeTracker();
        Started(t, Desktop(0));
        var list = t.Update(Game(1000) with { DriverMissing = true });
        var n = Assert.Single(list, x => x.Key == "driver");
        Assert.Equal(NoticeAction.OpenApp, n.Action);
    }

    [Fact]
    public void Stuck_key_release_is_mentioned_with_its_count()
    {
        var t = new NoticeTracker();
        Started(t, Game(0));
        var n = Assert.Single(t.Update(Game(1000) with { StuckKeysReleased = 3 }));
        Assert.Equal("Released 3 stuck keys", n.Title);
    }
}
