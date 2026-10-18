namespace AVASentinel.Engine;

/// <summary>
/// A landscape, step by step: the player looking about and acting on the square in his sights (absorbing what stands
/// on it, making trees, boulders and synthoids from his energy, moving into a synthoid, hyperspace), and the Sentinel
/// turning on its pedestal, draining the energy of a player whose square it can see.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step;
    public const double TurnSpeed = 1.6, TiltSpeed = 0.9, SentinelSight = 0.42, DrainEvery = 1.6;
    public const int Energy0 = 10;

    public static int Cost(Thing t) => t switch { Thing.Tree => 1, Thing.Boulder => 2, Thing.Synthoid => 3, Thing.Sentinel => 4, _ => 0 };

    public static void Start(World w, LandDef l, int seed)
    {
        w.Land = l;
        w.Dice = new Dice(seed);
        for (int x = 0; x < Field.N; x++) for (int y = 0; y < Field.N; y++) w.Stacks[x, y] = [];
        foreach (var t in l.Trees) w.Stacks[t.X, t.Y].Add(Thing.Tree);
        w.Stacks[l.Sentinel.X, l.Sentinel.Y].AddRange([Thing.Pedestal, Thing.Sentinel]);
        w.Stacks[l.Start.X, l.Start.Y].Add(Thing.Synthoid);
        w.Player = l.Start;
        w.Energy = Energy0;
        // Facing the middle of the landscape, looking a little down.
        w.Yaw = Math.Atan2(Field.N / 2.0 - l.Start.X, -(Field.N / 2.0 - l.Start.Y));
        w.Pitch = -0.15;
        w.Facing = w.Dice.Next() * Math.PI * 2;
        w.TurnIn = l.TurnEvery;
        w.Watched = false;
        w.DrainIn = DrainEvery;
        w.SentinelGone = w.Won = w.Dead = false;
        w.Cause = "";
        w.Clock = 0;
    }

    public static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    public static (double X, double Y, double Z) Look(double yaw, double pitch) =>
        (Math.Sin(yaw) * Math.Cos(pitch), -Math.Cos(yaw) * Math.Cos(pitch), Math.Sin(pitch));

    /// <summary>Whether a straight line between two points clears the ground (and the pedestal).</summary>
    public static bool Clear(World w, (double X, double Y, double Z) a, (double X, double Y, double Z) b) => w.Land.Clear(a, b);

    /// <summary>
    /// Whether a square can be worked on from a point: its ground seen from above with a clear line; or, with boulders
    /// on it, the top boulder in plain view (a stack can rise above the eye, the way to climb).
    /// </summary>
    public static bool SeesSquare(World w, (double X, double Y, double Z) eye, (int X, int Y) s)
    {
        double ground = w.Land.Ground(s.X + 0.5, s.Y + 0.5);
        if (eye.Z > ground + 0.05 && Clear(w, eye, (s.X + 0.5, s.Y + 0.5, ground + 0.02))) return true;
        double top = w.Base(s);
        return top > ground + 0.01 && Clear(w, eye, (s.X + 0.5, s.Y + 0.5, top - 0.05));
    }

    /// <summary>Whether the top of the Sentinel's pedestal can be seen from a point: from above it, with a clear line.</summary>
    public static bool SeesPedestal(World w, (double X, double Y, double Z) eye)
    {
        var s = w.Land.Sentinel;
        double top = w.Base(s);
        return eye.Z > top + 0.05 && Clear(w, eye, (s.X + 0.5, s.Y + 0.5, top + 0.02));
    }

    /// <summary>The square in the player's sights: where the line of sight first meets the ground (or a stack).</summary>
    public static (int X, int Y)? Target(World w)
    {
        var e = w.Eye;
        var (lx, ly, lz) = Look(w.Yaw, w.Pitch);
        for (double t = 0.3; t < 60; t += 0.04)
        {
            double x = e.X + lx * t, y = e.Y + ly * t, z = e.Z + lz * t;
            if (x < 0 || y < 0 || x >= Field.N || y >= Field.N) return null;
            var s = ((int)x, (int)y);
            if (s == w.Player) continue;
            double top = w.Base(s);
            bool things = w.Stacks[s.Item1, s.Item2].Count > 0;
            if (z < w.Land.Ground(x, y) || things && z < top + 0.6 && Math.Abs(x - s.Item1 - 0.5) < 0.35 && Math.Abs(y - s.Item2 - 0.5) < 0.35)
                return s;
        }
        return null;
    }

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        if (w.NoteTime > 0) w.NoteTime -= Dt;
        if (w.Dead || w.Won) return;
        w.Yaw = Wrap(w.Yaw + c.Turn * TurnSpeed * Dt);
        w.Pitch = Math.Clamp(w.Pitch + c.Tilt * TiltSpeed * Dt, -1.2, 0.5);
        if (c.Act != Act.None && c.Act != w.ActPrev) Do(w, c.Act);
        w.ActPrev = c.Act;
        Sentinel(w);
    }

    private static void Note(World w, string text)
    {
        w.Note = text;
        w.NoteTime = 1.6;
    }

    /// <summary>An act on the square in the sights.</summary>
    public static void Do(World w, Act act)
    {
        if (act == Act.Hyperspace) { Hyperspace(w); return; }
        if (Target(w) is not { } s) { w.Heard.Add(Sfx.Deny); return; }
        var stack = w.At(s);
        bool sentinel = s == w.Land.Sentinel;
        if (sentinel ? !SeesPedestal(w, w.Eye) : !SeesSquare(w, w.Eye, s)) { Note(w, sentinel ? "YOU MUST SEE THE TOP OF ITS PEDESTAL" : "YOU CAN'T SEE THAT SQUARE"); w.Heard.Add(Sfx.Deny); return; }
        switch (act)
        {
            case Act.Absorb:
            {
                if (stack.Count == 0 || stack[^1] == Thing.Pedestal) { w.Heard.Add(Sfx.Deny); return; }
                var t = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                w.Energy += Cost(t);
                if (t == Thing.Sentinel) { w.SentinelGone = true; w.Watched = false; Note(w, "THE SENTINEL IS ABSORBED - NOW HYPERSPACE"); w.Heard.Add(Sfx.SentinelGone); }
                else w.Heard.Add(Sfx.Absorb);
                return;
            }
            case Act.Tree or Act.Boulder or Act.Robot:
            {
                var t = act switch { Act.Tree => Thing.Tree, Act.Boulder => Thing.Boulder, _ => Thing.Synthoid };
                if (!w.Land.Flat(s.X, s.Y) || stack.Count > 0 && stack[^1] != Thing.Boulder) { Note(w, "NOT THERE"); w.Heard.Add(Sfx.Deny); return; }
                if (w.Energy < Cost(t)) { Note(w, "NOT ENOUGH ENERGY"); w.Heard.Add(Sfx.Deny); return; }
                w.Energy -= Cost(t);
                stack.Add(t);
                w.Heard.Add(Sfx.Create);
                return;
            }
            case Act.Transfer:
            {
                if (stack.Count == 0 || stack[^1] != Thing.Synthoid) { Note(w, "NO SYNTHOID THERE"); w.Heard.Add(Sfx.Deny); return; }
                // Into the new body, looking back the way he was looking (from there).
                w.Player = s;
                w.Heard.Add(Sfx.Transfer);
                return;
            }
        }
    }

    private static void Hyperspace(World w)
    {
        if (w.SentinelGone) { w.Won = true; w.Heard.Add(Sfx.Won); return; }
        if (w.Energy < Cost(Thing.Synthoid)) { Die(w, "LOST IN HYPERSPACE"); return; }
        // To a free flat square no higher than this one.
        double here = w.Land.Ground(w.Player.X + 0.5, w.Player.Y + 0.5);
        var free = new List<(int, int)>();
        for (int x = 0; x < Field.N; x++)
        for (int y = 0; y < Field.N; y++)
            if (w.Land.Flat(x, y) && w.Stacks[x, y].Count == 0 && w.Land.Level0(x, y) <= here) free.Add((x, y));
        if (free.Count == 0) { w.Heard.Add(Sfx.Deny); return; }
        var to = free[w.Dice.Next(free.Count)];
        w.Energy -= Cost(Thing.Synthoid);
        w.Stacks[to.Item1, to.Item2].Add(Thing.Synthoid);
        w.Player = to;
        w.Heard.Add(Sfx.Hyperspace);
    }

    /// <summary>The Sentinel turns, step by step; while it can see the player's square, his energy drains into a new tree.</summary>
    private static void Sentinel(World w)
    {
        if (w.SentinelGone) { w.Watched = false; return; }
        w.TurnIn -= Dt;
        if (w.TurnIn <= 0)
        {
            w.TurnIn = w.Land.TurnEvery;
            w.Facing = Wrap(w.Facing + Math.PI * 2 / 12);
            w.Heard.Add(Sfx.SentinelTurn);
        }
        var se = w.SentinelEye;
        double ang = Math.Atan2(w.Player.X + 0.5 - se.X, -(w.Player.Y + 0.5 - se.Y));
        bool was = w.Watched;
        w.Watched = Math.Abs(Wrap(ang - w.Facing)) < SentinelSight && SeesSquare(w, se, w.Player);
        if (w.Watched && !was) { w.DrainIn = DrainEvery; w.Heard.Add(Sfx.Watched); }
        if (!w.Watched) return;
        w.DrainIn -= Dt;
        if (w.DrainIn > 0) return;
        w.DrainIn = DrainEvery;
        w.Energy--;
        w.Heard.Add(Sfx.Drain);
        if (w.Energy < 0) { Die(w, "ABSORBED BY THE SENTINEL"); return; }
        // The energy goes into a new tree somewhere.
        for (int k = 0; k < 40; k++)
        {
            int x = w.Dice.Next(Field.N), y = w.Dice.Next(Field.N);
            if (w.Land.Flat(x, y) && w.Stacks[x, y].Count == 0) { w.Stacks[x, y].Add(Thing.Tree); break; }
        }
    }

    public static void Die(World w, string cause)
    {
        if (w.Dead) return;
        w.Dead = true;
        w.Cause = cause;
        w.Heard.Add(Sfx.Die);
    }
}
