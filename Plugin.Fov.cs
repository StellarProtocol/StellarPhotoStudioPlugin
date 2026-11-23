using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

// Field of view (1.7.0, player request "Photo Studio - FOV Slider"): the Camera › Lens slider (Plugin.Section.Camera.cs) and three
// rebindable hotkeys (FOV in ], FOV out [, reset \ by default) — all free camera only; the game camera is never changed.
// Shift+wheel inside the free camera keeps working.
public sealed partial class Plugin
{
    private readonly FovKeys _fovKeys = new();

    /// <summary>Hotkeys are framework-global and not gated on typing, so the FOV keys check the free camera and a focused
    /// Stellar text field themselves.</summary>
    private bool FovKeysLive() => _freeCam.Active && !_freeCam.TextFieldFocused;

    private void FovKeyPressed(bool zoomIn)
    {
        if (FovKeysLive()) _freeCam.NudgeFov(FovKeys.PressStep(zoomIn));
    }

    private void ResetFovKey()
    {
        if (FovKeysLive()) _freeCam.ResetFov();
    }

    /// <summary>Repeat while held (OnUpdate): the press already stepped once; past the delay the held key keeps moving.</summary>
    private void TickFovKeys(float dt)
    {
        if (!FovKeysLive()) { _fovKeys.Reset(); return; }
        var delta = _fovKeys.Tick(_services.Hotkeys.IsActionHeld(StudioHotkeys.FovIn), _services.Hotkeys.IsActionHeld(StudioHotkeys.FovOut), dt);
        if (delta != 0f) _freeCam.NudgeFov(delta);
    }
}
