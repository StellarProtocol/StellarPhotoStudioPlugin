using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Stellar.PhotoStudio;

/// <summary>Validates and lists LUT strip PNGs (256×16 or 1024×32, per the render recon).</summary>
internal static class LutLibrary
{
    private static readonly byte[] Sig = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static bool Validate(byte[] png)
    {
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(Sig)) return false;
        var w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h) is (256, 16) or (1024, 32);
    }

    public static IReadOnlyList<string> List(string dir) =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*")
                .Where(p => string.Equals(Path.GetExtension(p), ".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : Array.Empty<string>();
}
