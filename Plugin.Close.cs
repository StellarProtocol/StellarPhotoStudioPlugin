using System.Globalization;
using System.Text;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Closing Photo Studio (player request "It is not intuitive to close photo studio", owner go 2026-10-08; mockup
// claude.ai/artifact/4gvmBZzZVCzoL8bxBSm1yX). Two buttons, one meaning each, everywhere:
//   – minimizes the full panel to the compact strip (in or out of the game's photo mode) and changes nothing;
//   ✕ (panel, strip, or the Close Photo Studio hotkey) ends everything — free camera, freeze, posed people, lamps, hides —
//     after an in-panel confirm when any of that is running. A pinned Look stays (pinning means "keep this while I play").
public sealed partial class Plugin
{
    private bool _closeConfirm;   // the confirm bar is up (panel or strip)
    private bool _minimized;      // the strip is up because the player minimized, not because of the game's photo mode

    private CloseConfirm.Running RunningNow() => new(
        _freeCam.Active, SceneFrozen, ScenePosedCount, _lights.Count, _lights.LitCount > 0, _settings.Hides != VisibilityLayers.None);

    /// <summary>✕ on the panel or the strip, and the Close Photo Studio hotkey.</summary>
    private void RequestClose()
    {
        if (!RunningNow().NeedsConfirm) { CloseStudio(); return; }
        _closeConfirm = true;
        if (!_panelShown && !_dockedShown) _panel.Set(true);   // the hotkey with nothing on screen: show the confirm
        MarkCloseSurfacesDirty();
    }

    private void CancelClose()
    {
        _closeConfirm = false;
        MarkCloseSurfacesDirty();
    }

    /// <summary>Ends everything Photo Studio set up, then closes the panel and the strip.</summary>
    private void CloseStudio()
    {
        _closeConfirm = false;
        _minimized = false;
        if (_freeCam.Active) _freeCam.Exit();
        ResetScene();      // unfreeze + every posed person back to normal
        _lights.Clear();   // every lamp removed, every lit person restored
        _hideAllToken?.Dispose();
        _hideAllToken = null;
        _dockedDismissed = _services.PhotoMode.IsActive;   // the game's photo mode stays open; don't bring the strip back this visit
        ShowDocked(false);
        _panel.Set(false);
        ApplyLiveHides();
    }

    /// <summary>– on the full panel: the compact strip takes its place; nothing else changes.</summary>
    private void MinimizePanel()
    {
        _closeConfirm = false;
        _panel.Set(false);
        _minimized = true;   // after Set(false): ShowPanel(false) re-decides the strip from the game's photo mode
        ShowDocked(true);
    }

    private void MarkCloseSurfacesDirty()
    {
        _panelWin.MarkDirty();
        _dockedWin.MarkDirty();
    }

    private string CloseLinesText(string separator)
    {
        var sb = new StringBuilder();
        foreach (var (key, count) in CloseConfirm.Lines(RunningNow()))
        {
            if (sb.Length > 0) sb.Append(separator);
            sb.Append(count < 0 ? T(key) : _loc.TFormat(key, count.ToString(CultureInfo.InvariantCulture)));
        }
        return sb.ToString();
    }

    /// <summary>The confirm block at the top of the full panel.</summary>
    private HudElement PanelCloseConfirm() => new ConditionalElement(() => _closeConfirm, new ColumnElement(new HudElement[]
    {
        new TextElement(() => T("ps.close.title"), Emphasis: true, Color: () => _services.Theme.Colors.Warning),
        new TextElement(() => CloseLinesText("\n"), Color: Muted),
        CloseButtons(),
        new SeparatorElement(),
    }, Gap: 6f));

    /// <summary>The confirm row that replaces the strip's slider row.</summary>
    private HudElement DockedCloseConfirm() => new RowElement(new HudElement[]
    {
        new TextElement(() => T("ps.close.title"), Emphasis: true, Color: () => _services.Theme.Colors.Warning, NoWrap: true),
        new TextElement(() => CloseLinesText(" · "), Color: Muted, NoWrap: true),
        new SpacerElement(),
        CloseButtons(),
    }, Gap: 12f);

    private HudElement CloseButtons() => new RowElement(new HudElement[]
    {
        new SpacerElement(),
        new ButtonElement(() => T("ps.close.cancel"), OnClick: CancelClose, Width: 88f),
        new ButtonElement(() => T("ps.close.confirm"), OnClick: CloseStudio, Style: MenuButtonStyle.Filled, Width: 88f),
    }, Gap: 6f);

    /// <summary>"Shift+F10 Photo Studio" beside the SCENE / camera pills while neither the panel nor the strip is up — the
    /// way back is always on screen.</summary>
    private HudElement ReopenPill() => new ConditionalElement(() => !_panelShown && !_dockedShown,
        new PillElement(() => _loc.TFormat("sc.pill.open", BindingText(StudioHotkeys.Panel)), Color: () => _services.Theme.Colors.HudText));
}
