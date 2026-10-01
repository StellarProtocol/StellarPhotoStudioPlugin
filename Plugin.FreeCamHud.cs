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
            DefaultRect: new WindowRect(0f, 16f, 760f, 0f),
            Category: WindowCategory.Tools,
            Style: WindowPanelStyle.HudOverlay)
        {
            ShowTitleBar = false, StartVisible = false, Draggable = true, EditModeDragOnly = true,
            Anchor = WindowAnchor.Top, ShouldRender = () => InWorld() && _freeCam.Active,
        },
        new ColumnElement(new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new PillElement(HudLine),
                new ConditionalElement(() => _freeCam.Frozen,
                    new PillElement(() => T("fc.badge.frozen"), Color: () => _services.Theme.Colors.Accent)),
                new ConditionalElement(() => _services.CombatState.LocalPlayerInCombat,
                    new PillElement(() => T("fc.badge.combat"), Color: () => _services.Theme.Colors.HpFill)),
            }, Gap: 6f, Justify: RowJustify.Center),
            new ConditionalElement(() => !_fcSettings.HintHidden,
                new TextElement(HudHint, Color: Muted, Align: TextAlign.Center, Shadow: true, NoWrap: true)),
        }, Gap: 4f)));

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
