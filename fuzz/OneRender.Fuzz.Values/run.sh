#!/bin/bash
# Runs one fuzz target under SharpFuzz with the libFuzzer driver. Usage:
#   fuzz/OneRender.Fuzz.Values/run.sh <target> <seconds> <seed-dir> <out-dir>
# Targets: values, tree, html, layout, flex, fragmentation (noop for a driver check). Run setup.sh once first.
# The seed directory is only read; libFuzzer works on a copy in <out-dir>/corpus. Crashes, timeouts and out-of-memory
# inputs land in <out-dir>/crashes; the log and headline numbers (executions, coverage features, crash count) in
# <out-dir>/fuzz.log and <out-dir>/result.txt. One process only; at most 3600 s per call (reuse <out-dir>/corpus to continue).
# Everything is built under $ONERENDER_FUZZ_HOME (default ~/onerender/fuzz). Environment: FUZZ_MAX_LEN (default 4096),
# FUZZ_SEED (default 1), SKIP_BUILD=1 to reuse the last build.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
TARGET=${1:?usage: run.sh <target> <seconds> <seed-dir> <out-dir>}
SECONDS_TO_RUN=${2:?seconds}
SEEDS=${3:?seed dir}
OUT=${4:?out dir}
WH=${ONERENDER_FUZZ_HOME:-$HOME/onerender/fuzz}
[ "$SECONDS_TO_RUN" -le 3600 ] || { echo "at most 3600 s per call"; exit 2; }

export PATH=$HOME/dotnet10:$PATH DOTNET_ROOT=$HOME/dotnet10 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export MSBUILDDISABLENODEREUSE=1 NUGET_PACKAGES=$WH/nuget ONERENDER_ARTIFACTS=$WH/artifacts

BUILD=$WH/build/fuzz
if [ "${SKIP_BUILD:-0}" != 1 ]; then
    rm -rf "$BUILD"
    dotnet publish "$ROOT/fuzz/OneRender.Fuzz.Values" -c Release -o "$BUILD" -v quiet -clp:NoSummary
    # Instrument the engine only; the harness and the libraries it uses stay plain.
    "$WH/tools/sharpfuzz" "$BUILD/OneRender.dll"
fi

mkdir -p "$OUT/corpus" "$OUT/crashes"
cp -n "$SEEDS"/* "$OUT/corpus/" 2>/dev/null || true

# libfuzzer-dotnet starts one process per run of the target; this wrapper names the target for it.
cat > "$OUT/target.sh" <<EOT
#!/bin/sh
exec "$DOTNET_ROOT/dotnet" "$BUILD/OneRender.Fuzz.Values.dll" "$TARGET"
EOT
chmod +x "$OUT/target.sh"

set +e
"$WH/tools/libfuzzer-dotnet" --target_path="$OUT/target.sh" \
    -max_total_time="$SECONDS_TO_RUN" -max_len="${FUZZ_MAX_LEN:-4096}" -seed="${FUZZ_SEED:-1}" \
    -timeout=30 -rss_limit_mb=2048 -artifact_prefix="$OUT/crashes/" -print_final_stats=1 \
    "$OUT/corpus" > "$OUT/fuzz.log" 2>&1
STATUS=$?
set -e

{
    echo "target: $TARGET, $SECONDS_TO_RUN s, seed ${FUZZ_SEED:-1}, exit status $STATUS"
    grep -E '^stat::number_of_executed_units' "$OUT/fuzz.log" | tail -1 | sed 's/^stat:://'
    grep -E '^stat::average_exec_per_sec' "$OUT/fuzz.log" | tail -1 | sed 's/^stat:://'
    grep -oE 'cov: [0-9]+ ft: [0-9]+ corp: [0-9]+/[0-9]+[a-zA-Z]*' "$OUT/fuzz.log" | tail -1
    echo "crash artifacts: $(find "$OUT/crashes" -type f | wc -l)"
} 2>/dev/null | tee "$OUT/result.txt"
exit $STATUS
