#!/usr/bin/env bash
# Public-text check: tracked text files must not mention the people, tools or planning documents behind the project, or private
# paths and hosts. Run from anywhere inside the checkout; exit status 1 and the offending lines when something is found.
#
#   tools/ci/check-public-text.sh
#
# "user agent" (the browser term) is fine. Generated or third-party material (fonts, pictures, WPT copies, measurement
# results) is not scanned.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
files=$( (git ls-files; git ls-files --others --exclude-standard) | sort -u \
  | grep -vE '\.(ttf|otf|ttc|woff2?|png|jpe?g|bin|pdf)$|^results/|^testdata/wpt-results/|^tests/OneRender\.Tests/testdata/wpt-|^tools/ci/check-(public-text\.sh|packages\.py)$' || true)
status=0
# Words, any case. Lines that only use "user agent"/"user-agent" are removed before matching.
if echo "$files" | xargs grep -nIiE 'PLAN\.md|DESIGN\.md|/home/[a-z]' 2>/dev/null; then status=1; fi
if echo "$files" | xargs grep -nIE '\[(Agent default|Owner|PM)' 2>/dev/null; then status=1; fi
if echo "$files" | xargs grep -nIiE '\bagents?\b' 2>/dev/null | grep -viE 'user[- ]agent|string agent|agent = ' ; then status=1; fi
# Labels, case-sensitive: PM, question numbers and milestone numbers as standalone words.
if echo "$files" | xargs grep -nIE '\bPM\b|\bQ[0-9]\b|\bM[0-9]\b' 2>/dev/null | grep -vE 'Q[0-9]-20|<sub>|[A-Za-z]M[0-9]|M[0-9]+ [0-9-]' ; then status=1; fi
if [[ $status -ne 0 ]]; then
  echo "public-text check: found the lines above."
  exit 1
fi
echo "public-text check: clean."
