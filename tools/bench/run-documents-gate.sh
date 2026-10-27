#!/usr/bin/env bash
# The document benchmark as one command: onerender against headless Chrome print-to-PDF (and WeasyPrint, for information).
#
#   tools/bench/run-documents-gate.sh            the gate: takes the benchmark lock, publishable numbers
#   tools/bench/run-documents-gate.sh --smoke    a short run with no lock; numbers are NOT publishable
#
# The four documents (tools/bench/documents/make-documents.py): the invoice template, a 50-page report, a table of 10,000 rows (about 210
# pages) and a Tailwind 4.3.3 marketing page. Each tool turns each document into a PDF:
#   onerender    HtmlPdf.ToPdf with default options (tagged). A fresh process per run (wall time, peak memory, cold start) of the NativeAOT
#                executable tools/bench/OneRender.PdfCli, and of the same call under the JIT for information; then BenchmarkDotNet
#                in-process (time and allocation of a warm call)
#   Chrome       Chrome for Testing 156.0.8078.47, headless, Page.printToPDF over the DevTools Protocol (tools/bench/OneRender.ChromePdf):
#                a fresh browser per cold run, then repeated prints in the last browser; fonts limited to the font files the fonts package ships
#   WeasyPrint   a fresh `weasyprint` process per run, from its own uv environment; same font files through the same fontconfig file
#
# Output: $OUT (default $ONERENDER_WORK/bench/documents-<mode>-<UTC time>) holds machine.txt (CPU, cores, load before, during and after; tool
# versions), onerender.jsonl, chrome.jsonl, weasyprint.jsonl, bdn/ (BenchmarkDotNet reports), the PDFs, and summary.md.
#
# Environment: ONERENDER_WORK (default ~/onerender); GATE_CORES (default 8-15) and SMOKE_CORES (default 0-7) pin onerender and WeasyPrint
# (Chrome is not pinned: it is multi-process); DOTNET_DIR (default ~/dotnet10 if present); WEASYPRINT_VERSION (default 70.0); SKIP_WEASYPRINT=1; NO_SANDBOX=1 runs Chrome unsandboxed.
# The gate does not wait for the lock: when ~/WORKER-BENCH.lock exists it prints the holder and exits 3.
set -u -o pipefail

MODE=gate
if [ "${1:-}" = "--smoke" ]; then MODE=smoke; fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="${ONERENDER_WORK:-$HOME/onerender}"
export ONERENDER_HOME="$WORK"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="${OUT:-$WORK/bench/documents-$MODE-$STAMP}"
LOCK="$HOME/WORKER-BENCH.lock"
if [ -n "${DOTNET_DIR:-}" ]; then :; elif [ -d "$HOME/dotnet10" ]; then DOTNET_DIR="$HOME/dotnet10"; else DOTNET_DIR=""; fi
if [ -n "$DOTNET_DIR" ]; then export PATH="$DOTNET_DIR:$PATH" DOTNET_ROOT="$DOTNET_DIR"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1
export ONERENDER_ARTIFACTS="${ONERENDER_ARTIFACTS:-$WORK/bench-build}"
WEASYPRINT_VERSION="${WEASYPRINT_VERSION:-70.0}"
DOCS="$WORK/bench-docs"
mkdir -p "$OUT/pdf" "$OUT/bdn" "$WORK" "$DOCS"

if [ "$MODE" = gate ]; then
  CORES="${GATE_CORES:-8-15}"
  OR_RUNS=7; CH_COLD=5; CH_WARM=7; WP_RUNS=3
  BDN=(--warmupCount 3 --iterationCount 15 --launchCount 2 --invocationCount 1 --unrollFactor 1)
  PRIORITY=()
else
  CORES="${SMOKE_CORES:-0-7}"
  OR_RUNS=3; CH_COLD=2; CH_WARM=3; WP_RUNS=1
  BDN=(--warmupCount 2 --iterationCount 5 --launchCount 1 --invocationCount 1 --unrollFactor 1)
  PRIORITY=(nice -n 15)
fi
BDN+=(--exporters json --memory true)

SAMPLER=
record() { { echo "== $1 $(date -u +%FT%TZ)"; cat /proc/loadavg; ps -eo pcpu,pmem,etimes,comm --sort=-pcpu | head -6; } >> "$OUT/machine.txt"; }
cleanup() {
  [ -n "$SAMPLER" ] && kill "$SAMPLER" 2>/dev/null
  if [ "$MODE" = gate ] && [ "${HAVE_LOCK:-0}" = 1 ]; then rm -f "$LOCK"; fi
}
trap cleanup EXIT

# ---- preparation: builds, fetches and documents (network and build work; the lock is taken after, so it is held for the measurements only)
echo "preparing"
for p in bench/OneRender.Benchmarks.Documents tools/bench/OneRender.ChromePdf; do
  ( cd "$ROOT" && dotnet build "$p" -c Release -v quiet -clp:NoSummary > "$OUT/build-$(basename "$p").log" 2>&1 ) || { tail -20 "$OUT/build-$(basename "$p").log"; echo "build failed"; exit 1; }
done
( cd "$ROOT" && dotnet publish tools/bench/OneRender.PdfCli -c Release -r linux-x64 -v quiet -clp:NoSummary > "$OUT/build-PdfCli.log" 2>&1 ) || { tail -20 "$OUT/build-PdfCli.log"; echo "native publish failed"; exit 1; }
GEOM="$ROOT/tools/geomdiff/scripts"
FONTS="$("$GEOM/fetch-fonts.sh")" || exit 1
CONF="$("$GEOM/fontconfig.sh" "$FONTS" "$WORK/cache/fonts-documents.conf")" || exit 1
CHROME="$("$GEOM/fetch-chrome.sh" | tail -1)" || exit 1
TWDIR="$WORK/corpus/tailwind-src/tw-landing"
if [ ! -f "$TWDIR/styles.css" ]; then "$GEOM/build-tailwind.sh" > "$OUT/tailwind-build.log" 2>&1 || { tail -5 "$OUT/tailwind-build.log"; exit 1; }; fi
python3 "$ROOT/tools/bench/documents/make-documents.py" "$DOCS" --tailwind "$TWDIR" > "$OUT/documents.txt" || exit 1
WP=
if [ "${SKIP_WEASYPRINT:-0}" != 1 ]; then
  VENV="$WORK/weasyprint-venv"
  if [ ! -x "$VENV/bin/weasyprint" ] || ! "$VENV/bin/weasyprint" --version 2>/dev/null | grep -q "$WEASYPRINT_VERSION"; then
    rm -rf "$VENV"
    if "$HOME/.local/bin/uv" venv -q --python 3.13 "$VENV" && "$HOME/.local/bin/uv" pip install -q --python "$VENV/bin/python" "weasyprint==$WEASYPRINT_VERSION"; then :; else echo "WeasyPrint could not be installed; skipping it"; fi
  fi
  [ -x "$VENV/bin/weasyprint" ] && WP="$VENV/bin/weasyprint"
fi
BIN="$ONERENDER_ARTIFACTS/bin"
ORDLL="$BIN/OneRender.Benchmarks.Documents/release/OneRender.Benchmarks.Documents.dll"
ORNATIVE="$ONERENDER_ARTIFACTS/publish/OneRender.PdfCli/release_linux-x64/OneRender.PdfCli"
CHDLL="$BIN/OneRender.ChromePdf/release/OneRender.ChromePdf.dll"

{
  echo "mode: $MODE$([ "$MODE" = smoke ] && echo ' (NOT PUBLISHABLE: no lock, short job)')"
  echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2- | sed 's/^ //')"
  echo "cores: $(nproc --all) logical; onerender and WeasyPrint pinned to $CORES; Chrome unpinned"
  echo "governor: $(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_governor 2>/dev/null || echo unknown)"
  echo "memory: $(awk '/MemTotal/ {printf "%.0f GB", $2/1048576}' /proc/meminfo)"
  echo "kernel: $(uname -sr)"
  echo "dotnet sdk: $(dotnet --version 2>/dev/null); runtime: $(dotnet --list-runtimes | grep -m1 'NETCore.App' | cut -d' ' -f2)"
  echo "chrome: $("$CHROME" --version 2>/dev/null || echo unknown)"
  echo "weasyprint: $([ -n "$WP" ] && "$WP" --version || echo not run)"
  echo "fonts: $(grep -m1 fontset "$CONF" | sed "s/<!-- //; s/ -->//")"
  echo "runs: onerender $OR_RUNS processes, Chrome $CH_COLD cold + $CH_WARM warm, WeasyPrint $WP_RUNS; bdn: ${BDN[*]}"
  echo "commit: ${GATE_COMMIT:-unknown}"
} > "$OUT/machine.txt"

# ---- the lock (gate mode): claimed here, inside this job, atomically
if [ "$MODE" = gate ]; then
  set -o noclobber
  if ! echo "onerender documents-gate $(date -u +%FT%TZ) expected end $(date -u -d '+45 minutes' +%FT%TZ)" > "$LOCK" 2>/dev/null; then
    echo "the worker benchmark lock is held: $(cat "$LOCK" 2>/dev/null)"; echo "not waiting; run again when it is free"; exit 3
  fi
  set +o noclobber
  HAVE_LOCK=1
fi

record "load before"
( while true; do sleep 30; record "load during"; done ) &
SAMPLER=$!

rc=0
export FONTCONFIG_FILE="$CONF"
unset FONTCONFIG_PATH
: > "$OUT/onerender.jsonl"; : > "$OUT/onerender-jit.jsonl"; : > "$OUT/chrome.jsonl"; : > "$OUT/weasyprint.jsonl"

echo "onerender (fresh native processes)"
python3 "$ROOT/tools/bench/documents/measure.py" --label onerender --docs "$DOCS" --out "$OUT/pdf" --runs "$OR_RUNS" -- \
  "${PRIORITY[@]}" taskset -c "$CORES" "$ORNATIVE" '{in}' '{out}' >> "$OUT/onerender.jsonl" || rc=1

echo "onerender (fresh JIT processes)"
python3 "$ROOT/tools/bench/documents/measure.py" --label onerender-jit --docs "$DOCS" --out "$OUT/pdf" --runs "$OR_RUNS" -- \
  "${PRIORITY[@]}" taskset -c "$CORES" dotnet "$ORDLL" render '{in}' '{out}' >> "$OUT/onerender-jit.jsonl" || rc=1

echo "onerender (BenchmarkDotNet, in-process)"
( cd "$ROOT/bench/OneRender.Benchmarks.Documents" && ONERENDER_DOCS="$DOCS" "${PRIORITY[@]}" taskset -c "$CORES" dotnet "$ORDLL" bdn \
    "${BDN[@]}" --filter "*DocumentBenchmarks*" --artifacts "$OUT/bdn" > "$OUT/bdn.log" 2>&1 ) || { echo "bdn failed, see $OUT/bdn.log"; rc=1; }

echo "Chrome"
"${PRIORITY[@]}" dotnet "$CHDLL" --chrome "$CHROME" --fontconfig "$CONF" --docs "$DOCS" --out "$OUT/pdf" --cold "$CH_COLD" --repeat "$CH_WARM" ${NO_SANDBOX:+--no-sandbox} \
  >> "$OUT/chrome.jsonl" 2> "$OUT/chrome.err" || { echo "chrome run failed, see $OUT/chrome.err"; rc=1; }

if [ -n "$WP" ]; then
  echo "WeasyPrint"
  python3 "$ROOT/tools/bench/documents/measure.py" --label weasyprint --docs "$DOCS" --out "$OUT/pdf" --runs "$WP_RUNS" -- \
    "${PRIORITY[@]}" taskset -c "$CORES" "$WP" '{in}' '{out}' >> "$OUT/weasyprint.jsonl" || rc=1
fi

kill "$SAMPLER" 2>/dev/null; SAMPLER=
record "load after"
python3 "$ROOT/tools/bench/documents/summarize.py" "$OUT" "$MODE" > "$OUT/summary.md" || rc=1
cat "$OUT/summary.md"
echo "results: $OUT"
exit $rc
