namespace Stellar.PhotoStudio.ReShade;

/// <summary>What a Look preset remembers about ReShade (spec § 6, D4): the ReShade preset FILE NAME in Photo Studio's
/// presets folder (null = leave ReShade's preset alone) and whether ReShade's effects are on.</summary>
internal sealed record ReShadeChoice(string? Preset, bool Enabled);
