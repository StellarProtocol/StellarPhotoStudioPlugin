using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Posing;

namespace Stellar.PhotoStudio;

// Posing by person (spec 2026-10-02-photo-studio-posing-design.md; scene-stays spec § 5): the Person group's controller.
// It keeps its own selection, so posing works with the free camera off; while the camera is on the selection and the
// orbit subject are one. Poses belong to the scene (Plugin.Scene.cs): leaving the free camera keeps them; Reset scene and
// the framework's scene-end reasons (targets released) end them. The UI is Plugin.Panel.Person.cs.
public sealed partial class Plugin
{
    private const float PosePollInterval = 0.1f;   // the panel's own ~10 Hz refresh
    private PosingController _posingCtl = null!;
    private Action _onPosingChanged = null!;
    private Action _onPosesCounted = null!;
    private Func<EntityId, bool> _isSeen = null!;
    private float _posePollIn;

    private void StartPosing()
    {
        StartSceneSelection();   // Plugin.Scene.cs — needs the free camera session
        _isSeen = _selection.IsSeen;
        _posingCtl = new PosingController(_services.Posing,
            new PosingHost(() => _freeCam.Subject, _selection.Select, OnPoseResult, () => _services.CombatSnapshot.LocalEntityId,
                DescribeAction));
        _scene.TrackPoses(() => _posingCtl.PosedCount);
        _onPosesCounted = _scene.NotifyPosesChanged;
        _posingCtl.PosedChanged += _onPosesCounted;
        _onPosingChanged = OnPosingChanged;
        _services.Posing.Changed += _onPosingChanged;
        RetireLookAtToggle();
    }

    private void StopPosing()
    {
        _services.Posing.Changed -= _onPosingChanged;
        _posingCtl.PosedChanged -= _onPosesCounted;
    }

    /// <summary>From OnFreeCamStateChanged: follow the orbit subject while the free camera is on. Leaving the free camera
    /// keeps the selection and every pose (scene-stays spec § 1).</summary>
    private void SyncPosing()
    {
        if (_freeCam.Active) _posingCtl.SyncSubject();
    }

    /// <summary>From OnUpdate: while the panel is up (free camera on or off — scene-stays spec § 5), follow what the
    /// selected person is already doing (owner bug 2026-10-02: someone mid-emote showed "Pick a pose" at 0 %) — one cheap
    /// framework read per 0.1 s. Out of the world the selection is forgotten (entity ids no longer mean anyone); off the
    /// free camera a selected person who left falls back to yourself on the same tick (no extra poll).</summary>
    private void TickPosing(float dt)
    {
        if (!InWorld())
        {
            if (!_posingCtl.Subject.IsNone) _posingCtl.Clear();
            return;
        }
        if (!_panelWin.IsShown) return;
        _posingCtl.EnsureSubject();
        _posePollIn -= dt;
        if (_posePollIn > 0f) return;
        _posePollIn = PosePollInterval;
        var gone = !_freeCam.Active && _posingCtl.FallBackIfGone(_isSeen);
        PruneLitPeople();                        // Plugin.Lights.cs — raises its own change (marks the panel dirty)
        if (_posingCtl.PollCurrentAction() | gone) _panelWin.MarkDirty();
    }

    /// <summary>The emote for an action a person is already doing: the unlocked emote when it is one, else "Current pose"
    /// (not unlocked: ↺ is off for it).</summary>
    private DescribedAction DescribeAction(int actionId)
    {
        foreach (var e in _services.Emotes.Unlocked)
            if (e.Id == actionId) return new DescribedAction(e, true);
        return new DescribedAction(new EmoteInfo(actionId, T("pz.pose.current"), "", false), false);
    }

    private void OnPosingChanged()
    {
        _posingCtl.OnPosingChanged();
        PruneLitPeople();                        // Plugin.Lights.cs — a lit person whose model left stops counting
        _panelWin.MarkDirty();
    }

    // Refused = the game already showed its own message (as for 1.1 emotes); only our own failures get a toast.
    private void OnPoseResult(PoseResult result)
    {
        if (result == PoseResult.Unavailable) _services.Notifications.Notify(T("fc.toast.emoteFailed"), NotificationKind.Warning);
    }

    /// <summary>1.2.0: the head look is per person (Person group → Head). The 1.1 "Look at camera" entry toggle is gone;
    /// a saved "on" from 1.1 testing would otherwise turn your head at every entry with no control left to undo it.</summary>
    private void RetireLookAtToggle()
    {
        if (_fcSettings.LookAt) _fcSettings.SetLookAt(false);
    }
}
