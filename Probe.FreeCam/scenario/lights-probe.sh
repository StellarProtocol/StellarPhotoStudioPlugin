#!/usr/bin/env bash
# Run 13 (lamps-on-characters probe) — copy to <devkit>/tools/scenarios/lights-probe.sh (untracked), run
#   STELLAR_AUTONAV_ACCOUNT=2 tools/run-scenario.sh lights-probe
# and delete it afterwards. CLICK FAILED is filtered (R8_restore may teleport home; AutoNav retries close-newbie).
# shellcheck disable=SC1091
source "$SCENARIO_DIR/in-world.sh"
export STELLAR_FREECAMPROBE_AUTO=1
TIMEOUT_S=${TIMEOUT_S_OVERRIDE:-600}
_f=(); for p in "${MUST_NOT_SEE[@]}"; do [[ "$p" == *"CLICK FAILED"* ]] || _f+=("$p"); done; MUST_NOT_SEE=("${_f[@]}")
MUST_SEE+=(
    '\[FreeCamProbe\] loaded; auto=True'
    '\[FreeCamProbe\] SEQUENCE START'
    '\[FreeCamProbe\] DONE'
)
