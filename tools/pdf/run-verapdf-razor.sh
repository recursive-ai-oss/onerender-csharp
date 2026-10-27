#!/usr/bin/env bash
# Renders the sample invoice component to PDF through the Razor package (the test project writes it when ONERENDER_RAZOR_OUT is set) and
# validates it with veraPDF against PDF/UA-1. Run from the repository root; needs .NET 10 and tools/pdf/fetch-verapdf.sh already run.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
base="${ONERENDER_HOME:-$HOME/onerender}"
OUT="${OUT:-$base/u33/out}"
export ONERENDER_ARTIFACTS="${ONERENDER_ARTIFACTS:-$base/u33/art}"
mkdir -p "$OUT"
cd "$repo"
dotnet build tests/OneRender.Razor.Tests -v quiet -clp:NoSummary >"$OUT/build.log" 2>&1 || { tail -20 "$OUT/build.log"; exit 2; }
( cd "$ONERENDER_ARTIFACTS/bin/OneRender.Razor.Tests/debug" && ONERENDER_RAZOR_OUT="$OUT" ./OneRender.Razor.Tests >"$OUT/test.log" 2>&1 ) || { tail -20 "$OUT/test.log"; exit 2; }
export JAVA_HOME="$base/u25/tools/jre" PATH="$base/u25/tools/jre/bin:$PATH"
"$base/u25/tools/verapdf/verapdf" --flavour ua1 --format xml "$OUT/invoice.pdf" >"$OUT/invoice.verapdf.xml" 2>"$OUT/invoice.verapdf.err"
if grep -q 'isCompliant="true"' "$OUT/invoice.verapdf.xml"; then verdict=PASS; rc=0; else verdict=FAIL; rc=1; fi
echo "invoice $verdict $(grep -o 'failedRules="[0-9]*"' "$OUT/invoice.verapdf.xml" | head -1) $(grep -o 'failedChecks="[0-9]*"' "$OUT/invoice.verapdf.xml" | head -1)"
exit $rc
