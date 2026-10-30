using System.Runtime.InteropServices;
using BogiHub.Core.Output;
using BogiHub.Output;

namespace BogiHub.Probe;

/// <summary>
/// `--vigem`: proves the virtual-controller path end to end without a game. Plugs in a virtual Xbox 360 pad,
/// holds A and a right-trigger pull, reads it back through XInput (the same API games use), then unplugs.
/// </summary>
internal static partial class VigemCheck
{
    public static int Run()
    {
        Console.WriteLine($"Driver: {ViGEmGamepadSink.ProbeDriver().Message}");
        var before = ConnectedSlots();
        Console.WriteLine($"XInput slots in use before: [{string.Join(",", before)}]");

        using var sink = new ViGEmGamepadSink();
        if (!sink.Connect())
        {
            Console.WriteLine($"FAIL: {sink.Status.Message}");
            return 1;
        }

        var report = new GamepadReport { Buttons = XButtons.A, RightTrigger = 200, LeftX = 16000 };
        int? slot = null;
        XINPUT_STATE st = default;
        for (int i = 0; i < 40 && slot is null; i++)
        {
            sink.Submit(report, force: true);
            Thread.Sleep(50);
            foreach (int s in ConnectedSlots().Except(before))
            {
                if (XInputGetState((uint)s, out st) == 0 && (st.Buttons & (ushort)XButtons.A) != 0)
                {
                    slot = s;
                    break;
                }
            }
        }

        sink.Neutralize();
        Thread.Sleep(100);
        bool released = slot is int sl && XInputGetState((uint)sl, out var after) == 0 && after.Buttons == 0 && after.RightTrigger == 0;
        sink.Disconnect();
        Thread.Sleep(300);
        bool unplugged = slot is int s2 && !ConnectedSlots().Contains(s2);

        if (slot is null)
        {
            Console.WriteLine("FAIL: virtual pad was created but XInput never saw A pressed.");
            return 1;
        }
        Console.WriteLine($"PASS: XInput slot {slot} saw buttons=0x{st.Buttons:X4} RT={st.RightTrigger} LX={st.LeftX}");
        Console.WriteLine($"{(released ? "PASS" : "FAIL")}: neutralise released everything");
        Console.WriteLine($"{(unplugged ? "PASS" : "FAIL")}: virtual pad unplugged cleanly");
        return released && unplugged ? 0 : 1;
    }

    private static List<int> ConnectedSlots()
    {
        var list = new List<int>();
        for (uint i = 0; i < 4; i++) if (XInputGetState(i, out _) == 0) list.Add((int)i);
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }

    [LibraryImport("xinput1_4.dll")]
    private static partial uint XInputGetState(uint userIndex, out XINPUT_STATE state);
}
