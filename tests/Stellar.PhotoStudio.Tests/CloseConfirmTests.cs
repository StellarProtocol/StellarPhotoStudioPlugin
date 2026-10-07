using System.Linq;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

// Player request "It is not intuitive to close photo studio" (owner go 2026-10-08): ✕ always closes everything, and asks
// first only when something the player set up would be undone — an idle panel still closes in one click.
public sealed class CloseConfirmTests
{
    private static CloseConfirm.Running Idle => new(false, false, 0, 0, false, false);

    [Fact]
    public void Idle_or_hides_only_closes_without_asking()
    {
        Assert.False(Idle.NeedsConfirm);
        Assert.False((Idle with { Hides = true }).NeedsConfirm);
    }

    [Theory]
    [InlineData(true, false, 0, 0, false)]
    [InlineData(false, true, 0, 0, false)]
    [InlineData(false, false, 2, 0, false)]
    [InlineData(false, false, 0, 3, false)]
    [InlineData(false, false, 0, 0, true)]
    public void Free_camera_freeze_poses_or_lights_ask_first(bool freeCam, bool frozen, int posed, int lamps, bool lit) =>
        Assert.True(new CloseConfirm.Running(freeCam, frozen, posed, lamps, lit, false).NeedsConfirm);

    [Fact]
    public void Lines_name_only_what_is_running_in_order()
    {
        var lines = CloseConfirm.Lines(new CloseConfirm.Running(true, true, 2, 3, true, true));
        Assert.Equal(new[] { "ps.close.freecam", "ps.close.unfreeze", "ps.close.posed", "ps.close.lamps", "ps.close.hides" },
            lines.Select(l => l.Key));
        Assert.Equal(2, lines[2].Count);
        Assert.Equal(3, lines[3].Count);
        Assert.Equal(new[] { "ps.close.lights" }, CloseConfirm.Lines(Idle with { LitPeople = true }).Select(l => l.Key));
        Assert.Empty(CloseConfirm.Lines(Idle));
    }
}
