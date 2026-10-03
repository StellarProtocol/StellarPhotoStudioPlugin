using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>
/// Bridges the game's own photo mode into the plugin: while the game's camera-frame/selfie screen is active, the
/// compact docked strip is shown (the approved design — the full panel would cover the game's own
/// photo controls); when it ends, the strip hides. The look preview follows the strip (<c>Plugin.ShowDocked</c>).
/// Whether the strip may open (auto-open setting, full panel already open, dismissed this visit) is the owner's
/// decision inside <c>setDocked</c>. Cutscene suspension is wired separately in <c>Plugin.cs</c>.
/// </summary>
internal sealed class PhotoModeAttach : IDisposable
{
    private readonly IPhotoModeState _photoMode;
    private readonly Action<bool> _setDocked;
    private readonly Action<PhotoModeKind> _onEntered;
    private readonly Action _onExited;

    public PhotoModeAttach(IPhotoModeState photoMode, Action<bool> setDocked)
    {
        _photoMode = photoMode;
        _setDocked = setDocked;
        _onEntered = OnEntered;
        _onExited = OnExited;
        _photoMode.Entered += _onEntered;
        _photoMode.Exited += _onExited;
    }

    public void Dispose()
    {
        _photoMode.Entered -= _onEntered;
        _photoMode.Exited -= _onExited;
    }

    // The look preview follows the strip (the owner decides whether it opens), so this only reports the transition.
    private void OnEntered(PhotoModeKind kind) => _setDocked(true);

    private void OnExited() => _setDocked(false);
}
