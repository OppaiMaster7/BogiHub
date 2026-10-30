using BogiHub.Core.Output;

namespace BogiHub.Core.Tests;

public class StrayInputTrackerTests
{
    private const int LWin = 0x5B, Esc = 0x1B, LeftMouse = 1;

    [Fact]
    public void Pressed_and_released_leaves_nothing_to_release()
    {
        var t = new StrayInputTracker();
        t.Note(Esc, down: true, StraySource.Injected, 0);
        t.Note(Esc, down: false, StraySource.Injected, 50);
        Assert.Empty(t.TakeDue(1000, 150, _ => true));
        Assert.Empty(t.TakeAll());
    }

    [Fact]
    public void Key_held_past_threshold_is_due_once()
    {
        var t = new StrayInputTracker();
        t.Note(LWin, down: true, StraySource.ControllerBuiltIn, 100);
        Assert.Empty(t.TakeDue(200, 150, _ => true));
        Assert.Equal([LWin], t.TakeDue(260, 150, _ => true));
        Assert.Empty(t.TakeDue(5000, 150, _ => true));
    }

    [Fact]
    public void Auto_repeat_keeps_the_first_press_time()
    {
        var t = new StrayInputTracker();
        t.Note(Esc, down: true, StraySource.Injected, 0);
        t.Note(Esc, down: true, StraySource.Injected, 140); // repeat
        Assert.Equal([Esc], t.TakeDue(160, 150, _ => true));
    }

    [Fact]
    public void Only_eligible_sources_are_released_by_the_watchdog()
    {
        var t = new StrayInputTracker();
        t.Note(Esc, down: true, StraySource.Injected, 0);
        t.Note(LWin, down: true, StraySource.ControllerBuiltIn, 0);
        Assert.Equal([LWin], t.TakeDue(1000, 150, s => s == StraySource.ControllerBuiltIn));
        Assert.Equal([Esc], t.TakeAll());
    }

    [Fact]
    public void Mouse_buttons_are_tracked_like_keys()
    {
        var t = new StrayInputTracker();
        t.Note(LeftMouse, down: true, StraySource.Injected, 0);
        Assert.True(StrayInputTracker.IsMouseButton(LeftMouse));
        Assert.False(StrayInputTracker.IsMouseButton(Esc));
        Assert.Equal([LeftMouse], t.TakeAll());
    }

    [Fact]
    public void Invalid_codes_are_ignored()
    {
        var t = new StrayInputTracker();
        t.Note(0, down: true, StraySource.Injected, 0);
        t.Note(300, down: true, StraySource.Injected, 0);
        Assert.Equal(0, t.Count);
    }
}
