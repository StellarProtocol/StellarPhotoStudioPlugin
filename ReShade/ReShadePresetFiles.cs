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
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, Utf8.GetBytes(text));
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);   // never leave a half-written temp file behind
            throw;
        }
        try
        {
            File.Move(tmp, path);   // no overwrite: a file that appeared meanwhile wins
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(tmp);
            return false;
        }
        return true;
    }
}
