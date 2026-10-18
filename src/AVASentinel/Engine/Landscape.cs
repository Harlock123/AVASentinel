namespace AVASentinel.Engine;

/// <summary>What the player can do from where he stands: nothing, or one of these, at the square in the sights.</summary>
public enum Act : byte { None, Absorb, Tree, Boulder, Robot, Transfer, Hyperspace }

/// <summary>Turn the view left or right, tilt it up or down, and act on the square in the sights.</summary>
public readonly record struct Controls(int Turn = 0, int Tilt = 0, Act Act = Act.None);

/// <summary>The screen, and the landscape: squares, and the heights of their corners.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int N = 32;
    /// <summary>How high one step of the landscape is, in squares.</summary>
    public const double Rise = 0.5;
    public const double BoulderHeight = 0.5, PedestalHeight = 1.5, EyeHeight = 0.8;
}

/// <summary>What can stand on a square, bottom to top: boulders (and the Sentinel's pedestal), then a tree, a synthoid or the Sentinel.</summary>
public enum Thing : byte { Tree, Boulder, Synthoid, Sentinel, Pedestal }

/// <summary>A landscape: the corner heights, where the Sentinel stands, where the player begins, and the trees.</summary>
public sealed class LandDef
{
    public int Level;
    /// <summary>Corner heights in steps, (N + 1) by (N + 1).</summary>
    public int[,] Corner = new int[Field.N + 1, Field.N + 1];
    public (int X, int Y) Sentinel, Start;
    public List<(int X, int Y)> Trees = [];
    public double TurnEvery;
    public uint Ground1, Ground2;

    public bool Flat(int x, int y) => x >= 0 && y >= 0 && x < Field.N && y < Field.N &&
        Corner[x, y] == Corner[x + 1, y] && Corner[x, y] == Corner[x, y + 1] && Corner[x, y] == Corner[x + 1, y + 1];

    /// <summary>The ground's height at a point, in squares (blended across a square from its corners).</summary>
    public double Ground(double x, double y)
    {
        x = Math.Clamp(x, 0, Field.N - 1e-6);
        y = Math.Clamp(y, 0, Field.N - 1e-6);
        int i = (int)x, j = (int)y;
        double u = x - i, v = y - j;
        double a = Corner[i, j], b = Corner[i + 1, j], c = Corner[i, j + 1], d = Corner[i + 1, j + 1];
        return (a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v) * Field.Rise;
    }

    /// <summary>Whether a straight line between two points clears the ground.</summary>
    public bool Clear((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        int n = Math.Max(4, (int)(Math.Sqrt(dx * dx + dy * dy) * 6));
        for (int i = 1; i < n; i++)
        {
            double t = (double)i / n, x = a.X + dx * t, y = a.Y + dy * t, z = a.Z + dz * t;
            if (z < Ground(x, y) - 0.02) return false;
        }
        return true;
    }

    /// <summary>A flat square's height (its ground).</summary>
    public double Level0(int x, int y) => Corner[x, y] * Field.Rise;
}

/// <summary>The landscapes, each higher and more rugged, with a quicker Sentinel.</summary>
public static class LandBook
{
    public const int Count = 8;

    private static readonly (uint, uint)[] Colours =
    [
        (0xFF30A040, 0xFF3060C0), (0xFFC0A030, 0xFF308060), (0xFF4080C0, 0xFFC04060), (0xFF80A030, 0xFF8040A0),
        (0xFFC06030, 0xFF3090A0), (0xFF60C0A0, 0xFFA03060), (0xFFA0A0A0, 0xFF4040C0), (0xFFC0C040, 0xFF60A040),
    ];

    public static LandDef Build(int level)
    {
        for (int attempt = 0; ; attempt++)
        {
            var l = Try(level, 9000 + level * 97 + attempt * 7919);
            if (l != null) return l;
        }
    }

    private static LandDef? Try(int level, int seed)
    {
        var d = new Dice(seed);
        const int n = Field.N;
        var l = new LandDef { Level = level, TurnEvery = Math.Max(2.4, 6 - level * 0.45) };
        (l.Ground1, l.Ground2) = Colours[level % Colours.Length];
        // Rolling hills: coarse random heights, blended, with finer ones on top.
        double[,] h = new double[n + 1, n + 1];
        foreach (var (step, amp) in new[] { (8, 1.0), (4, 0.45), (2, 0.2) })
        {
            int m = n / step + 2;
            var g = new double[m, m];
            for (int i = 0; i < m; i++) for (int j = 0; j < m; j++) g[i, j] = d.Next();
            for (int x = 0; x <= n; x++)
            for (int y = 0; y <= n; y++)
            {
                double fx = (double)x / step, fy = (double)y / step;
                int i = (int)fx, j = (int)fy;
                double u = fx - i, v = fy - j;
                u = u * u * (3 - 2 * u);
                v = v * v * (3 - 2 * v);
                h[x, y] += amp * (g[i, j] * (1 - u) * (1 - v) + g[i + 1, j] * u * (1 - v) + g[i, j + 1] * (1 - u) * v + g[i + 1, j + 1] * u * v);
            }
        }
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (double v in h) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        int top = 10 + Math.Min(8, level);
        for (int x = 0; x <= n; x++)
        for (int y = 0; y <= n; y++)
            l.Corner[x, y] = (int)Math.Round((h[x, y] - lo) / (hi - lo) * top);
        // Flats: plenty of level squares to stand on and build on.
        for (int k = 0; k < n * n / 3; k++)
        {
            int x = d.Next(n), y = d.Next(n);
            int lev = Math.Min(Math.Min(l.Corner[x, y], l.Corner[x + 1, y]), Math.Min(l.Corner[x, y + 1], l.Corner[x + 1, y + 1]));
            if (d.Next(2) == 0) lev = Math.Max(Math.Max(l.Corner[x, y], l.Corner[x + 1, y]), Math.Max(l.Corner[x, y + 1], l.Corner[x + 1, y + 1]));
            l.Corner[x, y] = l.Corner[x + 1, y] = l.Corner[x, y + 1] = l.Corner[x + 1, y + 1] = lev;
        }
        var flats = new List<(int X, int Y)>();
        for (int x = 0; x < n; x++) for (int y = 0; y < n; y++) if (l.Flat(x, y)) flats.Add((x, y));
        if (flats.Count < n * n / 5) return null;
        // The Sentinel on the highest flat square; the player on a low one well away from it.
        l.Sentinel = flats.OrderByDescending(f => l.Corner[f.X, f.Y]).ThenBy(f => Math.Abs(f.X - n / 2) + Math.Abs(f.Y - n / 2)).First();
        var far = flats.Where(f => Math.Abs(f.X - l.Sentinel.X) + Math.Abs(f.Y - l.Sentinel.Y) > n * 0.6).ToList();
        if (far.Count == 0) return null;
        int least = far.Min(f => l.Corner[f.X, f.Y]);
        var low = far.Where(f => l.Corner[f.X, f.Y] <= least + 1).ToList();
        l.Start = low[d.Next(low.Count)];
        if (l.Corner[l.Sentinel.X, l.Sentinel.Y] - l.Corner[l.Start.X, l.Start.Y] < 5) return null;
        // Something to work with from the start: flat squares in sight.
        var eye = (l.Start.X + 0.5, l.Start.Y + 0.5, l.Level0(l.Start.X, l.Start.Y) + Field.EyeHeight);
        if (flats.Count(f => f != l.Start && eye.Item3 > l.Level0(f.X, f.Y) + 0.05 && l.Clear(eye, (f.X + 0.5, f.Y + 0.5, l.Level0(f.X, f.Y) + 0.02))) < 5) return null;
        // Trees, scattered on flat squares.
        var spare = flats.Where(f => f != l.Sentinel && f != l.Start).OrderBy(_ => d.Next()).ToList();
        int trees = Math.Max(8, 22 - level);
        l.Trees = spare.Take(trees).ToList();
        return l;
    }
}
