using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>
/// R1: "is every switch Photo Studio asked ReShade for applied yet?". IReShade setters are asynchronous (applied at
/// ReShade's next present, visible after a later Changed), and the framework picks a shaped photo's depth techniques
/// (D8) from what ReShade reports at capture time — so a photo taken in the same frame as a switch could carry a depth
/// effect that was just turned on. Checked only from <see cref="Tick"/>, so at least one framework tick always passes;
/// released when ReShade is Ready and every expectation reads true, or after <see cref="TimeoutSeconds"/> (a request
/// ReShade drops, e.g. a technique missing from the new preset, must not wedge the shutter).
/// </summary>
internal sealed class ReShadeSettle
{
    public const float TimeoutSeconds = 3f;

    private readonly IReShade _rs;
    private readonly List<Func<bool>> _expect = new();
    private float _waited;

    public ReShadeSettle(IReShade rs) => _rs = rs;

    public bool Pending => _expect.Count > 0;

    /// <summary>The last wait ended by timeout rather than by ReShade applying the switches.</summary>
    public bool TimedOut { get; private set; }

    public void Expect(Func<bool> applied)
    {
        if (_rs.State == ReShadeState.NotInstalled) return;
        _expect.Add(applied);
        _waited = 0f;
        TimedOut = false;
    }

    public void Tick(float dt)
    {
        if (_expect.Count == 0) return;
        _waited += dt;
        if (_rs.State == ReShadeState.NotInstalled || (_rs.State == ReShadeState.Ready && AllApplied()))
        {
            _expect.Clear();
            return;
        }
        if (_waited < TimeoutSeconds) return;
        TimedOut = true;
        _expect.Clear();
    }

    private bool AllApplied()
    {
        for (var i = 0; i < _expect.Count; i++)
            if (!_expect[i]()) return false;
        return true;
    }
}
