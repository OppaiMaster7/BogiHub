namespace BogiHub.Core.Output;

/// <summary>Who pressed a key BogiHub did not send.</summary>
public enum StraySource
{
    /// <summary>Synthetic input from another program (e.g. Steam's desktop layout).</summary>
    Injected,
    /// <summary>The controller's own firmware mouse/keyboard ("lizard") mode.</summary>
    ControllerBuiltIn,
}

/// <summary>
/// Remembers keys and mouse buttons that untrusted sources pressed and have not released yet, so they can be
/// released if their "up" never arrives (that is what leaves a Windows/Alt/Esc key stuck down).
/// Keys use Windows virtual-key codes; mouse buttons use their VK codes too (1 left, 2 right, 4 middle, 5/6 X1/X2).
/// Pure logic: the caller decides when to ask and sends the releases. Not thread-safe (one owner thread).
/// </summary>
public sealed class StrayInputTracker
{
    private readonly Dictionary<int, (StraySource Source, long DownTick)> _held = [];

    public int Count => _held.Count;

    public static bool IsMouseButton(int vk) => vk is 1 or 2 or 4 or 5 or 6;

    public void Note(int vk, bool down, StraySource source, long tick)
    {
        if (vk is <= 0 or > 255) return;
        if (!down)
        {
            _held.Remove(vk);
            return;
        }
        // Auto-repeat sends more "downs" while a key is held: keep the time of the first one.
        _held.TryAdd(vk, (source, tick));
    }

    /// <summary>Removes and returns keys held for at least <paramref name="minHeldMs"/> from eligible sources.</summary>
    public List<int> TakeDue(long now, int minHeldMs, Func<StraySource, bool> eligible)
    {
        var due = new List<int>();
        foreach (var (vk, (source, downTick)) in _held)
            if (eligible(source) && now - downTick >= minHeldMs) due.Add(vk);
        foreach (int vk in due) _held.Remove(vk);
        return due;
    }

    /// <summary>Removes and returns everything still held (used when control changes hands).</summary>
    public List<int> TakeAll()
    {
        var all = _held.Keys.ToList();
        _held.Clear();
        return all;
    }
}
