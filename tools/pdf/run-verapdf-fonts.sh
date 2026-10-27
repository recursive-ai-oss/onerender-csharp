#!/usr/bin/env bash
# Writes the mixed-script pages set in the opt-in fonts packages (the font package test project saves them when ONERENDER_PDF_OUT is
# set) and validates them with veraPDF against PDF/UA-1. The first page must pass; the second holds scripts whose glyphs
# are split into several per character, which veraPDF's rule 7.21.7 reports (informational). Run from the repository root; needs
# .NET 10 and tools/pdf/fetch-verapdf.sh already run.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
base="${ONERENDER_HOME:-$HOME/onerender}"
OUT="${OUT:-$base/fonts-pdf/out}"
export ONERENDER_ARTIFACTS="${ONERENDER_ARTIFACTS:-$base/fonts-pdf/art}"
mkdir -p "$OUT"
cd "$repo"
dotnet build tests/OneRender.OptionalFonts.Tests -v quiet -clp:NoSummary >"$OUT/build.log" 2>&1 || { tail -20 "$OUT/build.log"; exit 2; }
( cd "$ONERENDER_ARTIFACTS/bin/OneRender.OptionalFonts.Tests/debug" && ONERENDER_PDF_OUT="$OUT" ./OneRender.OptionalFonts.Tests >"$OUT/test.log" 2>&1 ) || { tail -20 "$OUT/test.log"; exit 2; }
tools="${VERAPDF_HOME:-$base/u25/tools}"
export JAVA_HOME="$tools/jre" PATH="$tools/jre/bin:$PATH"
rc=0
for name in mixed-scripts complex-scripts; do
  "$tools/verapdf/verapdf" --flavour ua1 --format xml "$OUT/$name.pdf" >"$OUT/$name.verapdf.xml" 2>"$OUT/$name.verapdf.err"
  if grep -q 'isCompliant="true"' "$OUT/$name.verapdf.xml"; then verdict=PASS; else verdict=FAIL; [ "$name" = mixed-scripts ] && rc=1; fi
  echo "$name $verdict $(grep -o 'failedRules="[0-9]*"' "$OUT/$name.verapdf.xml" | head -1) $(grep -o 'failedChecks="[0-9]*"' "$OUT/$name.verapdf.xml" | head -1)"
done
exit $rc
