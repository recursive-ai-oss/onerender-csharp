#!/usr/bin/env bash
# Builds the Tailwind page set: fetches the pinned Tailwind v4 standalone CLI and the pinned daisyUI and Flowbite npm
# releases (all MIT), compiles testdata/tailwind/input.css against our page sources, and writes one folder per page
# (index.html + styles.css) to $ONERENDER_HOME/corpus/tailwind-src/. Nothing third-party is stored in the repository.
# Usage: build-tailwind.sh
set -euo pipefail
. "$(dirname "$0")/env.sh"
TW_VERSION=4.3.3
TW_URL=https://github.com/tailwindlabs/tailwindcss/releases/download/v$TW_VERSION/tailwindcss-linux-x64
TW_SHA256=dc61b3ac6b8c9ca874c0cc4c57b2409791a64c5540404ca5f5367360babc313a
# npm release tarballs carry the built plugin files; integrity is the registry's published sha512 (base64).
DAISY_URL=https://registry.npmjs.org/daisyui/-/daisyui-5.7.50.tgz
DAISY_SRI='+/LRqnTCoc7s/bJEKKwtJSwzeXCVdAmRdNmZeZzgHCaQ9PJyl7j+2V2U32GQDMyD+VDkP9e/+JGoC72Y1hLk7w=='
FLOW_URL=https://registry.npmjs.org/flowbite/-/flowbite-4.0.2.tgz
FLOW_SRI='TxBdfZpd3HktHH4ashiYjirrSZeVPtG1OCRZMKTeXUa9FOlMxSF98zgjMB/AxE49KZ+rlfgWynAlaNpWxrqZmA=='
cache="$ONERENDER_HOME/cache/tailwind"
work="$ONERENDER_HOME/cache/tailwind-work"
src="$GEOMDIFF_REPO/testdata/tailwind"
out="$ONERENDER_HOME/corpus/tailwind-src"
mkdir -p "$cache"

fetch() { # url file kind expected
  local url="$1" file="$2" kind="$3" want="$4" got
  if [ ! -f "$file" ]; then curl -sfL --retry 3 -o "$file" "$url"; fi
  if [ "$kind" = sha256 ]; then got=$(sha256sum "$file" | cut -d' ' -f1); else got=$(openssl dgst -sha512 -binary "$file" | base64 -w0); fi
  if [ "$got" != "$want" ]; then echo "$kind mismatch for $url: $got" >&2; rm -f "$file"; exit 1; fi
}
fetch "$TW_URL" "$cache/tailwindcss-$TW_VERSION" sha256 "$TW_SHA256"
chmod +x "$cache/tailwindcss-$TW_VERSION"
fetch "$DAISY_URL" "$cache/daisyui.tgz" sha512 "$DAISY_SRI"
fetch "$FLOW_URL" "$cache/flowbite.tgz" sha512 "$FLOW_SRI"

rm -rf "$work"; mkdir -p "$work/node_modules/daisyui" "$work/node_modules/flowbite"
tar -xzf "$cache/daisyui.tgz" -C "$work/node_modules/daisyui" --strip-components=1
tar -xzf "$cache/flowbite.tgz" -C "$work/node_modules/flowbite" --strip-components=1
cp "$src/input.css" "$work/input.css"
cp -r "$src/pages" "$work/pages"
(cd "$work" && "$cache/tailwindcss-$TW_VERSION" -i input.css -o styles.css --minify)

rm -rf "$out"; mkdir -p "$out"
for p in "$work"/pages/*.html; do
  id=$(basename "$p" .html)
  mkdir -p "$out/$id"
  cp "$p" "$out/$id/index.html"
  cp "$work/styles.css" "$out/$id/styles.css"
done
echo "$out"
