using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASentinel.Engine;

namespace AVASentinel.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASentinel/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double ViewH = 690, Focal = 620, Near = 0.08, Cy = 345;
    private static readonly (double X, double Y, double Z) Light = Norm(0.4, 0.6, 0.75);

    private readonly record struct Cam(double X, double Y, double Z, double Rx, double Ry, double Ux, double Uy, double Uz, double Fx, double Fy, double Fz);
    private readonly record struct V3(double S, double U, double F);

    private sealed class Item
    {
        public double Depth;
        public Point[] Pts = [];
        public uint Fill, Edge;
    }

    private readonly List<Item> _items = [];
    private LandDef? _titleLand;
    private World? _titleWorld;

    private static (double X, double Y, double Z) Norm(double x, double y, double z)
    {
        double l = Math.Sqrt(x * x + y * y + z * z);
        return (x / l, y / l, z / l);
    }

    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int sh) => (uint)Math.Round(((a >> sh) & 0xFF) * (1 - t) + ((b >> sh) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Shade(uint c, double k) => Mix(0xFF000000, c, k);

    private static Cam MakeCam((double X, double Y, double Z) e, double yaw, double pitch)
    {
        double sy = Math.Sin(yaw), cy = Math.Cos(yaw), sp = Math.Sin(pitch), cp = Math.Cos(pitch);
        return new Cam(e.X, e.Y, e.Z, cy, sy, -sy * sp, cy * sp, cp, sy * cp, -cy * cp, sp);
    }

    private static V3 ToCam(in Cam c, double x, double y, double z)
    {
        double dx = x - c.X, dy = y - c.Y, dz = z - c.Z;
        return new V3(dx * c.Rx + dy * c.Ry, dx * c.Ux + dy * c.Uy + dz * c.Uz, dx * c.Fx + dy * c.Fy + dz * c.Fz);
    }

    private static Point Screen(V3 v) => new(Field.Width / 2 + v.S / v.F * Focal, Cy - v.U / v.F * Focal);

    private static Point[]? Project(List<V3> poly)
    {
        var outp = new List<V3>(poly.Count + 2);
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            bool ina = a.F >= Near, inb = b.F >= Near;
            if (ina) outp.Add(a);
            if (ina != inb)
            {
                double t = (Near - a.F) / (b.F - a.F);
                outp.Add(new V3(a.S + (b.S - a.S) * t, a.U + (b.U - a.U) * t, Near));
            }
        }
        if (outp.Count < 3) return null;
        var pts = new Point[outp.Count];
        for (int i = 0; i < pts.Length; i++) pts[i] = Screen(outp[i]);
        return pts;
    }

    private void Face(in Cam c, double depth, uint fill, uint edge, params (double X, double Y, double Z)[] v)
    {
        var poly = new List<V3>(v.Length);
        foreach (var p in v) poly.Add(ToCam(c, p.X, p.Y, p.Z));
        if (Project(poly) is { } pts) _items.Add(new Item { Depth = depth, Pts = pts, Fill = fill, Edge = edge });
    }

    /// <summary>One face of a convex solid, if it faces the eye, lit by the light.</summary>
    private void Solid(in Cam c, (double X, double Y, double Z) centre, double depth, uint colour, params (double X, double Y, double Z)[] v)
    {
        var (a, b, d) = (v[0], v[1], v[2]);
        double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, wx = d.X - a.X, wy = d.Y - a.Y, wz = d.Z - a.Z;
        var n = Norm(uy * wz - uz * wy, uz * wx - ux * wz, ux * wy - uy * wx);
        double mx = v.Average(p => p.X), my = v.Average(p => p.Y), mz = v.Average(p => p.Z);
        if (n.X * (mx - centre.X) + n.Y * (my - centre.Y) + n.Z * (mz - centre.Z) < 0) n = (-n.X, -n.Y, -n.Z);
        if (n.X * (c.X - a.X) + n.Y * (c.Y - a.Y) + n.Z * (c.Z - a.Z) <= 0) return;
        double lit = 0.45 + 0.55 * Math.Max(0, n.X * Light.X + n.Y * Light.Y + n.Z * Light.Z);
        Face(c, depth, Shade(colour, lit), Shade(colour, lit * 0.5), v);
    }

    private void Box(in Cam c, double depth, uint colour, double x, double y, double z0, double w, double d, double h)
    {
        double x0 = x - w / 2, x1 = x + w / 2, y0 = y - d / 2, y1 = y + d / 2, z1 = z0 + h;
        var m = (x, y, z0 + h / 2);
        Solid(c, m, depth, colour, (x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1));
        Solid(c, m, depth, colour, (x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0));
        Solid(c, m, depth, colour, (x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0));
        Solid(c, m, depth, colour, (x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1));
        Solid(c, m, depth, colour, (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1));
    }

    private void Pyramid(in Cam c, double depth, uint colour, double x, double y, double z0, double w, double h)
    {
        double x0 = x - w / 2, x1 = x + w / 2, y0 = y - w / 2, y1 = y + w / 2;
        var top = (x, y, z0 + h);
        var m = (x, y, z0 + h / 3);
        Solid(c, m, depth, colour, (x1, y0, z0), (x1, y1, z0), top);
        Solid(c, m, depth, colour, (x0, y1, z0), (x0, y0, z0), top);
        Solid(c, m, depth, colour, (x1, y1, z0), (x0, y1, z0), top);
        Solid(c, m, depth, colour, (x0, y0, z0), (x1, y0, z0), top);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
            {
                var w = g.World;
                using (dc.PushClip(new Rect(0, 0, Field.Width, ViewH)))
                {
                    DrawLand(dc, w, MakeCam(w.Eye, w.Yaw, w.Pitch), Sim.Target(w));
                    DrawSight(dc);
                    if (w.Watched)
                        dc.DrawRectangle(null, Pen(Alpha(0xFFFF2020, 0.5 + 0.4 * Math.Sin(_clock * 8)), 10), new Rect(5, 5, Field.Width - 10, ViewH - 10));
                    if (g.Phase == Phase.Won)
                        dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, Math.Max(0, 0.7 - g.PhaseTime * 0.3))), new Rect(0, 0, Field.Width, ViewH));
                }
                DrawHud(dc, w);
                if (w.NoteTime > 0) Text(dc, w.Note, new Point(512, 620), 26, 0xFFFFE080, center: true);
                if (g.Message is { } msg)
                {
                    double wdt = msg.Length * 19 + 40;
                    dc.FillRectangle(Brush(0xC0000000), new Rect(512 - wdt / 2, 130, wdt, 56), 6);
                    Text(dc, msg, new Point(512, 158), 40, g.Phase == Phase.Dying ? Red : Yellow, center: true);
                }
                break;
            }
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 22), 22, Yellow, center: true);
    }

    /// <summary>The landscape from the eye: the sky, then every square farthest first, each with what stands on it.</summary>
    private void DrawLand(DrawingContext dc, World w, Cam c, (int X, int Y)? target)
    {
        var land = w.Land;
        // A night sky, the stars fixed to the world (they turn as the view does).
        dc.FillRectangle(Brush(0xFF04040C), new Rect(0, 0, Field.Width, ViewH));
        for (int k = 0; k < 160; k++)
        {
            double a = Hash(k, 1, 7) % 6283 / 1000.0, el = Hash(k, 2, 7) % 900 / 1000.0;
            var v = ToCam(c, c.X + Math.Sin(a) * Math.Cos(el) * 100, c.Y - Math.Cos(a) * Math.Cos(el) * 100, c.Z + Math.Sin(el) * 100);
            if (v.F <= 0) continue;
            var p = Screen(v);
            if (p.Y < ViewH) dc.FillEllipse(p.X, p.Y, 1 + Hash(k, 3, 7) % 2 * 0.5, 0xFFC0C0E0);
        }
        _items.Clear();
        const int n = Field.N;
        for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
        {
            double cx = x + 0.5, cy = y + 0.5;
            double depth = (cx - c.X) * (cx - c.X) + (cy - c.Y) * (cy - c.Y);
            // Behind the eye altogether: skip.
            if (ToCam(c, cx, cy, land.Ground(cx, cy)).F < -1.5) continue;
            double z00 = land.Corner[x, y] * Field.Rise, z10 = land.Corner[x + 1, y] * Field.Rise, z01 = land.Corner[x, y + 1] * Field.Rise, z11 = land.Corner[x + 1, y + 1] * Field.Rise;
            uint edge = 0xFF000000;
            if (land.Flat(x, y))
            {
                uint fill = (x + y) % 2 == 0 ? land.Ground1 : land.Ground2;
                if (target == (x, y)) fill = Mix(fill, 0xFFFFFFFF, 0.45 + 0.2 * Math.Sin(_clock * 8));
                Face(c, depth + 0.002, fill, edge, (x, y, z00), (x + 1, y, z10), (x + 1, y + 1, z11), (x, y + 1, z01));
            }
            else
            {
                // A slope: two triangles, each shaded by how it faces the light.
                foreach (var tri in new[] { new[] { (x + 0.0, y + 0.0, z00), (x + 1.0, y + 0.0, z10), (x + 1.0, y + 1.0, z11) }, new[] { (x + 0.0, y + 0.0, z00), (x + 1.0, y + 1.0, z11), (x + 0.0, y + 1.0, z01) } })
                {
                    var (a, b, d) = (tri[0], tri[1], tri[2]);
                    var nn = Norm((b.Item2 - a.Item2) * (d.Item3 - a.Item3) - (b.Item3 - a.Item3) * (d.Item2 - a.Item2),
                                  (b.Item3 - a.Item3) * (d.Item1 - a.Item1) - (b.Item1 - a.Item1) * (d.Item3 - a.Item3),
                                  (b.Item1 - a.Item1) * (d.Item2 - a.Item2) - (b.Item2 - a.Item2) * (d.Item1 - a.Item1));
                    if (nn.Z < 0) nn = (-nn.X, -nn.Y, -nn.Z);
                    double lit = 0.35 + 0.65 * Math.Max(0, nn.X * Light.X + nn.Y * Light.Y + nn.Z * Light.Z);
                    uint col = Shade(Mix(0xFF707070, (x + y) % 2 == 0 ? land.Ground1 : land.Ground2, 0.35), lit);
                    Face(c, depth + 0.002, col, edge, tri[0], tri[1], tri[2]);
                }
            }
            // What stands here.
            double z = land.Ground(cx, cy);
            var stack = w.Stacks[x, y];
            for (int i = 0; i < stack.Count; i++)
            {
                double dd = depth - 0.001 * (i + 1);
                switch (stack[i])
                {
                    case Thing.Boulder:
                        Box(c, dd, 0xFF8A8478, cx, cy, z, 0.7, 0.7, Field.BoulderHeight);
                        z += Field.BoulderHeight;
                        break;
                    case Thing.Pedestal:
                        Box(c, dd, 0xFFB0B0C0, cx, cy, z, 0.9, 0.9, Field.PedestalHeight);
                        z += Field.PedestalHeight;
                        break;
                    case Thing.Tree:
                        Box(c, dd, 0xFF6A4A2A, cx, cy, z, 0.12, 0.12, 0.35);
                        Pyramid(c, dd - 0.0005, 0xFF30A040, cx, cy, z + 0.25, 0.6, 0.9);
                        break;
                    case Thing.Synthoid:
                        if ((x, y) == w.Player) break;
                        Box(c, dd, 0xFF4080E0, cx, cy, z, 0.36, 0.24, 0.7);
                        Box(c, dd - 0.0005, 0xFFE0E0F0, cx, cy, z + 0.7, 0.26, 0.26, 0.24);
                        break;
                    case Thing.Sentinel:
                        Box(c, dd, 0xFFD04040, cx, cy, z, 0.5, 0.5, 1.0);
                        Box(c, dd - 0.0005, 0xFFF0D0A0, cx, cy, z + 1.0, 0.36, 0.36, 0.3);
                        // Its eyes, on the side it faces.
                        double fx = Math.Sin(w.Facing), fy = -Math.Cos(w.Facing);
                        Box(c, dd - 0.001, 0xFFFFFF40, cx + fx * 0.19, cy + fy * 0.19, z + 1.12, 0.22, 0.04, 0.07);
                        break;
                }
            }
        }
        foreach (var it in _items.OrderByDescending(i => i.Depth))
            Poly(dc, it.Fill, it.Edge, 1, it.Pts);
    }

    private static void DrawSight(DrawingContext dc)
    {
        uint s = 0xE0FFFFFF;
        dc.DrawLine(Pen(s, 2), new Point(512 - 22, Cy), new Point(512 - 7, Cy));
        dc.DrawLine(Pen(s, 2), new Point(512 + 7, Cy), new Point(512 + 22, Cy));
        dc.DrawLine(Pen(s, 2), new Point(512, Cy - 22), new Point(512, Cy - 7));
        dc.DrawLine(Pen(s, 2), new Point(512, Cy + 7), new Point(512, Cy + 22));
    }

    /// <summary>The strip along the bottom: the energy (as synthoids, boulders and trees), the Sentinel's gaze, the landscape.</summary>
    private void DrawHud(DrawingContext dc, World w)
    {
        var g = Engine;
        double y0 = ViewH;
        dc.FillRectangle(Brush(0xFF0C0C18), new Rect(0, y0, Field.Width, Field.Height - y0));
        dc.FillRectangle(Brush(w.Land.Ground1), new Rect(0, y0, Field.Width, 3));
        Text(dc, $"ENERGY {w.Energy}", new Point(20, y0 + 22), 26, 0xFFFFE080);
        int e = Math.Max(0, w.Energy), x = 20;
        for (int i = 0; i < e / 3 && x < 520; i++, x += 22) { dc.FillRectangle(Brush(0xFF4080E0), new Rect(x, y0 + 46, 12, 18)); dc.FillRectangle(Brush(0xFFE0E0F0), new Rect(x + 2, y0 + 40, 8, 7)); }
        if (e % 3 == 2) { dc.FillRectangle(Brush(0xFF8A8478), new Rect(x, y0 + 50, 16, 14)); x += 22; }
        if (e % 3 == 1) Poly(dc, 0xFF30A040, null, 0, new Point(x, y0 + 64), new Point(x + 8, y0 + 42), new Point(x + 16, y0 + 64));
        Text(dc, $"LANDSCAPE {g.Level + 1:0000}", new Point(Field.Width / 2 + 40, y0 + 22), 24, 0xFFB0B0E0, center: true);
        Text(dc, $"SCORE {g.Score:000000}", new Point(Field.Width / 2 + 40, y0 + 52), 24, Yellow, center: true);
        // Where the Sentinel is looking, round from where the player is looking.
        double cx = Field.Width - 50, cyy = y0 + 39;
        dc.DrawEllipse(Brush(0xFF181828), Pen(w.Watched ? Red : 0xFF5050A0, 2), new Point(cx, cyy), 28, 28);
        if (!w.SentinelGone)
        {
            var se = w.SentinelEye;
            double bearing = Math.Atan2(se.X - w.Eye.X, -(se.Y - w.Eye.Y)) - w.Yaw;
            double sx = cx + Math.Sin(bearing) * 18, sy = cyy - Math.Cos(bearing) * 18;
            dc.FillEllipse(sx, sy, 5, 0xFFD04040);
            double look = w.Facing - w.Yaw;
            dc.DrawLine(Pen(0xFFFFFF40, 2), new Point(sx, sy), new Point(sx + Math.Sin(look) * 12, sy - Math.Cos(look) * 12));
        }
        dc.FillEllipse(cx, cyy, 3, 0xFF80FF80);
        Text(dc, w.Watched ? "SEEN!" : "", new Point(cx - 40, cyy), 24, Red, right: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        var land = _titleLand ??= LandBook.Build(0);
        if (_titleWorld == null) { _titleWorld = new World(); Sim.Start(_titleWorld, land, 1); }
        var w = _titleWorld;
        w.Facing = _clock * 0.3;
        double a = _clock * 0.08;
        var eye = (Field.N / 2.0 - Math.Sin(a) * 22, Field.N / 2.0 + Math.Cos(a) * 22, 14.0);
        using (dc.PushClip(new Rect(0, 0, Field.Width, Field.Height)))
            DrawLand(dc, w, MakeCam(eye, a, -0.32), null);
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, 30, Field.Width, 150));
        Text(dc, "AVA SENTINEL", new Point(cx, 92), 110, 0xFFE0E0FF, center: true);
        Text(dc, "AFTER GEOFF CRAMMOND'S 1986 GAME FOR FIREBIRD", new Point(cx, 160), 22, Dim, center: true);
        dc.FillRectangle(Brush(0xC0000000), new Rect(0, 470, Field.Width, 298));
        Text(dc, "THE SENTINEL STANDS ON THE HIGHEST POINT, TURNING, ABSORBING THE ENERGY OF ANYTHING WHOSE SQUARE IT SEES.", new Point(cx, 492), 19, White, center: true);
        Text(dc, "ABSORB TREES FOR ENERGY; MAKE BOULDERS AND SYNTHOIDS FROM IT; STACK, TRANSFER, AND CLIMB, UNTIL YOU CAN SEE", new Point(cx, 518), 19, White, center: true);
        Text(dc, "THE TOP OF ITS PEDESTAL. THEN ABSORB THE SENTINEL, AND HYPERSPACE TO THE NEXT LANDSCAPE.", new Point(cx, 544), 19, White, center: true);
        Text(dc, "PRESS ENTER TO START", new Point(cx, 604), 40, (int)(_clock * 2) % 2 == 0 ? Yellow : Orange, center: true);
        Text(dc, "ARROWS: LOOK   A: ABSORB   T: TREE   B: BOULDER   R: SYNTHOID   Q: TRANSFER   H: HYPERSPACE   P: PAUSE", new Point(cx, 660), 19, Dim, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore:000000}", new Point(cx, 710), 24, Cyan, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "GAME OVER", new Point(cx, 240), 110, Red, center: true);
        Text(dc, Engine.World.Cause == "" ? "" : Engine.World.Cause, new Point(cx, 330), 34, White, center: true);
        Text(dc, $"LANDSCAPES WON {Engine.Cleared}", new Point(cx, 390), 30, Dim, center: true);
        Text(dc, $"SCORE {Engine.Score}", new Point(cx, 470), 44, Yellow, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE GREATEST SYNTHOIDS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE     WON", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,6}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
