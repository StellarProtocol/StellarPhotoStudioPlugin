# FreeCamProbe — runbook (throwaway recon, branch `probe/free-camera`, never merged)

Answers § 9 items 1-6 of `docs/superpowers/specs/2026-10-01-photo-studio-free-camera-design.md`
(devkit branch `docs/free-camera-spec`). Results go to `docs/recon/free-camera-recon.md`.

**TEST client only (`/opt/game/BlueProtocol2`).** Per agent-process-rules § 56: do not launch, capture or drive
the test client while the owner's MAIN client is up and in use. Check first: `pgrep -af StarSEA.exe`, then read
that process's `WINEPREFIX` (`tr '\0' '\n' < /proc/<pid>/environ | grep WINEPREFIX`). The probe takes over the
game camera, raises an input shield, freezes effects/animation and plays one emote: it never touches
`/opt/game/BlueProtocol`.

## 0. Prerequisite — test-prefix framework must be feat/photo-studio-services `3ce054e` (2.13.0)

The probe is built against `Stellar.Abstractions 2.13.0-photostudio-dev`. The test prefix currently runs an
older framework (`86f3585` per the dispatch; the slot's `.stellar-version` marker read `2.9.0` on 2026-10-01).
Update it first, using the existing worktree at `3ce054e`
(`/tmp/claude-1000/-opt-game-game-project-stellar-devkit/fe180696-7724-478c-9305-4192f5eb4dfa/scratchpad/wt-fw`,
or make one: `git -C <devkit>/framework worktree add <dir> 3ce054e`):

```bash
FW=<worktree at 3ce054e>
git -C "$FW" log --oneline -1                       # must print 3ce054e
( cd "$FW/src" && /home/dorasu/.dotnet/dotnet build -c Release )
STELLAR_FRAMEWORK_ONLY=1 "$FW/tools/install-stellar.sh"   # SRC defaults to the worktree's src/; test prefix by default
```

Proof to record: `sha1sum <game_mini>/BepInEx/plugins/Stellar.Framework/Stellar.*.dll` and, after the run, the boot
line `=== Stellar Framework v… loaded ===` (the loaded build, not the on-disk sha1). `install-stellar.sh` resets
`game_mini/stellar_perf.flags` — re-arm perf flags afterwards if anything needs them.

## 1. Build

```bash
cd <plugin repo>/Probe.FreeCam        # git checkout probe/free-camera first
/home/dorasu/.dotnet/dotnet build -c Release \
  -p:RestoreSources="/opt/game/game-project/stellar-devkit/.localfeed;https://api.nuget.org/v3/index.json"
# -> bin/Release/Stellar.PhotoStudio.FreeCamProbe.dll
```

## 2. Deploy to the TEST prefix lowercase slot

`install-stellar.sh` only knows the shipping plugins, so a probe slot is a manual copy (the render-recon probe was
deployed the same way to `stellar/plugins/photostudioprobe/`). Lowercase slot, exactly one copy, no backup inside
`stellar/plugins/` (CLAUDE.md shadow-load rule):

```bash
GM=$(ls -d /opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_*/game_mini | sort -V | tail -1)
mkdir -p "$GM/stellar/plugins/freecamprobe"
cp bin/Release/Stellar.PhotoStudio.FreeCamProbe.dll "$GM/stellar/plugins/freecamprobe/"
sha1sum "$GM/stellar/plugins/freecamprobe/Stellar.PhotoStudio.FreeCamProbe.dll"
ls -d "$GM"/stellar/plugins/*[Ff]ree[Cc]am*      # must list ONLY freecamprobe/
```

Plugin id = lowercased assembly name = `stellar.photostudio.freecamprobe`. Without `STELLAR_FREECAMPROBE_AUTO=1`
the probe is inert apart from two hotkeys (F8 = next step, F6 = ping).

## 3. Run unattended

`tools/run-scenario.sh in-world` on its own is NOT enough: its MUST_SEE list completes at AutoNav's
`close-newbie` click and the runner then kills the client, while the probe starts 15 s after the first World phase.
Env vars pass straight through (`run-scenario.sh` sources the scenario file, then `launch-with-bepinex.sh` execs
wine with the inherited environment — that is how `in-world.sh`'s own `export STELLAR_AUTONAV=1` reaches the game).
Use the scenario shipped here, which sources `in-world.sh`, exports `STELLAR_FREECAMPROBE_AUTO=1` and also waits
for `[FreeCamProbe] DONE` (timeout 420 s):

```bash
cd /opt/game/game-project/stellar-devkit
git -C plugin-repos/StellarPhotoStudioPlugin show probe/free-camera:Probe.FreeCam/scenario/freecam-probe.sh \
  > tools/scenarios/freecam-probe.sh          # untracked; delete after the run
STELLAR_AUTONAV_ACCOUNT=2 tools/run-scenario.sh freecam-probe
```

(`STELLAR_AUTONAV_ACCOUNT=2` because AccountSwitcher slot 1 was expired on the test prefix in the render-recon runs —
see `docs/dev-workflow.md` § "AutoNav logs in through AccountSwitcher".) No `--screenshot`: every image the probe
needs is an in-process `Camera.Render` capture, so nothing reads the desktop.

Fallback without the scenario file (the render-recon method — keeps the client alive during the runner's
screenshot settle, but DOES take a window-scoped scrot at the end, so only when the desktop is free):
`STELLAR_FREECAMPROBE_AUTO=1 STELLAR_AUTONAV_ACCOUNT=2 SCREENSHOT_SETTLE_S=240 tools/run-scenario.sh in-world --screenshot`.

Manual mode: launch the test client with the probe deployed, stand in world, press **F8** repeatedly (one step per
press, order = list below; auto-only skip rules do not apply), press **F6** while step 2 holds the shield.

## 4. Outputs

| What | Where |
|---|---|
| Probe log (same lines, timestamped) | `<game_mini>/stellar/screenshots/freecamprobe/freecamprobe.log` |
| BepInEx log | `<game_mini>/BepInEx/LogOutput.log`; the runner's slice path is in its JSON (`log_slice`) |
| Evidence PNGs (`fcp_<step>.png`, full 1x frame, no UI) | `<game_mini>/stellar/screenshots/freecamprobe/` |

## 5. Extract results

```bash
GM=$(ls -d /opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_*/game_mini | sort -V | tail -1)
grep -F '[FreeCamProbe]' "$GM/BepInEx/LogOutput.log"                       # everything
grep -E '\[FreeCamProbe\] (VCAMS|BRAIN|BLEND .* summary|ORBIT summary|LENS|VCAM|FALLBACK summary)' "$GM/BepInEx/LogOutput.log"   # item 1
grep -E '\[FreeCamProbe\] (SHIELD|HOTKEY)' "$GM/BepInEx/LogOutput.log"     # item 2
grep -E '\[FreeCamProbe\] (EFFECTS|ANIM|HOLD)' "$GM/BepInEx/LogOutput.log" # item 3
grep -E '\[FreeCamProbe\] EMOTE' "$GM/BepInEx/LogOutput.log"               # item 4
grep -E '\[FreeCamProbe\] COMBAT' "$GM/BepInEx/LogOutput.log"              # item 5
grep -E '\[FreeCamProbe\] (LIGHT|LOOKAT|FRONTCAM)' "$GM/BepInEx/LogOutput.log"   # item 6
grep -E '\[FreeCamProbe\] (STEP EXCEPTION|ROUTINE EXCEPTION|RELEASE .*FAILED|.* FAILED)' "$GM/BepInEx/LogOutput.log"   # problems
grep -E '\[FreeCamProbe\] (TIMING|DONE)' "$GM/BepInEx/LogOutput.log"
```

## Steps (auto order) and how to read them

| # | Step | Verdict lines |
|---|---|---|
| env | screen/GPU, Main Camera pose, CameraManager FOV state, self + nearby players | — |
| 1 | `1_camera_takeover_vcam` | `VCAM created` (or `VCAM create FAILED` → auto runs 1b); `BLEND blend-in summary`; `ORBIT summary maxDPos/maxDAng` (≈0 ⇒ brain follows); `LENS … mainCam fov/roll` (stays at the set value ⇒ no UpdateResetFov fight); `BLEND blend-out summary maxBlendDuration`, `BRAIN after release active=` |
| 1b | `1b_camera_fallback_brain_off` (auto only if the vcam failed; F8 any time) | `FALLBACK summary posOverwrittenNextFrame / fovOverwrittenNextFrame / lateUpdateOverwrites` |
| 2 | `2_input_shield` | `(a)` lines: does `SetPhotoPlayerMoveShield` change any state / raise the Lua event; `(b)` ignore bitstring (one char per `EInputMask`, Move first) with/without the EPhoto mask; `HOTKEY ping` lines |
| 3a | `3a_freeze_effects` | `froze N … us/effect`, `createdWhileFrozen`, `unfroze` |
| 3b | `3b_freeze_animation` | `ANIM baseline` diff vs each path's `frozen` diff (P1 AnimComp.Speed, P2 BattleFrameSpeed, P3 SetEModelAnimTimeSwitch, P4 Animator.speed); `snap-back readback` |
| 3c | `3c_freeze_position_hold` | `HOLD observe … phase:`; `HOLD reassert cost avg/max perCall extrapolated50` vs the 0.3 ms budget; `gameRewroteBeforeNextUpdate`, `rootOffHeld`; `HOLD released: snap-back` |
| 4 | `4_emote` | VM registry matches + function lists; gates; `EMOTE t+…s actorState` + diff vs before |
| 5 | `5_combat_flag` | C# + Lua reads; `COMBAT summary has104=`; `COMBAT FIRED` lines (only if combat happened) |
| 6a | `6a_point_light` | `LIGHT i=… delta=` luminance of the character region |
| 6b | `6b_head_look_at` | `LOOKAT camera` / `LOOKAT point` state + head-region diff; `LOOKAT released` |

Every override is released in the step's `finally` and again by `RELEASE ALL` on scene change, sequence end and
plugin dispose; a `pendingReleases=0` in the `TIMING total` line confirms nothing leaked.

## Run 2 steps (auto since 2026-10-01 run 2; run-1 steps stay on F8, off in auto except `env` + `5_combat_flag`)

| Step | Verdict lines |
|---|---|
| `A_anim_freeze_isolated` | `A <path> VERDICT FROZEN/MOVING self … changed=…% \| other …` vs `A0_static` (noise) and `A0_emote_unfrozen` (moving); paths P1 AnimComp.Speed, P3 SetEModelAnimTimeSwitch, P4 Animator.speed, P5a SkillStageTimeFactor, P5b AnimResFactor; `A model …` lines give ECS vs GameObject model kind |
| `B_effect_freeze_visual` | `B_U_unfrozen` / `B_F_frozen` particle `particleTimeAdvanced`; `B createdWhileFrozen mode1/mode2 … contextIsFreeze=a/b particlesStillAdvancing=c/d`; `B hooks: addHits displayHits` |
| `C_hold_write_cost` | `C W1/W2/W3 … perChar=…us … END-OF-FRAME visual off-hold a/b`; self proxy (`C self …`) when nobody is within 30 m |
| `D_character_lights` | `D1/D1b/D2/D2b/D3 … ON: lum … delta=`; `after game-off: restoredExactly=`; `after snapshot write-back: restoredExactly=` |
| `F_lookat_restore` | `F after game-recipe release: equalsPre=… diff[…]`; `F after snapshot restore […]: equalsPre=` |
| `E_input_mask_source` | `E source 28/40/17: …`; `E coexist: …` |
