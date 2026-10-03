using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

/// <summary>The photo's shape (spec 2026-10-03 photo shapes). <see cref="Screen"/> = the window's own shape (default).</summary>
internal enum PhotoShape
{
    Screen = 0,
    Portrait9x16 = 1,
    Portrait4x5 = 2,
    Portrait2x3 = 3,
    Square = 4,
    Wide21x9 = 5,
}

/// <summary>Shape ↔ framework aspect, the persisted key and the ratio label ("9:16"). Language-neutral: the word
/// "Screen" is a UI string and is never produced here.</summary>
internal static class PhotoShapes
{
    /// <summary>The Shape row's order: Screen, 9:16, 4:5, 2:3, 1:1, 21:9.</summary>
    public static IReadOnlyList<PhotoShape> All { get; } = new[]
    {
        PhotoShape.Screen, PhotoShape.Portrait9x16, PhotoShape.Portrait4x5, PhotoShape.Portrait2x3, PhotoShape.Square, PhotoShape.Wide21x9,
    };

    /// <summary>The framework aspect; null for <see cref="PhotoShape.Screen"/>.</summary>
    public static CaptureAspect? Aspect(PhotoShape s) => s switch
    {
        PhotoShape.Portrait9x16 => new CaptureAspect(9, 16),
        PhotoShape.Portrait4x5 => new CaptureAspect(4, 5),
        PhotoShape.Portrait2x3 => new CaptureAspect(2, 3),
        PhotoShape.Square => new CaptureAspect(1, 1),
        PhotoShape.Wide21x9 => new CaptureAspect(21, 9),
        _ => null,
    };

    /// <summary>"9:16" etc.; empty for <see cref="PhotoShape.Screen"/> (the UI shows its localized "Screen").</summary>
    public static string RatioLabel(PhotoShape s) => Aspect(s)?.ToString() ?? "";

    /// <summary>Persisted key (config + presets): "screen" or the ratio label.</summary>
    public static string Key(PhotoShape s) => s == PhotoShape.Screen ? "screen" : RatioLabel(s);

    /// <summary>Reads a persisted key; anything unknown (or null) is <see cref="PhotoShape.Screen"/>.</summary>
    public static PhotoShape Parse(string? key)
    {
        foreach (var s in All)
            if (string.Equals(Key(s), key?.Trim(), StringComparison.OrdinalIgnoreCase)) return s;
        return PhotoShape.Screen;
    }

    /// <summary>Like <see cref="Parse"/>, but null when the key is absent or unknown (a preset that sets no shape).</summary>
    public static PhotoShape? TryParse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (var s in All)
            if (string.Equals(Key(s), key.Trim(), StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }
}
