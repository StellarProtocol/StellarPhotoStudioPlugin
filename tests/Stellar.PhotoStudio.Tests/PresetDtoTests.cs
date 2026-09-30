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

    [Fact]
    public void Lut_is_stored_as_a_file_name_so_exported_presets_travel()
    {
        var dto = PresetDto.From("X", new LookSettings { Lut = new LutLook { FilePath = System.IO.Path.Combine("C:", "Users", "me", "luts", "teal.png"), Contribution = 1f } });
        Assert.Equal("teal.png", dto.LutFile);
    }
}
