using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace BogiHub.App.Theme;

/// <summary>
/// Line icons from Tabler Icons 3.49.0 (outline set, MIT licence, https://tabler.io/icons; see THIRD_PARTY_NOTICES.md).
/// Path data is copied verbatim from the SVGs: 24×24 grid, drawn with a round-capped stroke and no fill.
/// </summary>
public static class Icons
{
    public const string Gamepad = "M12 5h3.5a5 5 0 0 1 0 10h-5.5l-4.015 4.227a2.3 2.3 0 0 1 -3.923 -2.035l1.634 -8.173a5 5 0 0 1 4.904 -4.019h3.4 M14 15l4.07 4.284a2.3 2.3 0 0 0 3.925 -2.023l-1.6 -8.232 M8 9v2 M7 10h2 M14 10h2"; // device-gamepad-2
    public const string Unplugged = "M20 16l-4 4 M7 12l5 5l-1.5 1.5a3.536 3.536 0 1 1 -5 -5l1.5 -1.5 M17 12l-5 -5l1.5 -1.5a3.536 3.536 0 1 1 5 5l-1.5 1.5 M3 21l2.5 -2.5 M18.5 5.5l2.5 -2.5 M10 11l-2 2 M13 14l-2 2 M16 16l4 4"; // plug-connected-x
    public const string Mouse = "M6 7a4 4 0 0 1 4 -4h4a4 4 0 0 1 4 4v10a4 4 0 0 1 -4 4h-4a4 4 0 0 1 -4 -4l0 -10 M12 7l0 4"; // mouse
    public const string AppWindow = "M3 7a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-10 M6 8h.01 M9 8h.01"; // app-window (another app: Steam)
    public const string Pause = "M6 6a1 1 0 0 1 1 -1h2a1 1 0 0 1 1 1v12a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1l0 -12 M14 6a1 1 0 0 1 1 -1h2a1 1 0 0 1 1 1v12a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1l0 -12"; // player-pause
    public const string Warning = "M12 9v4 M10.363 3.591l-8.106 13.534a1.914 1.914 0 0 0 1.636 2.871h16.214a1.914 1.914 0 0 0 1.636 -2.87l-8.106 -13.536a1.914 1.914 0 0 0 -3.274 0 M12 16h.01"; // alert-triangle
    public const string Download = "M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2 -2v-2 M7 11l5 5l5 -5 M12 4l0 12"; // download
    public const string Keyboard = "M2 8a2 2 0 0 1 2 -2h16a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-16a2 2 0 0 1 -2 -2l0 -8 M6 10l0 .01 M10 10l0 .01 M14 10l0 .01 M18 10l0 .01 M6 14l0 .01 M18 14l0 .01 M10 14l4 .01"; // keyboard
    public const string Battery = "M6 7h11a2 2 0 0 1 2 2v.5a.5 .5 0 0 0 .5 .5a.5 .5 0 0 1 .5 .5v3a.5 .5 0 0 1 -.5 .5a.5 .5 0 0 0 -.5 .5v.5a2 2 0 0 1 -2 2h-11a2 2 0 0 1 -2 -2v-6a2 2 0 0 1 2 -2"; // battery
    public const string BatteryCharging = "M16 7h1a2 2 0 0 1 2 2v.5a.5 .5 0 0 0 .5 .5a.5 .5 0 0 1 .5 .5v3a.5 .5 0 0 1 -.5 .5a.5 .5 0 0 0 -.5 .5v.5a2 2 0 0 1 -2 2h-2 M8 7h-2a2 2 0 0 0 -2 2v6a2 2 0 0 0 2 2h1 M12 8l-2 4h3l-2 4"; // battery-charging
    public const string LayoutGrid = "M4 5a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4 M14 5a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4 M4 15a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4 M14 15a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4"; // layout-grid
    public const string Stack = "M12 4l-8 4l8 4l8 -4l-8 -4 M4 12l8 4l8 -4 M4 16l8 4l8 -4"; // stack-2
    public const string Adjustments = "M12 6a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M4 6l8 0 M16 6l4 0 M6 12a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M4 12l2 0 M10 12l10 0 M15 18a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M4 18l11 0 M19 18l1 0"; // adjustments-horizontal

    /// <summary>Inside of the <see cref="Battery"/> outline, for drawing its charge level (24×24 units).</summary>
    public static readonly Rect BatteryInside = new(6.5, 9.5, 10.5, 5);

    private static readonly Dictionary<string, Geometry> Cache = [];

    public static Geometry Geo(string data)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(data, out var g)) Cache[data] = g = StreamGeometry.Parse(data);
            return g;
        }
    }

    /// <summary>Strokes an icon into a <paramref name="size"/>-unit square at <paramref name="origin"/>.</summary>
    public static void Draw(DrawingContext ctx, string data, Point origin, double size, IBrush stroke, double thickness = 1.8)
    {
        double k = size / 24.0;
        using (ctx.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(origin.X, origin.Y)))
            ctx.DrawGeometry(null, new Pen(stroke, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Geo(data));
    }

    // ---- App / tray icon ----------------------------------------------------------------------

    /// <summary>The BogiHub mark: the gamepad icon in white, centred on a dark rounded square.</summary>
    public static RenderTargetBitmap RenderAppIcon(int px)
    {
        var bmp = new RenderTargetBitmap(new PixelSize(px, px), new Vector(96, 96));
        using var ctx = bmp.CreateDrawingContext();
        double u = px / 64.0;
        ctx.DrawRectangle(new ImmutableSolidColorBrush(Palette.Surface), new ImmutablePen(new ImmutableSolidColorBrush(Palette.LineStrong), Math.Max(1, 1.5 * u)),
            new Rect(2 * u, 2 * u, 60 * u, 60 * u), 14 * u, 14 * u);
        // Small sizes get a heavier line so the shape survives the downscale.
        double stroke = px <= 16 ? 2.6 : px <= 32 ? 2.2 : 1.9;
        Draw(ctx, Gamepad, new Point(10 * u, 10 * u), 44 * u, new ImmutableSolidColorBrush(Palette.Accent), stroke);
        return bmp;
    }

    public static WindowIcon AppIcon()
    {
        var ms = new MemoryStream();
        RenderAppIcon(64).Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    /// <summary>Writes a multi-size .ico (PNG entries) for the .exe and shortcuts. Used by scripts/make-icon.ps1.</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = [256, 64, 48, 32, 16];
        var pngs = sizes.Select(s =>
        {
            var ms = new MemoryStream();
            RenderAppIcon(s).Save(ms);
            return ms.ToArray();
        }).ToList();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0);
        w.Write((short)1); // icon
        w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
    }

    /// <summary>
    /// Installer artwork (BOGIHUB_INSTALLER_ART=folder): the tall welcome/finish panel and the small header mark, each
    /// at 100 % and 200 % so the wizard stays sharp on high-DPI screens.
    /// </summary>
    public static void WriteInstallerArt(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (int scale in new[] { 1, 2 })
        {
            RenderWizardPanel(scale).Save(Path.Combine(dir, $"wizard-{scale * 100}.png"));
            RenderAppIcon(55 * scale).Save(Path.Combine(dir, $"small-{scale * 100}.png"));
        }
    }

    private static RenderTargetBitmap RenderWizardPanel(int scale)
    {
        int w = 164 * scale, h = 314 * scale;
        var bmp = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using var ctx = bmp.CreateDrawingContext();
        // Grayscale text smoothing: ClearType's coloured fringes suit a live screen, not a picture the installer scales
        // (at 125 % display scaling they smear into stray marks between the letters).
        using (ctx.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }))
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            var bounds = new Rect(0, 0, 164, 314);
            ctx.FillRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.3, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Palette.Raised, 0), new GradientStop(Palette.Bg, 1) },
            }, bounds);
            // Soft light behind the mark, like the glow in the app's sidebar.
            ctx.DrawEllipse(new RadialGradientBrush
            {
                GradientStops = { new GradientStop(Palette.WithAlpha(Palette.Accent, 0.16), 0), new GradientStop(Palette.WithAlpha(Palette.Accent, 0), 1) },
            }, null, new Point(82, 128), 78, 78);
            Draw(ctx, Gamepad, new Point(46, 92), 72, new ImmutableSolidColorBrush(Palette.Accent), 1.6);

            var title = new FormattedText("BogiHub", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold), 20, new ImmutableSolidColorBrush(Palette.Text));
            ctx.DrawText(title, new Point(82 - title.Width / 2, 192));
            var sub = new FormattedText("for Steam Controller", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Inter"), 11, new ImmutableSolidColorBrush(Palette.Muted));
            ctx.DrawText(sub, new Point(82 - sub.Width / 2, 220));
        }
        return bmp;
    }

    /// <summary>Every icon on one sheet, for checking them without opening the app (BOGIHUB_ICON_SHEET=path).</summary>
    public static void WriteSheet(string path)
    {
        string[] all = [Gamepad, Unplugged, Mouse, AppWindow, Pause, Warning, Keyboard, Battery, BatteryCharging, LayoutGrid, Stack, Adjustments];
        const int cell = 72;
        var bmp = new RenderTargetBitmap(new PixelSize(cell * 6, cell * 2 + 140), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.FillRectangle(new ImmutableSolidColorBrush(Palette.Bg), new Rect(0, 0, cell * 6, cell * 2 + 140));
            var white = new ImmutableSolidColorBrush(Palette.Text);
            for (int i = 0; i < all.Length; i++)
            {
                var cellRect = new Rect(i % 6 * cell, i / 6 * cell, cell, cell);
                ctx.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(Palette.Line), 1), cellRect.Deflate(4));
                Draw(ctx, all[i], new Point(cellRect.X + 12, cellRect.Y + 12), 48, white, 1.8);
            }
            int x = 8;
            foreach (int s in new[] { 128, 64, 32, 16 })
            {
                using (ctx.PushTransform(Matrix.CreateTranslation(x, cell * 2 + 6)))
                    ctx.DrawImage(RenderAppIcon(s), new Rect(0, 0, s, s));
                x += s + 16;
            }
        }
        bmp.Save(path);
    }
}
