#!/usr/bin/env bash
# Fetches the pinned web-platform-tests commit (tools/wpt/WPT_COMMIT) into
#   ${ONERENDER_CACHE:-$HOME/onerender/cache}/wpt/<commit>/
# Only the directories reftests need are unpacked (css/, fonts/, images/, common/, resources/);
# the checkout is a cache and is never committed. Safe to re-run: a finished fetch is kept.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
commit="$(tr -d '[:space:]' < "$here/WPT_COMMIT")"
cache="${ONERENDER_CACHE:-$HOME/onerender/cache}"
dest="$cache/wpt/$commit"
if [ -f "$dest/.complete" ]; then echo "$dest"; exit 0; fi
rm -rf "$dest.partial"; mkdir -p "$dest.partial"
curl -fsSL --retry 3 "https://codeload.github.com/web-platform-tests/wpt/tar.gz/$commit" |
  tar -xz --strip-components=1 -C "$dest.partial" --wildcards \
    '*/css/*' '*/fonts/*' '*/images/*' '*/common/*' '*/resources/*' '*/LICENSE.md'
rm -rf "$dest"; mv "$dest.partial" "$dest"
touch "$dest/.complete"
echo "$dest"
