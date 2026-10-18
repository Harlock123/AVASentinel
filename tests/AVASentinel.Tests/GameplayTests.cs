using Avalonia.Input;
using AVASentinel.Engine;

namespace AVASentinel.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avasentinel-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A flat landscape (height 2 steps), the Sentinel up on a hill in the far corner facing away.</summary>
    private static World Flat(Action<LandDef>? build = null)
    {
        var l = new LandDef { TurnEvery = 1e9, Sentinel = (28, 28), Start = (4, 4) };
        for (int x = 0; x <= Field.N; x++) for (int y = 0; y <= Field.N; y++) l.Corner[x, y] = 2;
        for (int x = 26; x <= 31; x++) for (int y = 26; y <= 31; y++) l.Corner[x, y] = 14;
        build?.Invoke(l);
        var w = new World();
        Sim.Start(w, l, 1);
        w.Facing = Math.PI / 4;
        return w;
    }

    /// <summary>Look straight at a square from the player's eye.</summary>
    private static void Aim(World w, (int X, int Y) s)
    {
        var e = w.Eye;
        double tx = s.X + 0.5, ty = s.Y + 0.5, tz = w.Base(s) + (w.At(s).Count > 0 ? 0.25 : 0.01);
        w.Yaw = Math.Atan2(tx - e.X, -(ty - e.Y));
        w.Pitch = Math.Atan2(tz - e.Z, Math.Sqrt((tx - e.X) * (tx - e.X) + (ty - e.Y) * (ty - e.Y)));
    }

    private static void Press(World w, AVASentinel.Engine.Act a)
    {
        Sim.Step(w, new Controls(Act: a));
        Sim.Step(w, default);
    }

    [Fact]
    public void Looking_round_turns_and_tilts_the_view()
    {
        var w = Flat();
        double yaw = w.Yaw, pitch = w.Pitch;
        for (int i = 0; i < 30; i++) Sim.Step(w, new Controls(1, 1));
        Assert.Equal(Sim.Wrap(yaw + Sim.TurnSpeed / 2), w.Yaw, 3);
        Assert.Equal(pitch + Sim.TiltSpeed / 2, w.Pitch, 3);
    }

    [Fact]
    public void The_square_in_the_sights_is_where_the_line_of_sight_meets_the_ground()
    {
        var w = Flat();
        Aim(w, (8, 4));
        Assert.Equal((8, 4), Sim.Target(w));
    }

    [Fact]
    public void Making_things_costs_energy_and_absorbing_gives_it_back()
    {
        var w = Flat();
        Aim(w, (8, 4));
        Press(w, AVASentinel.Engine.Act.Tree);
        Assert.Equal(Sim.Energy0 - 1, w.Energy);
        Assert.Equal([Thing.Tree], w.At((8, 4)));
        Press(w, AVASentinel.Engine.Act.Absorb);
        Assert.Equal(Sim.Energy0, w.Energy);
        Assert.Empty(w.At((8, 4)));
        Press(w, AVASentinel.Engine.Act.Boulder);
        Aim(w, (8, 4));
        Press(w, AVASentinel.Engine.Act.Boulder);
        Aim(w, (8, 4));
        Press(w, AVASentinel.Engine.Act.Robot);
        Assert.Equal([Thing.Boulder, Thing.Boulder, Thing.Synthoid], w.At((8, 4)));
        Assert.Equal(Sim.Energy0 - 7, w.Energy);
    }

    [Fact]
    public void Not_enough_energy_and_nothing_is_made()
    {
        var w = Flat();
        w.Energy = 2;
        Aim(w, (8, 4));
        Press(w, AVASentinel.Engine.Act.Robot);
        Assert.Empty(w.At((8, 4)));
        Assert.Equal(2, w.Energy);
    }

    [Fact]
    public void Transfer_into_a_synthoid_on_boulders_and_the_eye_is_higher()
    {
        var w = Flat();
        Aim(w, (8, 4));
        Press(w, AVASentinel.Engine.Act.Boulder);
        Aim(w, (8, 4));
        Press(w, AVASentinel.Engine.Act.Boulder);
        Aim(w, (8, 4));
        Press(w, AVASentinel.Engine.Act.Robot);
        double eye = w.Eye.Z;
        Aim(w, (8, 4));
        Press(w, AVASentinel.Engine.Act.Transfer);
        Assert.Equal((8, 4), w.Player);
        Assert.Equal(eye + 2 * Field.BoulderHeight, w.Eye.Z, 3);
        // The old body, left behind, absorbed from up here.
        Aim(w, (4, 4));
        int e = w.Energy;
        Press(w, AVASentinel.Engine.Act.Absorb);
        Assert.Equal(e + 3, w.Energy);
    }

    [Fact]
    public void A_square_behind_a_hill_cannot_be_worked_on()
    {
        var w = Flat(l => { for (int x = 7; x <= 8; x++) for (int y = 0; y <= Field.N; y++) l.Corner[x, y] = 8; });
        Assert.False(Sim.SeesSquare(w, w.Eye, (12, 4)));
    }

    [Fact]
    public void The_sentinel_drains_a_player_whose_square_it_sees_into_a_new_tree()
    {
        var w = Flat();
        // Facing the player.
        var se = w.SentinelEye;
        w.Facing = Math.Atan2(w.Player.X + 0.5 - se.X, -(w.Player.Y + 0.5 - se.Y));
        int trees = 0;
        foreach (var st in w.Stacks) trees += st.Count(t => t == Thing.Tree);
        for (int i = 0; i < 60 * 2; i++) Sim.Step(w, default);
        Assert.True(w.Watched);
        Assert.Equal(Sim.Energy0 - 1, w.Energy);
        int after = 0;
        foreach (var st in w.Stacks) after += st.Count(t => t == Thing.Tree);
        Assert.Equal(trees + 1, after);
        w.Energy = 0;
        for (int i = 0; i < 60 * 2; i++) Sim.Step(w, default);
        Assert.True(w.Dead);
        Assert.Equal("ABSORBED BY THE SENTINEL", w.Cause);
    }

    [Fact]
    public void Facing_away_it_does_not_see()
    {
        var w = Flat();
        for (int i = 0; i < 60 * 3; i++) Sim.Step(w, default);
        Assert.False(w.Watched);
        Assert.Equal(Sim.Energy0, w.Energy);
    }

    [Fact]
    public void The_sentinel_is_absorbed_only_from_above_its_pedestal_then_hyperspace_wins()
    {
        var w = Flat();
        Aim(w, w.Land.Sentinel);
        Press(w, AVASentinel.Engine.Act.Absorb);
        Assert.False(w.SentinelGone);
        // Put the player up on a high stack, looking down on it.
        w.Stacks[w.Player.X, w.Player.Y].Clear();
        w.Player = (20, 20);
        w.Stacks[20, 20].AddRange(Enumerable.Repeat(Thing.Boulder, 20).Append(Thing.Synthoid));
        Aim(w, w.Land.Sentinel);
        Press(w, AVASentinel.Engine.Act.Absorb);
        Assert.True(w.SentinelGone);
        Press(w, AVASentinel.Engine.Act.Hyperspace);
        Assert.True(w.Won);
    }

    [Fact]
    public void Hyperspace_costs_a_synthoid_and_without_the_energy_it_is_the_end()
    {
        var w = Flat();
        var from = w.Player;
        Press(w, AVASentinel.Engine.Act.Hyperspace);
        Assert.NotEqual(from, w.Player);
        Assert.Equal(Sim.Energy0 - 3, w.Energy);
        w.Energy = 2;
        Press(w, AVASentinel.Engine.Act.Hyperspace);
        Assert.True(w.Dead);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void A_landscape_won_scores_its_energy_and_on_to_the_next()
    {
        var g = Engine();
        g.Go();
        g.World.Energy = 12;
        g.World.SentinelGone = true;
        Sim.Do(g.World, AVASentinel.Engine.Act.Hyperspace);
        g.Run(2);
        Assert.Equal(Phase.Won, g.Phase);
        Assert.Equal(GameEngine.LevelPoints + 12 * GameEngine.EnergyPoints, g.Score);
        g.Run((int)(60 * GameEngine.WonSeconds) + 4);
        Assert.Equal(1, g.Level);
        Assert.Equal(12, g.World.Energy);
    }

    [Fact]
    public void Absorbed_is_game_over()
    {
        var g = Engine();
        g.Go();
        Sim.Die(g.World, "ABSORBED BY THE SENTINEL");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 4);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        var before = (g.World.Yaw, g.World.Pitch, g.World.Energy);
        g.Idle(60 * 6);
        Assert.NotEqual(before, (g.World.Yaw, g.World.Pitch, g.World.Energy));
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go();
        g.Pause();
        double yaw = g.World.Yaw;
        g.Run(60, new Controls(1));
        Assert.Equal(yaw, g.World.Yaw);
        g.Resume();
        g.Run(10, new Controls(1));
        Assert.NotEqual(yaw, g.World.Yaw);
    }

    [Fact]
    public void The_keys_look_and_act()
    {
        using var input = new InputHandler();
        foreach (var (key, check) in new (Key, Func<Controls, bool>)[]
                 {
                     (Key.Left, c => c.Turn == -1), (Key.Right, c => c.Turn == 1), (Key.Up, c => c.Tilt == 1), (Key.Down, c => c.Tilt == -1),
                     (Key.A, c => c.Act == AVASentinel.Engine.Act.Absorb), (Key.T, c => c.Act == AVASentinel.Engine.Act.Tree),
                     (Key.B, c => c.Act == AVASentinel.Engine.Act.Boulder), (Key.R, c => c.Act == AVASentinel.Engine.Act.Robot),
                     (Key.Q, c => c.Act == AVASentinel.Engine.Act.Transfer), (Key.H, c => c.Act == AVASentinel.Engine.Act.Hyperspace),
                 })
        {
            input.OnKeyDown(key);
            Assert.True(check(input.Player), key.ToString());
            input.OnKeyUp(key);
        }
    }
}
