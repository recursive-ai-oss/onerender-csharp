#!/usr/bin/env bash
# Fetches the pinned Chrome for Testing build into
#   ${ONERENDER_CACHE:-$HOME/onerender/cache}/chrome/<version>/chrome-linux64/chrome
# and verifies the zip against the pinned SHA-256. Chrome is used only as a black box for
# calibration and is never redistributed. Safe to re-run.
set -euo pipefail
version=156.0.8078.47
sha256=e6c94490f0b6b337225aeb2d2a5034858516b7017356980ba94f5176d8774a50
cache="${ONERENDER_CACHE:-$HOME/onerender/cache}"
dest="$cache/chrome/$version"
if [ -x "$dest/chrome-linux64/chrome" ]; then echo "$dest/chrome-linux64/chrome"; exit 0; fi
mkdir -p "$dest"
curl -fsSL --retry 3 -o "$dest/chrome-linux64.zip" \
  "https://storage.googleapis.com/chrome-for-testing-public/$version/linux64/chrome-linux64.zip"
echo "$sha256  $dest/chrome-linux64.zip" | sha256sum -c -
unzip -q -o "$dest/chrome-linux64.zip" -d "$dest"
echo "$dest/chrome-linux64/chrome"
