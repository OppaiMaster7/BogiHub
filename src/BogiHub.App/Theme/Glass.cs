using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using BogiHub.App.Controls;

namespace BogiHub.App.Theme;

public enum AmbientPalette { Aurora, Mono }

/// <summary>
/// Liquid-glass rendering shared by every glass surface.
/// The window background (<see cref="AmbientBackdrop"/>) renders slow-moving light blobs into a small bitmap
/// 15 times a second. Glass surfaces draw their slice of that bitmap slightly magnified (refraction) and upscaled
/// (blur), then add tint, sheen, a light-catching rim and a highlight that follows the pointer.
/// Glass only repaints when what it shows changes: the backdrop frame, or the pointer light.
/// </summary>
public static class Glass
{
    /// <summary>Raised every animation frame (UI thread) with the frame delta in seconds. For springs.</summary>
    public static event Action<float>? Tick;

    /// <summary>Raised when glass must repaint: new backdrop frame or the pointer light moved.</summary>
    public static event Action? SurfaceChanged;

    /// <summary>Pointer position in window coordinates, used as the "light" glass surfaces react to.</summary>
    public static Point Light;
    internal static Spring LightStrength = new(90, 1f);
    public static bool LightActive;

    /// <summary>True while the window is hidden: every animation loop stops requesting frames.</summary>
    public static bool Paused { get; private set; }

    /// <summary>Raised when the window becomes visible again so animation loops can restart.</summary>
    public static event Action? Resumed;

    public static void SetPaused(bool paused)
    {
        if (Paused == paused) return;
        Paused = paused;
        if (!paused) Resumed?.Invoke();
    }

    internal static void RaiseTick(float dt) => Tick?.Invoke(dt);
    internal static void RaiseSurfaceChanged() => SurfaceChanged?.Invoke();

    private static readonly IImmutableSolidColorBrush[] ShadowLayers =
        [.. Enumerable.Range(0, 3).Select(i => new ImmutableSolidColorBrush(Color.FromArgb((byte)(20 - i * 5), 0, 0, 0)))];

    private static readonly IImmutableBrush Sheen = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(26, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.45) },
    }.ToImmutable();

    private static readonly Dictionary<int, IPen> RimPens = [];
    private static readonly Dictionary<int, IImmutableSolidColorBrush> TintBrushes = [];

    /// <summary>Rim: bright where light hits (top-left), faint along the bottom, small catchlight bottom-right.</summary>
    private static IPen RimPen(double boost)
    {
        int key = (int)Math.Round(Math.Clamp(boost, 0, 1) * 20);
        if (RimPens.TryGetValue(key, out var pen)) return pen;
        double b = key / 20.0;
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(120 + 100 * b), 255, 255, 255), 0),
                new GradientStop(Color.FromArgb((byte)(22 + 30 * b), 255, 255, 255), 0.45),
                new GradientStop(Color.FromArgb((byte)(14 + 20 * b), 255, 255, 255), 0.75),
                new GradientStop(Color.FromArgb((byte)(60 + 60 * b), 255, 255, 255), 1),
            },
        }.ToImmutable();
        pen = new ImmutablePen(brush, 1);
        RimPens[key] = pen;
        return pen;
    }

    private static IImmutableSolidColorBrush TintBrush(double tint)
    {
        int a = (int)Math.Clamp(tint * 255, 0, 255);
        if (!TintBrushes.TryGetValue(a, out var b)) TintBrushes[a] = b = new ImmutableSolidColorBrush(Color.FromArgb((byte)a, 255, 255, 255));
        return b;
    }

    /// <summary>Draws a glass surface into <paramref name="r"/> (in <paramref name="v"/>'s coordinates).</summary>
    public static void Draw(DrawingContext ctx, Visual v, Rect r, double radius, double tint = 0.05, double rimBoost = 0, bool shadow = true, double lens = 0.035, double shadowDrop = 1)
    {
        if (r.Width <= 1 || r.Height <= 1) return;
        // Whole-pixel edges keep the 1 px rim crisp instead of smeared across two pixels.
        r = new Rect(Math.Round(r.X), Math.Round(r.Y), Math.Round(r.Width), Math.Round(r.Height));
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);

        if (shadow)
        {
            for (int i = 0; i < ShadowLayers.Length; i++)
            {
                double grow = 2 + i * 6;
                var sr = r.Inflate(grow).Translate(new Vector(0, (3 + i * 3) * shadowDrop));
                ctx.DrawRectangle(ShadowLayers[i], null, sr, radius + grow, radius + grow);
            }
        }

        using (ctx.PushClip(new RoundedRect(r, radius)))
        {
            var bd = AmbientBackdrop.Current;
            if (bd?.Frame is { } frame && v.TranslatePoint(r.TopLeft, bd) is { } o)
            {
                // Refraction: sample a slightly smaller window region than the surface covers, so the backdrop
                // looks magnified through thick glass. Upscaling the low-res frame doubles as the blur.
                double k = bd.FrameScale;
                var win = new Rect(o, r.Size).Deflate(new Thickness(r.Width * lens, r.Height * lens));
                var src = new Rect(win.X * k, win.Y * k, Math.Max(1, win.Width * k), Math.Max(1, win.Height * k));
                ctx.DrawImage(frame, src, r);
            }

            ctx.FillRectangle(TintBrush(tint), r);
            ctx.FillRectangle(Sheen, r);

            // Pointer highlight: the only per-frame brush, and only while the pointer is over the window.
            float ls = Math.Clamp(LightStrength.Value, 0, 1);
            if (ls > 0.01 && TopLevel.GetTopLevel(v) is { } top && top.TranslatePoint(Light, v) is { } lp)
            {
                double rad = Math.Max(180, Math.Min(r.Width, r.Height) * 0.9);
                if (lp.X > r.X - rad && lp.X < r.Right + rad && lp.Y > r.Y - rad && lp.Y < r.Bottom + rad)
                {
                    var spot = new RadialGradientBrush
                    {
                        Center = new RelativePoint(lp, RelativeUnit.Absolute),
                        GradientOrigin = new RelativePoint(lp, RelativeUnit.Absolute),
                        RadiusX = new RelativeScalar(rad, RelativeUnit.Absolute),
                        RadiusY = new RelativeScalar(rad, RelativeUnit.Absolute),
                        GradientStops = { new GradientStop(Color.FromArgb((byte)(30 * ls), 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
                    };
                    ctx.FillRectangle(spot, r);
                }
            }
        }

        ctx.DrawRectangle(null, RimPen(rimBoost), r.Deflate(0.5), radius, radius);
    }
}

/// <summary>
/// Full-window animated background. Owns the single animation clock that drives every glass surface,
/// so all glass moves in lock-step with the light behind it.
/// </summary>
public sealed class AmbientBackdrop : Control
{
    /// <summary>The blobs move tens of pixels per second through a heavy blur; 15 updates/s is indistinguishable from 60.</summary>
    private const double BackdropFps = 15;

    public static AmbientBackdrop? Current { get; private set; }

    /// <summary>Low-resolution render of the backdrop for glass surfaces to sample.</summary>
    public RenderTargetBitmap? Frame { get; private set; }
    /// <summary>Frame pixels per backdrop unit.</summary>
    public double FrameScale { get; } = 1 / 4.0;

    private AmbientPalette _palette = AmbientPalette.Aurora;
    public AmbientPalette Palette
    {
        get => _palette;
        set { _palette = value; _lastRender = double.MinValue; }
    }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _last, _lastRender = double.MinValue;
    private bool _running;
    private Point _lastLight;
    private float _lastLightStrength;

    private readonly record struct Blob(double Cx, double Cy, double Ax, double Ay, double Wx, double Wy, double Phase, double Radius, Color Aurora, Color Mono);

    // Slow Lissajous paths (periods 40–90 s). Colours are dim and desaturated: depth, not decoration.
    private static readonly Blob[] Blobs =
    [
        new(0.22, 0.20, 0.18, 0.14, 0.11, 0.07, 0.0, 0.55, Color.Parse("#2E3A66"), Color.Parse("#2A2D33")),
        new(0.80, 0.30, 0.16, 0.20, 0.08, 0.10, 1.7, 0.50, Color.Parse("#3B2E5C"), Color.Parse("#26292E")),
        new(0.55, 0.85, 0.25, 0.10, 0.06, 0.12, 3.1, 0.60, Color.Parse("#1F4A4A"), Color.Parse("#23262B")),
        new(0.10, 0.80, 0.12, 0.16, 0.09, 0.05, 4.4, 0.40, Color.Parse("#4A3A2A"), Color.Parse("#2B2E34")),
        new(0.65, 0.55, 0.20, 0.22, 0.05, 0.08, 2.2, 0.35, Color.Parse("#3A4466"), Color.Parse("#31353C")),
    ];

    private static readonly Color Base = Color.Parse("#07080A");
    private static readonly IImmutableBrush BaseBrush = new ImmutableSolidColorBrush(Base);
    private static readonly IImmutableBrush Vignette = new RadialGradientBrush
    {
        RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(110, 0, 0, 0), 1) },
    }.ToImmutable();

    public AmbientBackdrop()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Current = this;
        _running = true;
        Glass.Resumed += Resume;
        Resume();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _running = false;
        Glass.Resumed -= Resume;
        if (Current == this) Current = null;
        base.OnDetachedFromVisualTree(e);
    }

    private bool _loopActive;

    private void Resume()
    {
        if (!_running || _loopActive) return;
        _loopActive = true;
        _last = _clock.Elapsed.TotalSeconds;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan _)
    {
        if (!_running || Glass.Paused)
        {
            _loopActive = false; // stopped; Resume() restarts it when the window is shown again
            return;
        }
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _last, 0, 0.1);
        _last = now;

        Glass.LightStrength.Target = Glass.LightActive ? 1 : 0;
        Glass.LightStrength.Step(dt);

        bool changed = false;
        if (now - _lastRender >= 1 / BackdropFps || Frame is null)
        {
            _lastRender = now;
            RenderFrame(now);
            InvalidateVisual();
            changed = true;
        }

        float ls = Glass.LightStrength.Value;
        if (Math.Abs(ls - _lastLightStrength) > 0.002f || (ls > 0.01f && Glass.Light != _lastLight))
        {
            _lastLight = Glass.Light;
            _lastLightStrength = ls;
            changed = true;
        }

        if (changed) Glass.RaiseSurfaceChanged();
        Glass.RaiseTick(dt);
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    private void RenderFrame(double t)
    {
        var size = Bounds.Size;
        if (size.Width < 2 || size.Height < 2) return;
        var px = new PixelSize(Math.Max(2, (int)(size.Width * FrameScale)), Math.Max(2, (int)(size.Height * FrameScale)));
        if (Frame is null || Frame.PixelSize != px)
        {
            Frame?.Dispose();
            Frame = new RenderTargetBitmap(px, new Vector(96, 96));
        }

        using var dc = Frame.CreateDrawingContext();
        var area = new Rect(0, 0, px.Width, px.Height);
        dc.FillRectangle(BaseBrush, area);
        double m = Math.Max(px.Width, px.Height);
        foreach (var b in Blobs)
        {
            var c = new Point(
                (b.Cx + b.Ax * Math.Sin(t * b.Wx + b.Phase)) * px.Width,
                (b.Cy + b.Ay * Math.Cos(t * b.Wy + b.Phase * 0.7)) * px.Height);
            double r = b.Radius * m * (1 + 0.08 * Math.Sin(t * 0.13 + b.Phase));
            var col = _palette == AmbientPalette.Mono ? b.Mono : b.Aurora;
            var brush = new RadialGradientBrush
            {
                Center = new RelativePoint(c, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(c, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(r, RelativeUnit.Absolute),
                RadiusY = new RelativeScalar(r, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(Color.FromArgb(200, col.R, col.G, col.B), 0), new GradientStop(Color.FromArgb(0, col.R, col.G, col.B), 1) },
            };
            dc.FillRectangle(brush, area);
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var all = new Rect(Bounds.Size);
        if (Frame is null)
        {
            ctx.FillRectangle(BaseBrush, all);
            return;
        }
        ctx.DrawImage(Frame, new Rect(0, 0, Frame.PixelSize.Width, Frame.PixelSize.Height), all);
        ctx.FillRectangle(Vignette, all); // calm edges, readable centre
    }
}

/// <summary>A card made of glass. Content sits crisp on top; the surface refracts the backdrop behind it.</summary>
public class GlassPanel : Decorator
{
    public double Radius { get; set; } = 22;
    public double Tint { get; set; } = 0.045;
    public bool Shadow { get; set; } = true;

    private double _highlight;
    /// <summary>Brighter rim, e.g. the active profile.</summary>
    public bool Highlight
    {
        get => _highlight > 0;
        set { _highlight = value ? 1 : 0; InvalidateVisual(); }
    }

    public GlassPanel()
    {
        Padding = new Thickness(20);
        ClipToBounds = false;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Glass.SurfaceChanged += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Glass.SurfaceChanged -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext ctx) =>
        Glass.Draw(ctx, this, new Rect(Bounds.Size), Radius, Tint, _highlight * 0.8, Shadow);
}

/// <summary>Base for small animated glass controls: repaints while its springs move, or when the glass changes.</summary>
public abstract class GlassAnimated : Control
{
    protected GlassAnimated() => RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Glass.Tick += OnTick;
        Glass.SurfaceChanged += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Glass.Tick -= OnTick;
        Glass.SurfaceChanged -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTick(float dt)
    {
        if (Step(dt)) InvalidateVisual();
    }

    /// <summary>Advance springs; return true while anything is still moving.</summary>
    protected abstract bool Step(float dt);

    protected static bool Moving(in Spring s) => Math.Abs(s.Velocity) > 0.01f || Math.Abs(s.Target - s.Value) > 0.001f;
}

/// <summary>
/// The liquid selection lens: a glass capsule that springs to its target and stretches along its direction of travel
/// like a droplet, then settles. Used by the navigation rail and segmented switches.
/// </summary>
public sealed class LiquidLens : GlassAnimated
{
    private Spring _x = new(260, 0.72f), _y = new(260, 0.72f), _w = new(260, 0.8f), _h = new(260, 0.8f);
    private bool _placed;

    public double Radius { get; set; } = 14;
    public double Tint { get; set; } = 0.10;

    public LiquidLens()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    public void MoveTo(Rect target, bool instant = false)
    {
        if (instant || !_placed)
        {
            _x.Snap((float)target.X); _y.Snap((float)target.Y); _w.Snap((float)target.Width); _h.Snap((float)target.Height);
            _placed = true;
        }
        else
        {
            _x.Target = (float)target.X; _y.Target = (float)target.Y; _w.Target = (float)target.Width; _h.Target = (float)target.Height;
        }
        InvalidateVisual();
    }

    protected override bool Step(float dt)
    {
        _x.Step(dt); _y.Step(dt); _w.Step(dt); _h.Step(dt);
        return Moving(_x) || Moving(_y) || Moving(_w) || Moving(_h);
    }

    /// <summary>Current drawn rectangle including the droplet stretch.</summary>
    public Rect Current
    {
        get
        {
            double vx = Math.Abs(_x.Velocity), vy = Math.Abs(_y.Velocity);
            double sx = Math.Clamp(vx / 1400, 0, 0.45), sy = Math.Clamp(vy / 1400, 0, 0.45);
            double w = _w.Value * (1 + sx - 0.3 * sy);
            double h = _h.Value * (1 + sy - 0.3 * sx);
            double cx = _x.Value + _w.Value / 2, cy = _y.Value + _h.Value / 2;
            return new Rect(cx - w / 2, cy - h / 2, Math.Max(1, w), Math.Max(1, h));
        }
    }

    public override void Render(DrawingContext ctx)
    {
        if (!_placed) return;
        Glass.Draw(ctx, this, Current, Radius, Tint, rimBoost: 0.9, shadow: true, lens: -0.06, shadowDrop: 0.25);
    }
}

/// <summary>
/// Glass toggle. Off: clear glass track, white knob. On: the track fills with white light and the knob turns into
/// a dark glass bead. The knob slides on a spring and stretches while it travels.
/// </summary>
public sealed class GlassToggle : GlassAnimated
{
    private Spring _pos = new(420, 0.68f), _hover = new(400, 1f), _press = new(600, 0.8f);
    private bool _checked;

    public event Action<bool>? Toggled;

    public bool IsChecked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            _pos.Target = value ? 1 : 0;
            InvalidateVisual();
        }
    }

    public GlassToggle(bool value)
    {
        Width = 52;
        Height = 30;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        _checked = value;
        _pos.Snap(value ? 1 : 0);
    }

    protected override bool Step(float dt)
    {
        _pos.Step(dt); _hover.Step(dt); _press.Step(dt);
        return Moving(_pos) || Moving(_hover) || Moving(_press);
    }

    private void Flip()
    {
        IsChecked = !IsChecked;
        Toggled?.Invoke(IsChecked);
    }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover.Target = 1; }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover.Target = 0; _press.Target = 0; }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); _press.Target = 1; e.Handled = true; }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _press.Target = 0;
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Flip();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Space or Key.Enter)
        {
            Flip();
            e.Handled = true;
        }
    }

    // Screen readers and UI Automation see a check box they can read and toggle.
    protected override AutomationPeer OnCreateAutomationPeer() => new TogglePeer(this);

    private sealed class TogglePeer(GlassToggle owner) : ControlAutomationPeer(owner), IToggleProvider
    {
        public ToggleState ToggleState => owner.IsChecked ? ToggleState.On : ToggleState.Off;
        public void Toggle() => owner.Flip();
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;
    }

    private static readonly IImmutableBrush KnobLight = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromRgb(255, 255, 255), 0), new GradientStop(Color.FromRgb(214, 219, 226), 1) },
    }.ToImmutable();

    private static readonly IImmutableBrush KnobDark = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromRgb(46, 51, 60), 0), new GradientStop(Color.FromRgb(16, 18, 22), 1) },
    }.ToImmutable();

    private static readonly IImmutableBrush KnobShadow = new ImmutableSolidColorBrush(Color.FromArgb(70, 0, 0, 0));
    private static readonly IPen DarkRim = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1);
    private static readonly IPen FocusRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1.5);

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        float p = Math.Clamp(_pos.Value, -0.1f, 1.1f);
        double on = Math.Clamp(p, 0, 1);

        Glass.Draw(ctx, this, r, r.Height / 2, tint: 0.05 + 0.80 * on + 0.04 * _hover.Value, rimBoost: 0.3 + 0.5 * on + 0.3 * _hover.Value, shadow: false);

        double stretch = Math.Clamp(Math.Abs(_pos.Velocity) / 6, 0, 0.55) + 0.18 * _press.Value;
        double kh = r.Height - 6;
        double kw = kh * (1 + stretch);
        double travel = r.Width - 6 - kh;
        double cx = 3 + kh / 2 + travel * p;
        var knob = new Rect(cx - kw / 2, 3 + 0.04 * kh * _press.Value, kw, kh * (1 - 0.08 * _press.Value));
        ctx.DrawRectangle(KnobShadow, null, knob.Translate(new Vector(0, 1.5)), kh / 2, kh / 2);
        // Cross-fade light knob → dark bead as the toggle turns on.
        ctx.DrawRectangle(KnobLight, null, knob, kh / 2, kh / 2);
        if (on > 0.01)
        {
            using (ctx.PushOpacity(on))
            {
                ctx.DrawRectangle(KnobDark, DarkRim, knob, kh / 2, kh / 2);
            }
        }
        if (IsFocused) ctx.DrawRectangle(null, FocusRing, r.Inflate(3), r.Height / 2 + 3, r.Height / 2 + 3);
    }
}
