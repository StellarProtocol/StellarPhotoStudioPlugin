using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

// Camera section (approved prototype "Camera", free camera on / off): FREE CAMERA (status + Start (Primary) / Exit
// (Caution), Orbit / Fly, Show game UI), LENS (the field of view, moved from the 1.x Look tab), SCENE (Freeze / Reset scene
// + status, and the time of day moved from the Look tab's render quality) and the MOVEMENT fold. The free-camera-only
// controls (mode, game UI, field of view) are RECESSED while it is off (rule 22), never hidden. The on-entry hides moved to
// the Hide section; the Person fold became the Pose section. Lambdas throughout: every target is read when it fires.
public sealed partial class Plugin
{
    private const string SecCamera = "ps.tab.camera";
    private const float FcSliderSaveDelay = 0.5f;
    private const float CamPillLabelW = 126f;    // 150 px pill (prototype): "Exit free camera" in every locale
    private static readonly float[] CamModeSegW = { 52f, 52f };
    private float _fcSliderSaveIn = -1f;

    private HudElement BuildCameraSection() => SectionColumn(
        SectionPanel("fc.title.hud", null, new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new CellElement(new TextElement(() => _fcStatus, Color: () => _freeCam.Active ? AccentColor() : MutedColor()), Weight: 1f),
                new ConditionalElement(() => _freeCam.Active,
                    Pill(PillKind.Caution, "fc.exit", ToggleFreeCamera, CamPillLabelW),
                    Pill(PillKind.Primary, "ps.cam.start", ToggleFreeCamera, CamPillLabelW, enabled: InWorld)),
            }, Gap: 8f),
            // Orbit / Fly is the free camera's own live mode (Tab switches it too); there is no stored default to set.
            SettingRow("ps.cam.mode", Segmented(new[] { "ps.cam.orbit", "ps.cam.fly" }, CamModeSegW,
                    () => _freeCam.Mode == FreeCamMode.Fly ? 1 : 0, _ => _freeCam.ToggleMode(), () => _freeCam.Active),
                new HelpKeys("ps.cam.mode", "ps.help.cam.mode", SecCamera, "fc.title.hud"), () => _freeCam.Active),
            // Game UI spec 2026-10-10: shows the game's interface without leaving the free camera (same as U).
            SettingToggle("fc.gameUi", () => _freeCam.GameUiShown, on => _freeCam.SetGameUi(on),
                new HelpKeys("fc.gameUi", "fc.help.gameUi", SecCamera, "fc.title.hud"), enabled: () => _freeCam.Active),
        }),
        SectionPanel("ps.cam.lens", null, new HudElement[]
        {
            // [ / ] / \ and Shift+wheel change it too, free camera only; ↺ = the game's FOV at free-camera entry.
            SettingSlider("ps.look.fov",
                KitSlider(() => CameraMath.ClampFov(_freeCam.Fov), v => _freeCam.SetFov(v), CameraMath.MinFov, CameraMath.MaxFov,
                    () => _freeCam.Active),
                ValueText(() => _freeCam.Fov, v => v > 0f ? F(v, "0") + "°" : "—"),   // never entered yet: no FOV to show
                () => _freeCam.ResetFov(),
                new SliderOpts(new HelpKeys("ps.look.fov", "ps.help.look.fov", SecCamera, "ps.cam.lens") { Body = () => _fovHelp },
                    1f, ParseNumber)),
            NoteLine(() => _fovNote),
        }),
        SectionPanelWithTrailing("sc.group", null,
            new HudElement[] { HelpButton(new HelpKeys("sc.group", "sc.help", SecCamera)) }, new HudElement[]
        {
            // Scene-stays spec § 4: Freeze and Reset scene work with the free camera on or off.
            PillPair(
                new ConditionalElement(() => SceneFrozen,
                    Pill(PillKind.Primary, "ps.cam.unfreeze", () => ToggleSceneFreeze(), PillPairLabelW),
                    Pill(PillKind.Neutral, "ps.cam.freeze", () => ToggleSceneFreeze(), PillPairLabelW, enabled: InWorld)),
                Pill(PillKind.Neutral, "sc.reset", () => ResetScene(), PillPairLabelW, enabled: () => SceneIsSet)),
            NoteLine(() => _sceneStatus),
            SettingRow("ps.q.time", Segmented(new[] { "ps.q.off", "ps.q.composing", "ps.q.always" }, ModeSegW,
                    () => (int)_quality.Time, i => SetTimeMode((QualityMode)i), () => _services.TimeOfDay.IsAvailable),
                new HelpKeys("ps.q.time", "ps.help.q.time", SecCamera, "sc.group")
                    { Body = () => T(_services.TimeOfDay.IsAvailable ? "ps.help.q.time" : "ps.help.q.unavailable") },
                () => _services.TimeOfDay.IsAvailable),
            SettingSlider("ps.q.hour",
                KitSlider(() => _quality.Hour, SetHour, 0f, 24f, () => _services.TimeOfDay.IsAvailable && _quality.Time != QualityMode.Off),
                HourText, () => SetHour(12f), new SliderOpts(Step: 5f / 60f, Parse: ParseHour)),   // 5-minute steps
        }),
        FoldPanel("fc.group.movement", null, new FoldFlag(() => _fcSettings.MovementOpen, o => _fcSettings.SetMovementOpen(o)),
            new HudElement[] { HelpButton(new HelpKeys("fc.group.movement", "fc.help.movement", SecCamera)) }, new HudElement[]
        {
            MoveSlider("fc.speed", "fc.help.speed", () => _fcSettings.MoveSpeed, v => _fcSettings.SetMoveSpeed(v, false),
                FreeCamSettings.MinSpeed, FreeCamSettings.MaxSpeed, ValueText(() => _fcSettings.MoveSpeed,
                    v => _loc.TFormat("fc.unit.speed", F(v, "0.0")), "fc.unit.speed"), FreeCamSettings.DefaultSpeed, 0.1f),
            MoveSlider("fc.sensitivity", "fc.help.sensitivity", () => _fcSettings.Sensitivity, v => _fcSettings.SetSensitivity(v, false),
                FreeCamSettings.MinSensitivity, FreeCamSettings.MaxSensitivity, ValueText(() => _fcSettings.Sensitivity, v => F(v, "0.00")),
                FreeCamSettings.DefaultSensitivity, 0.05f),
            MoveSlider("fc.smoothing", "fc.help.smoothing", () => _fcSettings.Smoothing, v => _fcSettings.SetSmoothing(v, false),
                0f, 1f, ValueText(() => _fcSettings.Smoothing, v => F(v, "0.00")), FreeCamSettings.DefaultSmoothing, 0.01f),
            MoveSlider("fc.leash", "fc.help.leash", () => _fcSettings.Leash, v => _fcSettings.SetLeash(v, false),
                FreeCamSettings.MinLeash, FreeCamSettings.MaxLeash, ValueText(() => _fcSettings.Leash,
                    v => _loc.TFormat("fc.unit.metres", F(v, "0")), "fc.unit.metres"), FreeCamSettings.DefaultLeash, 1f),
            SettingToggle("fc.invertY", () => _fcSettings.InvertY, on => _fcSettings.SetInvertY(on),
                new HelpKeys("fc.invertY", "fc.help.invertY", SecCamera, "fc.group.movement")),
        }));

    /// <summary>A movement slider: applied live, saved once the drag settles (a config save is a main-thread file write).</summary>
    private HudElement MoveSlider(string key, string helpKey, Func<float> get, Action<float> set, float min, float max,
        Func<string> value, float reset, float step)
        => SettingSlider(key,
            KitSlider(get, v => { set(v); _fcSliderSaveIn = FcSliderSaveDelay; }, min, max),
            value, () => { set(reset); _fcSliderSaveIn = FcSliderSaveDelay; },
            new SliderOpts(new HelpKeys(key, helpKey, SecCamera, "fc.group.movement"), step));

    private void TickFreeCamUi(float dt)
    {
        if (_fcSliderSaveIn <= 0f) return;
        _fcSliderSaveIn -= dt;
        if (_fcSliderSaveIn > 0f) return;
        _fcSliderSaveIn = -1f;
        _fcSettings.SaveSliders();
    }
}
