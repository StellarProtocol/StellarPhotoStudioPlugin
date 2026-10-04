namespace Stellar.PhotoStudio.ReShade;

/// <summary>What a Look preset remembers about ReShade (spec § 6, D4): the ReShade preset (null = leave ReShade's preset
/// alone) and whether ReShade's effects are on. <paramref name="Preset"/> is a FILE NAME in Photo Studio's presets folder
/// when it comes from a preset file, or a FULL path when it was read live (the in-memory baseline / stash / close
/// snapshot), so ReShade's own preset outside our folder can be switched back to. <see cref="Presets.ReShadeDto"/> decides
/// what reaches disk. Effect switches are not part of it: they are saved in the ReShade preset file itself.</summary>
internal sealed record ReShadeChoice(string? Preset, bool Enabled);
