using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Presets;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class PresetDtoTests
{
    [Fact]
    public void Lut_with_an_empty_file_path_normalizes_to_no_lut_on_write()
    {
        var dto = PresetDto.From("X", new LookSettings { Lut = new LutLook { FilePath = "", Contribution = 0.7f } });

        Assert.Null(dto.LutFile);
        Assert.Null(dto.ToLook().Lut);
    }
}
