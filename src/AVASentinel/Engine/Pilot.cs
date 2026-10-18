namespace AVASentinel.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. It works the way a player must: it looks round at the
/// squares it can see the tops of, and climbs. It takes in the trees it can see for energy, builds a stack of
/// boulders on the highest square it can see with a synthoid on top, moves into it, and absorbs the body (and the
/// boulders) it left behind. Once it can see the top of the Sentinel's pedestal it absorbs the Sentinel and goes
/// into hyperspace. While the Sentinel is watching, it gets out of sight first. Every act is aimed: it turns and
/// tilts the view until the square is in its sights.
/// </summary>
public sealed class Pilot
{
    private readonly Queue<((int X, int Y)? At, Act Act)> _plan = new();
    private readonly HashSet<(int, int)> _been = [];
    /// <summary>Squares that couldn't be lined up in the sights from here (something nearer in the way).</summary>
    private readonly HashSet<(int, int)> _skip = [];
    private (int, int) _skipFrom;
    private bool _release;
    private int _stuck;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _plan.Clear();
        _been.Clear();
        _skip.Clear();
        _release = false;
        _stuck = 0;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        if (w.Dead || w.Won) return default;
        if (_release) { _release = false; return default; }
        if (_plan.Count == 0) Plan(w);
        if (_plan.Count == 0) { Doing = "looking round"; return new Controls(Turn: 1); }
        var (at, act) = _plan.Peek();
        if (at is not { } s)
        {
            _plan.Dequeue();
            _release = true;
            return new Controls(Act: act);
        }
        // Aim: turn and tilt until the square is in the sights.
        var e = w.Eye;
        double tx = s.X + 0.5, ty = s.Y + 0.5, tz = w.Base(s) + (act == Act.Absorb || act == Act.Transfer ? 0.25 : 0.03);
        double yaw = Math.Atan2(tx - e.X, -(ty - e.Y)), pitch = Math.Atan2(tz - e.Z, Math.Sqrt((tx - e.X) * (tx - e.X) + (ty - e.Y) * (ty - e.Y)));
        double dy = Sim.Wrap(yaw - w.Yaw), dp = pitch - w.Pitch;
        int turn = Math.Abs(dy) < 0.015 ? 0 : Math.Sign(dy), tilt = Math.Abs(dp) < 0.015 ? 0 : Math.Sign(dp);
        // (Beyond how far the view tilts: out of reach from here.)
        if (tilt < 0 && w.Pitch <= -1.19 || tilt > 0 && w.Pitch >= 0.49) tilt = 0;
        if (turn != 0 || tilt != 0) { _stuck = 0; return new Controls(turn, tilt); }
        if (Sim.Target(w) != s)
        {
            // Lined up but something else is in the sights: think again.
            if (++_stuck > 20) { _plan.Clear(); _stuck = 0; _skip.Add(s); }
            return default;
        }
        _plan.Dequeue();
        _release = true;
        return new Controls(Act: act);
    }

    private void Plan(World w)
    {
        var land = w.Land;
        var eye = w.Eye;
        _been.Add(w.Player);
        if (_skipFrom != w.Player) { _skip.Clear(); _skipFrom = w.Player; }
        if (w.SentinelGone) { Doing = "off into hyperspace"; _plan.Enqueue((null, Act.Hyperspace)); return; }
        if (Sim.SeesPedestal(w, eye)) { Doing = "absorbing the Sentinel"; _plan.Enqueue((land.Sentinel, Act.Absorb)); return; }
        // Trees in sight: energy.
        var trees = new List<(int X, int Y)>();
        for (int x = 0; x < Field.N; x++)
        for (int y = 0; y < Field.N; y++)
            if (w.Stacks[x, y].Count > 0 && w.Stacks[x, y][^1] == Thing.Tree && (x, y) != w.Player && !_skip.Contains((x, y)) && Sim.SeesSquare(w, eye, (x, y))) trees.Add((x, y));
        if (trees.Count > 0 && !w.Watched)
        {
            Doing = "absorbing trees";
            foreach (var t in trees.OrderBy(t => Math.Abs(t.X - w.Player.X) + Math.Abs(t.Y - w.Player.Y)).Take(4)) _plan.Enqueue((t, Act.Absorb));
            return;
        }
        // Up: the best square to build on, with as many boulders as can be spared under a new synthoid.
        var se = w.SentinelEye;
        double best = double.MinValue;
        (int X, int Y) pick = default;
        int pickBoulders = 0;
        for (int x = 0; x < Field.N; x++)
        for (int y = 0; y < Field.N; y++)
        {
            var s = (x, y);
            if (s == w.Player || !land.Flat(x, y) || s == land.Sentinel || _skip.Contains(s)) continue;
            var st = w.Stacks[x, y];
            if (st.Count > 0 && st[^1] != Thing.Boulder) continue;
            if (!Sim.SeesSquare(w, eye, s)) continue;
            // (Watched, there's no time to build: just a synthoid, out of sight.)
            int k = w.Watched ? 0 : Math.Max(0, Math.Min(6, (w.Energy - 3) / 2));
            if (w.Energy < 3) continue;
            double newEye = w.Base(s) + k * Field.BoulderHeight + Field.EyeHeight;
            var at = (x + 0.5, y + 0.5, newEye);
            double score = newEye;
            // Seeing the top of the Sentinel's pedestal from there wins it.
            if (newEye > w.Base(land.Sentinel) + 0.05 && Sim.Clear(w, at, (land.Sentinel.X + 0.5, land.Sentinel.Y + 0.5, w.Base(land.Sentinel) + 0.02))) score += 100;
            // Out of the Sentinel's sight is worth a lot while it's watching.
            bool seen = Sim.SeesSquare(w, se, s);
            if (w.Watched && !seen) score += 50;
            if (_been.Contains(s)) score -= 3;
            // (From below the square's ground the pilot can't aim low enough; and too steep a look down is out of the view's reach.)
            double dist = Math.Sqrt((x + 0.5 - eye.X) * (x + 0.5 - eye.X) + (y + 0.5 - eye.Y) * (y + 0.5 - eye.Y));
            if (Math.Atan2(w.Base(s) - eye.Z, dist) < -1.15) continue;
            if (score > best) { best = score; pick = s; pickBoulders = k; }
        }
        if (best == double.MinValue) { Doing = w.Energy >= 3 ? "hyperspace" : "stuck"; if (w.Energy >= 3) _plan.Enqueue((null, Act.Hyperspace)); return; }
        Doing = w.Watched ? "getting out of sight" : "climbing";
        var old = w.Player;
        int under = w.At(old).Count(t => t == Thing.Boulder);
        for (int i = 0; i < pickBoulders; i++) _plan.Enqueue((pick, Act.Boulder));
        _plan.Enqueue((pick, Act.Robot));
        _plan.Enqueue((pick, Act.Transfer));
        // Back for the body left behind, and the boulders under it.
        _plan.Enqueue((old, Act.Absorb));
        for (int i = 0; i < under; i++) _plan.Enqueue((old, Act.Absorb));
    }
}
