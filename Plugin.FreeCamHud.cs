using System.Globalization;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

// The free-camera HUD pill (spec § 5, mockup 2026-10-01-free-camera-mockup.html): top centre, framework HUD chrome,
// theme colours only — "ORBIT · <subject> · 12.4 / 30 m · FOV 45° · Roll 0°" + ❄ FROZEN (accent) + ⚔ IN COMBAT
// (the theme's HP red) and a key-hint line that H toggles. Never in the photo: capture renders the camera only.
public sealed partial class Plugin
{
    private IWindowControl _freeCamHudWin = null!;

    private IWindowControl RegisterFreeCamHud() => _services.Windows.Register(new WindowRegistration(
        new WindowSpec(
            Id: "photostudio.freecam.hud",
            Title: T("fc.title.hud"),
            DefaultRect: new WindowRect(0f, 16f, 800f, 0f),   // 800: the id fly hint with Ctrl+Shift+F10 needs 752 px of chip (sandbox S3)
            Category: WindowCategory.HUD,
            Style: WindowPanelStyle.Borderless)
        {
            // Same HUD set-up as PlayerHUD / RaidManager: HUD surface (shadowed text, transparent pill chips).
            Surface = SurfaceStyle.HudOverlay,
            ShowTitleBar = false, StartVisible = false, Draggable = true, EditModeDragOnly = true,
            Anchor = WindowAnchor.Top, ShouldRender = () => InWorld() && (_freeCam.Active || ScenePillVisible),
        },
        // Free camera on: the camera line + key hint. Off with a scene set: the SCENE pill (scene-stays spec § 7).
        new ConditionalElement(() => _freeCam.Active, CameraHud(), ScenePill())));

    private HudElement CameraHud() => new ColumnElement(new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new PillElement(HudLine, Color: () => _services.Theme.Colors.HudText),
                new ConditionalElement(() => SceneFrozen,
                    new PillElement(() => T("fc.badge.frozen"), Color: () => _services.Theme.Colors.HudText)),   // ❄ carries the meaning; HudAccent was unreadable / same red as combat in Crimson (sandbox S2)
                new ConditionalElement(() => _services.CombatState.LocalPlayerInCombat,
                    new PillElement(() => T("fc.badge.combat"), Color: () => _services.Theme.Colors.HpFill)),
            }, Gap: 6f, Justify: RowJustify.Center),
            new ConditionalElement(() => !_fcSettings.HintHidden,
                // The hint sits on its own HUD pill chip (mockup .hint) so it stays readable over a bright world.
                new RowElement(new HudElement[] { new PillElement(HudHint, Color: () => _services.Theme.Colors.HudText) },
                    Justify: RowJustify.Center)),
        }, Gap: 4f);

    // Mockup order: SCENE · ❄ FROZEN · N posed · ⚔ IN COMBAT · <key> back to the camera.
    private HudElement ScenePill() => new RowElement(new HudElement[]
    {
        new PillElement(() => T("sc.pill.title"), Color: () => _services.Theme.Colors.HudText),
        new ConditionalElement(() => SceneFrozen,
            new PillElement(() => T("fc.badge.frozen"), Color: () => _services.Theme.Colors.HudText)),
        new ConditionalElement(() => ScenePosedCount > 0,
            new PillElement(() => _loc.TFormat("sc.pill.posed", ScenePosedCount.ToString(CultureInfo.InvariantCulture)),
                Color: () => _services.Theme.Colors.HudText)),
        new ConditionalElement(() => _services.CombatState.LocalPlayerInCombat,
            new PillElement(() => T("fc.badge.combat"), Color: () => _services.Theme.Colors.HpFill)),
        new PillElement(() => _loc.TFormat("sc.pill.back", BindingText(StudioHotkeys.FreeCam)),
            Color: () => _services.Theme.Colors.HudText),
    }, Gap: 6f, Justify: RowJustify.Center);

    private string HudLine() => _loc.TFormat("fc.hud.line",
        T(_freeCam.Mode == FreeCamMode.Orbit ? "fc.mode.orbit" : "fc.mode.fly"),
        SubjectName(),
        F(_freeCam.Distance, "0.0"),
        F(_fcSettings.Leash, "0"),
        F(_freeCam.Fov, "0"),
        F(_freeCam.Roll, "0"));

    private string HudHint() => _loc.TFormat(_freeCam.Mode == FreeCamMode.Orbit ? "fc.hint.orbit" : "fc.hint.fly",
        BindingText(StudioHotkeys.FreeCam));
}
