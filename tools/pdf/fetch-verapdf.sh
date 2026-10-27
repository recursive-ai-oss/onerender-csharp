#!/usr/bin/env bash
# Fetches veraPDF (PDF/UA and PDF/A validator) and a user-local Java runtime into
#   ${ONERENDER_HOME:-$HOME/onerender}/u25/tools/
# veraPDF is dual-licensed GPLv3+/MPLv2+. It is used only as a separate program that validates files we write; it is never
# linked, committed or redistributed. Both downloads are checked against pinned SHA-256 values. Safe to re-run.
set -euo pipefail
root="${ONERENDER_HOME:-$HOME/onerender}/u25/tools"
vera_ver=1.30.3
vera_url="https://software.verapdf.org/releases/1.30/verapdf-greenfield-$vera_ver-installer.zip"
vera_sha=b512bf24945503a33fc10738ecffc1f2dccaaf8593a448e94ee9ac86a9559a59
jre_url="https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13%2B9/OpenJDK21U-jre_x64_linux_hotspot_21.0.13_9.tar.gz"
jre_sha=a535a1df9c6284d9618872984e026ae0352d1f7e9e0605425c0e6114f47351dd
mkdir -p "$root"

fetch() { # url sha256 file
  if [ ! -f "$3" ]; then
    curl -fsSL --retry 3 -o "$3.part" "$1"
    mv "$3.part" "$3"
  fi
  echo "$2  $3" | sha256sum -c --quiet -
}

if [ ! -x "$root/jre/bin/java" ]; then
  fetch "$jre_url" "$jre_sha" "$root/jre.tar.gz"
  rm -rf "$root/jre"; mkdir -p "$root/jre"
  tar -xzf "$root/jre.tar.gz" --strip-components=1 -C "$root/jre"
fi

if [ ! -x "$root/verapdf/verapdf" ]; then
  fetch "$vera_url" "$vera_sha" "$root/verapdf-installer.zip"
  rm -rf "$root/installer" "$root/verapdf"; mkdir -p "$root/installer"
  unzip -q "$root/verapdf-installer.zip" -d "$root/installer"
  cat > "$root/auto-install.xml" <<X
<?xml version="1.0" encoding="UTF-8" standalone="no"?>
<AutomatedInstallation langpack="eng">
  <com.izforge.izpack.panels.htmlhello.HTMLHelloPanel id="welcome"/>
  <com.izforge.izpack.panels.target.TargetPanel id="install_dir"><installpath>$root/verapdf</installpath></com.izforge.izpack.panels.target.TargetPanel>
  <com.izforge.izpack.panels.packs.PacksPanel id="sdk_pack_select">
    <pack index="0" name="veraPDF Runtime" selected="true"/>
    <pack index="1" name="veraPDF Mac and *nix Scripts" selected="true"/>
    <pack index="2" name="veraPDF Validation model" selected="true"/>
    <pack index="3" name="veraPDF Documentation" selected="false"/>
    <pack index="4" name="veraPDF Sample Plugins" selected="false"/>
  </com.izforge.izpack.panels.packs.PacksPanel>
  <com.izforge.izpack.panels.install.InstallPanel id="install"/>
  <com.izforge.izpack.panels.finish.FinishPanel id="finish"/>
</AutomatedInstallation>
X
  "$root/jre/bin/java" -jar "$root/installer/verapdf-greenfield-$vera_ver/verapdf-izpack-installer-$vera_ver.jar" "$root/auto-install.xml"
  rm -rf "$root/installer"
fi

export JAVA_HOME="$root/jre" PATH="$root/jre/bin:$PATH"
"$root/verapdf/verapdf" --version
echo "$root/verapdf/verapdf"
