using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Xunit;
namespace Stellar.PhotoStudio.Tests.FreeCam;

public sealed class EntryHidePlanTests
{
    [Fact]
    public void Without_entry_effects_the_capture_choices_are_ignored() =>
        Assert.Equal(VisibilityLayers.GameHud | VisibilityLayers.Self,
            EntryHidePlan.Resolve(VisibilityLayers.GameHud | VisibilityLayers.Self, VisibilityLayers.EffectsMine));

    [Fact]
    public void Entry_effects_use_exactly_the_capture_tab_effect_switches() =>
        Assert.Equal(VisibilityLayers.GameHud | VisibilityLayers.EffectsMonsters,
            EntryHidePlan.Resolve(VisibilityLayers.GameHud | VisibilityLayerSets.Effects,
                VisibilityLayers.EffectsMonsters | VisibilityLayers.OtherPlayers));

    [Fact]
    public void Entry_effects_with_no_capture_effect_switch_hide_no_effects() =>
        Assert.Equal(VisibilityLayers.None, EntryHidePlan.Resolve(VisibilityLayerSets.Effects, VisibilityLayers.GameHud));
}
