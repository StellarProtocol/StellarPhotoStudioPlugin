namespace Stellar.PhotoStudio;

/// <summary>
/// The ONE place panel-open state is driven from. <see cref="IStudioView.IsOpen"/> is the single source of
/// truth (no separate bool anywhere else); <see cref="LookController.SetPanelOpen"/> is always set from the
/// view's ACTUAL resulting state after <see cref="Set"/> calls <see cref="IStudioView.Open"/>/
/// <see cref="IStudioView.Close"/> — not from what was requested — so a concrete view that no-ops
/// <see cref="IStudioView.Close"/> (e.g. pinned open by the user) still previews correctly.
/// <c>Plugin</c>'s hotkey (<c>TogglePanel</c> → <see cref="Toggle"/>) and <c>PhotoModeAttach</c> (the game's own
/// photo mode → <see cref="Set"/>) both go through this one class; neither touches the view or the look
/// controller directly for open/close.
/// </summary>
internal sealed class PanelOpenState
{
    private readonly IStudioView _view;
    private readonly LookController _look;

    public PanelOpenState(IStudioView view, LookController look)
    {
        _view = view;
        _look = look;
    }

    /// <summary>Whether the panel is currently shown — forwards <see cref="IStudioView.IsOpen"/> verbatim.</summary>
    public bool IsOpen => _view.IsOpen;

    /// <summary>Ask the view to open or close, then push its ACTUAL resulting state into the look controller.</summary>
    public void Set(bool open)
    {
        if (open) _view.Open();
        else _view.Close();
        _look.SetPanelOpen(_view.IsOpen);
    }

    /// <summary>Flips the view's own current state (not a separately tracked guess).</summary>
    public void Toggle() => Set(!_view.IsOpen);
}
