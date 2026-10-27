#!/usr/bin/env bash
# Chrome vs engine boxes for CSS variants of one page: run-variants.sh <page-src-dir> <viewport> <path-prefix-regex> 'css'...
# Prints, per variant, the Chrome and engine boxes of the elements whose path matches the regex. Runs on the worker.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
. "$here/../geomdiff/scripts/env.sh"
src="$1"; vp="$2"; re="$3"; shift 3
work="$ONERENDER_HOME/forms/var"; rm -rf "$work"; mkdir -p "$work"
python3 "$here/variants.py" "$src" "$work/pages" "$@" > "$work/list.txt"
cat "$work/list.txt"
"$here/probe.sh" "$work/pages" var-probe "$vp" > "$work/chrome.txt" 2>&1 || { tail -5 "$work/chrome.txt"; exit 1; }
"$GEOMDIFF_SCRIPTS/geomdiff.sh" engine-table --snapshots "$ONERENDER_HOME/forms/var-probe/snap" --viewport "$vp" > "$work/ours.txt"
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
