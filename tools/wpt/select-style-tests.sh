#!/usr/bin/env bash
# Copies the web-platform-tests parsing and computed-value files that exercise a property the style
# pipeline supports into tests/OneRender.Tests/testdata/wpt-style/, keeping their css/<module>/ paths.
# Usage: tools/wpt/select-style-tests.sh [wpt-checkout]   (default: the cache fetch-wpt.sh fills)
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
commit="$(tr -d '[:space:]' < "$here/WPT_COMMIT")"
src="${1:-${ONERENDER_CACHE:-$HOME/onerender/cache}/wpt/$commit}"
dest="$root/tests/OneRender.Tests/testdata/wpt-style"
props='font-size|display|position|float|clear|box-sizing|top|right|bottom|left|width|height|min-width|min-height|max-width|max-height|aspect-ratio|margin(-top|-right|-bottom|-left)?|padding(-top|-right|-bottom|-left)?|border(-(top|right|bottom|left))?(-(width|style|color))?|border-(top|bottom)-(left|right)-radius|border-radius|overflow(-x|-y)?|z-index|visibility|font-family|font-weight|font-style|font-stretch|font|line-height|vertical-align|white-space(-collapse)?|text-wrap-mode|text-align|text-indent|letter-spacing|word-spacing|direction|unicode-bidi|color|opacity|background-color|content|list-style-type|list-style-position'
rm -rf "$dest"; mkdir -p "$dest"
cd "$src"
find css -type f \( -path '*/parsing/*.html' -o -name '*-computed.html' -o -path '*/computed/*.html' \) | LC_ALL=C sort |
while read -r f; do
  if grep -Eq "test_(valid|invalid|computed)_value\(\s*[\"'](${props})[\"']" "$f"; then
    mkdir -p "$dest/$(dirname "$f")"; cp "$f" "$dest/$f"
  fi
done
find "$dest" -type f | wc -l
du -sh "$dest" | cut -f1
