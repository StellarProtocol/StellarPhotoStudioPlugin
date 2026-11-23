using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

// The Pose section's pose list (the 1.1 Pose list — search, ★ favourites, a virtual list of the unlocked emotes; filter
// in FreeCam/EmoteFilter.cs, tested). Rows are house list tiles: [★ / ☆] [name], the current pose highlighted.
public sealed partial class Plugin
{
    private const int EmotePoolSize = 10;     // 8 visible rows + margin for a part-scrolled row
    private const int EmoteVisibleRows = 8;

    private string _emoteQuery = "";
    private List<EmoteInfo> _emoteView = new();
    private bool _emoteListDirty = true;
    private bool _emoteScrollReset;
    private int _emoteFirst;

    private HudElement PoseList() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            RowLabel("fc.search", null, RowLabelW),
            new CellElement(new InputElement(() => _emoteQuery, SetEmoteQuery, Width: 240f, OnChange: SetEmoteQuery)
                { Placeholder = Loc("ps.pose.searchHint") }, Weight: 1f),
        }, Gap: RowGap),
        new ConditionalElement(() => EmoteView().Count > 0,
            new VirtualListElement(() => EmoteView().Count, ListRowPitch, BuildEmotePool(), first => _emoteFirst = first,
                Height: ListHeight(EmoteVisibleRows)) { ResetScroll = TakeEmoteScrollReset },
            NoteLine(() => _services.Emotes.Unlocked.Count == 0 ? T("fc.pose.none") : T("fc.pose.empty"))),
    }, Gap: 6f);

    private HudElement[] BuildEmotePool()
    {
        var pool = new HudElement[EmotePoolSize];
        for (var i = 0; i < pool.Length; i++) pool[i] = EmoteRow(i);
        return pool;
    }

    // [★ amber / ☆ muted] [name, the whole rest of the row clicks to pose]. Plain-text stars (they draw in the 14 px
    // menu face; bold ★ is untrusted — KB §6).
    private HudElement EmoteRow(int slot) => ListSlot(ListTile(new RowElement(new HudElement[]
    {
        new SpacerElement(0f, ListTileH),
        new SelectableElement(new TextElement(() => IsFavouriteAt(slot) ? "★" : "☆",
            Color: () => IsFavouriteAt(slot) ? CautionColor() : MutedColor(), Width: 22f, Align: TextAlign.Center, NoWrap: true),
            OnClick: () => ToggleFavouriteAt(slot)) { Bare = true },
        new CellElement(new SelectableElement(new TextElement(() => EmoteAt(slot)?.Name ?? "", Color: () => TextColor(), NoWrap: true)
            { Ellipsize = true }, OnClick: () => PlayEmoteAt(slot)) { Bare = true }, Weight: 1f),
    }, Gap: 4f), selected: () => EmoteAt(slot) is { } e && _posingCtl.State.Action?.Id == e.Id));

    private bool IsFavouriteAt(int slot) => EmoteAt(slot) is { } e && _fcSettings.IsFavourite(e.Id);

    private EmoteInfo? EmoteAt(int slot)
    {
        var view = EmoteView();
        var i = _emoteFirst + slot;
        return i >= 0 && i < view.Count ? view[i] : null;
    }

    private List<EmoteInfo> EmoteView()
    {
        if (!_emoteListDirty) return _emoteView;
        _emoteView = EmoteFilter.Apply(_services.Emotes.Unlocked, _emoteQuery, _fcSettings.Favourites);
        _emoteListDirty = false;
        return _emoteView;
    }

    private void SetEmoteQuery(string q)
    {
        if (q == _emoteQuery) return;
        _emoteQuery = q;
        _emoteListDirty = true;
        _emoteScrollReset = true;
    }

    private bool TakeEmoteScrollReset()
    {
        var reset = _emoteScrollReset;
        _emoteScrollReset = false;
        return reset;
    }

    private void ToggleFavouriteAt(int slot)
    {
        if (EmoteAt(slot) is not { } e) return;
        _fcSettings.ToggleFavourite(e.Id);
        _emoteListDirty = true;
    }

    /// <summary>An emote row poses the selected person (spec 2026-10-02 § 3); refusals are reported by the controller.</summary>
    private void PlayEmoteAt(int slot)
    {
        if (EmoteAt(slot) is not { } e) return;
        _posingCtl.Play(e);
        _poseListOpen = false;
    }
}
