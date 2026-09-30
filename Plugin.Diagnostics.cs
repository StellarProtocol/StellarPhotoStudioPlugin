using Stellar.Abstractions.Diagnostics;

namespace Stellar.PhotoStudio;

/// <summary>Diagnostic logging only — gated on <see cref="StellarDiagnostics.IsEnabled"/> so normal play
/// produces no extra log volume. Filled in as capture/look/session wiring lands in later tasks.</summary>
public sealed partial class Plugin
{
    private void LogBootDiag()
    {
        if (!StellarDiagnostics.IsEnabled) return;
        _services.Log.Info("[PhotoStudio][diag] plugin constructed");
    }
}
