using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Posing;

namespace Stellar.PhotoStudio;

// Posing by person (spec 2026-10-02-photo-studio-posing-design.md): the Person group's controller, kept in step with the
// free camera's orbit subject (one selection). The framework resets every touched person whenever the free camera ends,
// whatever the reason; the controller then forgets the panel state. The UI is Plugin.Panel.Person.cs.
public sealed partial class Plugin
{
    private PosingController _posingCtl = null!;
    private Action _onPosingChanged = null!;

    private void StartPosing()
    {
        _posingCtl = new PosingController(_services.Posing,
            new PosingHost(() => _freeCam.Subject, id => _freeCam.SetSubject(id), OnPoseResult));
        _onPosingChanged = OnPosingChanged;
        _services.Posing.Changed += _onPosingChanged;
        RetireLookAtToggle();
    }

    private void StopPosing() => _services.Posing.Changed -= _onPosingChanged;

    /// <summary>From OnFreeCamStateChanged: follow the orbit subject while the free camera is on; forget everything when
    /// it ends (the framework has already reset every touched person by then).</summary>
    private void SyncPosing()
    {
        if (_freeCam.Active) _posingCtl.SyncSubject();
        else _posingCtl.Clear();
    }

    private void OnPosingChanged()
    {
        _posingCtl.OnPosingChanged();
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
