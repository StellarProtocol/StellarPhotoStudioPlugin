using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>
/// Bridges the game's own photo mode into the plugin: while the game's camera-frame/selfie screen is active, the
/// look previews as it would in the panel and the panel is shown; when it ends, play-mode look resumes and the
/// panel is asked to close (a concrete <see cref="IStudioView"/> may keep it open if the user pinned it — see
/// <see cref="IStudioView.Close"/>). Never touches <see cref="IStudioView"/> or <see cref="LookController"/>'s
/// panel-open state directly — both go through the SAME <paramref name="setViewOpen"/> delegate
/// (<c>Plugin.SetViewOpen</c>) that the panel hotkey uses, so panel-open state has exactly one writer. Cutscene
/// suspension is wired separately in <c>Plugin.cs</c> (<see cref="IPhotoModeState.CutsceneChanged"/> →
/// <see cref="LookController.SetSuspended"/>) since it applies regardless of whether the game's photo mode is
/// active.
/// </summary>
internal sealed class PhotoModeAttach : IDisposable
{
    private readonly IPhotoModeState _photoMode;
    private readonly LookController _look;
    private readonly Action<bool> _setViewOpen;
    private readonly Action<PhotoModeKind> _onEntered;
    private readonly Action _onExited;

    public PhotoModeAttach(IPhotoModeState photoMode, LookController look, Action<bool> setViewOpen)
    {
        _photoMode = photoMode;
        _look = look;
        _setViewOpen = setViewOpen;
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
        _setViewOpen(true);
    }

    private void OnExited()
    {
        _look.SetGamePhotoActive(false);
        _setViewOpen(false);
    }
}
