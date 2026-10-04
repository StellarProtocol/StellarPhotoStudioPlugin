using System;
using System.IO;
using System.Text;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>Preset file access (a seam for tests). Text is UTF-8 without BOM handling, so bytes round-trip exactly.</summary>
internal interface IReShadePresetFiles
{
    bool Exists(string path);
    string ReadAllText(string path);
    /// <summary>Writes a NEW file (temp file + move). Returns false and writes nothing when the file exists: an installed
    /// preset is never overwritten — ReShade saves the player's technique switches into it (edits are never lost).</summary>
    bool WriteNew(string path, string text);
}

internal sealed class DiskReShadePresetFiles : IReShadePresetFiles
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public bool Exists(string path) => File.Exists(path);

    public string ReadAllText(string path) => Utf8.GetString(File.ReadAllBytes(path));

    public bool WriteNew(string path, string text)
    {
        if (File.Exists(path)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".new-" + Guid.NewGuid().ToString("N");   // unique per call: never collides with another in flight
        try
        {
            File.WriteAllBytes(tmp, Utf8.GetBytes(text));
        }
        catch
        {
            TryDeleteTemp(tmp);   // never leave a half-written temp file behind
            throw;   // the write's own error, never the cleanup's
        }
        try
        {
            File.Move(tmp, path);   // no overwrite: a file that appeared meanwhile wins
        }
        catch (IOException) when (File.Exists(path))
        {
            TryDeleteTemp(tmp);
            return false;
        }
        return true;
    }

    /// <summary>Best-effort delete of an orphaned temp file. A failure here (the directory itself lost write access,
    /// a race with something else) must never replace the caller's real error — mirrors
    /// PluginDownloadService.SwapDirectory's own best-effort cleanup. Internal (not private) so a white-box test can
    /// exercise the swallow path directly, without needing to fabricate a real fault at the exact randomized temp path.</summary>
    internal static void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort only: the caller already has (or is about to throw/return) the real outcome.
        }
    }
}
