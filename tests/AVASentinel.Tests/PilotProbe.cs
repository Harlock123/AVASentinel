using AVASentinel.Engine;

namespace AVASentinel.Tests;

/// <summary>How the computer player gets on on each landscape (set SENT_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_player_does()
    {
        if (Environment.GetEnvironmentVariable("SENT_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int level = 0; level < LandBook.Count; level++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "sent-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Go(level);
            var doing = new Dictionary<string, int>();
            int t = 0, moves = 0, watched = 0;
            var at = g.World.Player;
            double eye0 = g.World.Eye.Z;
            for (; t < 60 * 600 && g.State == GameState.Playing && g.Cleared == 0 && g.Phase != Phase.Dying; t++)
            {
                var c = mode == "lazy" ? new Controls(1, t / 200 % 2 == 0 ? -1 : 1, t % 30 == 0 ? Act.Absorb : Act.None) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.World.Player != at) { moves++; at = g.World.Player; }
                if (g.World.Watched) watched++;
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
            }
            var w = g.World;
            output.WriteLine($"landscape {level + 1}: {(g.Cleared > 0 ? "WON" : g.Phase == Phase.Dying ? "DIED: " + w.Cause : "no")} after {t / 60}s, energy {w.Energy}, {moves} moves, eye {eye0:0.0} -> {w.Eye.Z:0.0} (sentinel base {w.Base(w.Land.Sentinel):0.0}), watched {watched / 60}s; {string.Join(", ", doing.OrderByDescending(d => d.Value).Take(5).Select(d => $"{d.Key} {d.Value / 60}s"))}");
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
