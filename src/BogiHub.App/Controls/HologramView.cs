using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using BogiHub.App.Theme;
using BogiHub.Core.Input;

namespace BogiHub.App.Controls;

/// <summary>
/// Live hologram of the Steam Controller (2026). The front edge (bumpers, triggers, USB-C port) is drawn above the
/// face, like a technical drawing, on the same horizontal scale so the shoulders line up. Every shape is traced from
/// photos: the face from Valve's top-down product photo, the front edge from a straight-on review photo (levelled and
/// mirrored so both sides match). The rear buttons are drawn see-through at their real positions, as in Valve's own
/// diagrams, and the grip sensors as soft strips. The drawing never moves; only what you touch lights up.
/// Without a controller it dims and says so.
/// </summary>
public sealed class HologramView : Control
{
    private const double W = 1000, H = 1000;

    // Photo pixels → design units. Face: top-down photo, centre line x = 431, top y = 68.
    // Front edge: u = pixels from the centre line, v = pixels from the seam through the USB-C port (+ toward the face).
    private const double K = 1.34, Axis = 431, FaceTop = 306, Seam = 150;

    private static Point F(double x, double y) => new(500 + (x - Axis) * K, FaceTop + (y - 68) * K);
    private static Point T(double u, double v) => new(500 + u * K, Seam + v * K);
    private static Point Mirror(Point p) => new(W - p.X, p.Y);

    public Func<ControllerState>? StateSource { get; set; }
    public Func<bool>? ConnectedSource { get; set; }
    /// <summary>Optional text shown next to an element, e.g. its binding ("A", "Space").</summary>
    public Func<string, string?>? LabelFor { get; set; }
    public bool Interactive { get; set; }
    public bool ShowAllLabels { get; set; }
    public string? SelectedId
    {
        get => _selected;
        set { _selected = value; InvalidateVisual(); }
    }

    public event Action<string>? ElementClicked;

    // ---- Face, traced from the top-down photo (left half; the right half is its mirror image) ---------------

    private static readonly double[] BodyPx =
    [
        431, 75.5, 380, 75.5, 330, 76.5, 290, 78, 255, 78.8, 225, 80.5, 200, 85, 182, 92, 169, 101, 161, 110, 153, 120,
        146, 131, 141, 143, 137, 158, 132, 173, 127, 190, 122, 207, 117, 225, 112, 243, 107, 261, 102, 282, 97, 304,
        92.5, 328, 88.5, 350, 85, 374, 82.5, 397, 80.7, 420, 80.5, 445, 81.2, 470, 83.5, 488, 87, 506, 92, 521,
        98.5, 536, 107, 548, 117, 558, 130, 567, 145, 572.5, 162, 575.7, 178, 576, 192, 573.5, 202, 567, 209, 558,
        214, 546, 218, 536, 222.5, 525, 228.5, 514, 236.5, 502, 245, 493, 256, 485.5, 270, 481.7, 300, 481.5, 431, 481,
    ];

    // Left bumper as seen from above: its outer edge tip to tip, then back along the body edge it sits on.
    private static readonly double[] BumperOuterPx =
        [166, 103, 168.5, 98.5, 171.5, 93.5, 175, 90, 180, 86, 185, 83, 190, 80, 195, 78, 200, 76, 210, 73.5, 220, 71.5, 230, 70, 240, 69.5, 250, 69.5, 258, 70.5, 264, 72.3, 269, 75, 273, 78.3];
    private static readonly double[] BumperInnerPx = [273, 78.3, 255, 78.8, 237, 79.5, 225, 80.5, 212, 82.5, 200, 85, 190, 88.5, 182, 92, 174, 97.5, 166, 103];

    // Edge of the raised trackpad deck: under the left stick, then down the inside of the grip, fading out.
    private static readonly double[] DeckPx =
    [
        285, 256, 262, 256.3, 242, 257.2, 229, 259.2, 218, 262.5, 209, 267, 201, 274, 194, 283, 188.5, 294, 184, 305, 180.5, 316, 178, 326,
        174, 346, 171, 366, 168, 386, 165, 406, 162.5, 425, 160.5, 448, 158, 470, 155.5, 492, 152.5, 515, 149.5, 537, 147, 556,
    ];

    private static readonly Point DpadC = F(209.5, 177), FaceC = F(652.5, 177);
    private const double DpadReach = 57 * K, DpadHalf = 21 * K, FaceGap = 41.5 * K, FaceR = 19.5 * K, FaceBezel = 22.5 * K;
    private static readonly Point SteamC = F(431, 175), Led = F(431, 125);
    private const double SteamR = 20.5 * K, SteamBezel = 24 * K;
    private static readonly Rect ViewRect = PillRect(F(302.5, 125), 48, 22), MenuRect = PillRect(F(559.5, 125), 48, 22), QamRect = PillRect(F(431, 368), 62, 27);
    private static readonly Point LStick = F(326, 229), RStick = F(536, 229);
    private const double Collar = 49 * K, Knob = 37.5 * K, Dish = 30 * K, StickTravel = 26;

    // Trackpads: rounded squares turned 9.3° so their inner lower corners point at the middle (corners TL, TR, BR, BL).
    private static readonly Point[] LPadQ = PadQuad(F(293, 368), 157, 9.3), RPadQ = PadQuad(F(569, 368), 157, -9.3);
    private static readonly Point[] LPadInnerQ = PadQuad(F(293, 368), 149, 9.3), RPadInnerQ = PadQuad(F(569, 368), 149, -9.3);

    // Rear buttons seen through the body. Positions from Valve's back-view diagram, checked against a photo of the back.
    // Paddle 1 = upper (L4/R4), paddle 2 = lower (L5/R5), following SDL's naming.
    private static readonly Point RearL4 = F(249, 347), RearL5 = F(217, 447), RearR4 = Mirror(RearL4), RearR5 = Mirror(RearL5);
    private static readonly Point RearL4Label = F(206, 347), RearL5Label = F(182, 447);
    private static readonly Vector RearUpper = new(27.5 * K, 40.5 * K), RearLower = new(22 * K, 39 * K);

    // Capacitive grip sensors: strips on the back of each grip, near the outer edge.
    private static readonly Point GripLTop = F(143, 300), GripLBottom = F(127, 480);
    private const double GripWidth = 30 * K;

    // ---- Front edge, traced from a straight-on photo (left half, u/v in face-photo pixels) --------------------

    private static readonly double[] EdgeBodyUv =
        [0, -50, -70, -50, -140, -50, -195, -48.5, -230, -44, -250, -35, -259, -22, -263.5, -5, -264, 12, -261, 33, -255, 48, -245, 57, -232, 62, -205, 63, -120, 63, 0, 63];
    private static readonly double[] L2Uv =
    [
        -199.2, -104.2, -185, -101, -175.4, -95.6, -166, -87.4, -157, -74.8, -148.3, -56.8, -148, -40, -151.5, -28.5, -175, -26,
        -204, -22.5, -230, -22, -243, -17.5, -248, -10, -248, -30, -245.7, -52.2, -240, -70, -234.3, -84.2, -224, -97.7, -213.5, -103,
    ];
    private static readonly double[] L1Uv =
    [
        -250, -5, -244, -13, -230, -16.5, -204, -17, -175, -20.5, -157, -22.5, -146, -23, -140.5, -15, -139, 5, -140, 25, -144, 34.5,
        -153, 38.5, -180, 39.5, -210, 40, -234, 39, -246, 34, -254, 22, -257, 8,
    ];
    private const double StickU = 105;
    private static readonly Point GyroPt = T(0, -78);
    private static readonly Rect UsbRect = new(T(-18, -5), T(18, 5));

    // ---- Geometry built once ------------------------------------------------------------------------------

    private static readonly Geometry Body = Spline(Mirrored(Pts(BodyPx, F)), closed: true);
    private static readonly Geometry LBumper = Bumper(mirror: false), RBumper = Bumper(mirror: true);
    private static readonly Geometry LDeck = Spline(Pts(DeckPx, F), closed: false);
    private static readonly Geometry RDeck = Spline(Pts(DeckPx, F).Select(Mirror).ToArray(), closed: false);
    private static readonly Geometry DpadGeo = Cross(DpadC, DpadReach, DpadHalf, 8 * K, 5 * K);
    private static readonly Geometry LPadGeo = Rounded(LPadQ, 26 * K), RPadGeo = Rounded(RPadQ, 26 * K);
    private static readonly Geometry LPadInner = Rounded(LPadInnerQ, 22 * K), RPadInner = Rounded(RPadInnerQ, 22 * K);
    private static readonly Geometry EdgeBody = Spline(Mirrored(Pts(EdgeBodyUv, T)), closed: true);
    private static readonly Geometry LTrig = Spline(Pts(L2Uv, T), closed: true), RTrig = Spline(Pts(L2Uv, T).Select(Mirror).ToArray(), closed: true);
    private static readonly Geometry LBump = Spline(Pts(L1Uv, T), closed: true), RBump = Spline(Pts(L1Uv, T).Select(Mirror).ToArray(), closed: true);
    // The body's outline is hidden where the triggers stand in front of it.
    private static readonly Geometry EdgeClip = new CombinedGeometry(GeometryCombineMode.Exclude,
        new RectangleGeometry(new Rect(0, 0, W, Seam + 70 * K)), new CombinedGeometry(GeometryCombineMode.Union, LTrig, RTrig));

    private enum LabelSide { Below, Left, Right, Above }

    private sealed record Element(string Id, Geometry[] Hits, PhysicalInput? Input, LabelSide Side = LabelSide.Below, int Priority = 1)
    {
        public Rect Bounds { get; } = Hits[0].Bounds;
    }

    private static readonly Element[] Elements =
    [
        new("LT", [LTrig], null, LabelSide.Left), new("RT", [RTrig], null, LabelSide.Right),
        new("LB", [LBump, LBumper], PhysicalInput.LB, LabelSide.Left), new("RB", [RBump, RBumper], PhysicalInput.RB, LabelSide.Right),
        // Labels for the D-pad arms and face buttons point outward, so all four stay readable at once.
        new("DpadUp", [Box(new Rect(DpadC.X - DpadHalf, DpadC.Y - DpadReach, DpadHalf * 2, DpadReach - DpadHalf))], PhysicalInput.DpadUp, LabelSide.Above),
        new("DpadDown", [Box(new Rect(DpadC.X - DpadHalf, DpadC.Y + DpadHalf, DpadHalf * 2, DpadReach - DpadHalf))], PhysicalInput.DpadDown),
        new("DpadLeft", [Box(new Rect(DpadC.X - DpadReach, DpadC.Y - DpadHalf, DpadReach - DpadHalf, DpadHalf * 2))], PhysicalInput.DpadLeft, LabelSide.Left),
        new("DpadRight", [Box(new Rect(DpadC.X + DpadHalf, DpadC.Y - DpadHalf, DpadReach - DpadHalf, DpadHalf * 2))], PhysicalInput.DpadRight, LabelSide.Right),
        new("Y", [Disc(FaceC + new Vector(0, -FaceGap), FaceBezel)], PhysicalInput.Y, LabelSide.Above),
        new("X", [Disc(FaceC + new Vector(-FaceGap, 0), FaceBezel)], PhysicalInput.X, LabelSide.Left),
        new("B", [Disc(FaceC + new Vector(FaceGap, 0), FaceBezel)], PhysicalInput.B, LabelSide.Right),
        new("A", [Disc(FaceC + new Vector(0, FaceGap), FaceBezel)], PhysicalInput.A),
        new("View", [Box(ViewRect.Inflate(6))], PhysicalInput.View, LabelSide.Above), new("Menu", [Box(MenuRect.Inflate(6))], PhysicalInput.Menu, LabelSide.Above),
        new("Steam", [Disc(SteamC, SteamBezel + 2)], PhysicalInput.Steam), new("Qam", [Box(QamRect.Inflate(6))], PhysicalInput.Qam),
        new("LeftStick", [Disc(LStick, Collar + 4)], null), new("RightStick", [Disc(RStick, Collar + 4)], null),
        new("LeftPad", [LPadGeo], null), new("RightPad", [RPadGeo], null),
        // Rear buttons sit mostly behind the trackpads, so the pads win where they overlap; the label is always clickable.
        new("LeftPaddle1", [Oval(RearL4, RearUpper), Disc(RearL4Label, 16)], PhysicalInput.LeftPaddle1, LabelSide.Left, 0),
        new("LeftPaddle2", [Oval(RearL5, RearLower), Disc(RearL5Label, 16)], PhysicalInput.LeftPaddle2, LabelSide.Left, 0),
        new("RightPaddle1", [Oval(RearR4, RearUpper), Disc(Mirror(RearL4Label), 16)], PhysicalInput.RightPaddle1, LabelSide.Right, 0),
        new("RightPaddle2", [Oval(RearR5, RearLower), Disc(Mirror(RearL5Label), 16)], PhysicalInput.RightPaddle2, LabelSide.Right, 0),
        new("LeftGripTouch", [Box(Strip(GripLTop, GripLBottom))], PhysicalInput.LeftGripTouch, LabelSide.Below, 0),
        new("RightGripTouch", [Box(Strip(Mirror(GripLTop), Mirror(GripLBottom)))], PhysicalInput.RightGripTouch, LabelSide.Below, 0),
        new("Gyro", [Disc(GyroPt, 26)], null, LabelSide.Right),
    ];

    public static IReadOnlyList<string> ElementIds { get; } = Elements.Select(e => e.Id).ToArray();

    // Hit testing checks higher priority first, then later elements first.
    private static readonly Element[] HitOrder = Elements.Select((e, i) => (e, i)).OrderByDescending(x => x.e.Priority).ThenByDescending(x => x.i).Select(x => x.e).ToArray();

    // ---- Geometry helpers ----------------------------------------------------------------------------------

    private static Point[] Pts(double[] flat, Func<double, double, Point> map)
    {
        var pts = new Point[flat.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = map(flat[i * 2], flat[i * 2 + 1]);
        return pts;
    }

    /// <summary>A left half (from the centre line round to the centre line) completed with its mirror image.</summary>
    private static Point[] Mirrored(Point[] half)
    {
        var all = new List<Point>(half);
        for (int i = half.Length - 2; i >= 1; i--) all.Add(Mirror(half[i]));
        return [.. all];
    }

    /// <summary>Smooth curve through the points (Catmull-Rom, as cubic Béziers).</summary>
    private static Geometry Spline(Point[] p, bool closed)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(p[0], closed);
            SplineTo(g, p, closed);
            g.EndFigure(closed);
        }
        return geo;
    }

    private static void SplineTo(StreamGeometryContext g, Point[] p, bool closed)
    {
        int n = p.Length;
        for (int i = 0; i < (closed ? n : n - 1); i++)
        {
            var p0 = closed ? p[(i - 1 + n) % n] : p[Math.Max(i - 1, 0)];
            var p1 = p[i];
            var p2 = p[(i + 1) % n];
            var p3 = closed ? p[(i + 2) % n] : p[Math.Min(i + 2, n - 1)];
            g.CubicBezierTo(p1 + (p2 - p0) / 6, p2 - (p3 - p1) / 6, p2);
        }
    }

    /// <summary>The bumper seen from above: two open curves meeting in sharp tips.</summary>
    private static Geometry Bumper(bool mirror)
    {
        var outer = Pts(BumperOuterPx, F);
        var inner = Pts(BumperInnerPx, F);
        if (mirror)
        {
            outer = outer.Select(Mirror).ToArray();
            inner = inner.Select(Mirror).ToArray();
        }
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(outer[0], true);
            SplineTo(g, outer, false);
            SplineTo(g, inner, false);
            g.EndFigure(true);
        }
        return geo;
    }

    private static Point[] PadQuad(Point c, double side, double degrees)
    {
        double a = degrees * Math.PI / 180, h = side * K / 2, cos = Math.Cos(a), sin = Math.Sin(a);
        Point R(double x, double y) => new(c.X + x * cos - y * sin, c.Y + x * sin + y * cos);
        return [R(-h, -h), R(h, -h), R(h, h), R(-h, h)];
    }

    private static Geometry Rounded(Point[] corners, double r) => RoundedPolygon(corners.Select(c => (c, r)).ToArray());

    /// <summary>Polygon with each corner rounded (convex or concave) by a quadratic curve of the given radius.</summary>
    private static Geometry RoundedPolygon((Point At, double R)[] c)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            int n = c.Length;
            for (int i = 0; i < n; i++)
            {
                var prev = c[(i + n - 1) % n].At;
                var cur = c[i].At;
                var next = c[(i + 1) % n].At;
                var a = cur + Unit(prev - cur) * c[i].R;
                var b = cur + Unit(next - cur) * c[i].R;
                if (i == 0) g.BeginFigure(a, true);
                else g.LineTo(a);
                g.QuadraticBezierTo(cur, b);
            }
            g.EndFigure(true);
        }
        return geo;
    }

    /// <summary>The D-pad: a plus with rounded arm ends and small fillets where the arms meet.</summary>
    private static Geometry Cross(Point c, double reach, double half, double endR, double fillet)
    {
        Point P(double x, double y) => new(c.X + x, c.Y + y);
        return RoundedPolygon(
        [
            (P(-half, -reach), endR), (P(half, -reach), endR), (P(half, -half), fillet), (P(reach, -half), endR),
            (P(reach, half), endR), (P(half, half), fillet), (P(half, reach), endR), (P(-half, reach), endR),
            (P(-half, half), fillet), (P(-reach, half), endR), (P(-reach, -half), endR), (P(-half, -half), fillet),
        ]);
    }

    private static Rect PillRect(Point c, double w, double h) => new(c.X - w * K / 2, c.Y - h * K / 2, w * K, h * K);
    private static Rect Strip(Point a, Point b) => new Rect(a, b).Normalize().Inflate(GripWidth / 2);
    private static Geometry Box(Rect r) => new RectangleGeometry(r);
    private static Geometry Disc(Point c, double r) => new EllipseGeometry(new Rect(c.X - r, c.Y - r, r * 2, r * 2));
    private static Geometry Oval(Point c, Vector r) => new EllipseGeometry(new Rect(c.X - r.X, c.Y - r.Y, r.X * 2, r.Y * 2));
    private static Vector Unit(Vector v) => v / Math.Sqrt(v.X * v.X + v.Y * v.Y);

    /// <summary>Bilinear position inside a pad quad for normalised touch coordinates (0..1, origin top-left).</summary>
    private static Point OnQuad(Point[] q, double u, double v)
    {
        u = Math.Clamp(u, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var top = q[0] + (q[1] - q[0]) * u;
        var bottom = q[3] + (q[2] - q[3]) * u;
        return top + (bottom - top) * v;
    }

    // ---- Animation state -----------------------------------------------------------------------------------

    private readonly Spring[] _press = new Spring[PhysicalInputs.Count];
    private Spring _lt = new(600), _rt = new(600);
    private Spring _lsx = new(700, 0.8f), _lsy = new(700, 0.8f), _rsx = new(700, 0.8f), _rsy = new(700, 0.8f);
    private Spring _lsTouch = new(260), _rsTouch = new(260);
    private Spring _lpVis = new(300), _rpVis = new(300), _lpPress = new(400), _rpPress = new(400);
    private Spring _alive = new(80, 1f), _gyroGlow = new(200);
    private Spring _hover = new(500);
    private float _motion;

    private readonly Trail _lTrail = new(), _rTrail = new();

    private ControllerState _state;
    private bool _connected;
    private string? _hoverId, _selected;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame;
    private bool _running;

    public HologramView()
    {
        ClipToBounds = false;
        for (int i = 0; i < _press.Length; i++) _press[i] = new Spring(700, 0.75f);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _running = true;
        Glass.Resumed += Resume;
        Resume();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _running = false;
        Glass.Resumed -= Resume;
        base.OnDetachedFromVisualTree(e);
    }

    private bool _loopActive;

    /// <summary>Starts the frame loop unless one is already pending (prevents doubled loops on quick re-attach).</summary>
    private void Resume()
    {
        if (!_running || _loopActive) return;
        _loopActive = true;
        _lastFrame = _clock.Elapsed.TotalSeconds;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    private int _frameCount;

    private void OnFrame(TimeSpan _)
    {
        if (!_running || Glass.Paused)
        {
            _loopActive = false;
            return;
        }
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _lastFrame, 0, 0.1);
        _lastFrame = now;

        if (Demo)
        {
            _connected = true;
            _state = DemoState();
        }
        else
        {
            _connected = ConnectedSource?.Invoke() ?? false;
            _state = _connected && StateSource is not null ? StateSource() : default;
        }
        Animate(dt, now);
        // Anything moving gets every frame. At rest only the slow scan line moves: 15 fps is plenty.
        bool calm = _motion < 0.02f && MathF.Abs(_alive.Velocity) < 0.01f && MathF.Abs(_hover.Velocity) < 0.01f;
        if (!calm || (_frameCount++ & 3) == 0) InvalidateVisual();
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    // Screenshot aid: with BOGIHUB_HOLO_DEMO=1 the hologram shows a fixed set of inputs instead of the controller.
    private static readonly bool Demo = Environment.GetEnvironmentVariable("BOGIHUB_HOLO_DEMO") == "1";

    private static ControllerState DemoState()
    {
        var s = new ControllerState
        {
            LeftTrigger = 0.62f,
            RightTrigger = 0.2f,
            LeftStick = new System.Numerics.Vector2(0.7f, 0.35f),
            RightPad = new TouchPoint(true, 0.35f, 0.6f, 0.5f),
            Gyro = new System.Numerics.Vector3(0.6f, 0, 0),
            MotionValid = true,
        };
        foreach (var i in new[] { PhysicalInput.LB, PhysicalInput.A, PhysicalInput.Steam, PhysicalInput.LeftPaddle1, PhysicalInput.RightGripTouch, PhysicalInput.LeftStickTouch, PhysicalInput.DpadRight })
            s.Set(i, true);
        return s;
    }

    private void Animate(float dt, double now)
    {
        ref var s = ref _state;
        float motion = 0;
        for (int i = 0; i < _press.Length; i++)
        {
            _press[i].Target = s.IsPressed((PhysicalInput)i) ? 1 : 0;
            _press[i].Step(dt);
            motion += MathF.Abs(_press[i].Velocity);
        }

        // Resting jitter (sticks within a few percent of centre, a still gyro) is too small to see; ignoring it lets
        // the drawing settle so the frame loop can slow down.
        motion += Step(ref _lt, s.LeftTrigger, dt) + Step(ref _rt, s.RightTrigger, dt);
        motion += Step(ref _lsx, Dz(s.LeftStick.X), dt) + Step(ref _lsy, Dz(s.LeftStick.Y), dt);
        motion += Step(ref _rsx, Dz(s.RightStick.X), dt) + Step(ref _rsy, Dz(s.RightStick.Y), dt);
        motion += Step(ref _lsTouch, s.IsPressed(PhysicalInput.LeftStickTouch) ? 1 : 0, dt);
        motion += Step(ref _rsTouch, s.IsPressed(PhysicalInput.RightStickTouch) ? 1 : 0, dt);
        motion += Step(ref _lpVis, s.LeftPad.Touching ? 1 : 0, dt) + Step(ref _rpVis, s.RightPad.Touching ? 1 : 0, dt);
        motion += Step(ref _lpPress, s.LeftPad.Touching ? s.LeftPad.Pressure : 0, dt);
        motion += Step(ref _rpPress, s.RightPad.Touching ? s.RightPad.Pressure : 0, dt);
        float spin = s.MotionValid ? Math.Clamp(s.Gyro.Length() / 3f, 0, 1) : 0;
        motion += Step(ref _gyroGlow, spin < 0.03f ? 0 : spin, dt);
        Step(ref _alive, _connected ? 1 : 0, dt);
        Step(ref _hover, _hoverId is null ? 0 : 1, dt);
        if (s.LeftPad.Touching || s.RightPad.Touching) motion += 1;
        _motion = motion;

        _lTrail.Update(s.LeftPad, now);
        _rTrail.Update(s.RightPad, now);
    }

    private static float Dz(float v) => MathF.Abs(v) < 0.04f ? 0 : v;

    private static float Step(ref Spring sp, float target, float dt)
    {
        sp.Target = target;
        sp.Step(dt);
        return MathF.Abs(sp.Velocity);
    }

    // ---- Rendering -----------------------------------------------------------------------------------------

    private double _scale = 1;
    private Point _offset;

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        _scale = Math.Min(b.Width / W, b.Height / H);
        _offset = new Point((b.Width - W * _scale) / 2, (b.Height - H * _scale) / 2);
        if (_scale <= 0) return;

        float alive = Math.Clamp(_alive.Value, 0, 1);
        double px = 1 / _scale;

        using (ctx.PushTransform(Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(_offset.X, _offset.Y)))
        {
            DrawProjector(ctx, alive);
            using (ctx.PushOpacity(0.35 + 0.65 * alive))
            {
                DrawFrontEdge(ctx, px);
                DrawBody(ctx, px, alive);
                DrawDeck(ctx, px);
                DrawGrips(ctx, px);
                DrawRear(ctx, px);
                DrawPad(ctx, px, LPadQ, LPadGeo, LPadInner, _state.LeftPad, _lpVis.Value, _lpPress.Value, P(PhysicalInput.LeftPadClick), _lTrail, "LeftPad");
                DrawPad(ctx, px, RPadQ, RPadGeo, RPadInner, _state.RightPad, _rpVis.Value, _rpPress.Value, P(PhysicalInput.RightPadClick), _rTrail, "RightPad");
                DrawBumper(ctx, px, LBumper, P(PhysicalInput.LB), "LB");
                DrawBumper(ctx, px, RBumper, P(PhysicalInput.RB), "RB");
                DrawDpad(ctx, px);
                DrawFace(ctx, px);
                DrawCenter(ctx, px, alive);
                DrawStick(ctx, px, LStick, _lsx.Value, _lsy.Value, _lsTouch.Value, P(PhysicalInput.LeftStickClick), "LeftStick");
                DrawStick(ctx, px, RStick, _rsx.Value, _rsy.Value, _rsTouch.Value, P(PhysicalInput.RightStickClick), "RightStick");
                DrawLabels(ctx, px);
            }
            if (alive < 0.98) DrawWaiting(ctx, 1 - alive);
        }
    }

    private float P(PhysicalInput i) => Math.Clamp(_press[(int)i].Value, 0, 1.2f);

    /// <summary>Hologram colour: a soft, cool white.</summary>
    private static readonly Color A = Color.Parse("#F3F6F9");

    // Pens and brushes are cached by (alpha, width, scale, style) so a frame allocates almost nothing.
    private static readonly Dictionary<(int, int, int, int), IPen> PenCache = [];
    private static readonly IImmutableSolidColorBrush[] FillCache =
        [.. Enumerable.Range(0, 256).Select(a => new ImmutableSolidColorBrush(Color.FromArgb((byte)a, A.R, A.G, A.B)))];
    private static readonly ImmutableDashStyle Dashed = new([3, 3], 0);
    private static readonly ImmutableDashStyle Knurl = new([0.35, 0.9], 0);

    /// <summary>Style 0 = solid (width in screen pixels), 1 = dashed, 2 = knurl ticks, 3 = solid with width in design units.</summary>
    private static IPen CachedPen(double px, double alpha, double width, int style = 0)
    {
        var key = ((int)(Math.Clamp(alpha, 0, 1) * 255), (int)(width * 10), style == 3 ? 0 : (int)(px * 1000), style);
        if (PenCache.TryGetValue(key, out var pen)) return pen;
        if (PenCache.Count > 3000) PenCache.Clear();
        var brush = FillCache[key.Item1];
        double thickness = style == 3 ? width : width * px;
        pen = style switch
        {
            1 => new ImmutablePen(brush, thickness, Dashed, PenLineCap.Round),
            2 => new ImmutablePen(brush, thickness, Knurl, PenLineCap.Flat),
            _ => new ImmutablePen(brush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
        };
        PenCache[key] = pen;
        return pen;
    }

    private static IPen Line(double px, double alpha = 0.9, double width = 1.6) => CachedPen(px, alpha, width);

    private static IPen Glow(double px, double alpha = 0.10, double width = 8) => CachedPen(px, alpha, width);

    private static IBrush Fill(double alpha) => FillCache[(int)(Math.Clamp(alpha, 0, 1) * 255)];

    private double Emph(string id) => (id == _selected ? 0.35 : 0) + (id == _hoverId ? 0.3 * _hover.Value : 0);

    private void DrawProjector(DrawingContext ctx, float alive)
    {
        var rg = new RadialGradientBrush
        {
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Palette.WithAlpha(A, 0.12 * (0.4 + 0.6 * alive)), 0), new GradientStop(Palette.WithAlpha(A, 0), 1) },
        };
        ctx.DrawEllipse(rg, null, new Point(500, 988), 400, 24);
    }

    // ---- Front edge ------------------------------------------------------------------------------------------

    private void DrawFrontEdge(DrawingContext ctx, double px)
    {
        DrawText(ctx, "FRONT EDGE", T(0, 78), 14, Palette.WithAlpha(Palette.Muted, 0.8), center: true, middle: true, bold: true);

        using (ctx.PushGeometryClip(EdgeClip))
            ctx.DrawGeometry(Fill(0.03), Line(px, 0.62), EdgeBody);

        // The seam runs either side of the USB-C port.
        var seam = Line(px, 0.3, 1.2);
        ctx.DrawLine(seam, T(-139, 0), T(-19, 0));
        ctx.DrawLine(seam, T(19, 0), T(139, 0));
        ctx.DrawRectangle(Fill(0.05), Line(px, 0.7, 1.4), UsbRect, UsbRect.Height / 2, UsbRect.Height / 2);
        ctx.DrawLine(Line(px, 0.4, 1.2), T(-11, 0), T(11, 0));

        // The sticks stand out below the face; they lean the way they are pushed.
        EdgeStick(ctx, px, -StickU, _lsx.Value, P(PhysicalInput.LeftStickClick), _lsTouch.Value, "LeftStick");
        EdgeStick(ctx, px, StickU, _rsx.Value, P(PhysicalInput.RightStickClick), _rsTouch.Value, "RightStick");

        EdgeBumper(ctx, px, LBump, P(PhysicalInput.LB), "LB", "L1", T(-163, -6));
        EdgeBumper(ctx, px, RBump, P(PhysicalInput.RB), "RB", "R1", T(163, -6));
        EdgeTrigger(ctx, px, LTrig, _lt.Value, _state.LeftTrigger, "LT", "L2", T(-166, -40), T(-198, -64));
        EdgeTrigger(ctx, px, RTrig, _rt.Value, _state.RightTrigger, "RT", "R2", T(166, -40), T(198, -64));
        DrawGyro(ctx, px);
    }

    private void EdgeStick(DrawingContext ctx, double px, double u, float x, float click, float touch, string id)
    {
        var pivot = T(u, 63);
        double angle = -Math.Clamp(x, -1.2f, 1.2f) * 0.3;
        var m = Matrix.CreateTranslation(-pivot.X, -pivot.Y) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(pivot.X, pivot.Y - 3 * click);
        double e = Emph(id);
        using (ctx.PushTransform(m))
        {
            var stem = new Rect(T(u - 13, 63), T(u + 13, 84));
            var cap = new Rect(T(u - 37.5, 82), T(u + 37.5, 97));
            ctx.DrawRectangle(Fill(0.03), Line(px, 0.4 + e, 1.2), stem, 4, 4);
            if (touch > 0.01) ctx.DrawRectangle(null, Glow(px, 0.22 * touch, 12), cap, 7, 7);
            ctx.DrawRectangle(Fill(0.06 + 0.5 * click + 0.06 * touch + e * 0.08), Line(px, 0.75 + e), cap, 6.7, 6.7);
            ctx.DrawLine(CachedPen(px, 0.2, 7, 2), new Point(cap.X + 9, cap.Center.Y), new Point(cap.Right - 9, cap.Center.Y));
        }
    }

    private void EdgeBumper(DrawingContext ctx, double px, Geometry g, float p, string id, string label, Point labelAt)
    {
        double e = Emph(id);
        if (p > 0.05) ctx.DrawGeometry(null, Glow(px, 0.22 * p, 12), g);
        ctx.DrawGeometry(Fill(0.04 + 0.6 * p + e * 0.15), Line(px, 0.72 + e), g);
        DrawText(ctx, label, labelAt, 15, p > 0.5 ? Palette.Bg : Palette.WithAlpha(A, 0.5), center: true, middle: true, bold: true);
    }

    /// <summary>
    /// Trigger seen from the front. Pulling it fills it from the hinge (next to the bumper) toward the tip, and the
    /// tip tucks in slightly toward the body, as on the real controller. The embossed name gives way to the depth.
    /// </summary>
    private void EdgeTrigger(DrawingContext ctx, double px, Geometry g, float v, float raw, string id, string label, Point labelAt, Point valueAt)
    {
        v = Math.Clamp(v, 0, 1);
        double e = Emph(id);
        var r = g.Bounds;
        double hinge = r.Bottom;
        var squash = Matrix.CreateTranslation(0, -hinge) * Matrix.CreateScale(1, 1 - 0.1 * v) * Matrix.CreateTranslation(0, hinge);
        using (ctx.PushTransform(squash))
        {
            if (v > 0.02) ctx.DrawGeometry(null, Glow(px, 0.18 * v, 12), g);
            ctx.DrawGeometry(Fill(0.035 + e * 0.1), Line(px, 0.78 + e), g);
            if (v > 0.002)
            {
                double top = r.Bottom - r.Height * v;
                using (ctx.PushGeometryClip(g))
                {
                    ctx.DrawRectangle(Fill(0.2 + 0.55 * v), null, new Rect(r.X - 2, top, r.Width + 4, r.Bottom - top + 2));
                    ctx.DrawLine(Line(px, Math.Min(1, v * 4) * 0.9, 1.4), new Point(r.X, top), new Point(r.Right, top));
                }
            }
        }
        double show = Math.Clamp(v * 6, 0, 1);
        if (show < 0.99) DrawText(ctx, label, labelAt, 15, Palette.WithAlpha(A, 0.5 * (1 - show)), center: true, middle: true, bold: true);
        if (show > 0.01)
        {
            // A dark halo keeps the number readable whether it sits on the fill or above it.
            string text = $"{raw * 100:0}%";
            var halo = Palette.WithAlpha(Palette.Bg, 0.8 * show);
            double o = 1.5 * px;
            foreach (var d in new[] { new Vector(-o, 0), new Vector(o, 0), new Vector(0, -o), new Vector(0, o) })
                DrawText(ctx, text, valueAt + d, 22, halo, center: true, middle: true, bold: true);
            DrawText(ctx, text, valueAt, 22, Palette.WithAlpha(A, show), center: true, middle: true, bold: true);
        }
    }

    private void DrawGyro(DrawingContext ctx, double px)
    {
        float g = Math.Clamp(_gyroGlow.Value, 0, 1);
        double e = Emph("Gyro");
        if (g > 0.02) ctx.DrawEllipse(null, Glow(px, 0.25 * g, 14), GyroPt, 17, 17);
        ctx.DrawEllipse(Fill(0.04 + 0.25 * g + e * 0.1), Line(px, 0.55 + e), GyroPt, 14, 14);
        double t = _clock.Elapsed.TotalSeconds * (0.6 + 4 * g);
        using (ctx.PushTransform(Matrix.CreateTranslation(-GyroPt.X, -GyroPt.Y) * Matrix.CreateRotation(t % (Math.PI * 2)) * Matrix.CreateTranslation(GyroPt.X, GyroPt.Y)))
            ctx.DrawEllipse(null, Line(px, 0.5 + 0.4 * g, 1.2), GyroPt, 23, 7.5);
        DrawText(ctx, "GYRO", new Point(GyroPt.X + 32, GyroPt.Y), 14, Palette.WithAlpha(A, 0.45 + 0.4 * g), middle: true, bold: true);
    }

    // ---- Face ------------------------------------------------------------------------------------------------

    private void DrawBody(DrawingContext ctx, double px, float alive)
    {
        ctx.DrawGeometry(Fill(0.03 + 0.015 * alive), Glow(px, 0.06, 10), Body);
        ctx.DrawGeometry(null, Line(px, 0.7), Body);

        // Slow scan line sweeping down the body: the "hologram" read, kept faint so it never competes with input.
        double y = FaceTop + (_clock.Elapsed.TotalSeconds * 120 % 720);
        var band = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Palette.WithAlpha(A, 0), 0), new GradientStop(Palette.WithAlpha(A, 0.045 * alive), 0.85), new GradientStop(Palette.WithAlpha(A, 0), 1) },
        };
        using (ctx.PushGeometryClip(Body))
            ctx.DrawRectangle(band, null, new Rect(20, y - 70, 960, 74));
    }

    // The deck edge fades out toward the bottom of each grip, as it does on the controller.
    private static readonly IImmutableBrush DeckFade = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Palette.WithAlpha(A, 0.34), 0), new GradientStop(Palette.WithAlpha(A, 0.26), 0.6), new GradientStop(Palette.WithAlpha(A, 0), 1) },
    }.ToImmutable();
    private IPen? _deckPen;
    private double _deckPenPx;

    private void DrawDeck(DrawingContext ctx, double px)
    {
        if (_deckPen is null || _deckPenPx != px)
        {
            _deckPen = new ImmutablePen(DeckFade, 1.2 * px, lineCap: PenLineCap.Round);
            _deckPenPx = px;
        }
        ctx.DrawGeometry(null, _deckPen, LDeck);
        ctx.DrawGeometry(null, _deckPen, RDeck);
        // Between the sticks the deck edge is straight; the stick collars cover where it meets them.
        double y = F(0, 256).Y, dx = Math.Sqrt(Collar * Collar - Math.Pow(y - LStick.Y, 2));
        ctx.DrawLine(Line(px, 0.34, 1.2), new Point(LStick.X + dx, y), new Point(RStick.X - dx, y));
    }

    private void DrawGrips(DrawingContext ctx, double px)
    {
        Grip(ctx, GripLTop, GripLBottom, P(PhysicalInput.LeftGripTouch), "LeftGripTouch");
        Grip(ctx, Mirror(GripLTop), Mirror(GripLBottom), P(PhysicalInput.RightGripTouch), "RightGripTouch");
    }

    /// <summary>Grip sensor strip: barely there at rest, a soft glow while a hand rests on it.</summary>
    private void Grip(DrawingContext ctx, Point a, Point b, float touch, string id)
    {
        double t = Math.Clamp(touch, 0, 1), e = Emph(id) * 0.12;
        ctx.DrawLine(CachedPen(1, 0.035 + 0.09 * t + e, GripWidth, 3), a, b);
        ctx.DrawLine(CachedPen(1, 0.04 + 0.16 * t + e, GripWidth * 0.55, 3), a, b);
        ctx.DrawLine(CachedPen(1, 0.05 + 0.32 * t + e, GripWidth * 0.2, 3), a, b);
    }

    private void DrawRear(DrawingContext ctx, double px)
    {
        Rear(ctx, px, RearL4, RearUpper, 12, PhysicalInput.LeftPaddle1, "L4", "LeftPaddle1", RearL4Label);
        Rear(ctx, px, RearL5, RearLower, 8, PhysicalInput.LeftPaddle2, "L5", "LeftPaddle2", RearL5Label);
        Rear(ctx, px, RearR4, RearUpper, -12, PhysicalInput.RightPaddle1, "R4", "RightPaddle1", Mirror(RearL4Label));
        Rear(ctx, px, RearR5, RearLower, -8, PhysicalInput.RightPaddle2, "R5", "RightPaddle2", Mirror(RearL5Label));
    }

    /// <summary>Rear button drawn see-through (dashed), tilted like the real one; it fills in while pressed.</summary>
    private void Rear(DrawingContext ctx, double px, Point c, Vector radii, double tilt, PhysicalInput input, string label, string id, Point labelAt)
    {
        float p = P(input);
        double e = Emph(id);
        using (ctx.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(tilt * Math.PI / 180) * Matrix.CreateTranslation(c.X, c.Y)))
        {
            if (p > 0.02) ctx.DrawEllipse(null, Glow(px, 0.25 * p, 14), c, radii.X, radii.Y);
            ctx.DrawEllipse(Fill(0.015 + 0.5 * p + e * 0.1), CachedPen(px, 0.34 + 0.5 * p + e, 1.3, 1), c, radii.X, radii.Y);
        }
        DrawText(ctx, label, labelAt, 15, Palette.WithAlpha(A, 0.42 + 0.5 * p + e), center: true, middle: true, bold: true);
    }

    private void DrawBumper(DrawingContext ctx, double px, Geometry g, float p, string id)
    {
        double e = Emph(id);
        if (p > 0.05) ctx.DrawGeometry(null, Glow(px, 0.2 * p, 12), g);
        ctx.DrawGeometry(Fill(0.05 + 0.6 * p + e * 0.15), Line(px, 0.65 + e), g);
    }

    private void DrawDpad(DrawingContext ctx, double px)
    {
        double e = Math.Max(Math.Max(Emph("DpadUp"), Emph("DpadDown")), Math.Max(Emph("DpadLeft"), Emph("DpadRight")));
        ctx.DrawGeometry(Fill(0.04), Line(px, 0.78 + e * 0.5), DpadGeo);
        using (ctx.PushGeometryClip(DpadGeo))
        {
            Arm(ctx, px, new Rect(DpadC.X - DpadHalf, DpadC.Y - DpadReach, DpadHalf * 2, DpadReach), PhysicalInput.DpadUp, "DpadUp");
            Arm(ctx, px, new Rect(DpadC.X - DpadHalf, DpadC.Y, DpadHalf * 2, DpadReach), PhysicalInput.DpadDown, "DpadDown");
            Arm(ctx, px, new Rect(DpadC.X - DpadReach, DpadC.Y - DpadHalf, DpadReach, DpadHalf * 2), PhysicalInput.DpadLeft, "DpadLeft");
            Arm(ctx, px, new Rect(DpadC.X, DpadC.Y - DpadHalf, DpadReach, DpadHalf * 2), PhysicalInput.DpadRight, "DpadRight");
        }
        // The real D-pad has a shallow dish in the middle.
        ctx.DrawEllipse(null, Line(px, 0.22, 1), DpadC, 11, 11);
    }

    private void Arm(DrawingContext ctx, double px, Rect r, PhysicalInput input, string id)
    {
        float p = P(input);
        double e = Emph(id);
        if (p < 0.01 && e < 0.01) return;
        ctx.DrawRectangle(Fill(0.6 * p + e * 0.18), null, r);
    }

    private void DrawFace(DrawingContext ctx, double px)
    {
        FaceButton(ctx, px, FaceC + new Vector(0, -FaceGap), PhysicalInput.Y, "Y");
        FaceButton(ctx, px, FaceC + new Vector(-FaceGap, 0), PhysicalInput.X, "X");
        FaceButton(ctx, px, FaceC + new Vector(FaceGap, 0), PhysicalInput.B, "B");
        FaceButton(ctx, px, FaceC + new Vector(0, FaceGap), PhysicalInput.A, "A");
    }

    private void FaceButton(DrawingContext ctx, double px, Point c, PhysicalInput input, string label)
    {
        float p = P(input);
        double e = Emph(label);
        double r = FaceR * (1 - 0.07 * p);
        ctx.DrawEllipse(null, Line(px, 0.26, 1), c, FaceBezel, FaceBezel);
        if (p > 0.02) ctx.DrawEllipse(null, Glow(px, 0.25 * p, 14), c, r + 2, r + 2);
        ctx.DrawEllipse(Fill(0.05 + 0.8 * p + e * 0.15), Line(px, 0.82 + e), c, r, r);
        DrawText(ctx, label, c, 25, p > 0.5 ? Palette.Bg : Palette.WithAlpha(A, 0.95), center: true, middle: true, bold: true);
    }

    private void DrawCenter(DrawingContext ctx, double px, float alive)
    {
        DrawSmiley(ctx, px);

        // Status light between View and Menu: lit while a controller is connected.
        if (alive > 0.05) ctx.DrawEllipse(Fill(0.12 * alive), null, Led, 10, 10);
        ctx.DrawEllipse(Fill(0.25 + 0.7 * alive), null, Led, 3.4, 3.4);

        Pill(ctx, px, ViewRect, P(PhysicalInput.View), "View");
        Pill(ctx, px, MenuRect, P(PhysicalInput.Menu), "Menu");
        Pill(ctx, px, QamRect, P(PhysicalInput.Qam), "Qam");

        // Glyphs: View = two overlapping windows, Menu = three lines, QAM = three dots.
        var glyph = Line(px, 0.72, 1.4);
        var v = ViewRect.Center;
        ctx.DrawRectangle(null, glyph, new Rect(v.X - 10, v.Y - 6, 12, 8), 1, 1);
        ctx.DrawRectangle(null, glyph, new Rect(v.X - 3, v.Y - 2, 12, 8), 1, 1);
        var m = MenuRect.Center;
        for (int i = -1; i <= 1; i++)
            ctx.DrawLine(glyph, new Point(m.X - 9, m.Y + i * 4.5), new Point(m.X + 9, m.Y + i * 4.5));
        var q = QamRect.Center;
        for (int i = -1; i <= 1; i++)
            ctx.DrawEllipse(Fill(0.85), null, new Point(q.X + i * 11.3 * K, q.Y), 3, 3);
    }

    /// <summary>Pill button inside its bezel, like View, Menu and "…" on the real controller.</summary>
    private void Pill(DrawingContext ctx, double px, Rect r, float p, string id)
    {
        double rad = r.Height / 2;
        ctx.DrawRectangle(null, Line(px, 0.24, 1), r, rad, rad);
        var inner = r.Deflate(new Thickness(r.Height * 0.16));
        double ir = inner.Height / 2;
        if (p > 0.02) ctx.DrawRectangle(null, Glow(px, 0.25 * p, 12), inner, ir, ir);
        ctx.DrawRectangle(Fill(0.05 + 0.7 * p + Emph(id) * 0.15), Line(px, 0.78 + Emph(id)), inner, ir, ir);
    }

    // The middle button gets a smiley (not Valve's logo); the smile widens while it is pressed.
    private readonly Geometry?[] _smiles = new Geometry?[11];

    private void DrawSmiley(DrawingContext ctx, double px)
    {
        float p = P(PhysicalInput.Steam);
        double e = Emph("Steam");
        var c = SteamC;
        ctx.DrawEllipse(null, Line(px, 0.26, 1), c, SteamBezel, SteamBezel);
        if (p > 0.02) ctx.DrawEllipse(null, Glow(px, 0.3 * p, 16), c, SteamR + 2, SteamR + 2);
        ctx.DrawEllipse(Fill(0.05 + 0.75 * p + e * 0.15), Line(px, 0.85 + e), c, SteamR, SteamR);

        var ink = p > 0.5 ? Palette.Bg : A;
        var brush = new ImmutableSolidColorBrush(Palette.WithAlpha(ink, 0.92));
        ctx.DrawEllipse(brush, null, c + new Vector(-8.5, -6.5), 2.7, 3.4);
        ctx.DrawEllipse(brush, null, c + new Vector(8.5, -6.5), 2.7, 3.4);
        int level = (int)Math.Round(Math.Clamp(p, 0, 1) * 10);
        var smile = _smiles[level] ??= Smile(c, level / 10.0);
        ctx.DrawGeometry(null, new ImmutablePen(brush, 2.6 * px, lineCap: PenLineCap.Round), smile);
    }

    private static Geometry Smile(Point c, double grin)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(c + new Vector(-12.5 - 1.5 * grin, 3), false);
            g.QuadraticBezierTo(c + new Vector(0, 16 + 6 * grin), c + new Vector(12.5 + 1.5 * grin, 3));
            g.EndFigure(false);
        }
        return geo;
    }

    private void DrawStick(DrawingContext ctx, double px, Point c, float x, float y, float touch, float click, string id)
    {
        double e = Emph(id);
        if (touch > 0.01) ctx.DrawEllipse(null, Glow(px, 0.22 * touch, 16), c, Collar + 4, Collar + 4);
        ctx.DrawEllipse(Fill(0.03 + e * 0.08), Line(px, 0.5 + e), c, Collar, Collar);

        var k = new Point(c.X + Math.Clamp(x, -1.2f, 1.2f) * StickTravel, c.Y - Math.Clamp(y, -1.2f, 1.2f) * StickTravel);
        double r = Knob * (1 - 0.05 * click);
        ctx.DrawEllipse(Fill(0.10 + 0.6 * click + 0.08 * touch), Line(px, 0.9), k, r, r);
        // Knurled rim and concave dish, like the real stick caps.
        ctx.DrawEllipse(null, CachedPen(px, 0.22, 4, 2), k, r - 4, r - 4);
        ctx.DrawEllipse(null, Line(px, 0.32, 1), k, Dish * (1 - 0.05 * click), Dish * (1 - 0.05 * click));
        ctx.DrawEllipse(null, Line(px, 0.14, 1), k, Dish * 0.45, Dish * 0.45);
    }

    private void DrawPad(DrawingContext ctx, double px, Point[] q, Geometry geo, Geometry inner, TouchPoint t, float vis, float pressure, float click, Trail trail, string id)
    {
        double e = Emph(id);
        if (click > 0.05) ctx.DrawGeometry(null, Glow(px, 0.2 * click, 12), geo);
        ctx.DrawGeometry(Fill(0.03 + 0.14 * click + e * 0.08), Line(px, 0.62 + 0.3 * click + e), geo);
        ctx.DrawGeometry(null, Line(px, 0.12, 1), inner);

        var dot = Fill(0.10);
        for (int i = 1; i < 4; i++)
            for (int j = 1; j < 4; j++)
                ctx.DrawEllipse(dot, null, OnQuad(q, i / 4.0, j / 4.0), 2, 2);

        trail.Draw(ctx, q, px, _clock.Elapsed.TotalSeconds);

        if (vis > 0.01)
        {
            var p = t.Touching ? OnQuad(q, t.X, t.Y) : trail.LastPoint(q) ?? OnQuad(q, 0.5, 0.5);
            double radius = 10 + 18 * Math.Clamp(pressure, 0, 1);
            ctx.DrawEllipse(Fill(0.08 * vis), null, p, radius + 14, radius + 14);
            ctx.DrawEllipse(Fill(0.75 * vis), Glow(px, 0.3 * vis, 10), p, radius * 0.55, radius * 0.55);
        }
    }

    private void DrawLabels(DrawingContext ctx, double px)
    {
        if (LabelFor is null) return;
        foreach (var el in Elements)
        {
            bool show = ShowAllLabels || el.Id == _hoverId || el.Id == _selected;
            if (!show) continue;
            string? label = LabelFor(el.Id);
            if (string.IsNullOrEmpty(label)) continue;

            var ft = Format(label, 22, Palette.Text, bold: false);
            double w = ft.Width + 18, h = ft.Height + 8;
            var r = el.Bounds;
            var box = el.Side switch
            {
                LabelSide.Left => new Rect(r.X - 10 - w, r.Center.Y - h / 2, w, h),
                LabelSide.Right => new Rect(r.Right + 10, r.Center.Y - h / 2, w, h),
                LabelSide.Above => new Rect(r.Center.X - w / 2, r.Top - 6 - h, w, h),
                _ => new Rect(r.Center.X - w / 2, r.Bottom + 6, w, h),
            };
            ctx.DrawRectangle(new ImmutableSolidColorBrush(Palette.WithAlpha(Palette.Raised, 0.94)), Line(px, 0.4, 1), box, 7, 7);
            ctx.DrawText(ft, new Point(box.X + 9, box.Y + 4));
        }
    }

    private void DrawWaiting(DrawingContext ctx, double strength)
    {
        double pulse = 0.6 + 0.4 * Math.Sin(_clock.Elapsed.TotalSeconds * 2.2);
        DrawText(ctx, "Waiting for controller", new Point(500, 600), 36, Palette.WithAlpha(Palette.Text, strength * 0.9), center: true, middle: true, bold: true);
        DrawText(ctx, "Turn it on, or plug in the puck / USB cable", new Point(500, 648), 22, Palette.WithAlpha(Palette.Muted, strength * pulse), center: true, middle: true);
    }

    // ---- Text ------------------------------------------------------------------------------------------------

    private readonly Dictionary<(string, double, Color, bool), FormattedText> _textCache = [];
    private static readonly Typeface Regular = new("Inter");
    private static readonly Typeface Bold = new("Inter", FontStyle.Normal, FontWeight.SemiBold);

    private FormattedText Format(string text, double size, Color color, bool bold)
    {
        var key = (text, size, color, bold);
        if (_textCache.TryGetValue(key, out var ft)) return ft;
        if (_textCache.Count > 400) _textCache.Clear();
        ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Bold : Regular, size, new ImmutableSolidColorBrush(color));
        _textCache[key] = ft;
        return ft;
    }

    private void DrawText(DrawingContext ctx, string text, Point at, double size, Color color, bool center = false, bool middle = false, bool right = false, bool bold = false)
    {
        // Quantise alpha so animated text reuses cached layouts.
        color = Color.FromArgb((byte)(color.A / 8 * 8), color.R, color.G, color.B);
        var ft = Format(text, size, color, bold);
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        double y = middle ? at.Y - ft.Height / 2 : at.Y;
        ctx.DrawText(ft, new Point(x, y));
    }

    // ---- Interaction -----------------------------------------------------------------------------------------

    private string? HitTest(Point p)
    {
        var d = new Point((p.X - _offset.X) / _scale, (p.Y - _offset.Y) / _scale);
        foreach (var el in HitOrder)
            foreach (var g in el.Hits)
                if (g.FillContains(d)) return el.Id;
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        string? id = HitTest(e.GetPosition(this));
        if (id != _hoverId)
        {
            _hoverId = id;
            _hover.Snap(0);
        }
        Cursor = Interactive && id is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverId = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!Interactive) return;
        string? id = HitTest(e.GetPosition(this));
        if (id is null) return;
        SelectedId = id;
        ElementClicked?.Invoke(id);
        e.Handled = true;
    }

    /// <summary>Short-lived fading trail of recent touch positions on one trackpad.</summary>
    private sealed class Trail
    {
        private const int Max = 28;
        private const double Life = 0.4;
        private readonly (float X, float Y, double T)[] _pts = new (float, float, double)[Max];
        private int _head, _count;

        public void Update(TouchPoint t, double now)
        {
            if (!t.Touching) return;
            if (_count > 0)
            {
                var last = _pts[(_head - 1 + Max) % Max];
                if (MathF.Abs(last.X - t.X) < 0.002f && MathF.Abs(last.Y - t.Y) < 0.002f) return;
            }
            _pts[_head] = (t.X, t.Y, now);
            _head = (_head + 1) % Max;
            _count = Math.Min(_count + 1, Max);
        }

        public Point? LastPoint(Point[] q)
        {
            if (_count == 0) return null;
            var p = _pts[(_head - 1 + Max) % Max];
            return OnQuad(q, p.X, p.Y);
        }

        public void Draw(DrawingContext ctx, Point[] q, double px, double now)
        {
            Point? prev = null;
            double prevT = 0;
            for (int i = 0; i < _count; i++)
            {
                var p = _pts[(_head - _count + i + Max) % Max];
                var pt = OnQuad(q, p.X, p.Y);
                double age = now - p.T;
                if (prev is { } prevPt && age < Life && now - prevT < Life)
                {
                    double a = (1 - age / Life) * 0.55;
                    ctx.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(Palette.WithAlpha(A, a)), (2 + 3 * (1 - age / Life)) * px, lineCap: PenLineCap.Round), prevPt, pt);
                }
                prev = pt;
                prevT = p.T;
            }
        }
    }
}
