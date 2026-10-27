#!/usr/bin/env bash
# Writes a fontconfig file that exposes exactly the font files under <fonts-dir> and nothing else, with the same family
# mapping the fonts package applies: sans-serif, system-ui, ui-sans-serif, Arial, Helvetica -> Liberation Sans;
# serif, ui-serif, Times, Times New Roman, cursive, fantasy -> Liberation Serif; monospace, ui-monospace, Courier,
# Courier New -> Liberation Mono; Calibri -> Carlito; Cambria -> Caladea. Hinting and sub-pixel rendering are off.
# <fonts-dir> is either the cache from fetch-fonts.sh or the fonts folder of the fonts package.
# Usage: fontconfig.sh <fonts-dir> <out.conf> [cache-dir]      prints the out.conf path
set -euo pipefail
fonts="$(cd "${1:?fonts dir}" && pwd)"
out="${2:?output file}"
home="${ONERENDER_HOME:-$HOME/onerender}"
cache="${3:-$home/cache/fontconfig-cache}"
mkdir -p "$cache" "$(dirname "$out")"
# Identifies the font set: sha256 over the sorted "<relative path> <file sha256>" lines of every font file.
set_id=$(cd "$fonts" && find . -type f \( -name '*.ttf' -o -name '*.otf' \) | sort | while read -r f; do printf '%s %s\n' "${f#./}" "$(sha256sum "$f" | cut -d' ' -f1)"; done | sha256sum | cut -c1-16)
count=$(find "$fonts" -type f \( -name '*.ttf' -o -name '*.otf' \) | wc -l)
map() { # family-as-written target
  printf '  <match target="pattern">\n    <test qual="any" name="family" compare="eq"><string>%s</string></test>\n    <edit name="family" mode="assign" binding="strong"><string>%s</string></edit>\n  </match>\n' "$1" "$2"
}
{
  echo '<?xml version="1.0"?>'
  echo '<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">'
  echo "<!-- fontset: $set_id ($count files) -->"
  echo '<fontconfig>'
  echo "  <dir>$fonts</dir>"
  echo "  <cachedir>$cache</cachedir>"
  for n in sans-serif sans system-ui ui-sans-serif Arial Helvetica; do map "$n" "Liberation Sans"; done
  for n in serif ui-serif Times 'Times New Roman' cursive fantasy; do map "$n" "Liberation Serif"; done
  for n in monospace mono ui-monospace Courier 'Courier New'; do map "$n" "Liberation Mono"; done
  map Calibri Carlito
  map Cambria Caladea
  # A pattern with no family, or one nothing above names (what system-ui becomes in Chrome), ends at Liberation Sans.
  echo '  <match target="pattern"><edit name="family" mode="append_last"><string>Liberation Sans</string></edit></match>'
  cat <<'XML'
  <match target="font">
    <edit name="antialias" mode="assign"><bool>true</bool></edit>
    <edit name="hinting" mode="assign"><bool>false</bool></edit>
    <edit name="hintstyle" mode="assign"><const>hintnone</const></edit>
    <edit name="autohint" mode="assign"><bool>false</bool></edit>
    <edit name="rgba" mode="assign"><const>none</const></edit>
    <edit name="lcdfilter" mode="assign"><const>lcdnone</const></edit>
  </match>
</fontconfig>
XML
} > "$out"
echo "$out"
