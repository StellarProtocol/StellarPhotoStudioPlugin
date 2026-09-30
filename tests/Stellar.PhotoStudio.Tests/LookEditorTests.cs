using Stellar.Abstractions.Domain;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

public sealed class LookEditorTests
{
    [Fact]
    public void Editing_a_group_switches_it_on()
    {
        var e = new LookEditor();
        e.EditColor(c => c with { Saturation = -40 });
        Assert.True(e.IsOn(LookGroups.Color));
        Assert.Equal(-40, e.Build().Color!.Saturation);
    }

    [Fact]
    public void Off_group_builds_null_but_keeps_its_values_for_later()
    {
        var e = new LookEditor();
        e.EditBloom(b => b with { Intensity = 3 });
        e.SetOn(LookGroups.Bloom, false);
        Assert.Null(e.Build().Bloom);
        e.SetOn(LookGroups.Bloom, true);
        Assert.Equal(3, e.Build().Bloom!.Intensity);
    }

    [Fact]
    public void Load_switches_on_exactly_the_preset_groups()
    {
        var e = new LookEditor();
        e.EditFilmGrain(f => f);
        e.Load(new LookSettings { Vignette = new VignetteLook { Intensity = 0.4f } });
        Assert.Equal(LookGroups.Vignette, e.Enabled);
        Assert.Equal(0.4f, e.Build().Vignette!.Intensity);
        Assert.Null(e.Build().FilmGrain);
    }

    [Fact]
    public void Lut_without_a_file_is_treated_as_off()
    {
        var e = new LookEditor();
        e.EditLut(l => l with { Contribution = 0.5f });
        Assert.Null(e.Build().Lut);
        e.EditLut(l => l with { FilePath = "/x/a.png" });
        Assert.Equal("/x/a.png", e.Build().Lut!.FilePath);
    }

    [Fact]
    public void Reset_restores_defaults_and_keeps_focus_tracking()
    {
        var e = new LookEditor();
        e.EditDof(d => d with { Aperture = 1.2f, FocusOnLocalPlayer = true });
        e.ResetGroup(LookGroups.Dof);
        Assert.Equal(new DofLook().Aperture, e.Dof.Aperture);
        Assert.True(e.Dof.FocusOnLocalPlayer);
    }

    [Fact]
    public void Changed_fires_on_edits_and_real_toggles_only()
    {
        var e = new LookEditor();
        var n = 0;
        e.Changed += () => n++;
        e.EditColor(c => c);
        e.SetOn(LookGroups.Color, true);   // already on: no event
        e.SetOn(LookGroups.Color, false);
        Assert.Equal(2, n);
    }
}
