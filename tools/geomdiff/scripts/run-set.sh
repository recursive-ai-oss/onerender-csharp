#!/usr/bin/env bash
# Runs one corpus set end to end: two Chrome captures, then the three comparisons (Chrome vs Chrome, Chrome vs a
# deliberately perturbed copy, Chrome vs the engine). Captures are kept under $ONERENDER_HOME/captures/<set>/, per-page
# mismatch detail under $ONERENDER_HOME/details/<set>/, and the summaries in <results-root>/<set>/.
# Usage: run-set.sh <set-name> <snapshot-dir> <results-root> [--page-table]
# Environment: JOBS (Chrome processes, default 4), FONTS_DIR (default $ONERENDER_HOME/cache/fonts; point it at the fonts
# package folder to use the package files), PHASES (any of "a b compare"; lets a long set run as separate jobs), VIEWPORTS (default "1280x800 390x844"), NO_SANDBOX=1 to run Chrome unsandboxed.
set -euo pipefail
. "$(dirname "$0")/env.sh"
set_name="${1:?set name}"
snaps="${2:?snapshot dir}"
results="${3:?results root}"
page_table="${4:-}"
jobs="${JOBS:-4}"
fonts="${FONTS_DIR:-$ONERENDER_HOME/cache/fonts}"
chrome=$("$GEOMDIFF_SCRIPTS/fetch-chrome.sh" | tail -1)
conf=$("$GEOMDIFF_SCRIPTS/fontconfig.sh" "$fonts" "$ONERENDER_HOME/cache/fonts-$set_name.conf")
cap="$ONERENDER_HOME/captures/$set_name"
det="$ONERENDER_HOME/details/$set_name"
out="$results/$set_name"
work="$ONERENDER_HOME/work/$set_name"
mkdir -p "$cap" "$det" "$out" "$work"
vps=()
for v in ${VIEWPORTS:-1280x800 390x844}; do vps+=(--viewport "$v"); done
sandbox=()
[ "${NO_SANDBOX:-}" = 1 ] && sandbox=(--no-sandbox)
G="$GEOMDIFF_SCRIPTS/geomdiff.sh"

phases=" ${PHASES:-a b compare} "
for run in a b; do
  case "$phases" in *" $run "*) ;; *) continue;; esac
  "$G" capture --snapshots "$snaps" --out "$cap/$run" --chrome "$chrome" --fontconfig "$conf" "${vps[@]}" --jobs "$jobs" --work "$work" "${sandbox[@]}"
done
case "$phases" in *" compare "*) ;; *) exit 0;; esac
cp "$cap/a/run.json" "$out/capture-run.json"

"$G" compare --chrome-dir "$cap/a" --ours-dir "$cap/b" --set "$set_name" --label chrome-vs-chrome --out "$out/chrome-vs-chrome" --detail "$det/chrome-vs-chrome" $page_table > /dev/null
rm -rf "$cap/perturbed"
"$G" perturb --in "$cap/a" --out "$cap/perturbed" --expect-out "$cap/expect.json" --seed 1
"$G" compare --chrome-dir "$cap/a" --ours-dir "$cap/perturbed" --set "$set_name" --label perturbed --out "$out/perturbed" --expect "$cap/expect.json" $page_table > /dev/null
cp "$cap/expect.json" "$out/perturbed/expected-classes.json"
"$G" compare --chrome-dir "$cap/a" --engine --snapshots "$snaps" --set "$set_name" --label engine --out "$out/engine" --detail "$det/engine" $page_table > /dev/null
for l in chrome-vs-chrome perturbed engine; do echo "== $set_name / $l"; sed -n '5,12p' "$out/$l/summary.md"; done
