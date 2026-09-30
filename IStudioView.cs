using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

/// <summary>
/// Seam for the Photo Studio panel UI (Task 14, not yet built). <see cref="Plugin"/> calls only these four
/// members and never reaches into panel internals; <see cref="NoOpStudioView"/> is wired in until the real panel
/// exists. The saved-capture toast routes through here (not <c>INotifications</c>) because it wants an
/// "Open folder" action, which <c>INotifications</c> toasts cannot carry — how (and whether) to render that
/// action is entirely the concrete panel's decision.
/// </summary>
internal interface IStudioView
{
    /// <summary>Whether the panel is currently shown. The single source of truth for panel-open state — callers
    /// (<c>Plugin.SetViewOpen</c>) never keep their own bool, and read this back after <see cref="Open"/>/
    /// <see cref="Close"/> to learn what ACTUALLY happened (a concrete view may no-op <see cref="Close"/> while
    /// pinned open).</summary>
    bool IsOpen { get; }

    /// <summary>Show the panel — a user hotkey, or the game entering its own photo mode.</summary>
    void Open();

    /// <summary>Hide the panel. A concrete implementation may choose to no-op this while the user has pinned the
    /// panel open — that policy belongs to the panel, not to callers of this seam. Callers read
    /// <see cref="IsOpen"/> afterward rather than assuming the request succeeded.</summary>
    void Close();

    /// <summary>A capture just saved successfully; show its outcome (path, dimensions, "Open folder").</summary>
    void ShowSavedToast(CaptureResult result);

    /// <summary>A non-fatal, panel-relevant problem occurred (e.g. a rejected LUT); show it inline.</summary>
    void ShowError(string message);
}

/// <summary>Renders nothing, but still tracks <see cref="IsOpen"/> honestly (unconditional open/close, never
/// pins) so callers driven by it behave correctly. Wired in until Task 14 replaces it with the real panel.</summary>
internal sealed class NoOpStudioView : IStudioView
{
    public bool IsOpen { get; private set; }
    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void ShowSavedToast(CaptureResult result) { }
    public void ShowError(string message) { }
}
