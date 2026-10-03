using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class FolderOpenerTests
{
    [Fact] public void Windows_uses_explorer() => Assert.Equal(("explorer.exe", "\"C:\\shots\""), FolderOpener.Command(@"C:\shots", isWine: false));
    [Fact] public void Wine_uses_winebrowser() => Assert.Equal(("winebrowser", "\"C:\\shots\""), FolderOpener.Command(@"C:\shots", isWine: true));
}
