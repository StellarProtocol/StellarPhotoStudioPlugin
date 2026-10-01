using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>The framework services the free camera drives (one record keeps the session's constructor small).</summary>
/// <summary>The framework services the free camera drives (one record keeps the session's constructor small).
/// <c>Posing</c> (1.2.0, optional) gives the visible position of a posed copy / NPC stand-in — the orbit centre while the
/// real person is hidden (controller decision Q5).</summary>
internal sealed record FreeCamPorts(
    ICameraOverride Camera, IInputShield Shield, ISceneFreeze Freeze, ICombatState Combat,
    ISceneVisibility Visibility, IEntityTransforms Transforms, ICombatSnapshot Snapshot, IEntityPicker Picker,
    IPosing? Posing = null);

/// <summary>What the session needs from the plugin around it — kept as plain callbacks so the session stays
/// game-independent and testable.</summary>
/// <param name="Notify">Shows the player a toast.</param>
/// <param name="PointerOverOwnWindow">Whether a screen point (pixels, origin top-left) lies over one of Photo Studio's
/// own windows: a left-click pick, a wheel turn or a right-button drag that starts there never moves the camera.</param>
/// <param name="DismissModalUi">Closes Photo Studio's open modal UI (the "?" help popover) if one is open and returns
/// true when it did — Esc then closes that instead of leaving the free camera.</param>
/// <param name="Warn">Writes a warning to the plugin log.</param>
internal sealed record FreeCamHost(
    Action<FreeCamNotice, CameraReleaseReason> Notify, Func<float, float, bool> PointerOverOwnWindow,
    Func<bool> DismissModalUi, Action<string> Warn);

internal enum FreeCamMode { Orbit, Fly }

/// <summary>What the session tells the player through a toast.</summary>
internal enum FreeCamNotice { Busy, Unavailable, Released, Error }
