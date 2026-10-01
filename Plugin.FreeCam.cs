using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

// Free camera wiring (spec 2026-10-01-photo-studio-free-camera-design.md): the session, its toasts, death, click-through
// protection. The HUD pill lives in Plugin.FreeCamHud.cs; the Camera tab in Plugin.Panel.Camera.cs.
public sealed partial class Plugin
{
    private FreeCamSession _freeCam = null!;
    private FreeCamSettings _fcSettings = null!;
    private Action<CombatEvent> _onFreeCamCombatEvent = null!;
    private Action _onEmotesChanged = null!;

    private void StartFreeCamera()
    {
        _fcSettings = new FreeCamSettings(_services.Config.GetSection("photostudio"));
        var ports = new FreeCamPorts(_services.CameraOverride, _services.InputShield, _services.SceneFreeze, _services.CombatState,
            _services.SceneVisibility, _services.EntityTransforms, _services.CombatSnapshot, _services.EntityPicker);
        _freeCam = new FreeCamSession(ports, _fcSettings, OnFreeCamNotice, PointerOverOwnWindow);
        _freeCam.StateChanged += OnFreeCamStateChanged;
        _onFreeCamCombatEvent = OnFreeCamCombatEvent;
        _services.CombatEvents.CombatEventOccurred += _onFreeCamCombatEvent;
        _onEmotesChanged = () => _emoteListDirty = true;
        _services.Emotes.UnlockedChanged += _onEmotesChanged;
    }

    private void StopFreeCamera()
    {
        _services.CombatEvents.CombatEventOccurred -= _onFreeCamCombatEvent;
        _services.Emotes.UnlockedChanged -= _onEmotesChanged;
        _freeCam.StateChanged -= OnFreeCamStateChanged;
        _freeCam.Dispose();
        _fcSettings.SaveSliders();
    }

    private void ToggleFreeCamera()
    {
        if (_freeCam.Active) _freeCam.Exit();
        else if (InWorld()) _freeCam.Enter();
    }

    private void OnFreeCamStateChanged()
    {
        _freeCamHudWin.SetVisible(_freeCam.Active);
        _freeCamHudWin.MarkDirty();
        _panelWin.MarkDirty();
    }

    // The event may arrive off the main thread: compare ids only after posting to the main thread.
    private void OnFreeCamCombatEvent(CombatEvent ev)
    {
        if (ev is not CombatEvent.EntityStateChanged { State: ActorState.Dead } dead) return;
        var target = dead.TargetId;
        _services.Framework.Post(() => { if (target == _services.CombatSnapshot.LocalEntityId) _freeCam.OnLocalDeath(); });
    }

    private void OnFreeCamNotice(FreeCamNotice notice, CameraReleaseReason reason)
    {
        var (text, kind) = notice switch
        {
            FreeCamNotice.Busy => (T("fc.toast.busy"), NotificationKind.Warning),
            FreeCamNotice.Unavailable => (T("fc.toast.unavailable"), NotificationKind.Warning),
            FreeCamNotice.Error => (T("fc.toast.error"), NotificationKind.Error),
            _ => (_loc.TFormat("fc.toast.released", ReasonText(reason)), NotificationKind.Info),
        };
        _services.Notifications.Notify(text, kind);
    }

    private string ReasonText(CameraReleaseReason reason) => reason switch
    {
        CameraReleaseReason.SceneChanged => T("fc.reason.scene"),
        CameraReleaseReason.Cutscene => T("fc.reason.cutscene"),
        CameraReleaseReason.GamePhotoMode => T("fc.reason.photo"),
        _ => T("fc.reason.disconnect"),
    };

    /// <summary>Click-to-orbit never picks through Photo Studio's own windows.</summary>
    private bool PointerOverOwnWindow(float x, float y)
    {
        var fw = _services.Framework;
        var scale = fw.CanvasWidth > 0 ? (float)fw.ScreenWidth / fw.CanvasWidth : 1f;
        foreach (var w in new[] { _panelWin, _freeCamHudWin, _tipWindow, _dockedWin, _toastWin })
            if (w.IsShown && UiHitTest.Contains(w.Rect, x, y, scale)) return true;
        return false;
    }

    private string SubjectName()
    {
        var id = _freeCam.Subject;
        if (id == _services.CombatSnapshot.LocalEntityId) return _services.PlayerState.Name ?? T("fc.you");
        return _services.CombatLookup.GetEntityName(id) ?? "—";
    }

}
