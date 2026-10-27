#!/usr/bin/env bash
# The interactive performance gate as one command.
#
#   tools/bench/run-interactive-gate.sh            the gate: takes the benchmark lock, publishable numbers
#   tools/bench/run-interactive-gate.sh --smoke    a short run with no lock; only proves the harness works, numbers are NOT publishable
#
# What it runs (BenchmarkDotNet, MemoryDiagnoser, Release):
#   bench/OneRender.Benchmarks            IncrementalBenchmarks (idle frame, hover, text change, subtree relayout) and
#                                         MotionBenchmarks (100 transform/opacity animations; the same plus width/color animations; idle clock;
#                                         a CSS transition on one row's width ticked every frame)
#   bench/OneRender.Benchmarks.OneGameUi  the same four changes through OneGame.UI and through the engine, side by side; and the like-for-like
#                                         rows: OneGame.UI's own flex-only page built through the engine's builder front end (FlexHudBenchmarks),
#                                         after a check that the two pages have the same nodes and border boxes (equivalence.txt)
#
# Output: $OUT (default $ONERENDER_WORK/bench/<mode>-<UTC time>) holds machine.txt (CPU, cores, load before, during, after), the BenchmarkDotNet
# reports (markdown, CSV, JSON, logs) per project, and summary.md (the verdicts against the bar and the comparison table).
#
# Environment: ONERENDER_WORK (default ~/onerender) for build output and results; GATE_CORES (default 8-15) the cores the gate pins to;
# SMOKE_CORES (default 0-7); DOTNET_DIR (default ~/dotnet10 if present, otherwise the dotnet on PATH).
# The gate does not wait for the lock: when ~/WORKER-BENCH.lock exists it prints the holder and exits 3.
set -u -o pipefail

MODE=gate
if [ "${1:-}" = "--smoke" ]; then MODE=smoke; fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="${ONERENDER_WORK:-$HOME/onerender}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="${OUT:-$WORK/bench/$MODE-$STAMP}"
LOCK="$HOME/WORKER-BENCH.lock"
if [ -n "${DOTNET_DIR:-}" ]; then :; elif [ -d "$HOME/dotnet10" ]; then DOTNET_DIR="$HOME/dotnet10"; else DOTNET_DIR=""; fi
if [ -n "$DOTNET_DIR" ]; then export PATH="$DOTNET_DIR:$PATH" DOTNET_ROOT="$DOTNET_DIR"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBUILDDISABLENODEREUSE=1
export ONERENDER_ARTIFACTS="$WORK/bench-build"
mkdir -p "$OUT" "$WORK"

if [ "$MODE" = gate ]; then
  CORES="${GATE_CORES:-8-15}"
  # BenchmarkDotNet settings are fixed so two gate runs are comparable.
  BDN=(--warmupCount 15 --iterationCount 40 --launchCount 3 --iterationTime 200)
  PRIORITY=()
else
  CORES="${SMOKE_CORES:-0-7}"
  BDN=(--warmupCount 5 --iterationCount 10 --launchCount 1 --iterationTime 200)
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

{
  echo "mode: $MODE$([ "$MODE" = smoke ] && echo ' (NOT PUBLISHABLE: no lock, short job)')"
  echo "host: $(hostname)"
  echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2- | sed 's/^ //')"
  echo "cores: $(nproc --all) logical; pinned to $CORES"
  echo "governor: $(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_governor 2>/dev/null || echo unknown)"
  echo "memory: $(awk '/MemTotal/ {printf "%.0f GB", $2/1048576}' /proc/meminfo)"
  echo "kernel: $(uname -sr)"
  echo "dotnet sdk: $(dotnet --version 2>/dev/null)"
  echo "bdn arguments: ${BDN[*]}"
  echo "commit: ${GATE_COMMIT:-unknown}"
} > "$OUT/machine.txt"
record "load before"

for p in OneRender.Benchmarks OneRender.Benchmarks.OneGameUi; do
  echo "building $p"
  "${PRIORITY[@]}" dotnet build -c Release -m:1 "$ROOT/bench/$p" -v quiet -clp:NoSummary > "$OUT/build-$p.log" 2>&1 || { tail -20 "$OUT/build-$p.log"; echo "build failed"; exit 1; }
done

# The like-for-like page must be the same page on both sides; this stops the run when it is not.
echo "checking the like-for-like scene"
"${PRIORITY[@]}" taskset -c "$CORES" dotnet "$ONERENDER_ARTIFACTS/bin/OneRender.Benchmarks.OneGameUi/release/OneRender.Benchmarks.OneGameUi.dll" --verify-scene > "$OUT/equivalence.txt" 2>&1 || { tail -5 "$OUT/equivalence.txt"; echo "the like-for-like scenes differ"; exit 1; }

if [ "$MODE" = gate ]; then
  set -o noclobber
  if ! echo "onerender interactive-gate $(date -u +%FT%TZ) expected end $(date -u -d '+40 minutes' +%FT%TZ)" > "$LOCK" 2>/dev/null; then
    echo "the worker benchmark lock is held: $(cat "$LOCK" 2>/dev/null)"; echo "not waiting; run again when it is free"; exit 3
  fi
  set +o noclobber
  HAVE_LOCK=1
fi

( while true; do sleep 30; record "load during"; done ) &
SAMPLER=$!

rc=0
for p in OneRender.Benchmarks OneRender.Benchmarks.OneGameUi; do
  echo "running $p"
  mkdir -p "$OUT/$p"
  case "$p" in
    OneRender.Benchmarks) FILTER=('*IncrementalBenchmarks*' '*MotionBenchmarks*') ;;
    *) FILTER=('*OneGameUiBenchmarks*' '*IncrementalBenchmarks*' '*FlexHudBenchmarks*') ;;
  esac
  # BenchmarkDotNet finds the project to rebuild from the working directory, so it runs inside the project folder.
  ( cd "$ROOT/bench/$p" && "${PRIORITY[@]}" taskset -c "$CORES" dotnet "$ONERENDER_ARTIFACTS/bin/$p/release/$p.dll" "${BDN[@]}" --filter "${FILTER[@]}" --artifacts "$OUT/$p/BenchmarkDotNet.Artifacts" > "$OUT/$p/run.log" 2>&1 ) || { echo "$p failed, see $OUT/$p/run.log"; rc=1; }
done

kill "$SAMPLER" 2>/dev/null; SAMPLER=
record "load after"

"$ROOT/tools/bench/summarize-gate.sh" "$OUT" "$MODE" > "$OUT/summary.md" || rc=1
cat "$OUT/summary.md"
echo "results: $OUT"
exit $rc
