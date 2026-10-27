#!/usr/bin/env bash
# Build, instrument and run the fuzz target under libFuzzer.
#
#   fuzz/run.sh <target> <seconds> [corpus dir] [extra libFuzzer flags...]
#   fuzz/run.sh replay <target> <file>...      # run saved inputs once, without libFuzzer (reproduce a finding)
#
# Targets: displaylist, fontdata, colorglyphs, grid, flow, table, effects, incremental, paged, pdf   (see fuzz/README.md)
#
# Environment (nothing is baked into the script):
#   ONERENDER_FUZZ_WORK    required: where builds, corpora and findings go. Keep it outside the checkout.
#   ONERENDER_FUZZ_TOOLS   directory holding `fuzz/libfuzzer-dotnet` (the libFuzzer host) and `sharpfuzz/sharpfuzz`
#                        (the instrumenter). LIBFUZZER_DOTNET and SHARPFUZZ override the two paths individually.
#   ONERENDER_ARTIFACTS    build output root, as for every other project in the repo
#   DOTNET               the dotnet executable (default: dotnet from PATH)
#   FUZZ_TIMEOUT         per-input timeout in seconds (default 10)
#   FUZZ_RSS_MB          memory limit in MB (default 1024)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_bin="${DOTNET:-dotnet}"
work="${ONERENDER_FUZZ_WORK:?set ONERENDER_FUZZ_WORK to a scratch directory outside the checkout}"
case "$(readlink -f "$work")/" in
  "$root"/*) echo "ONERENDER_FUZZ_WORK must be outside the checkout" >&2; exit 2 ;;
esac
tools="${ONERENDER_FUZZ_TOOLS:-}"
host="${LIBFUZZER_DOTNET:-$tools/fuzz/libfuzzer-dotnet}"
instrument="${SHARPFUZZ:-$tools/sharpfuzz/sharpfuzz}"

mode=fuzz
if [[ "${1:-}" == replay ]]; then
  mode=replay
  shift
fi
target="${1:?usage: run.sh <target> <seconds> [corpus dir] | run.sh replay <target> <file>...}"
shift

# Maximum input length per target.
case "$target" in
  displaylist|pdf) maxlen=4096 ;;
  fontdata|colorglyphs) maxlen=262144 ;;
  grid) maxlen=2048 ;;
  flow) maxlen=2048 ;;
  table) maxlen=2048 ;;
  effects) maxlen=2048 ;;
  incremental) maxlen=2048 ;;
  paged) maxlen=2048 ;;
  *) echo "unknown target '$target'" >&2; exit 2 ;;
esac

export ONERENDER_FUZZ_FONTS="$root/testdata/fonts"
export ONERENDER_FUZZ_COLOR_FONT="$root/src/OneRender.Fonts.Emoji/fonts/NotoColorEmoji_WindowsCompatible.ttf"
bin="$work/bin"
rm -rf "$bin"
mkdir -p "$bin"

# Build once per run: release, then instrument only the library assembly (the harness itself needs no coverage).
"$dotnet_bin" build "$root/fuzz/OneRender.Fuzz/OneRender.Fuzz.csproj" -c Release -v quiet -clp:NoSummary -o "$bin"


if [[ "$mode" == replay ]]; then
  ONERENDER_FUZZ_WORK="$work" "$dotnet_bin" "$bin/OneRender.Fuzz.dll" replay "$target" "$@"
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
"$dotnet_bin" "$bin/OneRender.Fuzz.dll" seeds "$target" "$corpus" >/dev/null
[[ -n "$(ls -A "$corpus")" ]] || printf '\x00' >"$corpus/seed-empty"

"$instrument" "$bin/OneRender.dll" >/dev/null
"$instrument" "$bin/OneRender.Image.dll" >/dev/null
"$instrument" "$bin/OneRender.Pdf.dll" >/dev/null

# An extra seed directory is read-only for libFuzzer: new finds go to $corpus.
dirs=("$corpus")
[[ -z "$extra" ]] || dirs+=("$extra")

# Run. -max_total_time bounds the run; crashes, hangs and out-of-memory inputs are written to the findings folder.
ONERENDER_FUZZ_TARGET="$target" ONERENDER_FUZZ_WORK="$work" "$host" \
  --target_path="$(command -v "$dotnet_bin")" --target_arg="$bin/OneRender.Fuzz.dll" \
  -max_total_time="$seconds" -timeout="${FUZZ_TIMEOUT:-10}" -rss_limit_mb="${FUZZ_RSS_MB:-1024}" \
  -max_len="$maxlen" -artifact_prefix="$findings/" -print_final_stats=1 "$@" "${dirs[@]}"
