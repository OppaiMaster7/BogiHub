using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace BogiHub.App.Theme;

/// <summary>Small factory helpers so pages read as layout, not boilerplate.</summary>
public static class Ui
{
    /// <summary>Apple-style "settle" curve: quick start, long soft landing.</summary>
    public static readonly Easing Settle = new SplineEasing(0.22, 1, 0.36, 1);
    public static readonly Easing EaseOut = new CubicEaseOut();
    public static TimeSpan Fast => TimeSpan.FromMilliseconds(140);
    public static TimeSpan Medium => TimeSpan.FromMilliseconds(260);
    public static TimeSpan Slow => TimeSpan.FromMilliseconds(520);

    public static TextBlock Text(string text, string? classes = null, double? size = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (classes is not null) foreach (var c in classes.Split(' ')) t.Classes.Add(c);
        if (size is { } s) t.FontSize = s;
        return t;
    }

    public static GlassPanel Card(Control content, string? eyebrow = null) => new()
    {
        Child = eyebrow is null ? content : Stack(12, Text(eyebrow.ToUpperInvariant(), "eyebrow"), content),
    };

    public static StackPanel Stack(double spacing, params Control[] children)
    {
        var s = new StackPanel { Spacing = spacing };
        s.Children.AddRange(children);
        return s;
    }

    public static StackPanel Row(double spacing, params Control[] children)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children) c.VerticalAlignment = VerticalAlignment.Center;
        s.Children.AddRange(children);
        return s;
    }

    public static Button Button(string text, Action onClick, string? cls = null)
    {
        var b = new Button { Content = text };
        if (cls is not null) b.Classes.Add(cls);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Label on the left, any control on the right, optional description below the label.</summary>
    public static Grid SettingRow(string title, string? description, Control control)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
        var left = Stack(2, Text(title));
        left.VerticalAlignment = VerticalAlignment.Center;
        if (description is not null) left.Children.Add(Text(description, "muted"));
        g.Children.Add(left);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    public static GlassToggle Toggle(bool value, Action<bool> changed)
    {
        var t = new GlassToggle(value);
        t.Toggled += changed;
        return t;
    }

    /// <summary>Slider with a live value label. <paramref name="format"/> turns the value into display text.</summary>
    public static Control Slider(double min, double max, double value, Func<double, string> format, Action<double> changed, double width = 200)
    {
        var label = new TextBlock { Text = format(value), Width = 58, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        label.Classes.Add("mono");
        var s = new Slider { Minimum = min, Maximum = max, Value = value, Width = width, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, e) =>
        {
            label.Text = format(e.NewValue);
            changed(e.NewValue);
        };
        return Row(10, s, label);
    }

    public static ComboBox Combo<T>(IEnumerable<T> items, T selected, Func<T, string> label, Action<T> changed, double width = 200)
    {
        var list = items.ToList();
        var c = new ComboBox
        {
            ItemsSource = list.Select(label).ToList(),
            SelectedIndex = Math.Max(0, list.FindIndex(i => EqualityComparer<T>.Default.Equals(i, selected))),
            Width = width,
        };
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedIndex >= 0 && c.SelectedIndex < list.Count) changed(list[c.SelectedIndex]);
        };
        return c;
    }

    public static Border Divider() => new() { Height = 1, Background = Palette.Brush(Colors.White, 0.07), Margin = new Thickness(0, 6) };

    /// <summary>Fades content in while it rises and settles from 98.5 % scale. Optional delay for staggering.</summary>
    public static void Enter(Control c, int delayMs = 0)
    {
        c.Transitions = null;
        c.Opacity = 0;
        c.RenderTransform = TransformOperations.Parse("translateY(14px) scale(0.985)");
        c.Transitions =
        [
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(380), Easing = Settle },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Slow, Easing = Settle },
        ];
        void Show()
        {
            c.Opacity = 1;
            c.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");
        }
        if (delayMs <= 0) Dispatcher.UIThread.Post(Show, DispatcherPriority.Render);
        else DispatcherTimer.RunOnce(Show, TimeSpan.FromMilliseconds(delayMs));
    }

    /// <summary>Cards arrive one after another (45 ms apart, at most 8 steps) instead of all at once.</summary>
    public static void Stagger(Control root)
    {
        int i = 0;
        foreach (var card in root.GetLogicalDescendants().OfType<GlassPanel>())
            Enter(card, 40 + Math.Min(i++, 8) * 45);
    }
}

/// <summary>Glass capsule status label whose colour and text animate when the state changes.</summary>
public sealed class StatusPill : Border
{
    private readonly TextBlock _text = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    private Color _color;

    public StatusPill()
    {
        CornerRadius = new CornerRadius(99);
        Padding = new Thickness(11, 5);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _dot, _text } };
        Transitions =
        [
            new BrushTransition { Property = BackgroundProperty, Duration = Ui.Medium },
            new BrushTransition { Property = BorderBrushProperty, Duration = Ui.Medium },
        ];
    }

    public void Set(string text, Color color)
    {
        _text.Text = text;
        if (color == _color && Background is not null) return;
        _color = color;
        _text.Foreground = Palette.Brush(color);
        _dot.Fill = Palette.Brush(color);
        Background = Palette.Brush(color, 0.10);
        BorderBrush = Palette.Brush(color, 0.22);
        _dot.Effect = new DropShadowEffect { BlurRadius = 8, OffsetX = 0, OffsetY = 0, Color = color, Opacity = 0.8 };
    }
}

/// <summary>Segmented control with a liquid glass lens that springs between options.</summary>
public sealed class SegmentedSwitch : Border
{
    private readonly Grid _grid = new();
    private readonly LiquidLens _lens = new() { Radius = 11, Tint = 0.12 };
    private readonly List<Button> _buttons = [];
    private int _index;

    public SegmentedSwitch(IReadOnlyList<string> options, int selected, Action<int> changed)
    {
        Background = Palette.Brush(Colors.Black, 0.22);
        BorderBrush = Palette.Brush(Colors.White, 0.08);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(3);
        _index = selected;

        Grid.SetColumnSpan(_lens, options.Count);
        _grid.Children.Add(_lens);

        for (int i = 0; i < options.Count; i++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            int idx = i;
            var b = new Button
            {
                Content = options[i],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(10, 8),
                FontWeight = FontWeight.SemiBold,
            };
            b.Classes.Add("bare");
            b.Click += (_, _) =>
            {
                Select(idx);
                changed(idx);
            };
            Grid.SetColumn(b, i);
            _buttons.Add(b);
            _grid.Children.Add(b);
        }
        Child = _grid;
        _grid.SizeChanged += (_, _) => Place(instant: true);
        Select(selected);
    }

    public void Select(int index)
    {
        _index = index;
        for (int i = 0; i < _buttons.Count; i++)
            _buttons[i].Foreground = i == index ? Palette.TextBrush : Palette.MutedBrush;
        Place(instant: false); // re-sending the same target never interrupts a running animation
    }

    private void Place(bool instant)
    {
        double w = _grid.Bounds.Width / Math.Max(1, _buttons.Count);
        if (w <= 0) return;
        _lens.MoveTo(new Rect(w * _index, 0, w, _grid.Bounds.Height), instant);
    }
}
