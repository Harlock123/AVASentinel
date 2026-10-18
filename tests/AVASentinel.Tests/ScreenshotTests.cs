using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASentinel.Engine;
using AVASentinel.Views;
using Controls = AVASentinel.Engine.Controls;

namespace AVASentinel.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SENT_SHOTS to a folder to save
/// them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 1)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avasentinel-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SENT_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASentinel-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer player play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("GC ", 16400, 6);
        g.HighScores.Insert("AVA", 5300, 2);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 200 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    private static void Play(int level, string name, Func<GameEngine, bool> moment, int seconds = 150)
    {
        var (w, c, g) = Open();
        g.Go(level);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_first_look() => Play(0, "start", g => g.World.Clock > 0.6);

    [AvaloniaFact]
    public void A_tree_absorbed() => Play(1, "trees", g => g.Pilot.Doing == "absorbing trees" && g.World.Energy > Sim.Energy0);

    [AvaloniaFact]
    public void Boulders_stacked() => Play(2, "boulders", g => g.World.Stacks.Cast<List<Thing>>().Any(s => s.Count(t => t == Thing.Boulder) >= 2 && s.Count > 0 && s[^1] == Thing.Synthoid));

    [AvaloniaFact]
    public void Up_high() => Play(3, "high", g => g.World.Eye.Z > g.World.Base(g.World.Land.Sentinel) - 1.5 && !g.World.SentinelGone);

    [AvaloniaFact]
    public void Watched() => Play(0, "watched", g => g.World.Watched && g.World.Clock > 3, 200);

    [AvaloniaFact]
    public void The_sentinel_in_the_sights() => Play(4, "sentinel", g => Sim.Target(g.World) == g.World.Land.Sentinel && Sim.SeesPedestal(g.World, g.World.Eye), 200);

    [AvaloniaFact]
    public void Won() => Play(1, "won", g => g.Phase == Phase.Won && g.PhaseTime > 1.2, 200);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go();
        Sim.Die(g.World, "ABSORBED BY THE SENTINEL");
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
