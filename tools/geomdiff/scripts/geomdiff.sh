#!/usr/bin/env bash
# Runs the built differential tool. Usage: geomdiff.sh <command> [options]   (see the tool's usage text)
set -euo pipefail
. "$(dirname "$0")/env.sh"
exec dotnet "$ONERENDER_ARTIFACTS/bin/OneRender.GeomDiff/release/onerender-geomdiff.dll" "$@"
