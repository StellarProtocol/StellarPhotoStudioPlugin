using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>
/// Bridges the game's own photo mode into the plugin: while the game's camera-frame/selfie screen is active, the
/// look previews as it would in the panel and the panel is shown; when it ends, play-mode look resumes and the
/// panel is asked to close (a concrete <see cref="IStudioView"/> may keep it open if the user pinned it — see
/// <see cref="IStudioView.Close"/>). Panel open/close goes through the SAME <see cref="PanelOpenState"/> the
/// panel hotkey uses (<c>Plugin.TogglePanel</c>), so panel-open state has exactly one writer. Cutscene suspension
/// is wired separately in <c>Plugin.cs</c> (<see cref="IPhotoModeState.CutsceneChanged"/> →
/// <see cref="LookController.SetSuspended"/>) since it applies regardless of whether the game's photo mode is
/// active.
/// </summary>
internal sealed class PhotoModeAttach : IDisposable
{
    private readonly IPhotoModeState _photoMode;
    private readonly LookController _look;
    private readonly PanelOpenState _panel;
    private readonly Action<PhotoModeKind> _onEntered;
    private readonly Action _onExited;

    public PhotoModeAttach(IPhotoModeState photoMode, LookController look, PanelOpenState panel)
    {
        _photoMode = photoMode;
        _look = look;
        _panel = panel;
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
        _panel.Set(true);
    }

    private void OnExited()
    {
        _look.SetGamePhotoActive(false);
        _panel.Set(false);
    }
}
