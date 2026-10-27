#!/usr/bin/env bash
# One fuzz target, the way CI (and a light worker job) runs it: replay the committed inputs, then a bounded libFuzzer run.
#
#   fuzz/ci.sh <target> [seconds]        default 60 seconds
#   fuzz/ci.sh --list                    print every target
#
# 1. Tools: the libFuzzer driver and the SharpFuzz instrumenter are installed once under $ONERENDER_FUZZ_HOME/tools
#    (fuzz/OneRender.Fuzz.Values/setup.sh; Linux x64 only, because the driver is a Linux x64 binary).
# 2. Replay, without libFuzzer: every committed input for the target runs once and must not throw. Those are the seed
#    folder of the target (fuzz/OneRender.Fuzz.Values/seeds/<target>/, which includes the inputs that were once findings)
#    and testdata/fuzz/<target>-*.bin (a minimized input of a finding, named after its target).
# 3. A bounded run: the seeds plus a corpus that grows in $ONERENDER_FUZZ_HOME/work. A crash, hang or out-of-memory input
#    fails the script and is left under $ONERENDER_FUZZ_HOME/work/<harness>/findings/<target>/ (or .../crashes/).
#
# The result line (target, executions, coverage features, findings) goes to stdout, to $ONERENDER_FUZZ_HOME/summary/<target>.txt
# and, in GitHub Actions, to the job summary.
#
# Environment: ONERENDER_FUZZ_HOME (default ~/onerender/fuzz), the usual DOTNET_ROOT/PATH for dotnet.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
values_targets=(values tree html layout inline positioning paint flex fragmentation motion)
main_targets=(displaylist fontdata colorglyphs grid flow table effects incremental paged pdf)

if [[ "${1:-}" == "--list" ]]; then
  printf '%s\n' "${values_targets[@]}" "${main_targets[@]}"
  exit 0
fi
target="${1:?usage: fuzz/ci.sh <target> [seconds] | --list}"
seconds="${2:-60}"
home="${ONERENDER_FUZZ_HOME:-$HOME/onerender/fuzz}"
mkdir -p "$home/summary" "$home/work"

harness=""
for t in "${values_targets[@]}"; do [[ $t == "$target" ]] && harness=values; done
for t in "${main_targets[@]}"; do [[ $t == "$target" ]] && harness=main; done
[[ -n $harness ]] || { echo "unknown target '$target' (see --list)" >&2; exit 2; }

if [[ ! -x "$home/tools/libfuzzer-dotnet" || ! -x "$home/tools/sharpfuzz" ]]; then
  ONERENDER_FUZZ_HOME="$home" bash "$root/fuzz/OneRender.Fuzz.Values/setup.sh"
fi
export ONERENDER_FUZZ_HOME="$home" LIBFUZZER_DOTNET="$home/tools/libfuzzer-dotnet" SHARPFUZZ="$home/tools/sharpfuzz"

# --- 2. Replay ---------------------------------------------------------------------------------------------------------
regressions=()
for f in "$root"/testdata/fuzz/"$target"-*.bin; do [[ -f $f ]] && regressions+=("$f"); done
echo "== replay $target ($harness harness)"
if [[ $harness == values ]]; then
  plain="$home/build/replay-values"
  rm -rf "$plain"
  dotnet build "$root/fuzz/OneRender.Fuzz.Values" -c Release -o "$plain" -v quiet -clp:NoSummary
  count=0
  for f in "$root/fuzz/OneRender.Fuzz.Values/seeds/$target"/* "${regressions[@]}"; do
    [[ -f $f ]] || continue
    dotnet "$plain/OneRender.Fuzz.Values.dll" "$target" "$f"
    count=$((count + 1))
  done
  echo "replayed $count committed inputs, no failures"
else
  export ONERENDER_FUZZ_WORK="$home/work/main"
  mkdir -p "$ONERENDER_FUZZ_WORK"
  if [[ ${#regressions[@]} -gt 0 ]]; then
    bash "$root/fuzz/run.sh" replay "$target" "${regressions[@]}"
  else
    echo "no committed regression inputs for $target (the generated seeds are replayed by the run below)"
  fi
fi

# --- 3. Bounded run ----------------------------------------------------------------------------------------------------
echo "== fuzz $target for $seconds s"
log="$home/work/$target.log"
status=0
if [[ $harness == values ]]; then
  out="$home/work/values/$target"
  mkdir -p "$out"
  bash "$root/fuzz/OneRender.Fuzz.Values/run.sh" "$target" "$seconds" "$root/fuzz/OneRender.Fuzz.Values/seeds/$target" "$out" >"$log" 2>&1 || status=$?
  findings="$out/crashes"
else
  bash "$root/fuzz/run.sh" "$target" "$seconds" >"$log" 2>&1 || status=$?
  findings="$home/work/main/findings/$target"
fi
tail -n 12 "$log"

execs=$(grep -E '^(stat::)?number_of_executed_units' "$log" | tail -1 | awk '{print $2}' || true)
cov=$(grep -oE 'cov: [0-9]+ ft: [0-9]+' "$log" | tail -1 || true)
found=$(find "$findings" -type f 2>/dev/null | wc -l)
line="$target: ${seconds}s, ${execs:-?} executions, ${cov:-cov: ?}, findings: $found, exit $status"
echo "$line" | tee "$home/summary/$target.txt"
if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then echo "- $line" >>"$GITHUB_STEP_SUMMARY"; fi
if [[ $status -ne 0 || $found -gt 0 ]]; then
  echo "FINDING in $target: see $findings" >&2
  exit 1
fi
