using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>The Pose list: search by name (case-insensitive), ★ favourites first, the game's wheel order otherwise.</summary>
internal static class EmoteFilter
{
    public static List<EmoteInfo> Apply(IReadOnlyList<EmoteInfo> all, string query, IReadOnlyList<int> favourites)
    {
        var q = query.Trim();
        var favs = new HashSet<int>(favourites);
        var top = new List<EmoteInfo>();
        var rest = new List<EmoteInfo>();
        foreach (var e in all)
        {
            if (q.Length > 0 && e.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
            (favs.Contains(e.Id) ? top : rest).Add(e);
        }
        top.AddRange(rest);
        return top;
    }
}
