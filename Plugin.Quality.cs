using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Render quality (spec 2026-10-01-photo-studio-render-quality-design.md § 5): supersampling 2× + TAA and high shadows from
// the framework's IRenderQuality, time of day from ITimeOfDay. Each lever is Off / While composing (panel or docked strip
// open) / Always; the token lifetimes live in RenderQualityController (tested). The controls are in the Capture section
// (quality) and Camera › Scene (time of day) since 2.0.0.
public sealed partial class Plugin
{
    private RenderQualityController _quality = null!;
    private const float HourSaveDelay = 0.5f;
    private float _hourSaveIn = -1f;

    private void StartRenderQuality()
    {
        _quality = new RenderQualityController(_services.RenderQuality, _services.TimeOfDay);
        _quality.Configure(_settings.Supersample, _settings.Shadows, _settings.TimeMode, _settings.TimeHour, _settings.BoostForCapture);
    }

    private void SetSupersample(QualityMode m) { _settings.SetSupersample(m); _quality.SetSupersample(m); }
    private void SetShadows(QualityMode m) { _settings.SetShadows(m); _quality.SetShadows(m); }
    private void SetTimeMode(QualityMode m) { _settings.SetTimeMode(m); _quality.SetTime(m); }
    private void SetBoostForCapture(bool on) { _settings.SetBoostForCapture(on); _quality.SetBoostForCapture(on); }

    private void SetHour(float hour)
    {
        _quality.SetHour(hour);
        _settings.SetTimeHour(hour, save: false);   // the slider fires every frame: save once the drag settles
        _hourSaveIn = HourSaveDelay;
    }

    private void TickHourSave(float dt)
    {
        RefreshLiveQuality(dt);
        if (_hourSaveIn <= 0f) return;
        _hourSaveIn -= dt;
        if (_hourSaveIn <= 0f) { _hourSaveIn = 0f; FlushHour(); }   // 0 = due (FlushHour skips only "none pending" < 0)
    }

    // The live read-back reflects into the game; refresh it twice a second, not on every panel poll.
    private const float LiveRefresh = 0.5f;
    private float _liveIn;
    private string _liveText = "";

    private void RefreshLiveQuality(float dt)
    {
        if (!_panelShown) return;
        _liveIn -= dt;
        if (_liveIn > 0f) return;
        _liveIn = LiveRefresh;
        var live = _services.RenderQuality.Live;
        _liveText = _loc.TFormat("ps.q.live", F(live.RenderScale, "0.00"), live.TaaOn ? T("ps.q.on") : T("ps.q.offLower"),
            live.ShadowResolution);
    }

    private void FlushHour()
    {
        if (_hourSaveIn < 0f) return;
        _hourSaveIn = -1f;
        _settings.SetTimeHour(_quality.Hour, save: true);
    }

    // Cached on the shown minutes (the slider fires every drag frame; the live clock moves once a game minute).
    private Cached<(bool, int, int, string)>? _hourText;

    private string HourText()
    {
        bool live = _quality.Time != QualityMode.Off && _services.TimeOfDay.IsAvailable;
        _hourText ??= new Cached<(bool, int, int, string)>(k => k.Item1
            ? _loc.TFormat("ps.q.hourLive", FormatHour(k.Item2 / 60f), FormatHour(k.Item3 / 60f))
            : FormatHour(k.Item2 / 60f));
        return _hourText.Get((live, Minutes(_quality.Hour), live ? Minutes(_services.TimeOfDay.CurrentHour) : 0, T("ps.q.hourLive")));
    }

    private static int Minutes(float hour) => (int)Math.Round(hour * 60f);

    private string LiveQualityText() => _liveText;

    /// <summary>24 h float → HH:MM (13.5 → "13:30"); 24.0 wraps to 00:00.</summary>
    private static string FormatHour(float hour)
    {
        var total = (int)Math.Round(hour * 60f) % (24 * 60);
        if (total < 0) total += 24 * 60;
        return $"{total / 60:00}:{total % 60:00}";
    }
}
