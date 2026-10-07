using System.Collections.Generic;

namespace Stellar.PhotoStudio;

/// <summary>What closing Photo Studio would end, as the confirm bar lists it (player request "It is not intuitive to close
/// photo studio", owner go 2026-10-08). Pure — unit-tested; <c>Plugin.Close.cs</c> renders it.</summary>
internal static class CloseConfirm
{
    /// <summary>The state that makes ✕ ask first.</summary>
    internal readonly record struct Running(bool FreeCam, bool Frozen, int Posed, int Lamps, bool LitPeople, bool Hides)
    {
        /// <summary>Anything a close would undo that the player set up — hides alone close without asking (they come
        /// back with the panel, nothing is lost).</summary>
        public bool NeedsConfirm => FreeCam || Frozen || Posed > 0 || Lamps > 0 || LitPeople;
    }

    /// <summary>Localization keys (and the count they format, or -1) for each line, in the order the bar shows them.</summary>
    public static List<(string Key, int Count)> Lines(Running r)
    {
        var lines = new List<(string, int)>(5);
        if (r.FreeCam) lines.Add(("ps.close.freecam", -1));
        if (r.Frozen) lines.Add(("ps.close.unfreeze", -1));
        if (r.Posed > 0) lines.Add(("ps.close.posed", r.Posed));
        if (r.Lamps > 0) lines.Add(("ps.close.lamps", r.Lamps));
        else if (r.LitPeople) lines.Add(("ps.close.lights", -1));
        if (r.Hides) lines.Add(("ps.close.hides", -1));
        return lines;
    }
}
