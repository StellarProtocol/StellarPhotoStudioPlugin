using System.Globalization;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Scene group (scene-stays spec § 4/§ 7; approved mockup 2026-10-02-scene-stays-mockup.html): Freeze / Unfreeze and
// Reset scene, working with the free camera on or off, then a one-line status. Theme colours only; every control is a
// lambda read when it fires — the tree is built once in RegisterWindows.
public sealed partial class Plugin
{
    private HudElement SceneGroup() => FoldGroup("sc.group", "sc.help",
        () => _fcSettings.SceneOpen, open => _fcSettings.SetSceneOpen(open), new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new CellElement(new ButtonElement(() => T(SceneFrozen ? "sc.unfreeze" : "sc.freeze"),
                    OnClick: () => ToggleSceneFreeze(), Enabled: () => InWorld() || SceneFrozen,
                    Active: () => SceneFrozen), Weight: 1f),
                new CellElement(new ButtonElement(() => T("sc.reset"), OnClick: () => ResetScene(),
                    Enabled: () => SceneIsSet), Weight: 1f),
            }, Gap: 6f),
            new TextElement(SceneStatus, Color: Muted),
        });

    private string SceneStatus()
    {
        var posed = ScenePosedCount.ToString(CultureInfo.InvariantCulture);
        if (SceneFrozen) return ScenePosedCount > 0 ? _loc.TFormat("sc.status.frozen", posed) : T("sc.status.frozenOnly");
        return ScenePosedCount > 0 ? _loc.TFormat("sc.status.posed", posed) : T("sc.status.empty");
    }
}
