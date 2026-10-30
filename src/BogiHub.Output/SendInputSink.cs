using System.Runtime.InteropServices;
using BogiHub.Core.Output;

namespace BogiHub.Output;

/// <summary>
/// Keyboard and mouse output through Win32 SendInput. Keys are sent as hardware scancodes because many games
/// (DirectInput/raw input) ignore virtual-key-only events. Every event carries <see cref="Signature"/> in dwExtraInfo
/// so BogiHub can tell its own injected input apart from other remappers such as Steam.
/// Only touched from the input thread.
/// </summary>
public sealed unsafe partial class SendInputSink
{
    /// <summary>"BGHB". Low-level hooks compare dwExtraInfo against this.</summary>
    public const nuint Signature = 0x42474842;

    private readonly bool[] _keysDown = new bool[256];
    private MouseButtons _mouseDown;
    private float _remX, _remY;
    private readonly INPUT[] _buffer = new INPUT[300];
    private int _count;

    public OutputStatus Status { get; private set; } = OutputStatus.Idle;

    public void Submit(OutputFrame f)
    {
        _count = 0;

        for (int vk = 1; vk < 256; vk++)
        {
            if (f.Keys[vk] == _keysDown[vk]) continue;
            AddKey((ushort)vk, up: !f.Keys[vk]);
            _keysDown[vk] = f.Keys[vk];
        }

        var changed = f.Mouse ^ _mouseDown;
        if (changed != 0)
        {
            AddMouseButton(changed, f.Mouse);
            _mouseDown = f.Mouse;
        }

        _remX += f.MouseDx;
        _remY += f.MouseDy;
        int dx = (int)_remX, dy = (int)_remY;
        if (dx != 0 || dy != 0)
        {
            _remX -= dx;
            _remY -= dy;
            Add(Mouse(dx, dy, 0, MOUSEEVENTF_MOVE));
        }

        if (f.WheelNotches != 0) Add(Mouse(0, 0, (uint)(f.WheelNotches * 120), MOUSEEVENTF_WHEEL));

        Flush();
    }

    /// <summary>Releases every key and mouse button this sink is holding.</summary>
    public void Neutralize()
    {
        _count = 0;
        for (int vk = 1; vk < 256; vk++)
        {
            if (!_keysDown[vk]) continue;
            AddKey((ushort)vk, up: true);
            _keysDown[vk] = false;
        }
        if (_mouseDown != 0)
        {
            AddMouseButton(_mouseDown, MouseButtons.None);
            _mouseDown = MouseButtons.None;
        }
        _remX = _remY = 0;
        Flush();
    }

    public bool IsHoldingAnything => _mouseDown != 0 || Array.IndexOf(_keysDown, true) >= 0;

    private void Flush()
    {
        if (_count == 0) return;
        uint sent;
        fixed (INPUT* p = _buffer) sent = SendInput((uint)_count, p, sizeof(INPUT));
        if (sent == _count)
        {
            Status = new OutputStatus(OutputReadiness.Ready, "Keyboard & mouse output active");
        }
        else
        {
            int err = Marshal.GetLastPInvokeError();
            Status = err == 5
                ? new OutputStatus(OutputReadiness.Blocked, "Windows blocked keyboard/mouse output. The focused app probably runs as administrator.")
                : new OutputStatus(OutputReadiness.Error, $"SendInput failed (error {err}).");
        }
        _count = 0;
    }

    private void AddKey(ushort vk, bool up)
    {
        uint scan = MapVirtualKeyW(vk, MAPVK_VK_TO_VSC_EX);
        var input = new INPUT { Type = INPUT_KEYBOARD };
        if (scan != 0)
        {
            input.U.Keyboard.wScan = (ushort)(scan & 0xFF);
            input.U.Keyboard.dwFlags = KEYEVENTF_SCANCODE | ((scan & 0xFF00) is 0xE000 or 0xE100 ? KEYEVENTF_EXTENDEDKEY : 0);
        }
        else
        {
            input.U.Keyboard.wVk = vk; // media and volume keys have no scancode mapping
        }
        if (up) input.U.Keyboard.dwFlags |= KEYEVENTF_KEYUP;
        input.U.Keyboard.dwExtraInfo = Signature;
        Add(input);
    }

    private void AddMouseButton(MouseButtons changed, MouseButtons now)
    {
        void One(MouseButtons b, uint down, uint up, uint data = 0)
        {
            if ((changed & b) != 0) Add(Mouse(0, 0, data, (now & b) != 0 ? down : up));
        }
        One(MouseButtons.Left, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
        One(MouseButtons.Right, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
        One(MouseButtons.Middle, MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
        One(MouseButtons.X1, MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON1);
        One(MouseButtons.X2, MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON2);
    }

    private static INPUT Mouse(int dx, int dy, uint data, uint flags)
    {
        var input = new INPUT { Type = INPUT_MOUSE };
        input.U.Mouse.dx = dx;
        input.U.Mouse.dy = dy;
        input.U.Mouse.mouseData = data;
        input.U.Mouse.dwFlags = flags;
        input.U.Mouse.dwExtraInfo = Signature;
        return input;
    }

    private void Add(in INPUT input)
    {
        if (_count == _buffer.Length) Flush();
        _buffer[_count++] = input;
    }

    // ---- Win32 -------------------------------------------------------------------------------

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;
    private const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4,
        MOUSEEVENTF_RIGHTDOWN = 0x8, MOUSEEVENTF_RIGHTUP = 0x10, MOUSEEVENTF_MIDDLEDOWN = 0x20, MOUSEEVENTF_MIDDLEUP = 0x40,
        MOUSEEVENTF_XDOWN = 0x80, MOUSEEVENTF_XUP = 0x100, MOUSEEVENTF_WHEEL = 0x800;
    private const uint XBUTTON1 = 1, XBUTTON2 = 2;
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

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint code, uint mapType);
}
