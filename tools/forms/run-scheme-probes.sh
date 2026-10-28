#!/usr/bin/env bash
# Probes Chrome's form controls under color-scheme and accent-color (a black box; see docs/forms.md, "Themed controls").
# Runs on the worker from the repository root; writes screenshots under out/probe/ and a tarball out/probe.tgz.
# Usage: tools/forms/run-scheme-probes.sh [extra shot-page.py specs...]
set -euo pipefail
export CHROME="${CHROME:-$(ls ~/onerender/cache/chrome/*/chrome-linux64/chrome | head -1)}" FONTCONFIG_FILE="${FONTCONFIG_FILE:-$HOME/onerender/cache/fonts-forms.conf}"
rm -rf out/probe pages; mkdir -p out/probe
V=(light=color-scheme:light dark=color-scheme:dark "ldlight=color-scheme:light dark" "lddark=color-scheme:light dark@dark" "normaldark=x:y@dark"
   "accent=accent-color:#d03060" "darkaccent=color-scheme:dark;accent-color:#d03060" "yellow=accent-color:#ffee00" "darkyellow=color-scheme:dark;accent-color:#ffee00"
   "onlydark=color-scheme:only dark" "lightonlypd=color-scheme:only light@dark" "darkpd=color-scheme:dark@dark" "unknown=color-scheme:foo"
   "darkfoo=color-scheme:dark foo" "darklightpd=color-scheme:dark light@dark" "lightdarkpd2=color-scheme:dark light")
for c in text checkbox checkbox-on radio radio-on button range select textarea input-button password placeholder listbox; do
  V+=("focus-$c~l=color-scheme:light" "focus-$c~d=color-scheme:dark")
done
SPECS=$(python3 tools/forms/scheme-shots.py pages "${V[@]}")
export WINDOW=1080x440
python3 tools/forms/shot-page.py out/probe $SPECS > /dev/null
# Hover and press at the middle of a few cells (cell (col,row) of scheme-shots.py is at 20 + 260 col, 20 + 44 row).
for m in hover press; do
  for pt in 290,72 550,200 810,30 40,30 546,70 286,114 860,116 30,72 810,292; do
    python3 tools/forms/shot-page.py out/probe pages/light.html@$m=$pt pages/dark.html@$m=$pt > /dev/null
  done
done
# The computed style of a hovered and of a pressed button, select and text field (the user-agent state colours).
for m in hover press; do
  for pt in 290,72 810,30 40,30 30,72; do
    for sch in light dark; do
      python3 tools/forms/shot-page.py out/probe "pages/$sch.html@$m=$pt@js=(function(){var r=[];document.querySelectorAll('button,select,input,textarea').forEach(function(e,i){if(e.matches(':hover')){var c=getComputedStyle(e);r.push(e.tagName+i+' bg='+c.backgroundColor+' bd='+c.borderTopColor+' fg='+c.color+' ol='+c.outlineColor+' '+c.outlineStyle+' '+c.outlineWidth)}});return r.join(' ; ')})()" | grep js: | sed "s|^|$sch $m $pt |"
    done
  done
done | tee out/probe/hover-styles.txt
P=testdata/forms-probes/scheme
WINDOW=950x600 python3 tools/forms/shot-page.py out/probe $P/accent-sweep.html $P/accent-sweep2.html $P/accent-sweep-dark.html $P/accent-sweep3-dark.html "$@" > /dev/null
WINDOW=900x400 python3 tools/forms/shot-page.py out/probe $P/nested.html $P/nested2.html $P/bodyscheme.html $P/rootbg.html $P/rootbg2.html > /dev/null
tar czf out/probe.tgz -C out probe
ls out/probe | wc -l
