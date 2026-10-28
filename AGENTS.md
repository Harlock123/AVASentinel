# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A game of height and line of sight on a solid 3D landscape, in the style of Geoff Crammond's 1986 game The
Sentinel, in C# on .NET 10 and Avalonia 12. One self-contained executable per platform (Windows, macOS and Linux,
x64 and ARM). The landscapes are drawn in code, and the sounds and the spoken lines are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASentinel.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASentinel.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVASentinel          # play it; --no-audio for silence
SENT_SHOTS=$PWD/Images dotnet test tests/AVASentinel.Tests --filter ScreenshotTests   # refresh the README's pictures
SENT_PROBE=1 dotnet test tests/AVASentinel.Tests --filter PilotProbe   # how the computer player gets on, for tuning
python3 tools/make_sounds.py                  # rebuild src/AVASentinel/Sounds (standard-library Python only)
```

`SENT_PROBE=lazy` looks round absorbing at random. Without `SENT_PROBE` the probe test does nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASentinel/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `Sim.cs` steps a landscape: the player looking about and acting on the square in his sights, and the Sentinel
    turning and draining the energy of a player it can see.
  - `Landscape.cs` has `Act`, `Controls`, the measures (`Field`), what can stand on a square (`Thing`), and the
    landscapes (`LandDef`, `LandBook`).
  - `World.cs` has the seeded `Dice` and the copyable `World`.
  - `Pilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASentinel/Views/`: `GameCanvas` (split over two partial files) draws the screen and runs the 60 Hz timer.
- `src/AVASentinel/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVASentinel.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, landscape and screenshot tests,
  and the pilot probe.
- `tools/`: `make_sounds.py` and the formant voice `speech.py` it uses.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` runs the simulation in fixed 1/60 s steps (`Field.Step`), independent of
  the frame rate, so tests play deterministically from a seed. Draw random numbers from the world's seeded `Dice`
  (`w.Dice`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** `Controls` is `Turn`, `Tilt` and an `Act`. There is no `dy`: `Tilt` is
  +1 for looking up, on keys and stick alike (the stick pushed up, `LeftStickY < -0.4`, gives +1). The class comment
  on `InputHandler` says "up the screen is -1"; the code is what counts. Keep keys and stick agreeing when adding
  controls.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVASentinel`); tests load
  the table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds and spoken lines go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
