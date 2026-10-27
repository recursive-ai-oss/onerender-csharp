#!/usr/bin/env bash
# Captures Chrome's boxes for hand-written probe pages and prints them as a table.
# Usage: probe.sh <src-dir-with-pages> <name> [WxH]   (runs on the worker; state under $ONERENDER_HOME/forms/<name>)
set -euo pipefail
. "$(cd "$(dirname "$0")/../geomdiff/scripts" && pwd)/env.sh"
src="$(cd "${1:?src dir}" && pwd)"; name="${2:?name}"; vp="${3:-1280x800}"
root="$ONERENDER_HOME/forms/$name"; mkdir -p "$root"
fonts="$GEOMDIFF_REPO/src/OneRender.Fonts/fonts"
chrome=$("$GEOMDIFF_SCRIPTS/fetch-chrome.sh" | tail -1)
conf=$("$GEOMDIFF_SCRIPTS/fontconfig.sh" "$fonts" "$ONERENDER_HOME/cache/fonts-forms.conf")
if [ ! -f "$ONERENDER_ARTIFACTS/bin/OneRender.GeomDiff/release/onerender-geomdiff.dll" ] || [ "${REBUILD:-}" = 1 ]; then "$GEOMDIFF_SCRIPTS/build.sh"; fi
G="$GEOMDIFF_SCRIPTS/geomdiff.sh"
rm -rf "$root/snap" "$root/cap" "$root/styles"
"$G" snapshot-static --src "$src" --out "$root/snap" > /dev/null
GEOMDIFF_STYLES="${STYLES:-}" GEOMDIFF_STYLES_OUT="$root/styles" "$G" capture --snapshots "$root/snap" --out "$root/cap" --chrome "$chrome" --fontconfig "$conf" --viewport "$vp" --jobs 2 --work "$root/work" --no-sandbox > "$root/capture.log" 2>&1 || { tail -20 "$root/capture.log"; exit 1; }
python3 - "$root/cap" "$vp" <<'PY'
import sys,gzip,json,glob
for f in sorted(glob.glob(sys.argv[1]+'/*/'+sys.argv[2]+'.json.gz')):
    d=json.load(gzip.open(f))
    print('#',f.split('/')[-2],d['documentWidth'],d['documentHeight'])
    for e in d['elements']:
        b=e.get('box')
        print(e['path'],e['tag'],e.get('display',''),' '.join('%g'%x for x in b) if b else '-')
PY
if [ -n "${STYLES:-}" ]; then python3 - "$root/styles" "$STYLES" <<'PY'
import sys,json,glob
props=sys.argv[2].split(',')
for f in sorted(glob.glob(sys.argv[1]+'/*.styles.json')):
    d=json.load(open(f)); S=d['strings']; doc=d['documents'][0]; n=doc['nodes']; l=doc['layout']
    first=None
    for k,ni in enumerate(l['nodeIndex']):
        tag=S[n['nodeName'][ni]].lower()
        if tag in ('html','body','div','i','head','style','meta','label'): continue
        at=n['attributes'][ni]; a=' '.join(S[at[j]]+'='+S[at[j+1]] for j in range(0,len(at),2) if S[at[j]] in ('type','size','multiple','value'))
        st=l['styles'][k]
        print('STYLE',tag,a,' | ',' '.join(p+'='+(S[v] if v>=0 else '?') for p,v in zip(['display']+props,st)))
PY
fi
