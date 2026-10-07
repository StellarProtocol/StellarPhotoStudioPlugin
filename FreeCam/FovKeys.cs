namespace Stellar.PhotoStudio.FreeCam;

/// <summary>
/// The FOV in / out hotkeys (1.7.0): a press steps <see cref="CameraMath.FovStep"/> degrees (the same step as one
/// Shift+wheel notch); a key held past <see cref="RepeatDelay"/> keeps moving at <see cref="RatePerSecond"/> — like a
/// held key repeats in a text box, but smooth. In narrows the view (smaller FOV), out widens it. Pure — unit-tested.
/// </summary>
internal sealed class FovKeys
{
    internal const float RepeatDelay = 0.35f;
    internal const float RatePerSecond = 25f;
    private const float MaxDt = 0.1f;   // a hitch must not turn into a jump

    private float _inHeld, _outHeld;

    /// <summary>The FOV change for a press of FOV in (negative) or FOV out (positive).</summary>
    public static float PressStep(bool zoomIn) => zoomIn ? -CameraMath.FovStep : CameraMath.FovStep;

    /// <summary>The FOV change this tick from the held keys (0 until a key has been held for <see cref="RepeatDelay"/>);
    /// releasing a key resets its delay. Both held cancel out.</summary>
    public float Tick(bool inHeld, bool outHeld, float dt)
    {
        dt = dt < 0f ? 0f : dt > MaxDt ? MaxDt : dt;
        var delta = Repeat(ref _inHeld, inHeld, dt) * -1f + Repeat(ref _outHeld, outHeld, dt);
        return delta;
    }

    public void Reset() => _inHeld = _outHeld = 0f;

    private static float Repeat(ref float heldFor, bool held, float dt)
    {
        if (!held) { heldFor = 0f; return 0f; }
        var before = heldFor;
        heldFor += dt;
        if (heldFor <= RepeatDelay) return 0f;
        var moving = before >= RepeatDelay ? dt : heldFor - RepeatDelay;   // only the part past the delay moves
        return moving * RatePerSecond;
    }
}
