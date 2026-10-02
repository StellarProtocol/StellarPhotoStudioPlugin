# shellcheck disable=SC1091
# Run 9 (time-pause probe) — copy to <devkit>/tools/scenarios/timepause-probe.sh (untracked), run
#   STELLAR_AUTONAV_ACCOUNT=2 STELLAR_TIMEPAUSE_MODE=town|field|long TIMEOUT_S_OVERRIDE=560 tools/run-scenario.sh timepause-probe
# and delete it afterwards. CLICK FAILED is filtered (the field mode teleports; AutoNav retries close-newbie).
source "$SCENARIO_DIR/in-world.sh"
export STELLAR_FREECAMPROBE_AUTO=1
TIMEOUT_S=${TIMEOUT_S_OVERRIDE:-560}
_f=(); for p in "${MUST_NOT_SEE[@]}"; do [[ "$p" == *"CLICK FAILED"* ]] || _f+=("$p"); done; MUST_NOT_SEE=("${_f[@]}")
MUST_SEE+=(
    '\[FreeCamProbe\] loaded; auto=True'
    '\[FreeCamProbe\] SEQUENCE START'
    '\[FreeCamProbe\] DONE'
)
