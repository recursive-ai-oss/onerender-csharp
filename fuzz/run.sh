#!/usr/bin/env bash
# Build, instrument and run a fuzz target under libFuzzer.
#
#   fuzz/run.sh <target> <seconds> [corpus dir] [extra libFuzzer flags...]
#   fuzz/run.sh replay <target> <file>...      # run saved inputs once, without libFuzzer (reproduce a finding)
#
# Targets: fontdata   (see fuzz/README.md)
#
# Environment (nothing is baked into the script):
#   ONERENDER_FUZZ_WORK    required: where builds, corpora and findings go. Keep it outside the checkout.
#   ONERENDER_FUZZ_TOOLS   directory holding `libfuzzer-dotnet` (the libFuzzer host) and `sharpfuzz/sharpfuzz` (the instrumenter).
#                          LIBFUZZER_DOTNET and SHARPFUZZ override the two paths individually.
#   ONERENDER_ARTIFACTS    build output root, as for every other project in the repo
#   DOTNET                 the dotnet executable (default: dotnet from PATH)
#   FUZZ_TIMEOUT           per-input timeout in seconds (default 10)
#   FUZZ_RSS_MB            memory limit in MB (default 1024)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_bin="${DOTNET:-dotnet}"
work="${ONERENDER_FUZZ_WORK:?set ONERENDER_FUZZ_WORK to a scratch directory outside the checkout}"
case "$(readlink -f "$work")/" in
  "$root"/*) echo "ONERENDER_FUZZ_WORK must be outside the checkout" >&2; exit 2 ;;
esac
tools="${ONERENDER_FUZZ_TOOLS:-}"
host="${LIBFUZZER_DOTNET:-$tools/libfuzzer-dotnet}"
instrument="${SHARPFUZZ:-$tools/sharpfuzz/sharpfuzz}"

mode=fuzz
if [[ "${1:-}" == replay ]]; then
  mode=replay
  shift
fi
target="${1:?usage: run.sh <target> <seconds> [corpus dir] | run.sh replay <target> <file>...}"
shift

case "$target" in
  fontdata) maxlen=262144 ;;
  *) echo "unknown target '$target'" >&2; exit 2 ;;
esac

bin="$work/bin"
rm -rf "$bin"
mkdir -p "$bin"
"$dotnet_bin" build "$root/fuzz/OneRender.Fuzz/OneRender.Fuzz.csproj" -c Release -v quiet -clp:NoSummary -o "$bin"

export ONERENDER_FUZZ_FONTS="$root/testdata/fonts"

if [[ "$mode" == replay ]]; then
  "$dotnet_bin" "$bin/OneRender.Fuzz.dll" replay "$target" "$@"
  exit $?
fi

seconds="${1:?usage: run.sh <target> <seconds> [corpus dir]}"
shift
extra=""
if [[ $# -gt 0 && "${1:0:1}" != "-" ]]; then
  extra="$1"
  shift
fi
[[ -x "$host" ]] || { echo "libFuzzer host not found at '$host' (set ONERENDER_FUZZ_TOOLS)" >&2; exit 2; }
[[ -x "$instrument" ]] || { echo "sharpfuzz not found at '$instrument' (set ONERENDER_FUZZ_TOOLS)" >&2; exit 2; }

export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v "$dotnet_bin")")")}"
corpus="$work/corpus/$target"
findings="$work/findings/$target"
mkdir -p "$corpus" "$findings"

# Seeds come from the uninstrumented build (instrumented code needs libFuzzer's shared memory).
"$dotnet_bin" "$bin/OneRender.Fuzz.dll" seeds "$target" "$corpus" "$ONERENDER_FUZZ_FONTS" >/dev/null
[[ -n "$(ls -A "$corpus")" ]] || printf '\x00' >"$corpus/seed-empty"

# Instrument the library only; the harness needs no coverage.
"$instrument" "$bin/OneRender.dll" >/dev/null

dirs=("$corpus")
[[ -z "$extra" ]] || dirs+=("$extra")

ONERENDER_FUZZ_TARGET="$target" "$host" \
  --target_path="$(command -v "$dotnet_bin")" --target_arg="$bin/OneRender.Fuzz.dll" \
  -max_total_time="$seconds" -timeout="${FUZZ_TIMEOUT:-10}" -rss_limit_mb="${FUZZ_RSS_MB:-1024}" \
  -max_len="$maxlen" -artifact_prefix="$findings/" -print_final_stats=1 "$@" "${dirs[@]}"
