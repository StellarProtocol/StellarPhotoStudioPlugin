using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.Posing;

/// <summary>‹ › on the Person row: the next / previous person in the framework's list (you first, then by distance),
/// wrapping; from someone not in the list, the first (›) or the last (‹).</summary>
internal static class PersonCycle
{
    public static EntityId Next(IReadOnlyList<PersonInfo> people, EntityId current, int direction)
    {
        if (people.Count == 0) return EntityId.None;
        var at = -1;
        for (var i = 0; i < people.Count; i++)
            if (people[i].Id == current) { at = i; break; }
        if (at < 0) return people[direction >= 0 ? 0 : people.Count - 1].Id;
        var n = people.Count;
        return people[((at + Math.Sign(direction)) % n + n) % n].Id;
    }
}
