#!/usr/bin/env bash
# Writes the six template PDFs and the paged sample (through the test project, which saves them when ONERENDER_PDF_OUT is set) and validates each
# with veraPDF against PDF/UA-1. Run from the repository root; needs .NET 10 and tools/pdf/fetch-verapdf.sh already run.
# Output: $OUT/<name>.pdf, $OUT/<name>.verapdf.xml and a one-line verdict per template on stdout.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
home="${ONERENDER_HOME:-$HOME/onerender}/u25"
OUT="${OUT:-$home/out}"
export ONERENDER_ARTIFACTS="${ONERENDER_ARTIFACTS:-$home/art}"
mkdir -p "$OUT"
cd "$repo"
dotnet build tests/OneRender.Pdf.Tests -c Release -v quiet -clp:NoSummary >"$OUT/build.log" 2>&1 || { tail -20 "$OUT/build.log"; exit 2; }
exe="$ONERENDER_ARTIFACTS/bin/OneRender.Pdf.Tests/release"
( cd "$exe" && ONERENDER_PDF_OUT="$OUT" ./OneRender.Pdf.Tests -method '*PassesPdfUaChecks*' >"$OUT/test.log" 2>&1 ) || { tail -20 "$OUT/test.log"; exit 2; }
export JAVA_HOME="$home/tools/jre" PATH="$home/tools/jre/bin:$PATH"
rc=0
for name in invoice report statement receipt certificate email paged; do
  "$home/tools/verapdf/verapdf" --flavour ua1 --format xml "$OUT/$name.pdf" >"$OUT/$name.verapdf.xml" 2>"$OUT/$name.verapdf.err"
  if grep -q 'isCompliant="true"' "$OUT/$name.verapdf.xml"; then verdict=PASS; else verdict=FAIL; rc=1; fi
  echo "$name $verdict $(grep -o 'failedRules="[0-9]*"' "$OUT/$name.verapdf.xml" | head -1) $(grep -o 'failedChecks="[0-9]*"' "$OUT/$name.verapdf.xml" | head -1)"
done
exit $rc
