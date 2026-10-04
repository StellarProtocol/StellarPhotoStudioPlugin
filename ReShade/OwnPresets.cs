using System;
using System.IO;
using System.Text;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>Photo Studio's own ReShade presets, shipped inside the plugin (Resources/Presets/&lt;id&gt;.ini, embedded as
/// "Stellar.PhotoStudio.Presets.&lt;id&gt;.ini"). Installing one writes this text to the presets folder — no download.</summary>
internal static class OwnPresets
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static string Text(string id)
    {
        using var stream = typeof(OwnPresets).Assembly.GetManifestResourceStream("Stellar.PhotoStudio.Presets." + id + ".ini")
            ?? throw new InvalidOperationException("no embedded Photo Studio preset '" + id + "'");
        using var reader = new StreamReader(stream, Utf8);
        return reader.ReadToEnd();
    }
}
