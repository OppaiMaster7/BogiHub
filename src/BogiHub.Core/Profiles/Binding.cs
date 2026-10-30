using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using BogiHub.Core.Output;

namespace BogiHub.Core.Profiles;

/// <summary>Digital Xbox outputs a physical control can be bound to. LT/RT here mean "fully pulled".</summary>
public enum GamepadButton
{
    A, B, X, Y, LB, RB, Back, Start, Guide, LeftStick, RightStick, DpadUp, DpadDown, DpadLeft, DpadRight, LT, RT,
}

public enum MouseAction { Left, Right, Middle, X1, X2, WheelUp, WheelDown }

public enum BindingKind { None, Gamepad, Key, Mouse }

/// <summary>
/// One output target. Serialised as a short readable string: "none", "gamepad:A", "key:Space", "mouse:Left".
/// </summary>
[JsonConverter(typeof(BindingJsonConverter))]
public readonly record struct Binding(BindingKind Kind, GamepadButton Gamepad = default, Key Key = default, MouseAction Mouse = default)
{
    public static readonly Binding None = new(BindingKind.None);
    public static Binding Pad(GamepadButton b) => new(BindingKind.Gamepad, Gamepad: b);
    public static Binding Keyboard(Key k) => new(BindingKind.Key, Key: k);
    public static Binding MouseButton(MouseAction m) => new(BindingKind.Mouse, Mouse: m);

    public bool IsNone => Kind == BindingKind.None;

    public override string ToString() => Kind switch
    {
        BindingKind.Gamepad => $"gamepad:{Gamepad}",
        BindingKind.Key => $"key:{Key}",
        BindingKind.Mouse => $"mouse:{Mouse}",
        _ => "none",
    };

    /// <summary>Short label for the UI, e.g. "A", "Space", "Left click".</summary>
    public string Label => Kind switch
    {
        BindingKind.Gamepad => Gamepad.ToString(),
        BindingKind.Key => Key.ToString(),
        BindingKind.Mouse => Mouse switch
        {
            MouseAction.Left => "Left click",
            MouseAction.Right => "Right click",
            MouseAction.Middle => "Middle click",
            MouseAction.WheelUp => "Scroll up",
            MouseAction.WheelDown => "Scroll down",
            _ => Mouse.ToString(),
        },
        _ => "—",
    };

    public static bool TryParse(string? text, [NotNullWhen(true)] out Binding? binding)
    {
        binding = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            binding = None;
            return true;
        }

        int colon = text.IndexOf(':');
        if (colon <= 0) return false;
        string kind = text[..colon], value = text[(colon + 1)..];

        switch (kind.ToLowerInvariant())
        {
            case "gamepad" when Enum.TryParse(value, true, out GamepadButton g) && Enum.IsDefined(g):
                binding = Pad(g);
                return true;
            case "key" when Enum.TryParse(value, true, out Key k) && Enum.IsDefined(k) && !int.TryParse(value, out _):
                binding = Keyboard(k);
                return true;
            case "mouse" when Enum.TryParse(value, true, out MouseAction m) && Enum.IsDefined(m):
                binding = MouseButton(m);
                return true;
            default:
                return false;
        }
    }
}

internal sealed class BindingJsonConverter : JsonConverter<Binding>
{
    public override Binding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return Binding.None;
        string? s = reader.GetString();
        return Binding.TryParse(s, out var b) ? b.Value : throw new JsonException($"Unknown binding \"{s}\". Use none, gamepad:<button>, key:<key> or mouse:<button>.");
    }

    public override void Write(Utf8JsonWriter writer, Binding value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
