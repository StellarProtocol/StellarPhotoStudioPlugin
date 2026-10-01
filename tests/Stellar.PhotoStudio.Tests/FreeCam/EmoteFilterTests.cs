using System.Linq;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// Spec § 4: Pose list = unlocked emotes, search, ★ favourites on top (wheel order otherwise).
public sealed class EmoteFilterTests
{
    private static readonly EmoteInfo[] All =
    {
        new(9001, "Sit", "", true), new(9011, "Wave", "", false), new(9020, "Victory", "", false), new(9030, "Bow", "", false),
    };

    [Fact]
    public void Favourites_come_first_and_both_groups_keep_wheel_order()
    {
        var v = EmoteFilter.Apply(All, "", new[] { 9030, 9011 });
        Assert.Equal(new[] { 9011, 9030, 9001, 9020 }, v.Select(e => e.Id));
    }

    [Fact]
    public void Search_is_case_insensitive_and_keeps_favourites_first()
    {
        var v = EmoteFilter.Apply(All, "  WA ", new int[0]);
        Assert.Equal(new[] { 9011 }, v.Select(e => e.Id));
        Assert.Equal(new[] { 9030, 9020 }, EmoteFilter.Apply(All, "o", new[] { 9030 }).Select(e => e.Id));
    }

    [Fact]
    public void Empty_query_returns_everything() => Assert.Equal(4, EmoteFilter.Apply(All, "", new int[0]).Count);
}
