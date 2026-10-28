using Avalonia.Input;

namespace AVASentinel.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: turning left and right, tilting up and down (looking up is +1),
/// and the act on the square in the sights.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// The arrows to look round (left and right) and up and down; then, at the square in the sights: A to absorb, T for
    /// a tree, B for a boulder, R for a synthoid (a robot), Q to transfer into it, and H for hyperspace. On a gamepad:
    /// the stick to look; A absorb, B boulder, X synthoid, Y transfer, the left bumper a tree, the right hyperspace.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int turn = (Any(Key.Right, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.NumPad4) ? 1 : 0);
            if (turn == 0) turn = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
            // Up the screen (looking up) is +1.
            int tilt = (Any(Key.Up, Key.NumPad8) ? 1 : 0) - (Any(Key.Down, Key.NumPad2) ? 1 : 0);
            if (tilt == 0) tilt = pad.DPadUp || pad.LeftStickY < -0.4f ? 1 : pad.DPadDown || pad.LeftStickY > 0.4f ? -1 : 0;
            var act = Any(Key.A) || pad.ButtonA ? Act.Absorb
                : Any(Key.T) || pad.LeftBumper ? Act.Tree
                : Any(Key.B) || pad.ButtonB ? Act.Boulder
                : Any(Key.R) || pad.ButtonX ? Act.Robot
                : Any(Key.Q) || pad.ButtonY ? Act.Transfer
                : Any(Key.H) || pad.RightBumper ? Act.Hyperspace
                : Act.None;
            return new Controls(turn, tilt, act);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
