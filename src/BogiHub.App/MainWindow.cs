using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BogiHub.App.Pages;
using BogiHub.App.Theme;
using BogiHub.Core.Control;
using BogiHub.Runtime;

namespace BogiHub.App;

public interface IPage
{
    /// <summary>Called ~10 times a second while the page is visible. Cheap UI updates only.</summary>
    void Refresh(RuntimeStatus status);
}

public sealed class MainWindow : Window
{
    private readonly HubRuntime _runtime;
    private readonly AmbientBackdrop _backdrop = new();
    private readonly ContentControl _host = new();
    private readonly List<Button> _navButtons = [];
    private readonly Panel _navWrap = new();
    private readonly LiquidLens _navLens = new() { Radius = 14, Tint = 0.09 };
    private readonly StatusPill _railStatus = new();
    private readonly Button _railStop;
    private readonly Dictionary<string, Func<Control>> _factories;
    private readonly Dictionary<string, Control> _pages = [];
    private string _current = "";

    private static readonly (string Key, string Label, string Icon)[] Nav =
    [
        ("overview", "Overview", Icons.LayoutGrid),
        ("controls", "Controls", Icons.Gamepad),
        ("profiles", "Profiles", Icons.Stack),
        ("settings", "Settings", Icons.Adjustments),
    ];

    public MainWindow(HubRuntime runtime)
    {
        _runtime = runtime;
        Title = "BogiHub";
        Width = 1260;
        Height = 820;
        MinWidth = 1000;
        MinHeight = 660;
        Background = Palette.BgBrush;
        Icon = Icons.AppIcon();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);

        // Edge-to-edge: the backdrop runs under the (system-drawn) caption buttons.
        // No drawn title bar: the backdrop runs edge to edge and the caption buttons are our own glass buttons.
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        if (Environment.GetEnvironmentVariable("BOGIHUB_DUMP_VISUALS") is not null)
            Opened += (_, _) => Dispatcher.UIThread.Post(DumpVisuals, DispatcherPriority.Loaded);

        _backdrop.Palette = runtime.Settings.AmbientColor ? AmbientPalette.Aurora : AmbientPalette.Mono;

        _factories = new()
        {
            ["overview"] = () => new OverviewPage(runtime),
            ["controls"] = () => new ControlsPage(runtime),
            ["profiles"] = () => new ProfilesPage(runtime, this),
            ["settings"] = () => new SettingsPage(runtime, this),
        };

        _railStop = Ui.Button("Emergency stop", () => _runtime.SetEmergencyPause(!_runtime.EmergencyPaused), "danger");
        _railStop.HorizontalAlignment = HorizontalAlignment.Stretch;
        _railStop.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(_railStop, "Releases every key, button and stick BogiHub is holding. Also: Ctrl+Shift+F12 or the tray menu.");

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("244,*") };
        layout.Children.Add(BuildRail());
        _host.Margin = new Thickness(16, 46, 28, 22);
        Grid.SetColumn(_host, 1);
        layout.Children.Add(_host);

        // Invisible strip along the top of the content column acts as the title bar: drag, snap, double-click.
        var dragStrip = new Border { Height = 40, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
        WindowDecorationProperties.SetElementRole(dragStrip, WindowDecorationsElementRole.TitleBar);
        dragStrip.PointerPressed += (_, e) =>
        {
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        Grid.SetColumn(dragStrip, 1);
        layout.Children.Add(dragStrip);
        var captions = BuildCaptionButtons();
        Grid.SetColumn(captions, 1);
        layout.Children.Add(captions);

        Content = new Panel { Children = { _backdrop, layout } };

        // The pointer is the light source for every glass surface.
        AddHandler(PointerMovedEvent, (_, e) =>
        {
            Glass.Light = e.GetPosition(this);
            Glass.LightActive = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += (_, _) => Glass.LightActive = false;

        Navigate("overview");
        _navWrap.SizeChanged += (_, _) => PlaceLens(instant: true);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());
        timer.Start();
    }

    /// <summary>Debug aid (BOGIHUB_DUMP_VISUALS=path): writes the visual tree with text and decoration roles.</summary>
    private void DumpVisuals()
    {
        var lines = new List<string>();
        void Walk(Visual v, int depth)
        {
            string extra = v switch
            {
                TextBlock t => $" text=\"{t.Text}\"",
                Control c when WindowDecorationProperties.GetElementRole(c) != WindowDecorationsElementRole.None => $" role={WindowDecorationProperties.GetElementRole(c)}",
                _ => "",
            };
            lines.Add(new string(' ', depth * 2) + v.GetType().Name + extra);
            foreach (var child in v.GetVisualChildren()) Walk(child, depth + 1);
        }
        Walk(this, 0);
        File.WriteAllLines(Environment.GetEnvironmentVariable("BOGIHUB_DUMP_VISUALS")!, lines);
    }

    /// <summary>Glass minimise / maximise / close, tagged with their window roles so Windows treats them as caption buttons.</summary>
    private Control BuildCaptionButtons()
    {
        Button Caption(string glyph, string tip, WindowDecorationsElementRole role, Action click, bool close = false)
        {
            var b = new Button
            {
                Content = new Avalonia.Controls.Shapes.Path
                {
                    Data = StreamGeometry.Parse(glyph),
                    Stroke = Palette.TextBrush,
                    StrokeThickness = 1.3,
                    StrokeLineCap = PenLineCap.Round,
                    Width = 10,
                    Height = 10,
                    Stretch = Stretch.None, // glyphs are drawn in a 10x10 box; a flat line can't be uniformly stretched
                },
                Width = 34,
                Height = 26,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            b.Classes.Add(close ? "caption-close" : "caption");
            ToolTip.SetTip(b, tip);
            WindowDecorationProperties.SetElementRole(b, role);
            b.Click += (_, _) => click();
            return b;
        }

        var row = Ui.Row(6,
            Caption("M0,5 L10,5", "Minimise", WindowDecorationsElementRole.MinimizeButton, () => WindowState = WindowState.Minimized),
            Caption("M0.5,0.5 H9.5 V9.5 H0.5 Z", "Maximise", WindowDecorationsElementRole.MaximizeButton,
                () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
            Caption("M0,0 L10,10 M10,0 L0,10", "Close to tray", WindowDecorationsElementRole.CloseButton, Close, close: true));
        row.HorizontalAlignment = HorizontalAlignment.Right;
        row.VerticalAlignment = VerticalAlignment.Top;
        row.Margin = new Thickness(0, 10, 14, 0);
        return row;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Minimised: nothing is visible, so stop all animation work (resumes when restored).
        if (change.Property == WindowStateProperty) Glass.SetPaused(WindowState == WindowState.Minimized);
    }

    public void SetAmbient(bool color) => _backdrop.Palette = color ? AmbientPalette.Aurora : AmbientPalette.Mono;

    private Control BuildRail()
    {
        var brandMark = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(11),
            Background = Palette.Brush(Colors.White, 0.10),
            BorderBrush = Palette.Brush(Colors.White, 0.35),
            BorderThickness = new Thickness(1),
            Child = new Viewbox
            {
                Width = 22,
                Height = 22,
                Child = new Canvas
                {
                    Width = 24,
                    Height = 24,
                    Children =
                    {
                        new Avalonia.Controls.Shapes.Path
                        {
                            Data = Icons.Geo(Icons.Gamepad),
                            Stroke = Palette.AccentBrush,
                            StrokeThickness = 1.9,
                            StrokeLineCap = PenLineCap.Round,
                            StrokeJoin = PenLineJoin.Round,
                        },
                    },
                },
            },
            BoxShadow = BoxShadows.Parse("0 0 22 0 #33FFFFFF"),
        };
        var brand = Ui.Row(12, brandMark, Ui.Stack(0, Ui.Text("BogiHub", "h2"), Ui.Text("for Steam Controller", "muted", 11.5)));
        brand.Margin = new Thickness(18, 22, 18, 26);
        brand.Background = Brushes.Transparent;
        brand.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };

        var navPanel = new StackPanel { Spacing = 4 };
        foreach (var (key, label, icon) in Nav)
        {
            var path = new Avalonia.Controls.Shapes.Path
            {
                Data = Icons.Geo(icon),
                Stroke = Palette.MutedBrush,
                StrokeThickness = 2,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
            };
            // The icons share a 24×24 grid; scale that whole grid (not each icon's own bounds) so they line up.
            var glyph = new Viewbox { Width = 20, Height = 20, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
            var b = new Button { Content = Ui.Row(12, glyph, new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeight.Medium }), Tag = key };
            b.Classes.Add("nav");
            b.Click += (_, _) => Navigate(key);
            _navButtons.Add(b);
            navPanel.Children.Add(b);
        }
        _navWrap.Margin = new Thickness(12, 0);
        _navWrap.Children.Add(_navLens);
        _navWrap.Children.Add(navPanel);

        var bottom = Ui.Stack(10, _railStatus, _railStop);
        bottom.Margin = new Thickness(16, 0, 16, 18);
        bottom.VerticalAlignment = VerticalAlignment.Bottom;

        var rail = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        rail.Children.Add(brand);
        Grid.SetRow(_navWrap, 1);
        rail.Children.Add(_navWrap);
        Grid.SetRow(bottom, 3);
        rail.Children.Add(bottom);

        return new GlassPanel { Child = rail, Radius = 26, Padding = new Thickness(0), Margin = new Thickness(12, 12, 6, 12), Tint = 0.035 };
    }

    public void Navigate(string key, Action<Control>? configure = null)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = _factories[key]();
            _pages[key] = page;
        }
        configure?.Invoke(page);
        if (key == _current) return;
        _current = key;
        _host.Content = page;
        Ui.Enter(page);
        Ui.Stagger(page);

        foreach (var b in _navButtons)
        {
            bool sel = (string?)b.Tag == key;
            b.Classes.Set("selected", sel);
            if (b.Content is StackPanel { Children: [Viewbox { Child: Canvas { Children: [Avalonia.Controls.Shapes.Path p] } }, ..] })
                p.Stroke = sel ? Palette.TextBrush : Palette.MutedBrush;
        }
        PlaceLens(instant: false);
        if (page is IPage ip) ip.Refresh(_runtime.GetStatus());
    }

    private void PlaceLens(bool instant, int retries = 20)
    {
        var b = _navButtons.FirstOrDefault(x => (string?)x.Tag == _current);
        if (b is null) return;
        if (b.Bounds.Height <= 0 || b.TranslatePoint(default, _navWrap) is not { } p)
        {
            // Not laid out yet. A window that was never shown (started minimised to the tray) never will be, so retry
            // only a few times: re-posting forever starved the UI thread and froze the tray. When the window is shown,
            // the rail's SizeChanged places the lens.
            if (retries > 0 && IsVisible) Dispatcher.UIThread.Post(() => PlaceLens(instant, retries - 1), DispatcherPriority.Loaded);
            return;
        }
        _navLens.MoveTo(new Rect(p, b.Bounds.Size), instant);
    }

    private void Tick()
    {
        if (!IsVisible) return;
        var s = _runtime.GetStatus();
        var (text, color) = s.Decision.Owner switch
        {
            ControlOwner.Hub when s.SteamConflict => ("BogiHub · Steam conflict", Palette.Warn),
            ControlOwner.Hub when s.Decision.IsMouseKeyboard => ("Mouse & keyboard", Palette.Accent),
            ControlOwner.Hub => ("Gamepad", Palette.Accent),
            ControlOwner.Steam => ("Steam in control", Palette.Muted),
            ControlOwner.Paused => ("Paused", Palette.Danger),
            _ => ("Mouse & keyboard", Palette.Muted),
        };
        _railStatus.Set(text, color);
        _railStop.Content = _runtime.EmergencyPaused ? "Resume" : "Emergency stop";
        if (_host.Content is IPage page) page.Refresh(s);
    }
}
