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
            _services.SceneVisibility, _services.EntityTransforms, _services.CombatSnapshot, _services.EntityPicker, _services.Posing);
        var host = new FreeCamHost(OnFreeCamNotice, PointerOverOwnWindow, DismissHelpTip, msg => _services.Log.Warning(msg));
        _freeCam = new FreeCamSession(ports, _fcSettings, host, _scene);
        _freeCam.StateChanged += OnFreeCamStateChanged;
        _freeCam.LampKey += OnLampKey;
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
        _freeCam.LampKey -= OnLampKey;
        _freeCam.Dispose();
        _fcSettings.SaveSliders();
    }

    private void OnLampKey(bool move) => LampToast(move ? MoveLampToCamera() : DropLampAtCamera());   // Plugin.Panel.Lights.cs

    private void ToggleFreeCamera()
    {
        if (_freeCam.Active) _freeCam.Exit();
        else if (InWorld()) _freeCam.Enter(_posingCtl.Subject);   // orbit the selected person; scene set → last pose
    }

    private void OnFreeCamStateChanged()
    {
        SyncPosing();   // Plugin.Posing.cs — selection follows the orbit subject while the free camera is on
        SyncSceneHud();
        _panelWin.MarkDirty();
    }

    // The event may arrive off the main thread: compare ids only after posting to the main thread. Death only matters
    // while frozen (spec § 7: the freeze ends — with or without the free camera, the freeze is the scene's), so any other
    // Dead event — every mob in a fight — posts nothing. Reading Frozen off the main thread is a benign race: the posted
    // callback re-checks through OnLocalDeath.
    private void OnFreeCamCombatEvent(CombatEvent ev)
    {
        if (ev is not CombatEvent.EntityStateChanged { State: ActorState.Dead } dead || !_scene.Frozen) return;
        var target = dead.TargetId;
        _services.Framework.Post(() => { if (target == _services.CombatSnapshot.LocalEntityId) _scene.OnLocalDeath(); });
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

    private IWindowControl[]? _interactiveWins;

    /// <summary>Click-to-orbit, wheel and right-drag never act through Photo Studio's own INTERACTIVE windows. The HUD
    /// pill is a non-interactive overlay (edit-mode drag only), so it is left out: otherwise a 760-px band across the
    /// top of the screen would swallow camera input while the free camera is on (review finding).</summary>
    private bool PointerOverOwnWindow(float x, float y)
    {
        var fw = _services.Framework;
        var scale = fw.CanvasWidth > 0 ? (float)fw.ScreenWidth / fw.CanvasWidth : 1f;
        _interactiveWins ??= new[] { _panelWin, _tipWindow, _dockedWin, _toastWin };
        foreach (var w in _interactiveWins)
            if (w.IsShown && UiHitTest.Contains(w.Rect, x, y, scale)) return true;
        return false;
    }

    private string SubjectName()
    {
        var id = _freeCam.Active ? _freeCam.Subject : _posingCtl.Subject;
        if (id == _services.CombatSnapshot.LocalEntityId) return _services.PlayerState.Name ?? T("fc.you");
        if (_posingCtl.Person is { Name.Length: > 0 } p && p.Id == id) return p.Name;
        return _services.CombatLookup.GetEntityName(id) ?? "—";
    }

}
