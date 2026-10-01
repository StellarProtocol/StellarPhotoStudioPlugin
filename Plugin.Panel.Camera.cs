using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

// Camera tab (spec § 5, mockup 2026-10-01-free-camera-mockup.html): Free camera button + status line; Movement group
// (speed, sensitivity, smoothing, leash, invert Y, entry hides); Pose group (search, ★ favourites, list, look at camera).
public sealed partial class Plugin
{
    private const int EmotePoolSize = 10;
    private const float EmoteRowHeight = 30f;
    private const float FcSliderSaveDelay = 0.5f;

    private string _emoteQuery = "";
    private List<EmoteInfo> _emoteView = new();
    private bool _emoteListDirty = true;
    private bool _emoteScrollReset;
    private int _emoteFirst;
    private float _fcSliderSaveIn = -1f;

    private HudElement BuildCameraTab() => new ColumnElement(new HudElement[]
    {
        // Mockup: status line left, Free camera button right, one row.
        new RowElement(new HudElement[]
        {
            new CellElement(new TextElement(FreeCamStatus, Color: Muted), Weight: 1f),
            new ButtonElement(() => _freeCam.Active ? T("fc.exit") : T("fc.enter"),
                OnClick: () => ToggleFreeCamera(), Style: MenuButtonStyle.Filled),
        }, Gap: 8f),
        new SeparatorElement(),
        MovementGroup(),
        new SeparatorElement(),
        PoseGroup(),
    }, Gap: 8f);

    private string FreeCamStatus() =>
        !_freeCam.Active ? _loc.TFormat("fc.status.off", BindingText(StudioHotkeys.FreeCam))
        : _freeCam.Mode == FreeCamMode.Fly ? T("fc.status.fly")
        : _loc.TFormat("fc.status.orbit", SubjectName());

    /// <summary>One Maestro slider row's inputs (a record keeps <see cref="HelpSliderRow"/> at one parameter).</summary>
    private sealed record SliderSpec(string Key, string HelpKey, Func<float> Get, Action<float> Set, float Min, float Max,
        Func<string> Value, float Reset);

    // Lambdas, not method groups, throughout: the panel tree is built in RegisterWindows and every target is read when
    // the control fires, never when the tree is built.
    private HudElement MovementGroup() => FoldGroup("fc.group.movement", "fc.help.movement",
        () => _fcSettings.MovementOpen, open => _fcSettings.SetMovementOpen(open), new HudElement[]
        {
            HelpSliderRow(new SliderSpec("fc.speed", "fc.help.speed", () => _fcSettings.MoveSpeed, v => _fcSettings.SetMoveSpeed(v, false),
                FreeCamSettings.MinSpeed, FreeCamSettings.MaxSpeed, () => _loc.TFormat("fc.unit.speed", F(_fcSettings.MoveSpeed, "0.0")), FreeCamSettings.DefaultSpeed)),
            HelpSliderRow(new SliderSpec("fc.sensitivity", "fc.help.sensitivity", () => _fcSettings.Sensitivity, v => _fcSettings.SetSensitivity(v, false),
                FreeCamSettings.MinSensitivity, FreeCamSettings.MaxSensitivity, () => F(_fcSettings.Sensitivity, "0.00"), FreeCamSettings.DefaultSensitivity)),
            HelpSliderRow(new SliderSpec("fc.smoothing", "fc.help.smoothing", () => _fcSettings.Smoothing, v => _fcSettings.SetSmoothing(v, false),
                0f, 1f, () => F(_fcSettings.Smoothing, "0.00"), FreeCamSettings.DefaultSmoothing)),
            HelpSliderRow(new SliderSpec("fc.leash", "fc.help.leash", () => _fcSettings.Leash, v => _fcSettings.SetLeash(v, false),
                FreeCamSettings.MinLeash, FreeCamSettings.MaxLeash, () => _loc.TFormat("fc.unit.metres", F(_fcSettings.Leash, "0")), FreeCamSettings.DefaultLeash)),
            HelpToggle("fc.invertY", () => _fcSettings.InvertY, on => _fcSettings.SetInvertY(on),
                new HelpText(() => T("fc.invertY"), () => T("fc.help.invertY"))),
            HelpToggle("fc.entryHides", () => _fcSettings.EntryHides, on => _fcSettings.SetEntryHides(on),
                new HelpText(() => T("fc.entryHides"), () => T("fc.entryHides"))),
        });

    private HudElement PoseGroup() => FoldGroup("fc.group.pose", "fc.help.pose",
        () => _fcSettings.PoseOpen, open => _fcSettings.SetPoseOpen(open), new HudElement[]
        {
            LabeledRow(() => T("fc.search"), new InputElement(() => _emoteQuery, SetEmoteQuery, Width: 220f, OnChange: SetEmoteQuery)),
            new ConditionalElement(() => EmoteView().Count > 0,
                new VirtualListElement(() => EmoteView().Count, EmoteRowHeight, BuildEmotePool(), first => _emoteFirst = first, Height: 220f)
                    { ResetScroll = TakeEmoteScrollReset },
                new TextElement(() => _services.Emotes.Unlocked.Count == 0 ? T("fc.pose.none") : T("fc.pose.empty"), Color: Muted)),
            HelpToggle("fc.lookAt", () => _fcSettings.LookAt, on => _freeCam.SetLookAt(on),
                new HelpText(() => T("fc.lookAt"), () => T("fc.help.lookAt"))),
        });

    /// <summary>The foldable group header QualityGroup uses: ▾/▸ title … "?".</summary>
    private HudElement FoldGroup(string titleKey, string helpKey, Func<bool> open, Action<bool> setOpen, HudElement[] rows) =>
        new ColumnElement(new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new SelectableElement(new RowElement(new HudElement[]
                {
                    new TextElement(() => open() ? "▾" : "▸", Width: 14f),
                    new TextElement(() => T(titleKey), Emphasis: true),
                }, Gap: 4f), OnClick: () => setOpen(!open())),
                new SpacerElement(),
                HelpDot(titleKey, () => T(titleKey), () => T(helpKey)),
            }, Gap: 6f),
            new ConditionalElement(open, new RowElement(new HudElement[]
            {
                new SpacerElement(Width: 16f),
                new CellElement(new ColumnElement(rows, Gap: 4f), Weight: 1f),
            })),
        }, Gap: 4f);

    /// <summary>Maestro slider row: [label] [slider] [value] [↺] [?]. Drags save once they settle.</summary>
    private HudElement HelpSliderRow(SliderSpec s) => new RowElement(new HudElement[]
    {
        new CellElement(SliderRow(() => T(s.Key), new SliderElement(s.Get, v => { s.Set(v); _fcSliderSaveIn = FcSliderSaveDelay; }, s.Min, s.Max),
            s.Value, () => { s.Set(s.Reset); _fcSliderSaveIn = FcSliderSaveDelay; }), Weight: 1f),
        HelpDot(s.Key, () => T(s.Key), () => T(s.HelpKey)),
    }, Gap: 6f);

    private HudElement[] BuildEmotePool()
    {
        var pool = new HudElement[EmotePoolSize];
        for (var i = 0; i < pool.Length; i++) pool[i] = EmoteRow(i);
        return pool;
    }

    private HudElement EmoteRow(int slot) => new RowElement(new HudElement[]
    {
        new CellElement(new ButtonElement(() => EmoteAt(slot) is { } e && _fcSettings.IsFavourite(e.Id) ? "★" : "☆",
            OnClick: () => ToggleFavouriteAt(slot)), Width: 28f),
        new CellElement(new SelectableElement(new TextElement(() => EmoteAt(slot)?.Name ?? "", NoWrap: true),
            OnClick: () => PlayEmoteAt(slot)), Weight: 1f),
    }, Gap: 6f);

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

    private void PlayEmoteAt(int slot)
    {
        if (EmoteAt(slot) is not { } e) return;
        var result = _services.Emotes.PlayAsync(e.Id).Result;   // synchronous Lua call (Task.FromResult)
        // Refused = the game already showed its own message (spec § 4); only our own failures get a toast.
        if (result is EmoteResult.Failed or EmoteResult.Unavailable)
            _services.Notifications.Notify(T("fc.toast.emoteFailed"), NotificationKind.Warning);
    }

    private void TickFreeCamUi(float dt)
    {
        if (_fcSliderSaveIn <= 0f) return;
        _fcSliderSaveIn -= dt;
        if (_fcSliderSaveIn > 0f) return;
        _fcSliderSaveIn = -1f;
        _fcSettings.SaveSliders();
    }
}
