namespace BogiHub.Core.Output;

/// <summary>Keyboard keys offered for binding. Values are Windows virtual-key codes.</summary>
public enum Key : byte
{
    Backspace = 0x08, Tab = 0x09, Enter = 0x0D, Pause = 0x13, CapsLock = 0x14, Escape = 0x1B, Space = 0x20,
    PageUp = 0x21, PageDown = 0x22, End = 0x23, Home = 0x24,
    Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28,
    PrintScreen = 0x2C, Insert = 0x2D, Delete = 0x2E,
    D0 = 0x30, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    A = 0x41, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    LeftWin = 0x5B,
    Numpad0 = 0x60, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
    F1 = 0x70, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    LeftShift = 0xA0, RightShift = 0xA1, LeftCtrl = 0xA2, RightCtrl = 0xA3, LeftAlt = 0xA4, RightAlt = 0xA5,
    VolumeMute = 0xAD, VolumeDown = 0xAE, VolumeUp = 0xAF,
    MediaNext = 0xB0, MediaPrevious = 0xB1, MediaPlayPause = 0xB3,
    Semicolon = 0xBA, Equal = 0xBB, Comma = 0xBC, Minus = 0xBD, Period = 0xBE, Slash = 0xBF, Backtick = 0xC0,
    LeftBracket = 0xDB, Backslash = 0xDC, RightBracket = 0xDD, Quote = 0xDE,
}
