using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>ReShade facts for the sidecar and the toast.</summary>
internal static class ReShadeInfo
{
    /// <summary>The framework's English note (fw 2.17.0 Stellar.Application IFrameGrabber.NotReadyNote). The framework has
    /// no note codes, so Photo Studio recognises this exact text to show it in the player's language.</summary>
    public const string NotReadyNote = "ReShade was not ready — photo taken without it.";
    public const string ErrorNote = "ReShade could not draw into the photo — photo taken without it.";
    public const string DrewNothingNote = "ReShade drew nothing into the photo in time — photo taken without it.";
    public const string NothingToDrawNote = "None of the active ReShade effects work in this photo shape — photo taken without them.";
    /// <summary>fw 2.17.0: a screen-shaped 2×/4× photo with a size-locked effect is taken at 1× — not a memory shortfall.</summary>
    public const string ScreenSizeOnlyNote = "Some ReShade effects only work at screen size, so this photo was taken at 1×.";
    public const string ScreenSizeOnlySkippedNote = "Some ReShade effects only work at screen size, so they were left out of this photo.";
    /// <summary>fw 2.17.0: a screen-shaped 2×/4× photo drawn by the separate capture runtime, which cannot see the game's depth.</summary>
    public const string DepthLeftOutNote = "ReShade effects that use depth were left out of this photo.";

    // The framework's English notes (fw 2.17.0 ReShadeCaptureNotes) → Photo Studio's own keys, so each shows in the player's language.
    private static readonly (string Note, string Key)[] NoteKeys =
    {
        (NotReadyNote, "ps.rs.note.notReady"), (ErrorNote, "ps.rs.note.error"), (DrewNothingNote, "ps.rs.note.drewNothing"),
        (NothingToDrawNote, "ps.rs.note.nothingToDraw"), (ScreenSizeOnlyNote, "ps.rs.note.screenSizeOnly"),
        (ScreenSizeOnlySkippedNote, "ps.rs.note.screenSizeOnlySkipped"), (DepthLeftOutNote, "ps.rs.note.depthLeftOut"),
    };

    /// <summary>True when the note means the photo was taken WITHOUT ReShade (the sidecar's <c>applied</c> is false).</summary>
    public static bool MeansWithoutReShade(string note) =>
        note == NotReadyNote || note == ErrorNote || note == DrewNothingNote || note == NothingToDrawNote;

    public static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split(new[] { ',', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : string.Join(".", parts);
    }

    /// <summary>ReShade's product version from the game folder's dxgi.dll (IReShade has no version member). Null when the
    /// file is missing or unreadable — never a guessed value.</summary>
    public static string? ReadVersion(string dllPath)
    {
        try
        {
            return File.Exists(dllPath) ? NormalizeVersion(FileVersionInfo.GetVersionInfo(dllPath).ProductVersion) : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;   // e.g. version.dll unavailable under an unusual Wine prefix: the sidecar records null
        }
    }

    public static string LocalizeNote(string note, Func<string, string> t)
    {
        foreach (var (n, key) in NoteKeys)
            if (n == note) return t(key);
        return note;
    }

    /// <summary>The toast's warning line: the existing warning (if any) and every capture note, joined with " · ".</summary>
    public static string JoinWarnings(string warning, IReadOnlyList<string> notes, Func<string, string> t)
    {
        var parts = new List<string>(notes.Count + 1);
        if (warning.Length > 0) parts.Add(warning);
        foreach (var n in notes)
            if (!string.IsNullOrWhiteSpace(n)) parts.Add(LocalizeNote(n, t));
        return string.Join(" · ", parts);
    }
}
