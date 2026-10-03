using System;

namespace Stellar.PhotoStudio;

/// <summary>The shutter delay between the Capture press and <c>StudioSession.CaptureAsync</c>: a few ticks for the
/// capture-only shadow boost to reallocate, then — R1 — no shot while a ReShade switch is still being applied (else a
/// just-enabled depth effect could reach a shaped photo, D8).</summary>
internal sealed class CaptureGate
{
    private int _ticks = -1;

    public bool Armed => _ticks >= 0;

    public void Arm(int settleTicks) => _ticks = Math.Max(0, settleTicks);

    /// <summary>True exactly once: the tick on which the capture should start.</summary>
    public bool Tick(bool reShadePending)
    {
        if (_ticks < 0) return false;
        if (_ticks > 0)
        {
            _ticks--;
            return false;
        }
        if (reShadePending) return false;
        _ticks = -1;
        return true;
    }
}
