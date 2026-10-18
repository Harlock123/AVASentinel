namespace AVASentinel.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>Everything on a landscape that changes, kept together so it can be copied whole.</summary>
public sealed class World
{
    public LandDef Land = null!;
    /// <summary>What stands on each square, bottom to top.</summary>
    public List<Thing>[,] Stacks = new List<Thing>[Field.N, Field.N];
    /// <summary>The square of the synthoid the player is in.</summary>
    public (int X, int Y) Player;
    /// <summary>Which way the player looks: round (0 is north, turning clockwise) and up or down.</summary>
    public double Yaw, Pitch;
    public int Energy;
    /// <summary>Which way the Sentinel faces, and how long until it turns again.</summary>
    public double Facing, TurnIn;
    /// <summary>The Sentinel can see the player's square: his energy drains away while it does.</summary>
    public bool Watched;
    public double DrainIn;
    public bool SentinelGone, Won, Dead;
    public string Cause = "";
    public double Clock;
    public Dice Dice = new(1);
    public Act ActPrev;
    public List<Sfx> Heard = [];
    /// <summary>A short-lived note of what just happened (for the screen).</summary>
    public string Note = "";
    public double NoteTime;

    public World Copy()
    {
        var w = new World
        {
            Land = Land, Player = Player, Yaw = Yaw, Pitch = Pitch, Energy = Energy, Facing = Facing, TurnIn = TurnIn, Watched = Watched,
            DrainIn = DrainIn, SentinelGone = SentinelGone, Won = Won, Dead = Dead, Cause = Cause, Clock = Clock, Dice = Dice, ActPrev = ActPrev,
        };
        for (int x = 0; x < Field.N; x++)
        for (int y = 0; y < Field.N; y++)
            w.Stacks[x, y] = [.. Stacks[x, y]];
        return w;
    }

    public List<Thing> At((int X, int Y) s) => Stacks[s.X, s.Y];

    /// <summary>The height of the top of what's solid on a square (its boulders and pedestal): where a thing set on it stands.</summary>
    public double Base((int X, int Y) s)
    {
        double z = Land.Ground(s.X + 0.5, s.Y + 0.5);
        foreach (var t in Stacks[s.X, s.Y])
            z += t switch { Thing.Boulder => Field.BoulderHeight, Thing.Pedestal => Field.PedestalHeight, _ => 0 };
        return z;
    }

    /// <summary>Where the player's eye is.</summary>
    public (double X, double Y, double Z) Eye => (Player.X + 0.5, Player.Y + 0.5, Base(Player) + Field.EyeHeight);

    /// <summary>Where the Sentinel's eye is.</summary>
    public (double X, double Y, double Z) SentinelEye => (Land.Sentinel.X + 0.5, Land.Sentinel.Y + 0.5, Base(Land.Sentinel) + 1.1);
}
