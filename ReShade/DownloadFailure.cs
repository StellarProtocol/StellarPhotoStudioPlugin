using System;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>Classifies a Failed pack's or preset's error for a player-friendly message. <see cref="Requirement"/> is
/// decided by the caller (whether <c>FailedRequirement</c> is set) and always wins over the error text, since a pack
/// that never even attempted its own download has nothing meaningful to classify from. The rest is classified from
/// the exact strings <c>PluginDownloadService</c> returns (framework src/Stellar.Infrastructure/Net/
/// PluginDownloadService.cs, <c>MapError</c> + the inline results in <c>RunDownloadAsync</c>/<c>Validate</c>):
/// "checksum mismatch" and "too large" both mean the upstream file no longer matches what we pinned (<see cref="Changed"/>);
/// "network error" (an HttpRequestException) and "timed out" (the inactivity timeout) mean the network
/// (<see cref="Network"/>); every other text — "busy", "invalid request", "https only", "invalid size", "bad zip",
/// "could not write to the data folder", "download failed unexpectedly", or no error text at all — is
/// <see cref="Other"/>.</summary>
internal enum DownloadFailure { None, Requirement, Changed, Network, Other }

internal static class DownloadFailureClassifier
{
    public static DownloadFailure Classify(string? error)
    {
        if (string.IsNullOrEmpty(error)) return DownloadFailure.Other;
        if (error.Contains("checksum mismatch", StringComparison.Ordinal) || error.Contains("too large", StringComparison.Ordinal))
            return DownloadFailure.Changed;
        if (error.Contains("network error", StringComparison.Ordinal) || error.Contains("timed out", StringComparison.Ordinal))
            return DownloadFailure.Network;
        return DownloadFailure.Other;
    }
}
