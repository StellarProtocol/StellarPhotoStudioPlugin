#!/usr/bin/env bash
# FreeCamProbe unattended run — copy to <devkit>/tools/scenarios/freecam-probe.sh (untracked) and run
#   tools/run-scenario.sh freecam-probe
# Reuses in-world.sh (AutoNav login → world → close newbie popup) and additionally waits for the probe's
# final "[FreeCamProbe] DONE" line, so the runner does not kill the game before the probe has run
# (in-world.sh alone passes — and the runner kills the client — right after close-newbie, while the probe
# only starts 15 s after the first World phase).
# shellcheck disable=SC1091
source "$SCENARIO_DIR/in-world.sh"
export STELLAR_FREECAMPROBE_AUTO=1
# 15 s settle + ~60-90 s of steps after the ~60-90 s login; generous ceiling.
TIMEOUT_S=420
MUST_SEE+=(
    '\[FreeCamProbe\] loaded; auto=True'
    '\[FreeCamProbe\] SEQUENCE START'
    '\[FreeCamProbe\] DONE'
)
