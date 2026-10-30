using System.Runtime.InteropServices;
using BogiHub.Output;
using BogiHub.Platform.Windows;

namespace BogiHub.Probe;

/// <summary>
/// `--guard`: verifies the Steam guard without Steam. A "foreign" injected mouse move during controller activity must be
/// dropped; BogiHub's own tagged input must pass; foreign input after the activity window must pass again.
/// The cursor is restored to where it started.
/// </summary>
internal static unsafe partial class GuardCheck
{
    public static int Run()
    {
        using var events = new WindowsEvents(SendInputSink.Signature);
        events.Start();
        events.SetConflictMonitoring(true);
        Thread.Sleep(400); // hooks are installed on the events thread

        GetCursorPos(out var start);
        int failures = 0;
        try
        {
            WindowsEvents.BlockForeignWhileControllerActive = true;

            WindowsEvents.NoteControllerActivity();
            long blockedBefore = WindowsEvents.BlockedCount;
            bool moved = MoveAndCheck(40, foreign: true);
            failures += Report(!moved && WindowsEvents.BlockedCount > blockedBefore, "foreign input during controller use is blocked");

            WindowsEvents.NoteControllerActivity();
            moved = MoveAndCheck(40, foreign: false);
            failures += Report(moved, "BogiHub's own input still passes during controller use");
            SetCursorPos(start.X, start.Y);

            Thread.Sleep(450); // outside the 300 ms activity window
            moved = MoveAndCheck(40, foreign: true);
            failures += Report(moved, "foreign input passes when the controller is idle");
            SetCursorPos(start.X, start.Y);

            WindowsEvents.BlockForeignWhileControllerActive = false;
            WindowsEvents.NoteControllerActivity();
            moved = MoveAndCheck(40, foreign: true);
            failures += Report(moved, "guard off: foreign input passes even during controller use");

            // --- Stuck-key regressions (harmless keys F21–F24) ---
            const int F21 = 0x84, F22 = 0x85, F23 = 0x86, F24 = 0x87;

            // 1. Down passes (guard off), guard turns on during the hold, the release must still get through.
            WindowsEvents.BlockForeignWhileControllerActive = false;
            ForeignKey(F24, up: false);
            WindowsEvents.BlockForeignWhileControllerActive = true;
            WindowsEvents.NoteControllerActivity();
            ForeignKey(F24, up: true);
            failures += Report(!IsDown(F24), "a key-up is never blocked (no stuck key when the guard switches on mid-press)");

            // 2. While BogiHub is in control with the guard on, a stray held key is released by the watchdog.
            WindowsEvents.HubInControl = true;
            Thread.Sleep(350); // let the activity window expire so the "down" passes, as in a race with Steam
            ForeignKey(F23, up: false);
            bool wasDown = IsDown(F23);
            Thread.Sleep(450);
            failures += Report(wasDown && !IsDown(F23), "watchdog releases a key Steam left held while BogiHub is in control");

            // 3. Control changes hands: everything still held is released immediately.
            WindowsEvents.HubInControl = false;
            WindowsEvents.BlockForeignWhileControllerActive = false;
            ForeignKey(F22, up: false);
            wasDown = IsDown(F22);
            events.ReleaseStrayInput();
            Thread.Sleep(200);
            failures += Report(wasDown && !IsDown(F22), "hand-over releases keys left held by Steam / built-in mode");

            // 4. Manual "Release stuck keys".
            ForeignKey(F21, up: false);
            wasDown = IsDown(F21);
            events.ReleaseAllStuckKeys();
            Thread.Sleep(200);
            failures += Report(wasDown && !IsDown(F21), "manual \"Release stuck keys\" works");
            Console.WriteLine($"     (stuck keys released in this test: {WindowsEvents.ReleasedCount})");
        }
        finally
        {
            WindowsEvents.BlockForeignWhileControllerActive = false;
            WindowsEvents.HubInControl = false;
            foreach (int vk in new[] { 0x84, 0x85, 0x86, 0x87 }) if (IsDown(vk)) ForeignKey(vk, up: true);
            SetCursorPos(start.X, start.Y);
        }
        return failures == 0 ? 0 : 1;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Injects a key event that looks like it came from another program (no BogiHub signature).</summary>
    private static void ForeignKey(int vk, bool up)
    {
        var input = new INPUT { Type = 1 };
        input.Keyboard.wVk = (ushort)vk;
        input.Keyboard.dwFlags = up ? 0x2u : 0u;
        SendInput(1, &input, sizeof(INPUT));
        Thread.Sleep(60);
    }

    private static int Report(bool ok, string what)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {what}");
        return ok ? 0 : 1;
    }

    private static bool MoveAndCheck(int dx, bool foreign)
    {
        GetCursorPos(out var before);
        var input = new INPUT { Type = 0 };
        input.Mouse.dx = dx;
        input.Mouse.dwFlags = 0x1; // MOUSEEVENTF_MOVE
        input.Mouse.dwExtraInfo = foreign ? 0 : SendInputSink.Signature;
        SendInput(1, &input, sizeof(INPUT));
        Thread.Sleep(80);
        GetCursorPos(out var after);
        return after.X != before.X;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public MOUSEINPUT Mouse;
        [FieldOffset(8)] public KEYBDINPUT Keyboard;
    }

    [LibraryImport("user32.dll")] private static partial short GetAsyncKeyState(int vk);

    [LibraryImport("user32.dll")] private static partial uint SendInput(uint n, INPUT* inputs, int size);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out POINT p);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetCursorPos(int x, int y);
}
