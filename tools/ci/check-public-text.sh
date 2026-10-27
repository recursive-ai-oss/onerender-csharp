#!/usr/bin/env bash
# Public-text check: tracked text files must not mention planning documents, decision tags or private paths and hosts. Run from anywhere inside the checkout; exit status 1 and the offending lines when something is found.
#
#   tools/ci/check-public-text.sh
#   ONERENDER_PRIVATE_PATTERNS=/path/to/patterns.txt tools/ci/check-public-text.sh   # with extra patterns
#
# Generated or third-party material (fonts, pictures, WPT copies, measurement
# results) is not scanned.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
files=$( (git ls-files; git ls-files --others --exclude-standard) | sort -u \
  | grep -vE '\.(ttf|otf|ttc|woff2?|png|jpe?g|bin|pdf)$|^results/|^testdata/wpt-results/|^tests/OneRender\.Tests/testdata/wpt-|^tools/ci/check-(public-text\.sh|packages\.py)$' || true)
status=0
# Generic patterns: home-directory paths, decision-tag syntax and references to planning documents.
if echo "$files" | xargs grep -nIE '/home/[A-Za-z]|/Users/[A-Za-z]|PLAN\.md|DESIGN\.md|\[(Agent default|Owner|PM)\b' 2>/dev/null; then status=1; fi
# Further patterns (names of hosts, people and internal tools) come from an optional file kept outside the repository, one
# extended regular expression per line, matched case-insensitively; "user agent" (the browser term) is not a hit.
if [[ -n "${ONERENDER_PRIVATE_PATTERNS:-}" ]]; then
  patterns=$(grep -vE '^[[:space:]]*(#|$)' "$ONERENDER_PRIVATE_PATTERNS" || true)
  if [[ -n $patterns ]] && echo "$files" | xargs grep -nIiE -e "$(echo "$patterns" | paste -sd'|')" 2>/dev/null | grep -viE 'user[- ]agent|string agent|agent = '; then status=1; fi
fi
if [[ $status -ne 0 ]]; then
  echo "public-text check: found the lines above."
  exit 1
fi
echo "public-text check: clean."
