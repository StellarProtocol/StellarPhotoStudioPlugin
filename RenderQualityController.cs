using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>When a render-quality lever is held: never, while the panel / docked strip is open, or always.</summary>
internal enum QualityMode { Off = 0, WhileComposing = 1, Always = 2 }

/// <summary>
/// Turns the player's render-quality choices into framework tokens: one <see cref="IRenderQuality"/> request
/// (supersampling + high shadows) and one <see cref="ITimeOfDay"/> pin. Reconciled on every change; a token is
/// replaced only when what it asks for changes (taking the new one before releasing the old, so the game never
/// sees a gap). "Boost shadows for captures" raises shadows for the capture alone.
/// </summary>
internal sealed class RenderQualityController : IDisposable
{
    private readonly IRenderQuality _quality;
    private readonly ITimeOfDay _time;
    private IDisposable? _qualityToken;
    private RenderQualityRequest? _held;
    private ITimePin? _pin;

    public RenderQualityController(IRenderQuality quality, ITimeOfDay time)
    {
        _quality = quality;
        _time = time;
    }

    public QualityMode Supersample { get; private set; }
    public QualityMode Shadows { get; private set; }
    public QualityMode Time { get; private set; }
    public float Hour { get; private set; } = 12f;
    public bool BoostForCapture { get; private set; } = true;
    public bool Composing { get; private set; }
    public bool Capturing { get; private set; }

    public void Configure(QualityMode supersample, QualityMode shadows, QualityMode time, float hour, bool boostForCapture)
    {
        Supersample = supersample;
        Shadows = shadows;
        Time = time;
        Hour = Math.Clamp(hour, 0f, 24f);
        BoostForCapture = boostForCapture;
        Reconcile();
    }

    public void SetSupersample(QualityMode m) { Supersample = m; Reconcile(); }
    public void SetShadows(QualityMode m) { Shadows = m; Reconcile(); }
    public void SetTime(QualityMode m) { Time = m; Reconcile(); }
    public void SetBoostForCapture(bool on) { BoostForCapture = on; Reconcile(); }
    public void SetComposing(bool on) { Composing = on; Reconcile(); }
    public void SetCapturing(bool on) { Capturing = on; Reconcile(); }

    public void SetHour(float hour)
    {
        Hour = Math.Clamp(hour, 0f, 24f);
        if (_pin is { IsActive: true }) _pin.SetHour(Hour);
        else Reconcile();
    }

    /// <summary>True when the current choices hold the shadow lever only because a capture is running.</summary>
    public bool BoostingForCapture => Capturing && BoostForCapture && !Active(Shadows);

    public void Dispose()
    {
        _qualityToken?.Dispose();
        _qualityToken = null;
        _held = null;
        _pin?.Dispose();
        _pin = null;
    }

    private bool Active(QualityMode m) => m == QualityMode.Always || (m == QualityMode.WhileComposing && Composing);

    private void Reconcile()
    {
        var want = new RenderQualityRequest
        {
            Supersample = Active(Supersample),
            HighShadows = Active(Shadows) || (Capturing && BoostForCapture),
        };
        if (want != _held)
        {
            var previous = _qualityToken;
            _qualityToken = want.Supersample || want.HighShadows ? _quality.Request(want) : null;
            _held = _qualityToken is null ? null : want;
            previous?.Dispose();
        }

        var pinWanted = Active(Time) && _time.IsAvailable;
        if (pinWanted && _pin is not { IsActive: true }) _pin = _time.Pin(Hour);
        else if (!pinWanted && _pin is not null) { _pin.Dispose(); _pin = null; }
    }
}
