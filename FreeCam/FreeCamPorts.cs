using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>The framework services the free camera drives (one record keeps the session's constructor small).</summary>
internal sealed record FreeCamPorts(
    ICameraOverride Camera, IInputShield Shield, ISceneFreeze Freeze, ICombatState Combat,
    ISceneVisibility Visibility, IEntityTransforms Transforms, ICombatSnapshot Snapshot, IEntityPicker Picker);

internal enum FreeCamMode { Orbit, Fly }

/// <summary>What the session tells the player through a toast.</summary>
internal enum FreeCamNotice { Busy, Unavailable, Released, Error }
