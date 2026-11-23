using System.Globalization;
using System.Text;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Closing Photo Studio (player request "It is not intuitive to close photo studio", owner go 2026-10-08; mockup
// claude.ai/artifact/4gvmBZzZVCzoL8bxBSm1yX). Two buttons, one meaning each, everywhere:
//   – minimizes the full panel to the compact strip (in or out of the game's photo mode) and changes nothing;
//   ✕ (panel, strip, or the Close Photo Studio hotkey) ends everything — free camera, freeze, posed people, lamps, hides —
//     after a confirm when any of that is running. A pinned Look stays (pinning means "keep this while I play").
// 2.0.0: on the full panel the confirm is the framework's MODAL card (ConfirmHostElement, Caution — approved prototype
// § 3: "!" tile, "Close Photo Studio?", "THIS UNDOES YOUR SCENE", the list, Cancel / Close). The 96 px compact strip has
// no room for that card (≈ 200 px tall), so it keeps its inline confirm in the slider row, restyled with the kit.
public sealed partial class Plugin
{
    private bool _closeConfirm;   // the strip's inline confirm is up
    private bool _minimized;      // the strip is up because the player minimized, not because of the game's photo mode
    private readonly ConfirmController _panelConfirm = new();   // the full panel's modal confirms (close, delete preset)

    private CloseConfirm.Running RunningNow() => new(
        _freeCam.Active, SceneFrozen, ScenePosedCount, _lights.Count, _lights.LitCount > 0, _settings.Hides != VisibilityLayers.None);

    /// <summary>✕ on the panel or the strip, and the Close Photo Studio hotkey.</summary>
    private void RequestClose()
    {
        if (!RunningNow().NeedsConfirm) { CloseStudio(); return; }
        if (_dockedShown) { _closeConfirm = true; MarkCloseSurfacesDirty(); return; }   // the strip asks in place
        if (_panelShown) { ShowCloseModal(); return; }
        // The hotkey with nothing on screen: open the panel, then ask once it is up — a request opened on a hidden host is
        // cancelled by the framework, so it waits one framework tick.
        _panel.Set(true);
        _services.Framework.Post(() => { if (_panelShown) ShowCloseModal(); });
    }

    private void ShowCloseModal()
    {
        _panelConfirm.Show(new ConfirmRequest(T("ps.close.title"), CloseLinesText("\n"), CloseStudio)
        {
            Subtitle = T("ps.close.subtitle"),
            Severity = ConfirmSeverity.Caution,
            ConfirmLabel = T("ps.close.confirm"),
            CancelLabel = T("ps.close.cancel"),
        });
        _panelWin.MarkDirty();
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
        _panelConfirm.Cancel();
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

    // The strip's lines, composed when the confirm opens (and on a language switch), not on every poll.
    private Cached<(bool, CloseConfirm.Running, string)>? _dockedCloseLines;

    private string DockedCloseLines() => (_dockedCloseLines ??= new Cached<(bool, CloseConfirm.Running, string)>(_ => CloseLinesText(" · ")))
        .Get((_closeConfirm, RunningNow(), T("ps.close.title")));

    /// <summary>The confirm row that replaces the strip's slider row: [Close Photo Studio? (Caution)] [what it undoes] …
    /// [Cancel] [Close (Caution)].</summary>
    private HudElement DockedCloseConfirm() => new RowElement(new HudElement[]
    {
        new SpacerElement(0f, PillH),
        new TextElement(Loc("ps.close.title"), Color: () => CautionColor(), NoWrap: true, FontSize: 13) { Bold = true },
        new CellElement(new TextElement(DockedCloseLines, Color: () => MutedColor(), NoWrap: true) { Ellipsize = true }, Weight: 1f),
        Pill(PillKind.Neutral, "ps.close.cancel", CancelClose, 64f, surface: WindowBg),
        Pill(PillKind.Caution, "ps.close.confirm", CloseStudio, 64f, surface: WindowBg),
    }, Gap: 12f);

    /// <summary>"Shift+F10 Photo Studio" beside the SCENE / camera pills while neither the panel nor the strip is up — the
    /// way back is always on screen.</summary>
    private HudElement ReopenPill() => new ConditionalElement(() => !_panelShown && !_dockedShown,
        new PillElement(() => _loc.TFormat("sc.pill.open", BindingText(StudioHotkeys.Panel)), Color: () => _services.Theme.Colors.HudText));

    /// <summary>"Ctrl+Shift+F10 close Photo Studio" on the SCENE pill row — a frozen or posed scene always shows how to end it.</summary>
    private HudElement ClosePill() => new PillElement(() => _loc.TFormat("sc.pill.close", BindingText(StudioHotkeys.Close)),
        Color: () => _services.Theme.Colors.HudText);
}
