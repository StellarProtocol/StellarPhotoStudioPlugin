using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.Posing;

/// <summary>What the Person group shows for one person (the framework target holds the real state; this is the panel's
/// copy so the controls redraw without reading the game).</summary>
internal sealed class PersonUiState
{
    private float _headX, _headY, _eyesX, _eyesY;

    public EmoteInfo? Action { get; set; }
    public bool Playing { get; set; }
    public float Moment { get; set; }
    public int ExpressionIndex { get; set; } = -1;
    public bool Hold { get; set; } = true;   // owner P3: an expression holds until changed
    public float Yaw { get; set; }
    public LookMode Head { get; private set; }
    public LookMode Eyes { get; private set; }
    public bool HeadLocked { get; private set; }
    public bool EyesLocked { get; private set; }

    public LookMode Mode(LookPart part) => part == LookPart.Head ? Head : Eyes;
    public bool Locked(LookPart part) => part == LookPart.Head ? HeadLocked : EyesLocked;
    public float AimX(LookPart part) => part == LookPart.Head ? _headX : _eyesX;
    public float AimY(LookPart part) => part == LookPart.Head ? _headY : _eyesY;

    public void SetMode(LookPart part, LookMode mode)
    {
        if (part == LookPart.Head) Head = mode;
        else Eyes = mode;
    }

    public void SetLocked(LookPart part, bool locked)
    {
        if (part == LookPart.Head) HeadLocked = locked;
        else EyesLocked = locked;
    }

    public void SetAim(LookPart part, float x, float y)
    {
        if (part == LookPart.Head) { _headX = x; _headY = y; }
        else { _eyesX = x; _eyesY = y; }
    }
}
