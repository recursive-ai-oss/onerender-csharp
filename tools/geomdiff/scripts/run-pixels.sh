#!/usr/bin/env bash
# Pixel similarity against Chrome for one corpus set (information only): screenshots the frozen pages in Chrome with the geometry
# differential's settings, renders them with the engine through the image backend, and writes SSIM per page and viewport.
# Usage: run-pixels.sh <set-name> <snapshot-dir> <results-root> [dump-dir]
# Environment: JOBS (Chrome processes, default 4), FONTS_DIR (default $ONERENDER_HOME/cache/fonts), NO_SANDBOX=1, MAX_HEIGHT (default 4000).
set -euo pipefail
. "$(dirname "$0")/env.sh"
set_name="${1:?set name}"
snaps="${2:?snapshot dir}"
results="${3:?results root}"
dump=()
[ -n "${4:-}" ] && dump=(--dump "$4")
fonts="${FONTS_DIR:-$ONERENDER_HOME/cache/fonts}"
chrome=$("$GEOMDIFF_SCRIPTS/fetch-chrome.sh" | tail -1)
conf=$("$GEOMDIFF_SCRIPTS/fontconfig.sh" "$fonts" "$ONERENDER_HOME/cache/fonts-$set_name.conf")
work="$ONERENDER_HOME/work/$set_name"
mkdir -p "$work" "$results/$set_name"
sandbox=()
[ "${NO_SANDBOX:-}" = 1 ] && sandbox=(--no-sandbox)
"$GEOMDIFF_SCRIPTS/geomdiff.sh" pixels --snapshots "$snaps" --out "$results/$set_name" --set "$set_name" --chrome "$chrome" --fontconfig "$conf" \
  --jobs "${JOBS:-4}" --max-height "${MAX_HEIGHT:-4000}" --work "$work" "${dump[@]}" "${sandbox[@]}"
