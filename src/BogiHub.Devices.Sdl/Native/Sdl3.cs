using System.Runtime.InteropServices;

namespace BogiHub.Devices.Sdl.Native;

/// <summary>
/// Minimal hand-written SDL3 bindings: only what BogiHub uses.
/// Values verified against SDL 3.4.18 headers (SDL_events.h, SDL_init.h, SDL_power.h, SDL_joystick.h, SDL_sensor.h).
/// </summary>
public static unsafe partial class Sdl3
{
    private const string Lib = "SDL3";

    public const uint InitJoystick = 0x00000200u;
    public const uint InitGamepad = 0x00002000u;
    public const uint InitEvents = 0x00004000u;
    public const uint InitSensor = 0x00008000u;

    public const string HintJoystickHidapiSteam = "SDL_JOYSTICK_HIDAPI_STEAM";
    public const string HintJoystickAllowBackgroundEvents = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS";

    public const ushort VendorValve = 0x28DE;
    public const ushort ProductProteusDongle = 0x1304;
    public const ushort ProductNereidDongle = 0x1305;

    [LibraryImport(Lib)] public static partial int SDL_GetVersion();
    [LibraryImport(Lib)] public static partial IntPtr SDL_GetRevision();
    [LibraryImport(Lib)] public static partial IntPtr SDL_GetError();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SetHint(string name, string value);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_Init(uint flags);

    [LibraryImport(Lib)] public static partial void SDL_Quit();
    [LibraryImport(Lib)] public static partial void SDL_free(void* mem);

    [LibraryImport(Lib)] public static partial void SDL_PumpEvents();

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_PollEvent(SdlEvent* ev);

    [LibraryImport(Lib)] public static partial void SDL_SetEventEnabled(uint type, [MarshalAs(UnmanagedType.U1)] bool enabled);

    // Joystick enumeration (by instance id, before opening)
    [LibraryImport(Lib)] public static partial uint* SDL_GetJoysticks(int* count);
    [LibraryImport(Lib)] public static partial IntPtr SDL_GetJoystickNameForID(uint instanceId);
    [LibraryImport(Lib)] public static partial IntPtr SDL_GetJoystickPathForID(uint instanceId);
    [LibraryImport(Lib)] public static partial ushort SDL_GetJoystickVendorForID(uint instanceId);
    [LibraryImport(Lib)] public static partial ushort SDL_GetJoystickProductForID(uint instanceId);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_IsGamepad(uint instanceId);

    // Gamepad
    [LibraryImport(Lib)] public static partial IntPtr SDL_OpenGamepad(uint instanceId);
    [LibraryImport(Lib)] public static partial void SDL_CloseGamepad(IntPtr gamepad);
    [LibraryImport(Lib)] public static partial IntPtr SDL_GetGamepadJoystick(IntPtr gamepad);
    [LibraryImport(Lib)] public static partial int SDL_GetNumGamepadTouchpads(IntPtr gamepad);
    [LibraryImport(Lib)] public static partial int SDL_GetNumGamepadTouchpadFingers(IntPtr gamepad, int touchpad);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_GetGamepadTouchpadFinger(IntPtr gamepad, int touchpad, int finger, byte* down, float* x, float* y, float* pressure);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_GamepadHasSensor(IntPtr gamepad, SensorType type);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SetGamepadSensorEnabled(IntPtr gamepad, SensorType type, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_GetGamepadSensorData(IntPtr gamepad, SensorType type, float* data, int numValues);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_RumbleGamepad(IntPtr gamepad, ushort lowFrequency, ushort highFrequency, uint durationMs);

    // Joystick (raw, after opening)
    [LibraryImport(Lib)] public static partial uint SDL_GetJoystickID(IntPtr joystick);
    [LibraryImport(Lib)] public static partial int SDL_GetNumJoystickButtons(IntPtr joystick);
    [LibraryImport(Lib)] public static partial int SDL_GetNumJoystickAxes(IntPtr joystick);
    [LibraryImport(Lib)] public static partial int SDL_GetNumJoystickHats(IntPtr joystick);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_GetJoystickButton(IntPtr joystick, int button);

    [LibraryImport(Lib)] public static partial short SDL_GetJoystickAxis(IntPtr joystick, int axis);
    [LibraryImport(Lib)] public static partial byte SDL_GetJoystickHat(IntPtr joystick, int hat);
    [LibraryImport(Lib)] public static partial ConnectionState SDL_GetJoystickConnectionState(IntPtr joystick);
    [LibraryImport(Lib)] public static partial PowerState SDL_GetJoystickPowerInfo(IntPtr joystick, int* percent);
    [LibraryImport(Lib)] public static partial IntPtr SDL_GetJoystickSerial(IntPtr joystick);
    [LibraryImport(Lib)] public static partial ushort SDL_GetJoystickFirmwareVersion(IntPtr joystick);

    // Raw HID enumeration (diagnostics only)
    [LibraryImport(Lib)] public static partial int SDL_hid_init();
    [LibraryImport(Lib)] public static partial HidDeviceInfo* SDL_hid_enumerate(ushort vendorId, ushort productId);
    [LibraryImport(Lib)] public static partial void SDL_hid_free_enumeration(HidDeviceInfo* devs);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr SDL_hid_open_path(string path);

    [LibraryImport(Lib)] public static partial int SDL_hid_send_feature_report(IntPtr dev, byte* data, nuint length);
    [LibraryImport(Lib)] public static partial int SDL_hid_read_timeout(IntPtr dev, byte* data, nuint length, int milliseconds);
    [LibraryImport(Lib)] public static partial int SDL_hid_write(IntPtr dev, byte* data, nuint length);
    [LibraryImport(Lib)] public static partial int SDL_hid_close(IntPtr dev);

    public static string? Str(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
    public static string LastError() => Str(SDL_GetError()) ?? "";

    public static string VersionString()
    {
        int v = SDL_GetVersion();
        return $"{v / 1000000}.{v / 1000 % 1000}.{v % 1000}";
    }

    public static uint[] GetJoystickIds()
    {
        int count;
        uint* ids = SDL_GetJoysticks(&count);
        if (ids == null) return [];
        var result = new uint[count];
        for (int i = 0; i < count; i++) result[i] = ids[i];
        SDL_free(ids);
        return result;
    }
}

public enum SensorType
{
    Invalid = -1,
    Unknown = 0,
    Accel = 1,
    Gyro = 2,
}

public enum ConnectionState
{
    Invalid = -1,
    Unknown = 0,
    Wired = 1,
    Wireless = 2,
}

public enum PowerState
{
    Error = -1,
    Unknown = 0,
    OnBattery = 1,
    NoBattery = 2,
    Charging = 3,
    Charged = 4,
}

public static class SdlEventType
{
    public const uint Quit = 0x100;
    public const uint JoystickAdded = 0x605;
    public const uint JoystickRemoved = 0x606;
    public const uint JoystickBatteryUpdated = 0x607;
    public const uint JoystickUpdateComplete = 0x608;
    public const uint GamepadAxisMotion = 0x650;
    public const uint GamepadButtonDown = 0x651;
    public const uint GamepadButtonUp = 0x652;
    public const uint GamepadAdded = 0x653;
    public const uint GamepadRemoved = 0x654;
    public const uint GamepadRemapped = 0x655;
    public const uint GamepadTouchpadDown = 0x656;
    public const uint GamepadTouchpadMotion = 0x657;
    public const uint GamepadTouchpadUp = 0x658;
    public const uint GamepadSensorUpdate = 0x659;
    public const uint GamepadUpdateComplete = 0x65A;
}

/// <summary>SDL_hid_device_info (x64 layout).</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HidDeviceInfo
{
    public IntPtr Path;
    public ushort VendorId;
    public ushort ProductId;
    public IntPtr SerialNumber;
    public ushort ReleaseNumber;
    public IntPtr ManufacturerString;
    public IntPtr ProductString;
    public ushort UsagePage;
    public ushort Usage;
    public int InterfaceNumber;
    public int InterfaceClass;
    public int InterfaceSubclass;
    public int InterfaceProtocol;
    public int BusType;
    public HidDeviceInfo* Next;
}

/// <summary>SDL_Event is a 128-byte union. Device events share this header: type, reserved, timestamp, which.</summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct SdlEvent
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(8)] public ulong TimestampNs;
    [FieldOffset(16)] public uint Which;
}
