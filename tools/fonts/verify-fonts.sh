#!/usr/bin/env bash
# Re-downloads every shipped font file and licence from its pinned upstream URL and checks
# the sha256 against src/OneRender.Fonts/fonts/MANIFEST.tsv and the committed bytes.
# Usage: tools/fonts/verify-fonts.sh [download-dir]   (default: a fresh temporary directory)
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
fonts="$root/src/OneRender.Fonts/fonts"
work="${1:-$(mktemp -d)}"
mkdir -p "$work"
fail=0
sum() { sha256sum "$1" | cut -d' ' -f1; }
fetch() { # url -> local cache path (one download per url)
  local p="$work/$(printf '%s' "$1" | sha256sum | cut -c1-16)"
  [ -f "$p" ] || curl -sfL --retry 3 -o "$p" "$1"
  printf '%s' "$p"
}
while IFS=$'\t' read -r kind a b c d; do
  case "$kind" in
    archive)
      got=$(sum "$(fetch "$a")")
      if [ "$got" = "$b" ]; then echo "ok   archive $a"; else echo "FAIL archive $a: $got"; fail=1; fi ;;
    file)
      dl=$(fetch "$b")
      if [ "$c" != "-" ]; then
        mkdir -p "$dl.x"; tar -xzf "$dl" -C "$dl.x" "$c"; dl="$dl.x/$c"
      fi
      got=$(sum "$dl")
      if [ "$got" != "$d" ]; then echo "FAIL $a: upstream sha256 $got, manifest $d"; fail=1
      elif ! cmp -s "$dl" "$fonts/$a"; then echo "FAIL $a: committed bytes differ from upstream"; fail=1
      else echo "ok   $a $got"; fi ;;
  esac
done < <(grep -v '^#' "$fonts/MANIFEST.tsv")
# Nothing committed may be missing from the manifest.
while IFS= read -r f; do
  grep -qF "$(printf 'file\t%s\t' "$f")" "$fonts/MANIFEST.tsv" || { echo "FAIL $f is not in the manifest"; fail=1; }
done < <(cd "$fonts" && find . -type f ! -name MANIFEST.tsv | sed 's|^\./||' | sort)
exit $fail
