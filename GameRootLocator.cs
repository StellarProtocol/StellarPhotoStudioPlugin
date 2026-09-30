using System;
using System.IO;

namespace Stellar.PhotoStudio;

/// <summary>Result of <see cref="GameRootLocator.Resolve"/>. <see cref="Verified"/> is false when neither
/// candidate directory could be confirmed to exist — <see cref="Path"/> is still the base-dir guess so a caller
/// has SOMETHING to combine a subfolder onto, but it may not actually be game_mini.</summary>
internal readonly record struct GameRootResolution(string Path, bool Verified);

/// <summary>
/// Pure resolution of the game's install root ("game_mini"), mirroring <c>StellarMaestroPlugin</c>'s
/// <c>BandMidiDir()</c> (<c>Plugin.Player.cs</c>): prefer <see cref="AppContext.BaseDirectory"/> (the running
/// exe's directory under BepInEx); if that doesn't exist, fall back to walking up three levels from the plugin
/// assembly's own directory (<c>…/stellar/plugins/&lt;slot&gt;/../../..</c> → game_mini). No I/O of its own —
/// <paramref name="dirExists"/> (normally <see cref="Directory.Exists"/>) is injected so this is unit-testable
/// without a real filesystem.
/// </summary>
internal static class GameRootLocator
{
    public static GameRootResolution Resolve(string baseDir, string assemblyDir, Func<string, bool> dirExists)
    {
        if (!string.IsNullOrEmpty(baseDir) && dirExists(baseDir)) return new GameRootResolution(baseDir, Verified: true);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            var fallback = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", ".."));
            if (dirExists(fallback)) return new GameRootResolution(fallback, Verified: true);
        }
        return new GameRootResolution(baseDir, Verified: false);
    }
}
