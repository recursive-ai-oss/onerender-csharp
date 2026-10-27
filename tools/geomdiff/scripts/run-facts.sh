#!/usr/bin/env bash
# Captures Chrome's DOMSnapshot facts for every page of a set and compares them with the facts the engine writes for the same
# serialized document. Captures are kept under $FACTS_HOME/facts/<set>/, examples of disagreements under
# $FACTS_HOME/details/<set>-facts/, and the summary in <results-root>/<set>/.
# Usage: run-facts.sh <set-name> <snapshot-dir> <results-root>
# Environment: FACTS_HOME (where captures, details and work files go, default $ONERENDER_HOME), JOBS (Chrome processes, default 4), FONTS_DIR (default $ONERENDER_HOME/cache/fonts), PHASES ("capture compare"),
# VIEWPORTS (default "1280x800 390x844"), NO_SANDBOX=1, LIMIT (pages, default all), PARALLEL (comparison threads, default 4).
set -euo pipefail
. "$(dirname "$0")/env.sh"
set_name="${1:?set name}"
snaps="${2:?snapshot dir}"
results="${3:?results root}"
jobs="${JOBS:-4}"
fonts="${FONTS_DIR:-$ONERENDER_HOME/cache/fonts}"
chrome=$("$GEOMDIFF_SCRIPTS/fetch-chrome.sh" | tail -1)
conf=$("$GEOMDIFF_SCRIPTS/fontconfig.sh" "$fonts" "$ONERENDER_HOME/cache/fonts-$set_name.conf")
state="${FACTS_HOME:-$ONERENDER_HOME}"
cap="$state/facts/$set_name"
det="$state/details/$set_name-facts"
out="$results/$set_name"
work="$state/work/$set_name-facts"
mkdir -p "$cap" "$det" "$out" "$work"
vps=()
for v in ${VIEWPORTS:-1280x800 390x844}; do vps+=(--viewport "$v"); done
sandbox=()
[ "${NO_SANDBOX:-}" = 1 ] && sandbox=(--no-sandbox)
G="$GEOMDIFF_SCRIPTS/geomdiff.sh"
phases=" ${PHASES:-capture compare} "
case "$phases" in *" capture "*)
  "$G" facts-capture --snapshots "$snaps" --out "$cap" --chrome "$chrome" --fontconfig "$conf" "${vps[@]}" --jobs "$jobs" --limit "${LIMIT:-0}" --work "$work" "${sandbox[@]}";;
esac
case "$phases" in *" compare "*)
  "$G" facts-compare --captures "$cap" --snapshots "$snaps" --set "$set_name" --out "$out" --detail "$det" --parallel "${PARALLEL:-4}"
  sed -n '1,60p' "$out/summary.md";;
esac
