using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace BogiHub.App.Theme;

/// <summary>
/// Clean, quiet dark UI with exactly one accent: the soft white of the hologram.
/// Status colours (ok / warn / danger) are only used for state, never decoration.
/// </summary>
public static class Palette
{
    public static readonly Color Bg = Color.Parse("#0A0C0F");
    public static readonly Color Surface = Color.Parse("#111419");
    public static readonly Color Surface2 = Color.Parse("#161A20");
    public static readonly Color Raised = Color.Parse("#1C2129");
    public static readonly Color Line = Color.Parse("#232932");
    public static readonly Color LineStrong = Color.Parse("#2F3641");
    public static readonly Color Text = Color.Parse("#E9ECF1");
    public static readonly Color Muted = Color.Parse("#8B94A3");
    public static readonly Color Faint = Color.Parse("#596170");
    public static readonly Color Accent = Color.Parse("#EEF1F5");
    public static readonly Color Ok = Color.Parse("#5FD39A");
    public static readonly Color Warn = Color.Parse("#F2B84B");
    public static readonly Color Danger = Color.Parse("#FF6B6B");

    public static readonly IImmutableSolidColorBrush BgBrush = new ImmutableSolidColorBrush(Bg);
    public static readonly IImmutableSolidColorBrush SurfaceBrush = new ImmutableSolidColorBrush(Surface);
    public static readonly IImmutableSolidColorBrush Surface2Brush = new ImmutableSolidColorBrush(Surface2);
    public static readonly IImmutableSolidColorBrush RaisedBrush = new ImmutableSolidColorBrush(Raised);
    public static readonly IImmutableSolidColorBrush LineBrush = new ImmutableSolidColorBrush(Line);
    public static readonly IImmutableSolidColorBrush LineStrongBrush = new ImmutableSolidColorBrush(LineStrong);
    public static readonly IImmutableSolidColorBrush TextBrush = new ImmutableSolidColorBrush(Text);
    public static readonly IImmutableSolidColorBrush MutedBrush = new ImmutableSolidColorBrush(Muted);
    public static readonly IImmutableSolidColorBrush FaintBrush = new ImmutableSolidColorBrush(Faint);
    public static readonly IImmutableSolidColorBrush AccentBrush = new ImmutableSolidColorBrush(Accent);
    public static readonly IImmutableSolidColorBrush OkBrush = new ImmutableSolidColorBrush(Ok);
    public static readonly IImmutableSolidColorBrush WarnBrush = new ImmutableSolidColorBrush(Warn);
    public static readonly IImmutableSolidColorBrush DangerBrush = new ImmutableSolidColorBrush(Danger);

    public static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);
    public static IImmutableSolidColorBrush Brush(Color c, double alpha = 1) => new ImmutableSolidColorBrush(WithAlpha(c, alpha));
}
