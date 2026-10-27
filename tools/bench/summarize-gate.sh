#!/usr/bin/env bash
# Reads the BenchmarkDotNet markdown reports under a gate output folder and prints the verdicts and the comparison table as markdown.
# usage: summarize-gate.sh <out-dir> <gate|smoke>
set -u
OUT="$1"; MODE="${2:-gate}"
TMP="$(mktemp -d "${TMPDIR:-$HOME}/gate.XXXXXX")"; trap 'rm -rf "$TMP"' EXIT

# rows <project>: "Class.Method<TAB>mean in microseconds<TAB>allocated bytes" for every row of the project's reports.
rows() {
  for f in "$OUT/$1"/BenchmarkDotNet.Artifacts/results/*-report-github.md; do
    [ -e "$f" ] || continue
    class=$(basename "$f" | sed -E 's/-report-github\.md$//; s/^.*\.//')
    awk -F'|' -v class="$class" '
      function trim(s) { gsub(/^ +| +$/, "", s); return s }
      function us(s,   a, v) { split(trim(s), a, " "); v = a[1]; gsub(/,/, "", v);
        return (a[2] == "ns") ? v / 1000 : (a[2] == "ms") ? v * 1000 : (a[2] == "s") ? v * 1000000 : v }
      function bytes(s,   a, v) { s = trim(s); if (s == "-" || s == "0 B") return 0; split(s, a, " "); v = a[1]; gsub(/,/, "", v);
        return (a[2] == "KB") ? v * 1024 : (a[2] == "MB") ? v * 1048576 : v }
      /^\| Method/ { for (i = 2; i < NF; i++) { h = trim($i); if (h == "Method") m = i; if (h == "Mean") mean = i; if (h == "Allocated") al = i } next }
      /^\|-/ { next }
      /^\| / && m { printf "%s.%s\t%.3f\t%d\n", class, trim($m), us($mean), bytes($al) }' "$f"
  done
}

A="$TMP/a.tsv"; B="$TMP/b.tsv"
rows OneRender.Benchmarks > "$A"; rows OneRender.Benchmarks.OneGameUi > "$B"
if [ ! -s "$A" ] || [ ! -s "$B" ]; then echo "no benchmark results found under $OUT"; exit 1; fi

echo "# Interactive gate summary ($MODE)"
if [ "$MODE" = smoke ]; then echo; echo "**Smoke run: no lock, short job. Not publishable; it only proves the harness runs.**"; fi
echo; echo "## Bar: each change at most 1 ms and 0 B allocated"; echo
echo "| Benchmark | Mean (us) | Allocated (B) | Verdict |"; echo "|---|---:|---:|---|"
fail=0
while IFS=$'\t' read -r name mean alloc; do
  v=PASS
  if awk -v m="$mean" 'BEGIN { exit !(m > 1000) }'; then v=FAIL; fi
  if [ "$alloc" != 0 ]; then v=FAIL; fi
  if [ "$v" = FAIL ]; then fail=1; fi
  echo "| $name | $mean | $alloc | $v |"
done < "$A"

echo; echo "## OneGame.UI and the engine on the same changes"; echo
echo "| Change | OneGame.UI mean (us) | OneGame.UI B | engine mean (us) | engine B | engine no slower |"; echo "|---|---:|---:|---:|---:|---|"
same=1
for change in IdleFrame HoverRestyle TextChange SubtreeRelayout; do
  og=$(grep -P "^OneGameUiBenchmarks\.$change\t" "$B" | head -1); en=$(grep -P "^IncrementalBenchmarks\.$change\t" "$B" | head -1)
  if [ -z "$og" ] || [ -z "$en" ]; then continue; fi
  om=$(echo "$og" | cut -f2); ob=$(echo "$og" | cut -f3); em=$(echo "$en" | cut -f2); eb=$(echo "$en" | cut -f3)
  ok=yes
  if awk -v e="$em" -v o="$om" 'BEGIN { exit !(e > o) }'; then ok=NO; same=0; fi
  echo "| $change | $om | $ob | $em | $eb | $ok |"
done
echo; echo "Interactive bar: $([ $fail = 0 ] && echo PASS || echo FAIL)"
echo "Engine no slower than OneGame.UI on every change: $([ $same = 1 ] && echo yes || echo NO)"
exit 0
