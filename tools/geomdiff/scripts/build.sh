#!/usr/bin/env bash
# Builds the differential tool in Release. Usage: build.sh
set -euo pipefail
. "$(dirname "$0")/env.sh"
dotnet build "$GEOMDIFF_REPO/tools/geomdiff/OneRender.GeomDiff.csproj" -c Release -v quiet -clp:NoSummary
