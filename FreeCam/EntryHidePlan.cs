using Stellar.Abstractions.Domain;
namespace Stellar.PhotoStudio.FreeCam;

/// <summary>"Hide effects on entry" stores the whole effects group as a marker; on entry it means "the Effects switches
/// on the Capture tab" (spec § 4).</summary>
internal static class EntryHidePlan
{
    public static VisibilityLayers Resolve(VisibilityLayers entry, VisibilityLayers captureHides) =>
        (entry & VisibilityLayerSets.Effects) == 0
            ? entry
            : (entry & ~VisibilityLayerSets.Effects) | (captureHides & VisibilityLayerSets.Effects);
}
