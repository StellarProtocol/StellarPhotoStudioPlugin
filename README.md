# Stellar.PhotoStudio

StellarResonance plugin for screenshots: hide the HUD, nameplates and everything the game's own photo screen can hide
(you, your Spirit Echo, other adventurers, friends, party, guild, non-players, enemies, weapons, collectibles, other
Spirit Echoes); take supersampled captures
(1×/2×/4×); grade the shot with looks built from the game's own render effects (depth of field, colour, white balance,
LUT, bloom, vignette, film grain); raise render quality (supersampling, high shadows) and pin the time of day; keep
presets. A compact strip appears beside the game's own photo/selfie mode.

Requires **Stellar framework ≥ 2.20.0** (capture, scene-visibility, look, photo-mode, render-quality and time-of-day
services; 2.14.0 adds the free-camera services — camera override, input shield, scene freeze, emotes, entity picker —
and `IHotkeys.MigrateSavedBinding` for the hide-all key move; 2.15.0 adds `IPosing`, posing a person inside the free
camera — you live, other players as a local copy, NPCs as a stand-in model; 2.17.0 adds `IReShade` and checked
plugin downloads; 2.18.0 adds `XYPadElement` for the Head/Eyes aim grid; 2.20.0 adds the game photo screen's full
hide list as `VisibilityLayers`, `IHotkeys.IsActionHeld` and the `[` `]` `\` keys).

**Hide list and field of view (1.7.0).** The Capture tab mirrors the game photo screen's hide list, in its order: Me, My
own Spirit Echo, Other adventurers, Non-players, Enemy, Weapon, Friends, Party, Guild, Collectible, Other Spirit Echo.
Friends, Party and Guild really hide their members, even while Other adventurers is shown (the game's own switches only
stop keeping them visible); a player in any hidden group is hidden, except that a party member stays while Party is
shown. Weapon hides every
player's weapon, not only yours. With Other adventurers, Friends, Party and Guild all on, Photo Studio uses the game's own
"no other player" switch, so nobody is left. 1.6.0 settings carry over (Me → Me
+ My own Spirit Echo, Other players → the four player groups, Keep my party visible → all but Party), and the 1.6.0 key
is kept so a rollback still finds them. In the free camera, the Look tab's Field of view slider (10–100°, ↺ = the game's
own FOV) and three hotkeys change the FOV: FOV in `]`, FOV out `[` (hold to keep going), reset `\` — rebindable in
Settings → Hotkeys; Shift+wheel still works.

**Minimize and close (1.7.0).** The full panel's `–` minimizes it to the compact strip (in or out of the game's photo
mode) and changes nothing. `✕` — on the panel or the strip, or the Close Photo Studio hotkey (`Ctrl+Shift+F10`) — always
closes Photo Studio fully: it leaves the free camera, unfreezes, resets posed people and lamps and shows what you hid. When
any of that is running it asks first, listing exactly what will end; a pinned Look stays. While neither the panel nor the
strip is up, the SCENE / camera pills show the key that reopens Photo Studio.

**ReShade (1.5.0).** Optional. The Stellar launcher installs ReShade 6.8.0 (add-on build, BSD-3-Clause) as the game
folder's `dxgi.dll` and the Stellar ReShade bridge add-on (MIT) beside it, for Modded launches only (Photo Studio's page →
Dependencies). Photo Studio's Look tab turns ReShade on/off, switches presets and effects, and downloads shader packs
from each pack's own GitHub at a pinned commit (sha256-checked, never re-hosted): ReShade standard
(`crosire/reshade-shaders`, licence per file), SweetFX (`CeeJayDK/SweetFX`, MIT), prod80 (`prod80/prod80-ReShade-Repository`,
MIT), FXShaders (`luluco250/FXShaders`, MIT), AcerolaFX (`GarrettGunnell/AcerolaFX`, MIT) and OtisFX (`FransBouma/OtisFX`,
MIT, PandaFX by Jukka Korhonen included). AcerolaFX effects only work between its `AcerolaFXStart` and `AcerolaFXEnd`
effects, in that order. qUINT is not offered: its licence reserves all rights. Packs and Photo Studio's ReShade presets live in
`stellar/plugindata/stellar.photostudio.data/reshade/`. A Look preset remembers the ReShade preset and on/off (a preset
outside that folder — ReShade's own — is remembered as on/off only); effect switches are saved in the ReShade preset
itself, so they do not mark the look modified and Reset all does not undo them.

### ReShade presets (Look → ReShade → Presets)

- **Photo Studio's own looks** — Cinematic warm, Soft anime, Cool night, Clean sharpen — ship inside the plugin
  (`Resources/Presets/`) and use only SweetFX and prod80 effects that do not read depth. Installing one downloads the
  shader packs it needs first.
- **Community presets** are downloaded from their authors' own repositories at a pinned commit and checked by sha256 —
  never re-hosted. The downloaded file is kept unchanged in `reshade/preset-sources/`; ReShade loads a copy in
  `reshade/presets/` that leaves out depth settings made for another game and textures no pack ships.
  - StarLuxe Galactic, Legacy, Luminescence — Dimitri-Matheus, [GPL-3.0](https://github.com/Dimitri-Matheus/StarLuxe/blob/6b82aff25e9eb3e69c3927ed724a2f463c92c40e/LICENSE)
  - Genshin Stella Mod default preset (Medium, High) — Sefinek, [CC BY-SA 4.0](https://github.com/Genshin-Stella-Mod/resources/blob/a15ae11517dad6c5681beb212422724c4455a276/public/resources/ReShade/Presets/LICENSE)
  - Okami City Ruins — Yomigami Okami, port by Meynan, [MIT](https://github.com/MeynanAneytha/YomigamiOkami-reshade-shaders/blob/53e9fe085845093f50189dc5cce9419e88e423ed/LICENSE)
  - AcerolaFX Gameplay, Golden Age, Draft, Distant Past — Garrett Gunnell, [MIT](https://github.com/GarrettGunnell/AcerolaFX/blob/c33f779b093fa1e25faf0c77ef22c3fe6902e2fe/LICENSE.md)
- **Link-only** presets (no licence for sharing) are listed with their author's page; Photo Studio never downloads them.
- An installed preset file is never overwritten. Any other `.ini` can be added through "Open folder" + Rescan.

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
  colour/white balance/LUT/bloom only; depth of field and film grain are photo-only. ReShade's on/off and preset are
  snapshotted when Photo Studio opens (panel or docked strip) and put back when it closes or unloads (owner ruling
  2026-10-04; if ReShade is still loading at close, the snapshot is kept and restored at the next close).
- **Never use `ZServerTime` for time of day** (it breaks the game's ping display; framework `ITimeOfDay` uses the
  game's own time-of-day calls).
- Design + decisions: devkit `docs/superpowers/specs/2026-09-30-photo-studio-core-design.md` (§ 11 as-built) and
  `2026-10-01-photo-studio-render-quality-design.md`.

## Known interaction

MahiruUtility's graphics / time-of-day options drive the same game settings; turn them off while using Photo
Studio's (until MahiruUtility moves onto the shared framework services).
