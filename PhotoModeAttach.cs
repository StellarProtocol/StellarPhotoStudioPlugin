using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>
/// Bridges the game's own photo mode into the plugin: while the game's camera-frame/selfie screen is active, the
/// look previews as it would in the panel and the panel is shown; when it ends, play-mode look resumes and the
/// panel is asked to close (a concrete <see cref="IStudioView"/> may keep it open if the user pinned it — see
/// <see cref="IStudioView.Close"/>). Cutscene suspension is wired separately in <c>Plugin.cs</c>
/// (<see cref="IPhotoModeState.CutsceneChanged"/> → <see cref="LookController.SetSuspended"/>) since it applies
/// regardless of whether the game's photo mode is active.
/// </summary>
internal sealed class PhotoModeAttach : IDisposable
{
    private readonly IPhotoModeState _photoMode;
    private readonly LookController _look;
    private readonly IStudioView _view;
    private readonly Action<PhotoModeKind> _onEntered;
    private readonly Action _onExited;

    public PhotoModeAttach(IPhotoModeState photoMode, LookController look, IStudioView view)
    {
        _photoMode = photoMode;
        _look = look;
        _view = view;
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
        _view.Open();
    }

    private void OnExited()
    {
        _look.SetGamePhotoActive(false);
        _view.Close();
    }
}
