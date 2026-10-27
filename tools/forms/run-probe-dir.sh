#!/usr/bin/env bash
# Chrome and engine boxes for hand-written pages side by side: run-probe-dir.sh <src-dir-with-pages> [WxH] [path-regex]. Runs on the worker.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
. "$here/../geomdiff/scripts/env.sh"
src="$1"; vp="${2:-1280x800}"; re="${3:-.}"
work="$ONERENDER_HOME/forms/pd"; rm -rf "$work"; mkdir -p "$work"
"$here/probe.sh" "$src" pd-probe "$vp" > "$work/chrome.txt" 2>&1 || { tail -5 "$work/chrome.txt"; exit 1; }
"$GEOMDIFF_SCRIPTS/geomdiff.sh" engine-table --snapshots "$ONERENDER_HOME/forms/pd-probe/snap" --viewport "$vp" > "$work/ours.txt"
python3 - "$work" "$re" <<'PY'
import sys,re
w,rx=sys.argv[1],re.compile(sys.argv[2])
def load(f):
    d={};page=None
    for l in open(f):
        l=l.rstrip('\n')
        if l.startswith('#'):
            page=l.split()[1]; continue
        p=l.split()
        if page and p and rx.match(p[0]): d.setdefault(page,[]).append(l)
    return d
c=load(w+'/chrome.txt'); o=load(w+'/ours.txt')
for k in sorted(set(c)|set(o)):
    print('==',k)
    for l in c.get(k,[]): print(' chrome',l)
    for l in o.get(k,[]): print(' ours  ',l)
PY
