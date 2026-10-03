using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Look tab → "Render quality" group (spec 2026-10-01-photo-studio-render-quality-design.md § 5): supersampling 2× +
// TAA and high shadows from the framework's IRenderQuality, time of day from ITimeOfDay. Each lever is Off / While
// composing (panel or docked strip open) / Always; the token lifetimes live in RenderQualityController (tested).
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

    private HudElement QualityGroup() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new SelectableElement(new RowElement(new HudElement[]
            {
                new TextElement(() => _settings.QualityOpen ? "▾" : "▸", Width: 14f),
                new TextElement(() => T("ps.q.title"), Emphasis: true),
            }, Gap: 4f), OnClick: () => _settings.SetQualityOpen(!_settings.QualityOpen)),
            new SpacerElement(),
            HelpDot("q.title", () => T("ps.q.title"), () => T("ps.help.q.title")),
        }, Gap: 6f),
        new ConditionalElement(() => _settings.QualityOpen, new RowElement(new HudElement[]
        {
            new SpacerElement(Width: 16f),
            new CellElement(new ColumnElement(new HudElement[]
            {
                ModeRow("q.ssaa", () => _quality.Supersample, SetSupersample, () => _services.RenderQuality.Capabilities.RenderScale),
                ModeRow("q.shadows", () => _quality.Shadows, SetShadows, () => _services.RenderQuality.Capabilities.Shadows),
                HelpToggle("q.boost", () => _quality.BoostForCapture, SetBoostForCapture,
                    new HelpText(() => T("ps.q.boost"), () => T("ps.help.q.boost")),
                    enabled: () => _services.RenderQuality.Capabilities.Shadows),
                ModeRow("q.time", () => _quality.Time, SetTimeMode, () => _services.TimeOfDay.IsAvailable),
                SliderRow(() => T("ps.q.hour"),
                    new SliderElement(() => _quality.Hour, SetHour, 0f, 24f, Enabled: () => _services.TimeOfDay.IsAvailable),
                    HourText,
                    () => SetHour(12f)),
                new TextElement(LiveQualityText, Color: Muted),
            }, Gap: 4f), Weight: 1f),
        })),
    }, Gap: 4f);

    /// <summary>[label] [Off][Composing][Always] … [?] — greyed out when the lever is unavailable on this client.</summary>
    private HudElement ModeRow(string key, Func<QualityMode> get, Action<QualityMode> set, Func<bool> available)
        => new RowElement(new HudElement[]
        {
            new CellElement(new TextElement(() => T("ps." + key), Color: () => available() ? Muted() : MenuMuted()), Width: LabelW),
            ModeButton(QualityMode.Off, "ps.q.off", get, set, available),
            ModeButton(QualityMode.WhileComposing, "ps.q.composing", get, set, available),
            ModeButton(QualityMode.Always, "ps.q.always", get, set, available),
            new SpacerElement(),
            HelpDot(key, () => T("ps." + key), () => available() ? T("ps.help." + key) : T("ps.help.q.unavailable")),
        }, Gap: 4f);

    private HudElement ModeButton(QualityMode mode, string labelKey, Func<QualityMode> get, Action<QualityMode> set, Func<bool> available)
        => new ButtonElement(() => T(labelKey), OnClick: () => set(mode), Enabled: available, Active: () => get() == mode);

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

    private string HourText() => _quality.Time != QualityMode.Off && _services.TimeOfDay.IsAvailable
        ? _loc.TFormat("ps.q.hourLive", FormatHour(_quality.Hour), FormatHour(_services.TimeOfDay.CurrentHour))
        : FormatHour(_quality.Hour);

    private string LiveQualityText() => _liveText;

    /// <summary>24 h float → HH:MM (13.5 → "13:30"); 24.0 wraps to 00:00.</summary>
    private static string FormatHour(float hour)
    {
        var total = (int)Math.Round(hour * 60f) % (24 * 60);
        if (total < 0) total += 24 * 60;
        return $"{total / 60:00}:{total % 60:00}";
    }
}
