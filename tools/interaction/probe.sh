#!/usr/bin/env bash
# Records what Chrome (a black box) answers for the pages in testdata/interaction/pages: getBoundingClientRect, the offset*, client* and
# scroll* members, and elementFromPoint on a grid. The answers go to testdata/interaction/chrome/<page>.json, which the engine's tests
# compare against. Pages hold boxes only (no text), so no font set is involved. Each page runs in a 400x300 frame, so its viewport has
# exactly that size whatever the browser window around it is.
# Usage: probe.sh [chrome-executable]    (default: the pinned Chrome for Testing, fetched by tools/geomdiff/scripts/fetch-chrome.sh)
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
chrome="${1:-$("$repo/tools/geomdiff/scripts/fetch-chrome.sh" | tail -1)}"
version="$("$chrome" --version 2>/dev/null || echo unknown)"
profile="$(mktemp -d "${TMPDIR:-$HOME}/probe-profile.XXXXXX")"
trap 'rm -rf "$profile"' EXIT
mkdir -p "$repo/testdata/interaction/chrome"
for page in "$repo"/testdata/interaction/pages/*.html; do
  name="$(basename "$page" .html)"
  wrapper="$profile/$name-wrapper.html"
  cat > "$wrapper" <<EOF
<!DOCTYPE html><html><body style="margin:0"><iframe id="f" src="file://$page" style="border:0;display:block;width:400px;height:300px"></iframe>
<script>document.getElementById('f').addEventListener('load', function () { setTimeout(function () {
  var p = document.getElementById('f').contentDocument.getElementById('__probe');
  var pre = document.createElement('pre'); pre.id = '__probe'; pre.textContent = p ? p.textContent : ''; document.body.appendChild(pre);
}, 200); });</script></body></html>
EOF
  dom="$("$chrome" --headless=new --disable-gpu --no-sandbox --no-first-run --hide-scrollbars --force-device-scale-factor=1 \
      --allow-file-access-from-files --window-size=500,400 --user-data-dir="$profile" --virtual-time-budget=5000 \
      --dump-dom "file://$wrapper" 2>/dev/null)"
  printf '%s' "$dom" | python3 -c '
import sys, re, json, html
dom = sys.stdin.read()
m = re.search(r"<pre id=\"__probe\">(.*?)</pre>", dom, re.S)
if not m or not m.group(1).strip():
    sys.exit("no probe output for " + sys.argv[1])
data = json.loads(html.unescape(m.group(1)))
data["browser"] = sys.argv[2]
json.dump(data, open(sys.argv[3], "w"), indent=1, sort_keys=True)
print(sys.argv[1], len(data["elements"]), "elements", len(data["points"]), "points", data["viewport"])
' "$name" "$version" "$repo/testdata/interaction/chrome/$name.json"
done
