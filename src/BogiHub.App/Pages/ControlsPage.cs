using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BogiHub.App.Controls;
using BogiHub.App.Theme;
using BogiHub.Core.Input;
using BogiHub.Core.Mapping;
using BogiHub.Core.Output;
using BogiHub.Core.Profiles;
using BogiHub.Runtime;

namespace BogiHub.App.Pages;

/// <summary>
/// Mapping editor. Click a part of the hologram (or pick it from the list) to edit it.
/// A local copy of the mapping engine previews the edited profile live; nothing reaches games until Save.
/// </summary>
public sealed class ControlsPage : UserControl, IPage
{
    private readonly HubRuntime _runtime;
    private readonly HologramView _holo;
    private readonly ComboBox _profilePicker = new() { Width = 220 };
    private readonly ComboBox _outputPicker = new() { Width = 190 };
    private readonly ComboBox _elementPicker = new() { Width = 260 };
    private readonly ContentControl _editorHost = new();
    private readonly TextBlock _dirty = Ui.Text("", "muted");
    private readonly Border _errors;
    private readonly TextBlock _errorText = Ui.Text("", "muted");
    private readonly GamepadOutputView _padPreview = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _kbmPreview = Ui.Text("", "mono");
    private readonly TextBlock _physical = Ui.Text("", "mono");
    private StickZoneView? _zone;
    private bool _zoneLeft;

    private List<Profile> _profiles = [];
    private Profile _editing;
    private MappingEngine _preview;
    private readonly OutputFrame _previewFrame = new();
    private readonly DispatcherTimer _previewTimer;
    private DateTime _lastPreview = DateTime.UtcNow;
    private string _selected = "A";
    private bool _isDirty, _suppress;

    private static readonly OutputMode[] Modes = [OutputMode.Gamepad, OutputMode.KeyboardMouse, OutputMode.Hybrid];

    public ControlsPage(HubRuntime runtime)
    {
        _runtime = runtime;
        var start = runtime.GetStatus().Decision.Profile
                    ?? runtime.Profiles.FirstOrDefault(p => p.Id == runtime.Settings.DefaultProfileId)
                    ?? runtime.Profiles.First();
        _editing = start.Clone();
        _preview = new MappingEngine(_editing);

        _holo = new HologramView
        {
            Interactive = true,
            StateSource = () => runtime.ControllerSnapshot,
            ConnectedSource = () => runtime.ControllerConnected,
            LabelFor = LabelFor,
            SelectedId = _selected,
        };
        _holo.ElementClicked += Select;

        _errors = new Border
        {
            Background = Palette.Brush(Palette.Warn, 0.08),
            BorderBrush = Palette.Brush(Palette.Warn, 0.35),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            IsVisible = false,
            Child = _errorText,
        };

        // ---- Top bar ------------------------------------------------------------------------
        _profilePicker.SelectionChanged += (_, _) =>
        {
            if (_suppress || _profilePicker.SelectedIndex < 0) return;
            Load(_profiles[_profilePicker.SelectedIndex]);
        };
        _outputPicker.ItemsSource = new[] { "Xbox controller", "Keyboard & mouse", "Hybrid (both)" };
        _outputPicker.SelectionChanged += (_, _) =>
        {
            if (_suppress || _outputPicker.SelectedIndex < 0) return;
            _editing.Output = Modes[_outputPicker.SelectedIndex];
            Changed(rebuildEditor: true);
        };
        ToolTip.SetTip(_outputPicker, "Xbox controller: games see a gamepad.\nKeyboard & mouse: games see keys and mouse.\nHybrid: both at once (some games handle this poorly).");

        var save = Ui.Button("Save", Save, "primary");
        var revert = Ui.Button("Revert", () => Load(_profiles.FirstOrDefault(p => p.Id == _editing.Id) ?? _editing), "ghost");
        var defaults = Ui.Button("Restore defaults", RestoreDefaults, "ghost");
        ToolTip.SetTip(defaults, "Built-in profiles go back to their original layout. Your own profiles get the standard Controller layout (name and games are kept).");

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        top.Children.Add(Ui.Row(10, Ui.Text("Editing", "muted"), _profilePicker, Ui.Text("Output", "muted"), _outputPicker));
        var actions = Ui.Row(8, _dirty, defaults, revert, save);
        Grid.SetColumn(actions, 2);
        top.Children.Add(actions);

        // ---- Left: hologram + preview -------------------------------------------------------
        var showAll = Ui.Row(10, Ui.Toggle(false, v => _holo.ShowAllLabels = v), Ui.Text("Show all bindings"));

        var previewCard = Ui.Card(Ui.Stack(10,
            new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                Children =
                {
                    Ui.Stack(6, Ui.Text("PHYSICAL INPUT", "eyebrow"), _physical),
                    WithColumn(Ui.Stack(6, Ui.Text("GENERATED OUTPUT · preview of this profile, not sent to games", "eyebrow"), _padPreview, _kbmPreview), 1),
                },
            }));

        var left = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto") };
        left.Children.Add(_holo);
        showAll.Margin = new Thickness(0, 6);
        Grid.SetRow(showAll, 1);
        left.Children.Add(showAll);
        Grid.SetRow(previewCard, 2);
        left.Children.Add(previewCard);

        // ---- Right: editor ------------------------------------------------------------------
        _elementPicker.ItemsSource = HologramView.ElementIds.Select(DisplayName).ToList();
        _elementPicker.SelectionChanged += (_, _) =>
        {
            if (_suppress || _elementPicker.SelectedIndex < 0) return;
            Select(HologramView.ElementIds[_elementPicker.SelectedIndex]);
        };

        var right = new ScrollViewer
        {
            Content = Ui.Stack(14, Ui.Card(Ui.Stack(12, _elementPicker, _editorHost), "Selected control"), _errors),
            Padding = new Thickness(0, 0, 6, 0),
        };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,400"), Margin = new Thickness(0, 18, 0, 0) };
        body.Children.Add(left);
        Grid.SetColumn(right, 2);
        body.Children.Add(right);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(top);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Content = root;

        runtime.ProfilesChanged += () => Dispatcher.UIThread.Post(ReloadProfileList);
        ReloadProfileList();
        Load(start);

        _previewTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => TickPreview());
        AttachedToVisualTree += (_, _) => _previewTimer.Start();
        DetachedFromVisualTree += (_, _) => _previewTimer.Stop();
    }

    private static Control WithColumn(Control c, int col)
    {
        Grid.SetColumn(c, col);
        return c;
    }

    /// <summary>Opens the editor on a specific profile (used by the Profiles page).</summary>
    public void Edit(string profileId)
    {
        var p = _profiles.FirstOrDefault(x => x.Id == profileId);
        if (p is not null) Load(p);
    }

    public void Refresh(RuntimeStatus status) { }

    // ---- Profile handling --------------------------------------------------------------------

    private void ReloadProfileList()
    {
        _profiles = _runtime.Profiles.ToList();
        _suppress = true;
        _profilePicker.ItemsSource = _profiles.Select(p => p.Name).ToList();
        _profilePicker.SelectedIndex = Math.Max(0, _profiles.FindIndex(p => p.Id == _editing.Id));
        _suppress = false;
    }

    private void Load(Profile p)
    {
        _editing = p.Clone();
        _preview = new MappingEngine(_editing);
        _isDirty = false;
        _suppress = true;
        _profilePicker.SelectedIndex = Math.Max(0, _profiles.FindIndex(x => x.Id == p.Id));
        _outputPicker.SelectedIndex = Array.IndexOf(Modes, _editing.Output);
        _suppress = false;
        UpdateDirty();
        _errors.IsVisible = false;
        Select(_selected);
    }

    private void Changed(bool rebuildEditor = false)
    {
        _isDirty = true;
        _preview.SetProfile(_editing);
        UpdateDirty();
        var problems = ProfileValidator.Validate(_editing);
        _errors.IsVisible = problems.Count > 0;
        _errorText.Text = string.Join("\n", problems);
        if (rebuildEditor) Select(_selected);
    }

    private void UpdateDirty() => _dirty.Text = _isDirty ? "Unsaved changes" : "";

    private void Save()
    {
        var problems = ProfileValidator.Validate(_editing);
        if (problems.Count > 0)
        {
            _errors.IsVisible = true;
            _errorText.Text = "Can't save yet:\n" + string.Join("\n", problems);
            return;
        }
        _runtime.SaveProfile(_editing);
        _isDirty = false;
        UpdateDirty();
    }

    private void RestoreDefaults()
    {
        var builtIn = DefaultProfiles.ById(_editing.Id);
        var fresh = builtIn ?? DefaultProfiles.Controller();
        if (builtIn is null)
        {
            fresh.Id = _editing.Id;
            fresh.Name = _editing.Name;
            fresh.Executables = [.. _editing.Executables];
        }
        _editing = fresh;
        _preview.SetProfile(_editing);
        _suppress = true;
        _outputPicker.SelectedIndex = Array.IndexOf(Modes, _editing.Output);
        _suppress = false;
        Changed(rebuildEditor: true);
    }

    // ---- Selection & editors -----------------------------------------------------------------

    private void Select(string id)
    {
        _selected = id;
        _holo.SelectedId = id;
        _suppress = true;
        _elementPicker.SelectedIndex = HologramView.ElementIds.ToList().IndexOf(id);
        _suppress = false;
        _zone = null;
        _editorHost.Content = BuildEditor(id);
        Ui.Enter((Control)_editorHost.Content!);
    }

    private bool AllowGamepad => _editing.Output != OutputMode.KeyboardMouse;

    private Control BuildEditor(string id)
    {
        if (Enum.TryParse<PhysicalInput>(id, out var input))
        {
            return Ui.Stack(12,
                Ui.Text(PhysicalInputs.DisplayName(input), "h2"),
                Ui.Text(Hint(input), "muted"),
                new BindingPicker(_editing.GetBinding(input), AllowGamepad, b => SetButton(input, b)));
        }

        return id switch
        {
            "LeftStick" => StickEditor(_editing.LeftStick, left: true),
            "RightStick" => StickEditor(_editing.RightStick, left: false),
            "LT" => TriggerEditor(_editing.LeftTrigger, "Left trigger (LT)"),
            "RT" => TriggerEditor(_editing.RightTrigger, "Right trigger (RT)"),
            "LeftPad" => PadEditor(_editing.LeftPad, "Left trackpad", PhysicalInput.LeftPadClick),
            "RightPad" => PadEditor(_editing.RightPad, "Right trackpad", PhysicalInput.RightPadClick),
            "Gyro" => GyroEditor(),
            _ => Ui.Text("Nothing to edit here."),
        };
    }

    private void SetButton(PhysicalInput input, Binding b)
    {
        if (b.IsNone) _editing.Buttons.Remove(input);
        else _editing.Buttons[input] = b;
        Changed();
    }

    private static string Hint(PhysicalInput input) => input switch
    {
        PhysicalInput.LeftPaddle1 or PhysicalInput.LeftPaddle2 or PhysicalInput.RightPaddle1 or PhysicalInput.RightPaddle2 =>
            "Rear grip button. Press it now: the matching paddle lights up on the hologram, so you can tell 1 from 2.",
        PhysicalInput.LeftGripTouch or PhysicalInput.RightGripTouch =>
            "Capacitive sensor: active while your hand rests on the grip. Handy as a gyro on/off switch.",
        PhysicalInput.Qam => "The \"…\" Quick Access button.",
        PhysicalInput.Steam => "Steam button. Mapped to the Xbox Guide button by default.",
        _ => "Choose what this control sends.",
    };

    private Control StickEditor(StickConfig c, bool left)
    {
        var modes = new[] { StickMode.None, StickMode.LeftStick, StickMode.RightStick, StickMode.Keys, StickMode.Mouse }
            .Where(m => AllowGamepad || m is not (StickMode.LeftStick or StickMode.RightStick)).ToArray();
        var panel = Ui.Stack(12, Ui.Text(left ? "Left stick" : "Right stick", "h2"));

        panel.Children.Add(Ui.SettingRow("Acts as", null, Ui.Combo(modes, c.Mode, m => m switch
        {
            StickMode.LeftStick => "Xbox left stick",
            StickMode.RightStick => "Xbox right stick",
            StickMode.Keys => "Keys (e.g. WASD)",
            StickMode.Mouse => "Mouse",
            _ => "Nothing",
        }, m => { c.Mode = m; Changed(rebuildEditor: true); }, 170)));

        if (c.Mode != StickMode.None)
        {
            _zone = new StickZoneView { HorizontalAlignment = HorizontalAlignment.Center };
            _zoneLeft = left;
            panel.Children.Add(Ui.Stack(4, _zone, Ui.Text("Hollow ring: where the stick physically is. Solid dot: what is sent after deadzones.", "muted")));
            panel.Children.Add(Ui.SettingRow("Deadzone", "Ignore small movements near the centre", Ui.Slider(0, 0.5, c.Deadzone, v => $"{v * 100:0}%", v => { c.Deadzone = (float)v; Changed(); }, 140)));
            panel.Children.Add(Ui.SettingRow("Outer deadzone", "Reach full output a little early", Ui.Slider(0, 0.3, c.OuterDeadzone, v => $"{v * 100:0}%", v => { c.OuterDeadzone = (float)v; Changed(); }, 140)));
            panel.Children.Add(Ui.SettingRow("Sensitivity", null, Ui.Slider(0.1, 3, c.Sensitivity, v => $"{v:0.00}×", v => { c.Sensitivity = (float)v; Changed(); }, 140)));
            panel.Children.Add(Ui.SettingRow("Invert vertical", null, Ui.Toggle(c.InvertY, v => { c.InvertY = v; Changed(); })));
        }
        if (c.Mode == StickMode.Keys)
        {
            panel.Children.Add(Ui.SettingRow("Press at", "How far to push before a key goes down", Ui.Slider(0.1, 0.9, c.KeyThreshold, v => $"{v * 100:0}%", v => { c.KeyThreshold = (float)v; Changed(); }, 140)));
            panel.Children.Add(Ui.Text("Up", "muted"));
            panel.Children.Add(new BindingPicker(c.Up, AllowGamepad, b => { c.Up = b; Changed(); }));
            panel.Children.Add(Ui.Text("Down", "muted"));
            panel.Children.Add(new BindingPicker(c.Down, AllowGamepad, b => { c.Down = b; Changed(); }));
            panel.Children.Add(Ui.Text("Left", "muted"));
            panel.Children.Add(new BindingPicker(c.Left, AllowGamepad, b => { c.Left = b; Changed(); }));
            panel.Children.Add(Ui.Text("Right", "muted"));
            panel.Children.Add(new BindingPicker(c.Right, AllowGamepad, b => { c.Right = b; Changed(); }));
        }
        if (c.Mode == StickMode.Mouse)
            panel.Children.Add(Ui.SettingRow("Cursor speed", "Pixels per second at full tilt", Ui.Slider(100, 5000, c.MouseSpeed, v => $"{v:0}", v => { c.MouseSpeed = (float)v; Changed(); }, 140)));

        var click = left ? PhysicalInput.LeftStickClick : PhysicalInput.RightStickClick;
        var touch = left ? PhysicalInput.LeftStickTouch : PhysicalInput.RightStickTouch;
        panel.Children.Add(Ui.Divider());
        panel.Children.Add(Ui.Text("Click (press the stick in)", "muted"));
        panel.Children.Add(new BindingPicker(_editing.GetBinding(click), AllowGamepad, b => SetButton(click, b)));
        panel.Children.Add(Ui.Text("Touch (thumb resting on the stick)", "muted"));
        panel.Children.Add(new BindingPicker(_editing.GetBinding(touch), AllowGamepad, b => SetButton(touch, b)));
        return panel;
    }

    private Control TriggerEditor(TriggerConfig c, string title)
    {
        var modes = new[] { TriggerMode.None, TriggerMode.LeftTrigger, TriggerMode.RightTrigger, TriggerMode.Button }
            .Where(m => AllowGamepad || m is not (TriggerMode.LeftTrigger or TriggerMode.RightTrigger)).ToArray();
        var panel = Ui.Stack(12, Ui.Text(title, "h2"));
        panel.Children.Add(Ui.SettingRow("Acts as", null, Ui.Combo(modes, c.Mode, m => m switch
        {
            TriggerMode.LeftTrigger => "Xbox LT (analog)",
            TriggerMode.RightTrigger => "Xbox RT (analog)",
            TriggerMode.Button => "A button / key",
            _ => "Nothing",
        }, m => { c.Mode = m; Changed(rebuildEditor: true); }, 170)));

        if (c.Mode is TriggerMode.LeftTrigger or TriggerMode.RightTrigger)
            panel.Children.Add(Ui.SettingRow("Deadzone", "Ignore light resting pressure", Ui.Slider(0, 0.4, c.Deadzone, v => $"{v * 100:0}%", v => { c.Deadzone = (float)v; Changed(); }, 140)));
        if (c.Mode == TriggerMode.Button)
        {
            panel.Children.Add(Ui.SettingRow("Fires at", "How far to pull before it counts as pressed", Ui.Slider(0.05, 1, c.Threshold, v => $"{v * 100:0}%", v => { c.Threshold = (float)v; Changed(); }, 140)));
            panel.Children.Add(new BindingPicker(c.Binding, AllowGamepad, b => { c.Binding = b; Changed(); }));
        }
        return panel;
    }

    private Control PadEditor(TrackpadConfig c, string title, PhysicalInput click)
    {
        var modes = new[] { PadMode.None, PadMode.Mouse, PadMode.Scroll, PadMode.Directional, PadMode.LeftStick, PadMode.RightStick }
            .Where(m => AllowGamepad || m is not (PadMode.LeftStick or PadMode.RightStick)).ToArray();
        var panel = Ui.Stack(12, Ui.Text(title, "h2"));
        panel.Children.Add(Ui.SettingRow("Acts as", null, Ui.Combo(modes, c.Mode, m => m switch
        {
            PadMode.Mouse => "Mouse (trackball)",
            PadMode.Scroll => "Scroll wheel",
            PadMode.Directional => "D-pad / 4 buttons",
            PadMode.LeftStick => "Xbox left stick",
            PadMode.RightStick => "Xbox right stick",
            _ => "Nothing",
        }, m => { c.Mode = m; Changed(rebuildEditor: true); }, 170)));

        if (c.Mode != PadMode.None)
            panel.Children.Add(Ui.SettingRow("Sensitivity", null, Ui.Slider(0.1, 4, c.Sensitivity, v => $"{v:0.00}×", v => { c.Sensitivity = (float)v; Changed(); }, 140)));

        if (c.Mode == PadMode.Mouse)
        {
            panel.Children.Add(Ui.SettingRow("Smoothing", "Steadier cursor, slightly softer response", Ui.Slider(0, 0.9, c.Smoothing, v => $"{v * 100:0}%", v => { c.Smoothing = (float)v; Changed(); }, 140)));
            panel.Children.Add(Ui.SettingRow("Trackball glide", "Keep moving after a flick", Ui.Toggle(c.Inertia, v => { c.Inertia = v; Changed(rebuildEditor: true); })));
            if (c.Inertia)
                panel.Children.Add(Ui.SettingRow("Glide friction", "Higher stops sooner", Ui.Slider(0.3, 0.99, c.Friction, v => $"{v * 100:0}%", v => { c.Friction = (float)v; Changed(); }, 140)));
        }
        if (c.Mode is PadMode.Directional or PadMode.LeftStick or PadMode.RightStick)
        {
            panel.Children.Add(Ui.SettingRow("Centre deadzone", null, Ui.Slider(0, 0.6, c.Deadzone, v => $"{v * 100:0}%", v => { c.Deadzone = (float)v; Changed(); }, 140)));
            panel.Children.Add(Ui.SettingRow("Only while clicked", "Touching alone does nothing; press the pad in", Ui.Toggle(c.RequireClick, v => { c.RequireClick = v; Changed(); })));
        }
        if (c.Mode is PadMode.Mouse or PadMode.Scroll or PadMode.Directional)
            panel.Children.Add(Ui.SettingRow("Haptics", c.Mode switch
            {
                PadMode.Mouse => "Ticks under your thumb as the cursor rolls, like a trackball",
                PadMode.Scroll => "A tick for every notch of the wheel",
                _ => "A click when you slide onto a new direction",
            }, Ui.Toggle(c.Haptics, v => { c.Haptics = v; Changed(); })));
        if (c.Mode == PadMode.Directional)
        {
            foreach (var (label, get, set) in new (string, Func<Binding>, Action<Binding>)[]
            {
                ("Up", () => c.Up, b => c.Up = b), ("Down", () => c.Down, b => c.Down = b),
                ("Left", () => c.Left, b => c.Left = b), ("Right", () => c.Right, b => c.Right = b),
            })
            {
                panel.Children.Add(Ui.Text(label, "muted"));
                panel.Children.Add(new BindingPicker(get(), AllowGamepad, b => { set(b); Changed(); }));
            }
        }

        panel.Children.Add(Ui.Divider());
        panel.Children.Add(Ui.Text("Click (press the pad in)", "muted"));
        panel.Children.Add(new BindingPicker(_editing.GetBinding(click), AllowGamepad, b => SetButton(click, b)));
        return panel;
    }

    private Control GyroEditor()
    {
        var g = _editing.Gyro;
        var modes = new[] { GyroMode.Off, GyroMode.Mouse, GyroMode.RightStick }.Where(m => AllowGamepad || m != GyroMode.RightStick).ToArray();
        var panel = Ui.Stack(12, Ui.Text("Gyro (motion aiming)", "h2"),
            Ui.Text("Calibrates itself: set the controller down or hold it still for a second and any drift is learned away.", "muted"));
        panel.Children.Add(Ui.SettingRow("Acts as", null, Ui.Combo(modes, g.Mode, m => m switch
        {
            GyroMode.Mouse => "Mouse",
            GyroMode.RightStick => "Xbox right stick",
            _ => "Off",
        }, m => { g.Mode = m; Changed(rebuildEditor: true); }, 170)));
        if (g.Mode == GyroMode.Off) return panel;

        panel.Children.Add(Ui.SettingRow("Turn with", null, Ui.Combo(
            [GyroTurnAxis.PlayerSpace, GyroTurnAxis.Yaw, GyroTurnAxis.Roll], g.TurnAxis, a => a switch
            {
                GyroTurnAxis.Yaw => "Yaw (twist it flat)",
                GyroTurnAxis.Roll => "Roll (tilt like a wheel)",
                _ => "Either (recommended)",
            }, a => { g.TurnAxis = a; Changed(); }, 200)));
        panel.Children.Add(Ui.SettingRow("Sensitivity", null, Ui.Slider(0.1, 6, g.Sensitivity, v => $"{v:0.00}×", v => { g.Sensitivity = (float)v; Changed(); }, 140)));
        panel.Children.Add(Ui.SettingRow("Up and down speed", null, Ui.Slider(0.25, 2, g.VerticalScale, v => $"{v * 100:0}%", v => { g.VerticalScale = (float)v; Changed(); }, 140)));
        panel.Children.Add(Ui.SettingRow("Fine aim", "Slows small movements only", Ui.Slider(0, 0.9, g.Precision, v => v < 0.005 ? "Off" : $"{v * 100:0}%", v => { g.Precision = (float)v; Changed(); }, 140)));
        panel.Children.Add(Ui.SettingRow("Steady hands", "Calms tiny shakes only", Ui.Slider(0, 1, g.Smoothing, v => v < 0.005 ? "Off" : $"{v * 100:0}%", v => { g.Smoothing = (float)v; Changed(); }, 140)));
        panel.Children.Add(Ui.SettingRow("Invert horizontal", null, Ui.Toggle(g.InvertX, v => { g.InvertX = v; Changed(); })));
        panel.Children.Add(Ui.SettingRow("Invert vertical", null, Ui.Toggle(g.InvertY, v => { g.InvertY = v; Changed(); })));

        var holds = new PhysicalInput?[] { null, PhysicalInput.RightGripTouch, PhysicalInput.LeftGripTouch, PhysicalInput.RightStickTouch, PhysicalInput.RightPaddle1, PhysicalInput.LeftPaddle1 };
        string HoldName(PhysicalInput? h) => h is null ? "Never" : "While " + PhysicalInputs.DisplayName(h.Value).ToLowerInvariant();
        panel.Children.Add(Ui.SettingRow("Only while", "Gyro works only while you hold or touch this", Ui.Combo(holds, g.EnableWhile, h => h is null ? "Always on" : HoldName(h),
            h => { g.EnableWhile = h; Changed(); }, 200)));
        panel.Children.Add(Ui.SettingRow("Pause while", "Gyro stops while you hold or touch this", Ui.Combo(holds, g.DisableWhile, HoldName,
            h => { g.DisableWhile = h; Changed(); }, 200)));
        return panel;
    }

    // ---- Labels & preview --------------------------------------------------------------------

    private string? LabelFor(string id)
    {
        if (Enum.TryParse<PhysicalInput>(id, out var input))
        {
            var b = _editing.GetBinding(input);
            return b.IsNone ? null : b.Label;
        }
        return id switch
        {
            "LeftStick" => StickLabel(_editing.LeftStick),
            "RightStick" => StickLabel(_editing.RightStick),
            "LT" => TriggerLabel(_editing.LeftTrigger),
            "RT" => TriggerLabel(_editing.RightTrigger),
            "LeftPad" => _editing.LeftPad.Mode == PadMode.None ? null : _editing.LeftPad.Mode.ToString(),
            "RightPad" => _editing.RightPad.Mode == PadMode.None ? null : _editing.RightPad.Mode.ToString(),
            "Gyro" => _editing.Gyro.Mode == GyroMode.Off ? null : $"Gyro → {_editing.Gyro.Mode}",
            _ => null,
        };
    }

    private static string? StickLabel(StickConfig c) => c.Mode switch
    {
        StickMode.LeftStick => "Left stick",
        StickMode.RightStick => "Right stick",
        StickMode.Keys => $"{c.Up.Label}{c.Left.Label}{c.Down.Label}{c.Right.Label}",
        StickMode.Mouse => "Mouse",
        _ => null,
    };

    private static string? TriggerLabel(TriggerConfig c) => c.Mode switch
    {
        TriggerMode.LeftTrigger => "LT",
        TriggerMode.RightTrigger => "RT",
        TriggerMode.Button => c.Binding.Label,
        _ => null,
    };

    private static string DisplayName(string id) =>
        Enum.TryParse<PhysicalInput>(id, out var input) ? PhysicalInputs.DisplayName(input) : id switch
        {
            "LeftStick" => "Left stick",
            "RightStick" => "Right stick",
            "LT" => "Left trigger (LT)",
            "RT" => "Right trigger (RT)",
            "LeftPad" => "Left trackpad",
            "RightPad" => "Right trackpad",
            "Gyro" => "Gyro",
            _ => id,
        };

    private void TickPreview()
    {
        if (Glass.Paused) return;
        var now = DateTime.UtcNow;
        float dt = (float)(now - _lastPreview).TotalSeconds;
        _lastPreview = now;

        bool connected = _runtime.ControllerConnected;
        var s = connected ? _runtime.ControllerSnapshot : default;
        _preview.GamepadOnly = _runtime.GamepadOnly; // preview matches what Gamepad mode really sends
        _preview.Map(in s, dt, _previewFrame);
        _padPreview.Update(_previewFrame.Pad, connected && _previewFrame.UsesGamepad);

        var parts = new List<string>();
        for (int vk = 1; vk < 256; vk++) if (_previewFrame.Keys[vk]) parts.Add(((Key)vk).ToString());
        if (_previewFrame.Mouse != MouseButtons.None) parts.Add($"Mouse {_previewFrame.Mouse}");
        if (Math.Abs(_previewFrame.MouseDx) + Math.Abs(_previewFrame.MouseDy) > 0.01) parts.Add("Cursor moving");
        if (_previewFrame.WheelNotches != 0) parts.Add($"Wheel {_previewFrame.WheelNotches:+0;-0}");
        _kbmPreview.Text = !_previewFrame.UsesKeyboardMouse ? "Keyboard & mouse: not used by this profile"
            : parts.Count == 0 ? "Keyboard & mouse: nothing held" : "Keyboard & mouse: " + string.Join(" · ", parts);

        var pressed = PhysicalInputs.All.Where(i => s.IsPressed(i)).Select(i => i.ToString()).ToList();
        _physical.Text = !connected ? "No controller connected" :
            $"Buttons: {(pressed.Count == 0 ? "none" : string.Join(", ", pressed))}\n" +
            $"Left stick  {s.LeftStick.X,5:0.00} {s.LeftStick.Y,5:0.00}   LT {s.LeftTrigger * 100,3:0}%\n" +
            $"Right stick {s.RightStick.X,5:0.00} {s.RightStick.Y,5:0.00}   RT {s.RightTrigger * 100,3:0}%\n" +
            $"Pads  L {(s.LeftPad.Touching ? $"{s.LeftPad.X:0.00},{s.LeftPad.Y:0.00}" : "—")}   R {(s.RightPad.Touching ? $"{s.RightPad.X:0.00},{s.RightPad.Y:0.00}" : "—")}";

        if (_zone is not null)
        {
            var c = _zoneLeft ? _editing.LeftStick : _editing.RightStick;
            var raw = _zoneLeft ? s.LeftStick : s.RightStick;
            _zone.Update(raw, Deadzone.Radial(raw, c.Deadzone, c.OuterDeadzone), c.Deadzone, c.OuterDeadzone);
        }
    }
}
