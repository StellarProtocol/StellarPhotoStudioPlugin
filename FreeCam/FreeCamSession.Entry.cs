using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>Where the camera starts on entry (scene-stays spec § 6): the pose it had when it left while the scene is set,
/// else the game camera's pose (R always snaps to the game camera).</summary>
internal sealed partial class FreeCamSession
{
    private SavedPose? _lastPose;

    /// <summary>The pose remembered at the last release, if any (null before the first exit).</summary>
    internal bool HasLastPose => _lastPose is not null;

    private void PlaceForEntry()
    {
        if (_scene.IsSet && _lastPose is { } last)
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

    private void RememberPose() =>
        _lastPose = new SavedPose(Mode, _orbit, _fly, new FlyState(_shownPos, _shownYaw, _shownPitch), (Roll, Fov));

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
    private readonly record struct SavedPose(FreeCamMode Mode, OrbitState Orbit, FlyState Fly, FlyState Shown, (float Roll, float Fov) Lens);
}
