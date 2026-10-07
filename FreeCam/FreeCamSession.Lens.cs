namespace Stellar.PhotoStudio.FreeCam;

/// <summary>Field of view (1.7.0, player request "FOV Slider"): the Look tab's slider and the FOV hotkeys set it; the
/// free camera applies it on its next frame (Step). Shift+wheel keeps working. Every setter is a no-op with the free
/// camera off — the game camera's FOV is never touched.</summary>
internal sealed partial class FreeCamSession
{
    /// <summary>The game camera's FOV when the free camera took over — what reset returns to.</summary>
    public float EntryFov => CameraMath.ClampFov(_entry.Fov);

    /// <summary>True while a Stellar text field has the keyboard: typed "[" / "]" / "\" must not move the FOV.</summary>
    public bool TextFieldFocused => _shield?.TextFieldFocused ?? false;

    public void SetFov(float fov)
    {
        if (Active) Fov = CameraMath.ClampFov(fov);
    }

    public void NudgeFov(float degrees)
    {
        if (Active) Fov = CameraMath.ClampFov(Fov + degrees);
    }

    public void ResetFov()
    {
        if (Active) Fov = EntryFov;
    }
}
