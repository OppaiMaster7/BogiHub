using static BogiHub.Devices.Sdl.Native.Sdl3;

namespace BogiHub.Probe;

/// <summary>
/// `--haptics`: plays trackpad haptics with side byte 0, then 1, then 2, so a person holding the controller can say
/// which pad each one reaches. BogiHub assumes 0 = left and 1 = right (SdlControllerService.HapticSideLeft/Right).
/// Also reports whether the controller accepts the output report at all over this link (puck, cable, Bluetooth).
/// </summary>
internal static unsafe class HapticsCheck
{
    public static int Run()
    {
        SDL_SetHint(HintJoystickHidapiSteam, "1");
        if (!SDL_Init(InitGamepad))
        {
            Console.WriteLine($"SDL_Init failed: {LastError()}");
            return 1;
        }
        try
        {
            for (int i = 0; i < 40; i++) { SDL_PumpEvents(); Thread.Sleep(25); }
            uint id = GetJoystickIds().FirstOrDefault(j => SDL_GetJoystickVendorForID(j) == VendorValve && SDL_IsGamepad(j));
            if (id == 0)
            {
                Console.WriteLine("No connected Steam Controller (is it asleep? press the Steam button).");
                return 1;
            }
            string? path = Str(SDL_GetJoystickPathForID(id));
            IntPtr hid = path is null ? IntPtr.Zero : SDL_hid_open_path(path);
            if (hid == IntPtr.Zero)
            {
                Console.WriteLine($"Could not open {path}: {LastError()}");
                return 1;
            }
            Console.WriteLine($"Controller PID {SDL_GetJoystickProductForID(id):X4}. Hold it with both thumbs on the trackpads.");
            byte* r = stackalloc byte[4];
            foreach (byte side in new byte[] { 0, 1, 2 })
            {
                Console.WriteLine($"\nSide {side}: one click, then ten ticks. Which pad do you feel?");
                Thread.Sleep(1200);
                int ok = 0, writes = 0;
                for (int i = 0; i < 11; i++)
                {
                    r[0] = 0x82;
                    r[1] = side;
                    r[2] = i == 0 ? (byte)2 : (byte)1; // click, then ticks
                    r[3] = 0;                          // 0 dB
                    writes++;
                    if (SDL_hid_write(hid, r, 4) == 4) ok++;
                    Thread.Sleep(i == 0 ? 400 : 70);
                }
                Console.WriteLine($"  {ok}/{writes} reports accepted{(ok == 0 ? $" ({LastError()})" : "")}");
            }
            SDL_hid_close(hid);
            Console.WriteLine("\nDone. Tell Claude what you felt for side 0, 1 and 2.");
            return 0;
        }
        finally
        {
            SDL_Quit();
        }
    }
}
