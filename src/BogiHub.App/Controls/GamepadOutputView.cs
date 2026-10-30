using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using BogiHub.App.Theme;
using BogiHub.Core.Output;

namespace BogiHub.App.Controls;

/// <summary>
/// Compact picture of what the game receives from the virtual Xbox controller. Deliberately plain (grey + white),
/// so it reads as "output" next to the white hologram that represents the physical controller.
/// </summary>
public sealed class GamepadOutputView : Control
{
    private GamepadReport _report;
    private bool _active;

    public GamepadOutputView()
    {
        Height = 120;
        MinWidth = 260;
    }

    public void Update(GamepadReport report, bool active)
    {
        if (report.Equals(_report) && active == _active) return;
        _report = report;
        _active = active;
        InvalidateVisual();
    }

    private static readonly IPen Outline = new ImmutablePen(Palette.LineStrongBrush, 1.2);
    private static readonly IBrush On = Palette.TextBrush;
    private static readonly IBrush Off = Palette.Surface2Brush;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        double s = Math.Min(w / 300, h / 120);
        using var _ = ctx.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation((w - 300 * s) / 2, 0));
        using var __ = ctx.PushOpacity(_active ? 1 : 0.45);
        var r = _report;
        bool B(XButtons b) => (r.Buttons & b) != 0;

        // Triggers and shoulders
        Bar(ctx, new Rect(30, 6, 60, 8), r.LeftTrigger / 255.0);
        Bar(ctx, new Rect(210, 6, 60, 8), r.RightTrigger / 255.0);
        ctx.DrawRectangle(B(XButtons.LeftShoulder) ? On : Off, Outline, new Rect(30, 18, 60, 8), 4, 4);
        ctx.DrawRectangle(B(XButtons.RightShoulder) ? On : Off, Outline, new Rect(210, 18, 60, 8), 4, 4);

        // Sticks
        Stick(ctx, new Point(70, 62), r.LeftX, r.LeftY, B(XButtons.LeftThumb));
        Stick(ctx, new Point(186, 92), r.RightX, r.RightY, B(XButtons.RightThumb));

        // D-pad
        var d = new Point(112, 92);
        ctx.DrawRectangle(B(XButtons.DpadUp) ? On : Off, Outline, new Rect(d.X - 6, d.Y - 20, 12, 14), 3, 3);
        ctx.DrawRectangle(B(XButtons.DpadDown) ? On : Off, Outline, new Rect(d.X - 6, d.Y + 6, 12, 14), 3, 3);
        ctx.DrawRectangle(B(XButtons.DpadLeft) ? On : Off, Outline, new Rect(d.X - 20, d.Y - 6, 14, 12), 3, 3);
        ctx.DrawRectangle(B(XButtons.DpadRight) ? On : Off, Outline, new Rect(d.X + 6, d.Y - 6, 14, 12), 3, 3);

        // Face buttons
        var f = new Point(232, 62);
        Face(ctx, new Point(f.X, f.Y - 16), B(XButtons.Y), "Y");
        Face(ctx, new Point(f.X - 16, f.Y), B(XButtons.X), "X");
        Face(ctx, new Point(f.X + 16, f.Y), B(XButtons.B), "B");
        Face(ctx, new Point(f.X, f.Y + 16), B(XButtons.A), "A");

        // Back / Guide / Start
        ctx.DrawRectangle(B(XButtons.Back) ? On : Off, Outline, new Rect(122, 52, 16, 9), 4, 4);
        ctx.DrawEllipse(B(XButtons.Guide) ? On : Off, Outline, new Point(150, 56), 8, 8);
        ctx.DrawRectangle(B(XButtons.Start) ? On : Off, Outline, new Rect(162, 52, 16, 9), 4, 4);
    }

    private static void Bar(DrawingContext ctx, Rect r, double v)
    {
        ctx.DrawRectangle(Off, Outline, r, 4, 4);
        if (v > 0.003) ctx.DrawRectangle(On, null, new Rect(r.X, r.Y, r.Width * v, r.Height), 4, 4);
    }

    private static void Stick(DrawingContext ctx, Point c, short x, short y, bool click)
    {
        ctx.DrawEllipse(Off, Outline, c, 20, 20);
        var k = new Point(c.X + x / 32767.0 * 13, c.Y - y / 32767.0 * 13);
        ctx.DrawEllipse(click ? On : Palette.MutedBrush, null, k, 7, 7);
    }

    private static readonly Typeface Face_ = new("Inter", FontStyle.Normal, FontWeight.SemiBold);

    private static void Face(DrawingContext ctx, Point c, bool on, string label)
    {
        ctx.DrawEllipse(on ? On : Off, Outline, c, 8, 8);
        var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face_, 9, on ? Palette.BgBrush : Palette.MutedBrush);
        ctx.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
    }
}

/// <summary>Stick calibration view: outer ring, deadzone rings, raw position (hollow) and processed output (solid).</summary>
public sealed class StickZoneView : Control
{
    private System.Numerics.Vector2 _raw, _processed;
    private float _inner, _outer;

    public StickZoneView()
    {
        Width = 150;
        Height = 150;
    }

    public void Update(System.Numerics.Vector2 raw, System.Numerics.Vector2 processed, float inner, float outer)
    {
        _raw = raw;
        _processed = processed;
        _inner = inner;
        _outer = outer;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double r = Math.Min(Bounds.Width, Bounds.Height) / 2 - 4;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        ctx.DrawEllipse(Palette.Surface2Brush, new ImmutablePen(Palette.LineStrongBrush, 1), c, r, r);
        ctx.DrawEllipse(Palette.Brush(Palette.Accent, 0.08), new ImmutablePen(Palette.Brush(Palette.Accent, 0.5), 1, new ImmutableDashStyle([3, 3], 0)), c, r * _inner, r * _inner);
        double outerR = r * (1 - _outer);
        ctx.DrawEllipse(null, new ImmutablePen(Palette.Brush(Palette.Accent, 0.35), 1, new ImmutableDashStyle([2, 4], 0)), c, outerR, outerR);
        ctx.DrawLine(new ImmutablePen(Palette.LineBrush, 1), new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        ctx.DrawLine(new ImmutablePen(Palette.LineBrush, 1), new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));

        var raw = new Point(c.X + _raw.X * r, c.Y - _raw.Y * r);
        var outp = new Point(c.X + _processed.X * r, c.Y - _processed.Y * r);
        ctx.DrawEllipse(null, new ImmutablePen(Palette.MutedBrush, 1.5), raw, 6, 6);
        ctx.DrawEllipse(Palette.AccentBrush, null, outp, 4.5, 4.5);
    }
}
