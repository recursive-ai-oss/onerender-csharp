#!/usr/bin/env bash
# Fetches the pinned Chrome for Testing build (linux-x64) into $ONERENDER_HOME/cache/chrome/<version>/ and checks its sha256.
# Chrome is only ever used as a black box on frozen, offline content, and is never redistributed.
# Usage: fetch-chrome.sh      (prints the path of the chrome executable on the last line)
set -euo pipefail
VERSION=156.0.8078.47
URL=https://storage.googleapis.com/chrome-for-testing-public/$VERSION/linux64/chrome-linux64.zip
SHA256=e6c94490f0b6b337225aeb2d2a5034858516b7017356980ba94f5176d8774a50
home="${ONERENDER_HOME:-$HOME/onerender}"
dest="$home/cache/chrome/$VERSION"
exe="$dest/chrome-linux64/chrome"
if [ ! -x "$exe" ]; then
  mkdir -p "$dest"
  zip="$dest/chrome-linux64.zip"
  curl -sfL --retry 3 -o "$zip" "$URL"
  got=$(sha256sum "$zip" | cut -d' ' -f1)
  if [ "$got" != "$SHA256" ]; then echo "sha256 mismatch for $URL: $got" >&2; rm -f "$zip"; exit 1; fi
  unzip -q -o "$zip" -d "$dest"
fi
"$exe" --version >&2 || true
echo "$exe"
