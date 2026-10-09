using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>Where the camera starts on entry (scene-stays spec § 6): the pose it had when it left while the SAME scene is
/// still set, else the game camera's pose (R always snaps to the game camera); and the entry hides, which the scene keeps
/// while the camera is away (review I-1).</summary>
internal sealed partial class FreeCamSession
{
    private SavedPose? _lastPose;

    /// <summary>A pose is remembered for the scene that is set now (the camera left it and it has not ended since). Lamps
    /// alone do not count (owner ruling 2026-10-03, "Come back"): with nothing frozen or posed, entry starts from the game
    /// camera.</summary>
    internal bool HasLastPose => _lastPose is { } last && last.Generation == _scene.Generation && _scene.KeepsCamera;

    private void PlaceForEntry()
    {
        if (HasLastPose && _lastPose is { } last)
        {
            Restore(last);
            return;
        }
        PlaceAtEntry();
        Mode = FreeCamMode.Orbit;
    }

    /// <summary>The person to orbit on entry: the requested one when the game still has them (or their posed copy),
    /// else yourself.</summary>
    private EntityId EntrySubject(EntityId requested)
    {
        var self = _p.Snapshot.LocalEntityId;
        if (requested.IsNone || requested == self) return self;
        if (_p.Posing is { } posing && posing.TryGetVisiblePosition(requested, out _)) return requested;
        return _p.Transforms.TryGetTransform(requested, out _, out _) ? requested : self;
    }

    /// <summary>Remembered only while the scene keeps the camera (frozen / posed), tagged with the scene's generation: a scene that ends (Reset scene,
    /// the framework unfreezing, the posed set emptying) makes the pose stale, so a NEW scene starts from the game camera
    /// (review I-3).</summary>
    private void RememberPose() =>
        _lastPose = _scene.KeepsCamera
            ? new SavedPose(Mode, _orbit, _fly, new FlyState(_shownPos, _shownYaw, _shownPitch), (Roll, Fov), _scene.Generation)
            : null;

    /// <summary>Entry: reuse the hide the scene kept when its layers still match the setting (no second hide of the same
    /// layers); after a settings change, hide the new layers first and then drop the kept one (no flash of the HUD
    /// between the two); with entry hides now off, just drop it.</summary>
    private void TakeEntryHides()
    {
        var kept = _scene.TakeEntryHide(out var keptLayers);
        var want = EntryHidePlan.Resolve(_settings.EntryHides, _host.CaptureHides?.Invoke() ?? VisibilityLayers.None);
        if (kept is not null && keptLayers == want)
        {
            (_hide, _hideLayers) = (kept, keptLayers);
            return;
        }
        // Always set both: a stale _hideLayers from the last session would let the game-UI swap re-hide old layers.
        (_hide, _hideLayers) = want != VisibilityLayers.None ? (_p.Visibility.Hide(want), want) : (null, VisibilityLayers.None);
        kept?.Dispose();
    }

    /// <summary>Release: the hides go to the scene when it keeps the camera (frozen / posed — it releases them when that ends),
    /// else they end now (lamps alone never keep them — owner ruling 2026-10-03).</summary>
    private void ReleaseEntryHides(bool keep)
    {
        if (_hide is not { } hide) return;
        _hide = null;
        if (keep) _scene.KeepEntryHide(hide, _hideLayers);
        else hide.Dispose();
    }

    /// <summary>Snap (no damping) back to the remembered pose. The orbit is kept relative to its centre, so a subject who
    /// stayed put (a frozen or posed person) is framed exactly as before; the leash still applies on the next frame.</summary>
    private void Restore(in SavedPose s)
    {
        Mode = s.Mode;
        _orbit = s.Orbit;
        _fly = s.Fly;
        (_shownPos, _shownYaw, _shownPitch) = (s.Shown.Position, s.Shown.Yaw, s.Shown.Pitch);
        (Roll, Fov) = s.Lens;
    }

    /// <summary>The camera as it left: mode, both rigs, the shown (damped) pose and the lens (roll, FOV).</summary>
    private readonly record struct SavedPose(FreeCamMode Mode, OrbitState Orbit, FlyState Fly, FlyState Shown, (float Roll, float Fov) Lens,
        int Generation);
}
