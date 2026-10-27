#!/usr/bin/env bash
# Fetches the pinned upstream font files listed in fonts.tsv into a directory laid out like the fonts package
# (Liberation/, Carlito/, Caladea/), verifying every sha256. Re-running only fetches what is missing or wrong.
# Usage: fetch-fonts.sh [dest]      default: $ONERENDER_HOME/cache/fonts
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
home="${ONERENDER_HOME:-$HOME/onerender}"
dest="${1:-$home/cache/fonts}"
dl="$home/cache/downloads"
mkdir -p "$dest" "$dl"
sum() { sha256sum "$1" | cut -d' ' -f1; }
while IFS=$'\t' read -r path url member sha; do
  case "$path" in ''|'#'*) continue;; esac
  out="$dest/$path"
  if [ -f "$out" ] && [ "$(sum "$out")" = "$sha" ]; then continue; fi
  mkdir -p "$(dirname "$out")"
  file="$dl/$(printf '%s' "$url" | sha256sum | cut -c1-16)"
  [ -f "$file" ] || curl -sfL --retry 3 -o "$file" "$url"
  if [ "$member" = "-" ]; then cp "$file" "$out"; else tar -xzf "$file" -O "$member" > "$out"; fi
  got=$(sum "$out")
  if [ "$got" != "$sha" ]; then echo "sha256 mismatch for $path: $got (expected $sha)" >&2; rm -f "$out"; exit 1; fi
done < "$here/fonts.tsv"
echo "$dest"
