using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>
/// Bridges the game's own photo mode into the plugin: while the game's camera-frame/selfie screen is active, the
/// look previews as it would in the panel and the compact docked strip is shown (the approved design — the full
/// panel would cover the game's own photo controls); when it ends, play-mode look resumes and the strip hides.
/// Whether the strip may open (auto-open setting, full panel already open, dismissed this visit) is the owner's
/// decision inside <c>setDocked</c>. Cutscene suspension is wired separately in <c>Plugin.cs</c>.
/// </summary>
internal sealed class PhotoModeAttach : IDisposable
{
    private readonly IPhotoModeState _photoMode;
    private readonly LookController _look;
    private readonly Action<bool> _setDocked;
    private readonly Action<PhotoModeKind> _onEntered;
    private readonly Action _onExited;

    public PhotoModeAttach(IPhotoModeState photoMode, LookController look, Action<bool> setDocked)
    {
        _photoMode = photoMode;
        _look = look;
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

    private void OnEntered(PhotoModeKind kind)
    {
        _look.SetGamePhotoActive(true);
        _setDocked(true);
    }

    private void OnExited()
    {
        _look.SetGamePhotoActive(false);
        _setDocked(false);
    }
}
