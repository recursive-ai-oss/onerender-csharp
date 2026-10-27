#!/usr/bin/env bash
# Runs the forms geometry set on the worker: build, snapshot testdata/forms, capture Chrome (cached after the first run), compare with the
# engine and print the root-cause mismatches. Usage: run-forms.sh [set-dir-under-testdata] [WxH]   (default forms, both viewports)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
. "$here/../geomdiff/scripts/env.sh"
set_name="${1:-forms}"
snaps="$ONERENDER_HOME/corpus/$set_name"
cd "$GEOMDIFF_SCRIPTS"
./build.sh > /dev/null
if [ "${COMPARE_ONLY:-}" != 1 ]; then
  rm -rf "$snaps"
  [ "${RECAPTURE:-}" = 1 ] && rm -rf "$ONERENDER_HOME/captures/$set_name"
  ./geomdiff.sh snapshot-static --src "${SRC_DIR:-$GEOMDIFF_REPO/testdata/$set_name}" --out "$snaps" > /dev/null
  for v in ${VIEWPORTS:-1280x800 390x844}; do
    NO_SANDBOX=1 JOBS=2 VIEWPORTS="$v" PHASES="a" ./run-set.sh "$set_name" "$snaps" "$GEOMDIFF_REPO/results/geometry" > /dev/null
  done
fi
./geomdiff.sh compare --chrome-dir "$ONERENDER_HOME/captures/$set_name/a" --engine --snapshots "$snaps" --set "$set_name" --label engine --out "$GEOMDIFF_REPO/results/geometry/$set_name/engine" --detail "$ONERENDER_HOME/details/$set_name/engine" --page-table > /dev/null
sed -n '5,12p' "$GEOMDIFF_REPO/results/geometry/$set_name/engine/summary.md"
for f in "$ONERENDER_HOME/details/$set_name/engine/"*.json.gz; do python3 "$here/detail.py" "$f" ${DETAIL_ARGS:-} | head -${DETAIL_LINES:-25}; done
