using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using BogiHub.App.Theme;
using BogiHub.Core.Notifications;

namespace BogiHub.App;

/// <summary>
/// Small glass notices that slide in over everything, games included (borderless/windowed; exclusive fullscreen games
/// draw over every window). The window never takes focus, never shows in the taskbar or Alt+Tab, lets clicks pass
/// straight through (except over a notice that has an action, while the cursor is on it), and is hidden whenever no
/// notice is showing, so it never sits on top of a game for nothing.
/// </summary>
public sealed class NotificationOverlay : Window
{
    private const double CardWidth = 380;
    private const double Gap = 10;
    private const double Edge = 14; // room for the shadow around the cards
    private const int MaxCards = 3;
    private const int TickMs = 33;

    private readonly StackPanel _stack = new() { Spacing = Gap, Width = CardWidth };
    private readonly List<NoticeCard> _cards = [];
    private readonly DispatcherTimer _tick;
    private long _lastTick;
    private NoticePosition _position = NoticePosition.TopCenter;
    private bool _clickThrough = true;

    /// <summary>A notice with an action was clicked.</summary>
    public event Action<Notice>? ActionInvoked;

    public NotificationOverlay()
    {
        Title = "BogiHub notifications";
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Focusable = false;
        SizeToContent = SizeToContent.Manual;
        Width = CardWidth + 2 * Edge;
        Height = MaxCards * 96 + (MaxCards - 1) * Gap + 2 * Edge;
        _stack.Margin = new Thickness(Edge);
        Content = _stack;

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(TickMs), DispatcherPriority.Normal, (_, _) => OnTick());
        ApplyWindowStyles();
        Opened += (_, _) => ApplyWindowStyles();
    }

    public void Show(Notice n, NoticePosition position)
    {
        if (position != _position)
        {
            _position = position;
            foreach (var c in _cards) _stack.Children.Remove(c);
            foreach (var c in _cards) Insert(c);
            if (IsVisible) PlaceOnScreen();
        }
        _stack.VerticalAlignment = _position == NoticePosition.BottomRight ? VerticalAlignment.Bottom : VerticalAlignment.Top;

        var same = _cards.FirstOrDefault(c => c.Notice.Key == n.Key && !c.Leaving);
        if (same is not null)
        {
            same.Update(n); // morphs in place: quick alt-tabs never stack up
        }
        else
        {
            var live = _cards.Where(c => !c.Leaving).ToList();
            if (live.Count >= MaxCards) live[0].Leave(); // oldest gives way
            var card = new NoticeCard(n, _position == NoticePosition.BottomRight ? 18 : -18);
            card.Clicked += () => ActionInvoked?.Invoke(card.Notice);
            _cards.Add(card);
            Insert(card);
            card.Enter();
        }

        if (!IsVisible)
        {
            PlaceOnScreen();
            base.Show();
            ApplyWindowStyles();
        }
        KeepOnTop();
        _lastTick = Environment.TickCount64;
        _tick.Start();
    }

    /// <summary>Newest next to the screen edge the notices come from.</summary>
    private void Insert(NoticeCard c)
    {
        if (_position == NoticePosition.BottomRight) _stack.Children.Add(c);
        else _stack.Children.Insert(0, c);
    }

    private void OnTick()
    {
        long now = Environment.TickCount64;
        int dt = (int)Math.Min(now - _lastTick, 200);
        _lastTick = now;

        // Clickable only while the cursor is over a notice that has an action; everywhere else clicks go to the game.
        NoticeCard? hovered = null;
        if (GetCursorPos(out var pt))
        {
            foreach (var c in _cards)
            {
                if (c.Leaving || c.Notice.Action == NoticeAction.None) continue;
                var origin = c.PointToScreen(new Point(0, 0));
                double s = RenderScaling;
                if (pt.X >= origin.X && pt.Y >= origin.Y && pt.X < origin.X + c.Bounds.Width * s && pt.Y < origin.Y + c.Bounds.Height * s)
                    hovered = c;
            }
        }
        SetClickThrough(hovered is null);

        foreach (var c in _cards.ToList())
        {
            c.SetHover(c == hovered);
            if (c.Leaving)
            {
                if (c.LeftFor(now) > 260)
                {
                    _cards.Remove(c);
                    _stack.Children.Remove(c);
                }
            }
            else if (c != hovered && (c.RemainingMs -= dt) <= 0)
            {
                c.Leave();
            }
        }

        if (_cards.Count == 0)
        {
            _tick.Stop();
            SetClickThrough(true);
            Hide(); // nothing on top of the game any more
        }
    }

    private void PlaceOnScreen()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        var wa = screen.WorkingArea;
        double s = screen.Scaling;
        int w = (int)Math.Ceiling(Width * s), h = (int)Math.Ceiling(Height * s);
        int margin = (int)(6 * s);
        Position = _position switch
        {
            NoticePosition.TopRight => new PixelPoint(wa.Right - w - margin, wa.Y + margin),
            NoticePosition.BottomRight => new PixelPoint(wa.Right - w - margin, wa.Bottom - h - margin),
            _ => new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Y + margin),
        };
    }

    // ---- Win32: no activation, no Alt+Tab, click-through ---------------------------------------

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint LWA_ALPHA = 0x2;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private IntPtr Hwnd => TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    private void ApplyWindowStyles()
    {
        var h = Hwnd;
        if (h == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_LAYERED;
        ex = _clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        SetLayeredWindowAttributes(h, 0, 255, LWA_ALPHA); // a layered window stays invisible until this is set
    }

    private void SetClickThrough(bool on)
    {
        if (on == _clickThrough) return;
        _clickThrough = on;
        ApplyWindowStyles();
    }

    /// <summary>Borderless games are often topmost too; step in front of them again for every new notice.</summary>
    private void KeepOnTop()
    {
        var h = Hwnd;
        if (h != IntPtr.Zero) SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
}

/// <summary>One notice: tone-coloured icon badge, title, detail, optional battery gauge. Animates in, morphs, and out.</summary>
internal sealed class NoticeCard : Border
{
    private readonly Border _badge = new() { Width = 38, Height = 38, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center };
    private readonly NoticeGlyph _glyph = new() { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _detail = new() { FontSize = 12, Foreground = Palette.MutedBrush, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _hint = new() { FontSize = 11, FontWeight = FontWeight.SemiBold, Text = "Click to open BogiHub", Margin = new Thickness(0, 3, 0, 0) };
    private readonly NoticeGlyph _batteryGlyph = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _batteryText = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _battery;
    private readonly Grid _body;
    private readonly double _fromY;
    private long _leftAt;

    public NoticeCard(Notice n, double fromY)
    {
        Notice = n;
        _fromY = fromY;
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(12, 11, 16, 12);
        BorderThickness = new Thickness(1);
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Palette.WithAlpha(Palette.Raised, 0.97), 0), new GradientStop(Palette.WithAlpha(Palette.Surface, 0.97), 1) },
        };
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 18, Color = Color.FromArgb(150, 0, 0, 0) });
        _badge.Child = _glyph;

        _body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _detail, _hint } };
        _battery = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), Children = { _batteryGlyph, _batteryText } };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_battery, 2);
        _body.Children.Add(_badge);
        _body.Children.Add(text);
        _body.Children.Add(_battery);
        _body.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }];
        Child = _body;

        Opacity = 0;
        RenderTransform = TransformOperations.Parse($"translateY({fromY}px) scale(0.96)");
        Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = Ui.Settle },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(460), Easing = Ui.Settle },
            new BrushTransition { Property = BorderBrushProperty, Duration = Ui.Medium },
        ];
        Apply(n);
        PointerPressed += (_, e) =>
        {
            if (Notice.Action == NoticeAction.None || Leaving) return;
            e.Handled = true;
            Clicked?.Invoke();
            Leave();
        };
    }

    public Notice Notice { get; private set; }
    public int RemainingMs { get; set; }
    public bool Leaving { get; private set; }
    public event Action? Clicked;

    public long LeftFor(long now) => now - _leftAt;

    /// <summary>
    /// Sample notices on a dark background, rendered without any window (BOGIHUB_NOTICE_SHEET=png), for the README.
    /// </summary>
    public static void WriteSheet(string path, IEnumerable<Notice> notices)
    {
        const int scale = 2;
        var stack = new StackPanel { Spacing = 10, Width = 380, Margin = new Thickness(28) };
        foreach (var n in notices)
        {
            var card = new NoticeCard(n, 0) { Transitions = null };
            card.Opacity = 1;
            card.RenderTransform = null;
            stack.Children.Add(card);
        }
        // Scaled as layout (not via DPI) so every part of the card is drawn at the same 2x size.
        var root = new LayoutTransformControl
        {
            LayoutTransform = new ScaleTransform(scale, scale),
            Child = new Border { Background = Palette.BgBrush, Child = stack },
        };
        root.Measure(Size.Infinity);
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(root.DesiredSize.Width), (int)Math.Ceiling(root.DesiredSize.Height)), new Vector(96, 96));
        bmp.Render(root);
        bmp.Save(path);
    }

    public void Enter() => Dispatcher.UIThread.Post(() =>
    {
        Opacity = 1;
        RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");
    }, DispatcherPriority.Render);

    /// <summary>Same subject, new state: the text cross-fades and the card gives a small nudge instead of a new card.</summary>
    public void Update(Notice n)
    {
        _body.Opacity = 0;
        DispatcherTimer.RunOnce(() =>
        {
            Apply(n);
            _body.Opacity = 1;
        }, TimeSpan.FromMilliseconds(110));
        RenderTransform = TransformOperations.Parse("translateY(0px) scale(1.025)");
        DispatcherTimer.RunOnce(() => { if (!Leaving) RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)"); }, TimeSpan.FromMilliseconds(140));
        Notice = n;
        RemainingMs = n.DurationMs;
    }

    public void Leave()
    {
        if (Leaving) return;
        Leaving = true;
        _leftAt = Environment.TickCount64;
        Opacity = 0;
        RenderTransform = TransformOperations.Parse($"translateY({_fromY * 0.6}px) scale(0.97)");
    }

    public void SetHover(bool on)
    {
        var tone = ToneColor(Notice.Tone);
        BorderBrush = Palette.Brush(tone == Palette.Accent ? Colors.White : tone, on ? 0.45 : tone == Palette.Accent ? 0.10 : 0.24);
        Cursor = on ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) : null;
    }

    private void Apply(Notice n)
    {
        Notice = n;
        RemainingMs = n.DurationMs;
        var tone = ToneColor(n.Tone);
        _badge.Background = Palette.Brush(tone, tone == Palette.Accent ? 0.10 : 0.16);
        _badge.BorderBrush = Palette.Brush(tone, 0.28);
        _badge.BorderThickness = new Thickness(1);
        _glyph.Set(n.Icon, tone, n.BatteryPercent);
        _title.Text = n.Title;
        _detail.Text = n.Detail;
        _detail.IsVisible = !string.IsNullOrEmpty(n.Detail);
        _hint.IsVisible = n.Action == NoticeAction.OpenApp;

        // Battery level on the side of any notice that knows it, unless the icon already is the battery.
        bool chip = n.BatteryPercent is not null && n.Icon is not (NoticeIcon.Battery or NoticeIcon.Charging);
        _battery.IsVisible = chip;
        if (chip)
        {
            int pct = n.BatteryPercent!.Value;
            var c = pct <= 10 ? Palette.Danger : pct <= 20 ? Palette.Warn : Palette.Muted;
            _batteryGlyph.Set(NoticeIcon.Battery, c, pct);
            _batteryText.Text = $"{pct} %";
            _batteryText.Foreground = Palette.Brush(c);
        }
        _hint.Foreground = Palette.Brush(tone);
        SetHover(false);
    }

    private static Color ToneColor(NoticeTone t) => t switch
    {
        NoticeTone.Ok => Palette.Ok,
        NoticeTone.Warn => Palette.Warn,
        NoticeTone.Danger => Palette.Danger,
        _ => Palette.Accent,
    };
}

/// <summary>Notice icons (Tabler, see <see cref="Icons"/>); the battery is drawn filled to its level.</summary>
internal sealed class NoticeGlyph : Control
{
    private static string PathOf(NoticeIcon icon) => icon switch
    {
        NoticeIcon.Gamepad or NoticeIcon.Controller => Icons.Gamepad,
        NoticeIcon.Unplugged => Icons.Unplugged,
        NoticeIcon.Mouse => Icons.Mouse,
        NoticeIcon.Steam => Icons.AppWindow,
        NoticeIcon.Pause => Icons.Pause,
        NoticeIcon.Keys => Icons.Keyboard,
        NoticeIcon.Download => Icons.Download,
        NoticeIcon.Battery => Icons.Battery,
        NoticeIcon.Charging => Icons.BatteryCharging,
        _ => Icons.Warning,
    };

    private NoticeIcon _icon;
    private Color _color = Palette.Accent;
    private int? _percent;

    public void Set(NoticeIcon icon, Color color, int? percent)
    {
        _icon = icon;
        _color = color;
        _percent = percent;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (_icon == NoticeIcon.Battery && _percent is { } p)
        {
            double k = size / 24.0;
            var r = Icons.BatteryInside;
            double w = r.Width * Math.Clamp(p, 0, 100) / 100.0;
            if (w > 0.4) ctx.DrawRectangle(Palette.Brush(_color, 0.85), null, new Rect(r.X * k, r.Y * k, w * k, r.Height * k), 0.8 * k, 0.8 * k);
        }
        Icons.Draw(ctx, PathOf(_icon), default, size, Palette.Brush(_color), size <= 18 ? 1.9 : 1.8);
    }
}
