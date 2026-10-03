using System;
using System.Collections.Generic;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>The Preset dropdown: labels (file names without ".ini"), the full paths to switch to, and the selected index.</summary>
internal sealed record PresetOptions(IReadOnlyList<string> Labels, IReadOnlyList<string> Paths, int Selected);

/// <summary>ReShade presets offered in Photo Studio: the .ini files in its own presets folder (data folder
/// reshade/presets — separate from the pack folders, which a download replaces whole). ReShade creates a preset file that
/// does not exist yet when it is switched to (IReShade.SetPreset), so an empty folder still offers <see cref="DefaultFile"/>.</summary>
internal static class ReShadePresets
{
    public const string DefaultFile = "Photo Studio.ini";

    public static IReadOnlyList<string> List(IEnumerable<string> files)
    {
        var names = new List<string>();
        foreach (var f in files)
        {
            var name = ReShadePaths.FileName(f);
            if (name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && !names.Contains(name)) names.Add(name);
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <param name="otherLabel">Formats the label of a current preset outside our folder (ReShade's own).</param>
    public static PresetOptions Options(string folder, IReadOnlyList<string> files, string? current, Func<string, string> otherLabel)
    {
        var labels = new List<string>();
        var paths = new List<string>();
        IReadOnlyList<string> names = files.Count == 0 ? new[] { DefaultFile } : files;
        foreach (var f in names)
        {
            labels.Add(Strip(f));
            paths.Add(ReShadePaths.PathFor(folder, f));
        }
        var selected = current is null ? -1 : paths.FindIndex(p => ReShadePaths.Same(current, p));
        if (current is not null && selected < 0)
        {
            labels.Insert(0, otherLabel(Strip(ReShadePaths.FileName(current))));
            paths.Insert(0, current);
            selected = 0;
        }
        return new PresetOptions(labels, paths, selected);
    }

    private static string Strip(string file) =>
        file.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - 4) : file;
}
