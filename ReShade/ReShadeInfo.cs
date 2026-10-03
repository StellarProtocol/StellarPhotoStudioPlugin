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

    public static string LocalizeNote(string note, Func<string, string> t) => note == NotReadyNote ? t("ps.rs.note.notReady") : note;

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
