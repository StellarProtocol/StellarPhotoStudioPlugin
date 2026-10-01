# Stellar.PhotoStudio

StellarResonance plugin for screenshots: hide the HUD, nameplates and other players; take supersampled captures
(1×/2×/4×); grade the shot with looks built from the game's own render effects (depth of field, colour, white balance,
LUT, bloom, vignette, film grain); raise render quality (supersampling, high shadows) and pin the time of day; keep
presets. A compact strip appears beside the game's own photo/selfie mode.

Requires **Stellar framework ≥ 2.14.0** (capture, scene-visibility, look, photo-mode, render-quality and time-of-day
services; 2.14.0 adds the free-camera services — camera override, input shield, scene freeze, emotes, entity picker —
and `IHotkeys.MigrateSavedBinding` for the hide-all key move).

```bash
dotnet build -c Release
```

Published via the [plugin registry](https://github.com/StellarProtocol/StellarResonancePlugins) (manifest pins this
repo + commit; CI builds it in an isolated container). AGPL-3.0-or-later.

## Hard product requirements

- **Windows and Linux (Wine/Proton) are both first-class** (owner, 2026-09-30). Nothing may depend on Heroic, Proton or
  Wine behaviour. "Open folder" uses Explorer on Windows and `winebrowser` under Wine. Folder, preset and LUT names
  must be valid on both.
- **Edits are never lost.** Applying another preset over unsaved edits keeps them in an "Unsaved look" row; restoring
  one swaps the current edits in. Renaming keeps the editor's look.
- **Closing Photo Studio returns the game to exactly its normal look.** Hides, looks, render quality and time of day
  are all reference-counted framework tokens, released on close and on unload. A look pinned "while playing" keeps
  colour/white balance/LUT/bloom only; depth of field and film grain are photo-only.
- **Never use `ZServerTime` for time of day** (it breaks the game's ping display; framework `ITimeOfDay` uses the
  game's own time-of-day calls).
- Design + decisions: devkit `docs/superpowers/specs/2026-09-30-photo-studio-core-design.md` (§ 11 as-built) and
  `2026-10-01-photo-studio-render-quality-design.md`.

## Known interaction

MahiruUtility's graphics / time-of-day options drive the same game settings; turn them off while using Photo
Studio's (until MahiruUtility moves onto the shared framework services).
