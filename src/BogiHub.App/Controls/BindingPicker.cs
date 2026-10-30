using Avalonia.Controls;
using Avalonia.Layout;
using BogiHub.Core.Output;
using BogiHub.Core.Profiles;

namespace BogiHub.App.Controls;

/// <summary>Two linked dropdowns: what kind of output (Xbox / key / mouse / none) and which one.</summary>
public sealed class BindingPicker : StackPanel
{
    private static readonly (BindingKind Kind, string Label)[] Kinds =
    [
        (BindingKind.None, "Nothing"),
        (BindingKind.Gamepad, "Xbox controller"),
        (BindingKind.Key, "Keyboard key"),
        (BindingKind.Mouse, "Mouse"),
    ];

    private readonly ComboBox _kind = new() { Width = 150 };
    private readonly ComboBox _value = new() { Width = 150 };
    private readonly Action<Binding> _changed;
    private readonly List<BindingKind> _kinds;
    private Binding _binding;
    private bool _updating;

    public BindingPicker(Binding binding, bool allowGamepad, Action<Binding> changed)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        _binding = binding;
        _changed = changed;
        _kinds = Kinds.Select(k => k.Kind).Where(k => allowGamepad || k != BindingKind.Gamepad).ToList();
        if (!allowGamepad && binding.Kind == BindingKind.Gamepad) _binding = Binding.None;

        _kind.ItemsSource = _kinds.Select(k => Kinds.First(x => x.Kind == k).Label).ToList();
        _kind.SelectedIndex = Math.Max(0, _kinds.IndexOf(_binding.Kind));
        _kind.SelectionChanged += (_, _) =>
        {
            if (_updating || _kind.SelectedIndex < 0) return;
            var kind = _kinds[_kind.SelectedIndex];
            _binding = kind switch
            {
                BindingKind.Gamepad => Binding.Pad(GamepadButton.A),
                BindingKind.Key => Binding.Keyboard(Key.Space),
                BindingKind.Mouse => Binding.MouseButton(MouseAction.Left),
                _ => Binding.None,
            };
            FillValues();
            _changed(_binding);
        };
        _value.SelectionChanged += (_, _) =>
        {
            if (_updating || _value.SelectedIndex < 0) return;
            _binding = _binding.Kind switch
            {
                BindingKind.Gamepad => Binding.Pad(Enum.GetValues<GamepadButton>()[_value.SelectedIndex]),
                BindingKind.Key => Binding.Keyboard(Keys[_value.SelectedIndex]),
                BindingKind.Mouse => Binding.MouseButton(Enum.GetValues<MouseAction>()[_value.SelectedIndex]),
                _ => Binding.None,
            };
            _changed(_binding);
        };

        Children.Add(_kind);
        Children.Add(_value);
        FillValues();
    }

    private static readonly Key[] Keys = Enum.GetValues<Key>();

    private void FillValues()
    {
        _updating = true;
        switch (_binding.Kind)
        {
            case BindingKind.Gamepad:
                _value.ItemsSource = Enum.GetValues<GamepadButton>().Select(g => g.ToString()).ToList();
                _value.SelectedIndex = Array.IndexOf(Enum.GetValues<GamepadButton>(), _binding.Gamepad);
                break;
            case BindingKind.Key:
                _value.ItemsSource = Keys.Select(k => k.ToString()).ToList();
                _value.SelectedIndex = Array.IndexOf(Keys, _binding.Key);
                break;
            case BindingKind.Mouse:
                _value.ItemsSource = Enum.GetValues<MouseAction>().Select(m => Binding.MouseButton(m).Label).ToList();
                _value.SelectedIndex = Array.IndexOf(Enum.GetValues<MouseAction>(), _binding.Mouse);
                break;
        }
        _value.IsVisible = _binding.Kind != BindingKind.None;
        _updating = false;
    }
}
