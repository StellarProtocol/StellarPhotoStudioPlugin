using Stellar.PhotoStudio.Presets;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

public sealed class PresetNamesTests
{
    [Theory]
    [InlineData("Warm Dusk (mine)")]
    [InlineData("夕焼け")]
    [InlineData("a.b")]
    public void Accepts_ordinary_names(string n) => Assert.Null(PresetNames.Check(n));

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("Warm: v2", 2)]
    [InlineData("a/b", 2)]
    [InlineData("a\\b", 2)]
    [InlineData("what?", 2)]
    [InlineData("trailing.", 2)]
    [InlineData("a..b", 2)]
    [InlineData("CON", 3)]
    [InlineData("nul", 3)]
    [InlineData("Com3", 3)]
    public void Rejects_names_the_store_or_windows_cannot_hold(string n, int p) => Assert.Equal((NameProblem)p, PresetNames.Check(n));

    [Fact]
    public void Rejects_overlong_names() => Assert.Equal(NameProblem.TooLong, PresetNames.Check(new string('x', PresetNames.MaxLength + 1)));

    [Theory]
    [InlineData("Warm: v2", "Warm_ v2")]
    [InlineData("../evil", "__evil")]
    [InlineData("   ", "Imported")]
    [InlineData("dots...", "dots")]
    [InlineData("nul", "nul_")]
    public void Sanitize_always_yields_a_valid_name(string raw, string expected)
    {
        var s = PresetNames.Sanitize(raw);
        Assert.Equal(expected, s);
        Assert.Null(PresetNames.Check(s));
    }
}
