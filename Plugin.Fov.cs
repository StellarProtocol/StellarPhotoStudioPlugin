using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

// Field of view (1.7.0, player request "Photo Studio - FOV Slider"): a Look-tab slider beside depth of field and three
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

    /// <summary>Look tab: [Field of view] [slider 10–100°] … [↺ = the game's FOV at free-camera entry], then a hint while
    /// the free camera is off (the slider is disabled and shows the last value).</summary>
    private HudElement FovRow() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new TextElement(() => T("ps.look.fov"), Emphasis: true, Color: () => _freeCam.Active ? Normal() : MenuMuted()),
            new SpacerElement(),
            HelpDot("look.fov", () => T("ps.look.fov"), () => _loc.TFormat("ps.help.look.fov",
                BindingText(StudioHotkeys.FovIn), BindingText(StudioHotkeys.FovOut), BindingText(StudioHotkeys.FovReset))),
        }, Gap: 6f),
        SliderRow(() => T("ps.look.fovAngle"),
            new SliderElement(() => CameraMath.ClampFov(_freeCam.Fov), v => _freeCam.SetFov(v), CameraMath.MinFov, CameraMath.MaxFov,
                Enabled: () => _freeCam.Active),
            () => _freeCam.Fov > 0f ? F(_freeCam.Fov, "0") + "°" : "—",   // never entered yet: no FOV to show
            () => _freeCam.ResetFov(), enabled: () => _freeCam.Active, step: 1f, parse: ParseNumber),
        new ConditionalElement(() => _freeCam.Active, new TextElement(() => _loc.TFormat("ps.look.fovKeys",
            BindingText(StudioHotkeys.FovOut), BindingText(StudioHotkeys.FovIn), BindingText(StudioHotkeys.FovReset)), Color: Muted)),
        new ConditionalElement(() => !_freeCam.Active, new TextElement(
            () => _loc.TFormat("ps.look.fovOff", BindingText(StudioHotkeys.FreeCam)), Color: Muted)),
    }, Gap: 4f);
}
