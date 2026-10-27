#!/usr/bin/env bash
# Variant experiment: ours-only widths of one element when parts of a snapshot page are removed.
# Usage: variant.sh <set> <page> <viewport> <path> <sed-expr>...
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
. "$here/../geomdiff/scripts/env.sh"
set_name="$1"; page="$2"; vp="$3"; path="$4"; shift 4
work="$ONERENDER_HOME/forms/variant"; rm -rf "$work"; mkdir -p "$work"
for expr in "$@"; do
  d="$work/v"; rm -rf "$d"; mkdir -p "$d"; cp -r "$ONERENDER_HOME/corpus/$set_name/$page" "$d/$page"
  sed -i -E "$expr" "$d/$page/index.html"
  out=$("$GEOMDIFF_SCRIPTS/geomdiff.sh" engine-table --snapshots "$d" --viewport "$vp" | awk -v p="$path" '$1==p')
  echo "[$expr] -> $out"
done
