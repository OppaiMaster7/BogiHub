using System.Runtime.InteropServices;

namespace BogiHub.Platform.Windows;

/// <summary>
/// Sends "release" events for keys and mouse buttons that are stuck down. Only ever sends key-ups / button-ups
/// (plus one harmless mask key), so it cannot press anything.
/// </summary>
internal static unsafe partial class InputReleaser
{
    /// <summary>
    /// Unassigned virtual key (same trick AutoHotkey uses): tapping it while a Windows key is held means releasing
    /// that Windows key does not open the Start menu.
    /// </summary>
    private const ushort MaskKey = 0xE8;
    private const int LWin = 0x5B, RWin = 0x5C;

    public static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Releases each key/button in <paramref name="vks"/> that Windows still thinks is held. Returns what it released.</summary>
    public static List<int> ReleaseHeld(IEnumerable<int> vks, nuint signature)
    {
        var released = new List<int>();
        var inputs = new List<INPUT>();
        foreach (int vk in vks.Distinct())
        {
            if (!IsDown(vk)) continue;
            released.Add(vk);
            switch (vk)
            {
                case 1: inputs.Add(Mouse(MOUSEEVENTF_LEFTUP, 0, signature)); break;
                case 2: inputs.Add(Mouse(MOUSEEVENTF_RIGHTUP, 0, signature)); break;
                case 4: inputs.Add(Mouse(MOUSEEVENTF_MIDDLEUP, 0, signature)); break;
                case 5: inputs.Add(Mouse(MOUSEEVENTF_XUP, 1, signature)); break;
                case 6: inputs.Add(Mouse(MOUSEEVENTF_XUP, 2, signature)); break;
                default:
                    if (vk is LWin or RWin)
                    {
                        inputs.Add(Key(MaskKey, up: false, signature));
                        inputs.Add(Key(MaskKey, up: true, signature));
                    }
                    inputs.Add(Key((ushort)vk, up: true, signature));
                    break;
            }
        }
        if (inputs.Count > 0)
        {
            var arr = inputs.ToArray();
            fixed (INPUT* p = arr) SendInput((uint)arr.Length, p, sizeof(INPUT));
        }
        return released;
    }

    public static string Name(int vk) => vk switch
    {
        1 => "left mouse button", 2 => "right mouse button", 4 => "middle mouse button", 5 => "mouse X1", 6 => "mouse X2",
        0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x10 => "Shift", 0x11 => "Ctrl", 0x12 => "Alt", 0x1B => "Esc",
        0x20 => "Space", 0x5B => "Windows (left)", 0x5C => "Windows (right)", 0xA0 => "Left Shift", 0xA1 => "Right Shift",
        0xA2 => "Left Ctrl", 0xA3 => "Right Ctrl", 0xA4 => "Left Alt", 0xA5 => "Right Alt",
        0x25 => "Left arrow", 0x26 => "Up arrow", 0x27 => "Right arrow", 0x28 => "Down arrow",
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        _ => $"key 0x{vk:X2}",
    };

    private static INPUT Key(ushort vk, bool up, nuint signature)
    {
        uint scan = MapVirtualKeyW(vk, MAPVK_VK_TO_VSC_EX);
        var input = new INPUT { Type = INPUT_KEYBOARD };
        input.U.Keyboard.wVk = vk;
        input.U.Keyboard.wScan = (ushort)(scan & 0xFF);
        input.U.Keyboard.dwFlags = (up ? KEYEVENTF_KEYUP : 0) | ((scan & 0xFF00) is 0xE000 or 0xE100 ? KEYEVENTF_EXTENDEDKEY : 0);
        input.U.Keyboard.dwExtraInfo = signature;
        return input;
    }

    private static INPUT Mouse(uint flags, uint data, nuint signature)
    {
        var input = new INPUT { Type = INPUT_MOUSE };
        input.U.Mouse.dwFlags = flags;
        input.U.Mouse.mouseData = data;
        input.U.Mouse.dwExtraInfo = signature;
        return input;
    }

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;
    private const uint MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_RIGHTUP = 0x10, MOUSEEVENTF_MIDDLEUP = 0x40, MOUSEEVENTF_XUP = 0x100;
    private const uint MAPVK_VK_TO_VSC_EX = 4;

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

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    [LibraryImport("user32.dll")] private static partial uint SendInput(uint count, INPUT* inputs, int size);
    [LibraryImport("user32.dll")] private static partial uint MapVirtualKeyW(uint code, uint mapType);
    [LibraryImport("user32.dll")] private static partial short GetAsyncKeyState(int vk);
}
