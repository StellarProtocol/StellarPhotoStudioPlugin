using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>Showing the game's interface while the free camera is on (player request "display game ui on freecam so I
/// can do my sliders", owner-approved 2026-10-10, spec photo-studio-freecam-game-ui): U or the Camera tab switch swaps
/// the entry hide for one without the game HUD, for this session only; leaving the free camera puts the full entry hide
/// back first, so a scene that keeps it keeps the HUD hidden. While shown, a click, wheel turn or right-button drag that
/// starts over the game's interface goes to the game, not the camera.</summary>
internal sealed partial class FreeCamSession
{
    /// <summary>True while the game's interface is shown in this free-camera session.</summary>
    public bool GameUiShown { get; private set; }

    /// <summary>Shows or hides the game's interface without leaving the free camera. No-op while the free camera is off.</summary>
    public void SetGameUi(bool shown)
    {
        if (!Active || shown == GameUiShown) return;
        GameUiShown = shown;
        SwapEntryHide();
        StateChanged?.Invoke();
    }

    /// <summary>Photo Studio's own windows always take the pointer; the game's take it only while they are shown (a hidden
    /// game HUD may still answer the raycast).</summary>
    private bool PointerOverUi(float x, float y) =>
        _host.PointerOverOwnWindow(x, y) || (GameUiShown && _p.Shield.IsPointerOverGameUi);

    /// <summary>Release step: the full entry hide is back before the scene may take it.</summary>
    private void EndGameUi()
    {
        if (!GameUiShown) return;
        GameUiShown = false;
        SwapEntryHide();
    }

    /// <summary>Takes the new hide BEFORE dropping the old one (no flash of the hidden layers in between).
    /// <see cref="_hideLayers"/> stays the full entry set — what the scene keeps and what re-entry compares.</summary>
    private void SwapEntryHide()
    {
        if ((_hideLayers & VisibilityLayers.GameHud) == 0) return;   // the entry hides no HUD: nothing to show
        var want = GameUiShown ? _hideLayers & ~VisibilityLayers.GameHud : _hideLayers;
        var previous = _hide;
        _hide = want == VisibilityLayers.None ? null : _p.Visibility.Hide(want);
        previous?.Dispose();
    }
}
