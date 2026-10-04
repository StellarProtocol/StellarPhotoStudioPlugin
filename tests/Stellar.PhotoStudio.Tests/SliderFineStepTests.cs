using Xunit;

namespace Stellar.PhotoStudio.Tests;

// Player feedback 2026-10-05 ("can't do precise changes on slider alone"): a slider value can be typed. The typed text is
// read in the unit the row shows ("1.4 m", "f/2.8", "35 mm", "17:30").
public sealed class SliderFineStepTests
{
    [Theory]
    [InlineData("1.4", 1.4f)]
    [InlineData("1,4 m", 1.4f)]
    [InlineData("f/2.8", 2.8f)]
    [InlineData("35mm", 35f)]
    [InlineData("  12 ", 12f)]
    public void Typed_numbers_are_read_in_the_rows_unit(string text, float expected) =>
        Assert.Equal(expected, Plugin.ParseNumber(text)!.Value, 3);

    [Theory]
    [InlineData("")]
    [InlineData("m")]
    [InlineData("abc")]
    public void Text_without_a_number_changes_nothing(string text) => Assert.Null(Plugin.ParseNumber(text));

    [Theory]
    [InlineData("17:30", 17.5f)]
    [InlineData("6h15", 6.25f)]
    [InlineData("17.5", 17.5f)]
    [InlineData("9", 9f)]
    public void Typed_times_are_read_as_hours(string text, float expected) =>
        Assert.Equal(expected, Plugin.ParseHour(text)!.Value, 3);
}
