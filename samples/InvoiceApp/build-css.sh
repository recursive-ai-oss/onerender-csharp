#!/usr/bin/env bash
# Compiles wwwroot/css/app.css from Styles/app.css with the pinned Tailwind CSS v4 standalone CLI (MIT, Copyright (c) Tailwind Labs, Inc.).
# The CLI is fetched by version and SHA-256 into $ONERENDER_HOME/cache/tailwind (default ~/onerender) and never committed;
# the compiled style sheet is committed with Tailwind's licence notice, which the CLI keeps at the top of its output.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
home="${ONERENDER_HOME:-$HOME/onerender}"
version=4.3.3
sha256=dc61b3ac6b8c9ca874c0cc4c57b2409791a64c5540404ca5f5367360babc313a
cli="$home/cache/tailwind/tailwindcss-$version"
mkdir -p "$(dirname "$cli")"
if [ ! -f "$cli" ]; then
  curl -sfL --retry 3 -o "$cli" "https://github.com/tailwindlabs/tailwindcss/releases/download/v$version/tailwindcss-linux-x64"
fi
[ "$(sha256sum "$cli" | cut -d' ' -f1)" = "$sha256" ] || { echo "checksum mismatch for $cli" >&2; rm -f "$cli"; exit 1; }
chmod +x "$cli"
cd "$here"
"$cli" -i Styles/app.css -o wwwroot/css/app.css --minify
