using System.Runtime.InteropServices;
using BogiHub.Output;
using BogiHub.Platform.Windows;

namespace BogiHub.Probe;

/// <summary>
/// `--sources [seconds]`: prints, once per second, how many mouse/keyboard events came from the controller's built-in
/// mode (VID 28DE), from injected input (Steam, other remappers), and from other devices (your real mouse/keyboard).
/// Run it while using the controller to see exactly which default inputs are leaking.
/// </summary>
internal static unsafe partial class SourcesCheck
{
    public static int Run(int seconds)
    {
        using var events = new WindowsEvents(SendInputSink.Signature);
        events.Start();
        events.SetSourceMonitoring(true);
        Thread.Sleep(300);

        // Self-test: one injected 1-pixel move there and back must be counted as injected.
        var before = WindowsEvents.SourceCounts;
        InjectMove(1);
        InjectMove(-1);
        Thread.Sleep(200);
        var after = WindowsEvents.SourceCounts;
        Console.WriteLine(after.Injected - before.Injected >= 2
            ? "PASS: monitor sees injected input"
            : $"FAIL: monitor did not see injected input ({after.Injected - before.Injected})");

        Console.WriteLine($"Watching for {seconds} s. Use the controller now (trackpads, buttons, sticks).");
        Console.WriteLine("  sec | controller mouse | controller keys | injected (Steam etc.) | real mouse/keyboard");
        var last = WindowsEvents.SourceCounts;
        long totalMouse = 0, totalKeys = 0;
        for (int s = 1; s <= seconds; s++)
        {
            Thread.Sleep(1000);
            var now = WindowsEvents.SourceCounts;
            long m = now.ControllerMouse - last.ControllerMouse, k = now.ControllerKeyboard - last.ControllerKeyboard;
            totalMouse += m;
            totalKeys += k;
            Console.WriteLine($"  {s,3} | {m,16} | {k,15} | {now.Injected - last.Injected,20} | {now.OtherDevices - last.OtherDevices,19}");
            last = now;
        }
        Console.WriteLine(totalMouse + totalKeys == 0
            ? "RESULT: the controller's built-in mouse/keyboard produced nothing."
            : $"RESULT: the controller's built-in mode produced {totalMouse} mouse and {totalKeys} key events.");
        return 0;
    }

    private static void InjectMove(int dx)
    {
        var input = new INPUT { Type = 0 };
        input.Mouse.dx = dx;
        input.Mouse.dwFlags = 0x1;
        SendInput(1, &input, sizeof(INPUT));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public MOUSEINPUT Mouse;
    }

    [LibraryImport("user32.dll")] private static partial uint SendInput(uint n, INPUT* inputs, int size);
}
