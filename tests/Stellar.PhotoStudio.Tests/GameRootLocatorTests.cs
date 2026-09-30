using System.IO;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class GameRootLocatorTests
{
    private const string BaseDir = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private const string AssemblyDir = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini/stellar/plugins/photostudio";

    [Fact]
    public void Base_dir_is_used_when_it_exists()
    {
        var r = GameRootLocator.Resolve(BaseDir, AssemblyDir, d => d == BaseDir);
        Assert.Equal(BaseDir, r.Path);
        Assert.True(r.Verified);
    }

    [Fact]
    public void Falls_back_to_walking_up_from_the_assembly_dir_when_the_base_dir_is_missing()
    {
        var fallback = Path.GetFullPath(Path.Combine(AssemblyDir, "..", "..", ".."));
        var r = GameRootLocator.Resolve("/does/not/exist", AssemblyDir, d => d == fallback);
        Assert.Equal(fallback, r.Path);
        Assert.True(r.Verified);
    }

    [Fact]
    public void Neither_candidate_exists_returns_the_base_dir_unverified()
    {
        var r = GameRootLocator.Resolve("/does/not/exist", "/also/not/here", _ => false);
        Assert.Equal("/does/not/exist", r.Path);
        Assert.False(r.Verified);
    }
}
