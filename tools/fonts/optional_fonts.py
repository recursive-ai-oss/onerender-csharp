#!/usr/bin/env python3
"""Fetches, verifies and lays out the opt-in fonts packages (Noto scripts, CJK, emoji).

  optional_fonts.py stage  [--cache DIR]   download the pinned upstream archives, copy the font and licence files out
                                           unchanged, and write each package's fonts/MANIFEST.tsv, project file, registration
                                           class and build files under src/
  optional_fonts.py verify [--cache DIR]   re-download everything and check every committed byte against the manifest and
                                           the upstream file

Every shipped file is a member of an upstream release archive (or a file at an upstream tag) copied byte for byte: nothing is
subset, renamed inside the font or re-saved. Archive and file hashes are recorded in the manifests; `stage` is the only place
the pins live. The download cache defaults to a temporary folder next to the repository's artifacts, never inside the repository.
"""
import argparse
import hashlib
import io
import os
import sys
import urllib.request
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "src")
PREFIX = "RecursiveAI."

NOTO = "https://github.com/notofonts/{repo}/releases/download/{tag}/{tag}.zip"

# (package suffix, notofonts repo, [(release tag, family name)]).  The suffix names the package
# RecursiveAI.OneRender.Fonts.Noto.<suffix>; it must match src/OneRender/Text/FontPackages.cs.
# Each release is the newest Noto Sans release for the script (Noto Serif Tibetan is the only Tibetan family published).
SCRIPTS = [
    ("Armenian", "armenian", [("NotoSansArmenian-v2.008", "Noto Sans Armenian")]),
    ("Hebrew", "hebrew", [("NotoSansHebrew-v3.001", "Noto Sans Hebrew")]),
    ("Arabic", "arabic", [("NotoSansArabic-v2.013", "Noto Sans Arabic")]),
    ("Syriac", "syriac", [("NotoSansSyriac-v3.000", "Noto Sans Syriac")]),
    ("Thaana", "thaana", [("NotoSansThaana-v3.001", "Noto Sans Thaana")]),
    ("Nko", "nko", [("NotoSansNKo-v2.004", "Noto Sans NKo")]),
    ("Devanagari", "devanagari", [("NotoSansDevanagari-v2.007", "Noto Sans Devanagari")]),
    ("Bengali", "bengali", [("NotoSansBengali-v3.011", "Noto Sans Bengali")]),
    ("Gurmukhi", "gurmukhi", [("NotoSansGurmukhi-v2.004", "Noto Sans Gurmukhi")]),
    ("Gujarati", "gujarati", [("NotoSansGujarati-v2.106", "Noto Sans Gujarati")]),
    ("Oriya", "oriya", [("NotoSansOriya-v2.007", "Noto Sans Oriya")]),
    ("Tamil", "tamil", [("NotoSansTamil-v2.004", "Noto Sans Tamil")]),
    ("Telugu", "telugu", [("NotoSansTelugu-v2.005", "Noto Sans Telugu")]),
    ("Kannada", "kannada", [("NotoSansKannada-v2.006", "Noto Sans Kannada")]),
    ("Malayalam", "malayalam", [("NotoSansMalayalam-v2.104", "Noto Sans Malayalam")]),
    ("Sinhala", "sinhala", [("NotoSansSinhala-v3.000", "Noto Sans Sinhala")]),
    ("Thai", "thai", [("NotoSansThai-v2.002", "Noto Sans Thai")]),
    ("Lao", "lao", [("NotoSansLao-v2.003", "Noto Sans Lao")]),
    ("Tibetan", "tibetan", [("NotoSerifTibetan-v2.103", "Noto Serif Tibetan")]),
    ("Myanmar", "myanmar", [("NotoSansMyanmar-v2.107", "Noto Sans Myanmar")]),
    ("Georgian", "georgian", [("NotoSansGeorgian-v2.005", "Noto Sans Georgian")]),
    ("Ethiopic", "ethiopic", [("NotoSansEthiopic-v2.102", "Noto Sans Ethiopic")]),
    ("Cherokee", "cherokee", [("NotoSansCherokee-v2.001", "Noto Sans Cherokee")]),
    ("Khmer", "khmer", [("NotoSansKhmer-v2.004", "Noto Sans Khmer")]),
    ("Mongolian", "mongolian", [("NotoSansMongolian-v3.103", "Noto Sans Mongolian")]),
    ("Balinese", "balinese", [("NotoSansBalinese-v2.006", "Noto Sans Balinese")]),
    ("Javanese", "javanese", [("NotoSansJavanese-v2.005", "Noto Sans Javanese")]),
    ("Symbols", "symbols", [("NotoSansSymbols-v2.003", "Noto Sans Symbols"), ("NotoSansSymbols2-v2.008", "Noto Sans Symbols 2")]),
    ("Math", "math", [("NotoSansMath-v3.000", "Noto Sans Math")]),
]

CJK_URL = "https://github.com/notofonts/noto-cjk/releases/download/Sans2.004/03_NotoSansCJK-OTC.zip"
# Faces 0..4 of each collection are the proportional Sans faces by region; 5..9 are the Mono faces, which are not shipped.
CJK_REGIONS = ["JP", "KR", "SC", "TC", "HK"]

EMOJI_TAG = "v2.051"
EMOJI_URL = "https://raw.githubusercontent.com/googlefonts/noto-emoji/" + EMOJI_TAG + "/fonts/{name}"


def sha256(data):
    return hashlib.sha256(data).hexdigest()


class Fetcher:
    def __init__(self, cache):
        self.cache = cache
        os.makedirs(cache, exist_ok=True)

    def get(self, url):
        path = os.path.join(self.cache, sha256(url.encode())[:20])
        plain = os.path.join(self.cache, url.rsplit("/", 1)[1])
        if not os.path.exists(path) and url.endswith(".zip") and os.path.exists(plain):
            path = plain  # a download kept under its own name
        if not os.path.exists(path):
            req = urllib.request.Request(url, headers={"User-Agent": "onerender-fonts-tool"})
            with urllib.request.urlopen(req, timeout=300) as r, open(path + ".part", "wb") as f:
                while True:
                    b = r.read(1 << 20)
                    if not b:
                        break
                    f.write(b)
            os.replace(path + ".part", path)
        with open(path, "rb") as f:
            return f.read()


class Package:
    """What one package ships: files to copy (name -> bytes, with provenance) and the faces to register."""

    def __init__(self, suffix, cls, description, priority):
        self.suffix = suffix            # Noto.Arabic, Cjk, Emoji
        self.cls = cls                  # NotoArabicFonts
        self.description = description
        self.priority = priority
        self.archives = []              # (url, sha256)
        self.files = []                 # (name, url, member or "-", sha256, bytes)
        self.faces = []                 # (family, weight, file, faceIndex)
        self.notes = []

    @property
    def project(self):
        return "OneRender.Fonts." + self.suffix

    @property
    def namespace(self):
        return "OneRender.Fonts.Noto" if self.suffix.startswith("Noto.") else "OneRender.Fonts." + self.suffix

    @property
    def package_id(self):
        return PREFIX + self.project


def build_scripts(fetch):
    packages = []
    for suffix, repo, releases in SCRIPTS:
        p = Package("Noto." + suffix, "Noto" + suffix + "Fonts",
                    "Noto " + suffix + " fonts for OneRender: the " + ", ".join(f for _, f in releases) + " release files, embedded byte-for-byte.", 0)
        licences = []
        for tag, family in releases:
            url = NOTO.format(repo=repo, tag=tag)
            blob = fetch.get(url)
            p.archives.append((url, sha256(blob)))
            z = zipfile.ZipFile(io.BytesIO(blob))
            base = tag.split("-v")[0]
            for style, weight in (("Regular", 400), ("Bold", 700)):
                member = base + "/unhinted/ttf/" + base + "-" + style + ".ttf"
                if member in z.namelist():
                    data = z.read(member)
                    name = base + "-" + style + ".ttf"
                    p.files.append((name, url, member, sha256(data), data))
                    p.faces.append((family, weight, name, 0))
            lic = [n for n in z.namelist() if n.endswith("OFL.txt")]
            if len(lic) != 1:
                raise SystemExit("expected one OFL.txt in " + tag + ", got " + repr(lic))
            licences.append((url, lic[0], z.read(lic[0])))
        # One OFL.txt per package. Two archives (Symbols) must carry identical licence text; otherwise stop.
        if len({sha256(d) for _, _, d in licences}) != 1:
            raise SystemExit("licence texts differ inside package " + suffix)
        url, member, data = licences[0]
        p.files.append(("OFL.txt", url, member, sha256(data), data))
        packages.append(p)
    return packages


def build_cjk(fetch):
    p = Package("Cjk", "CjkFonts",
                "Noto Sans CJK for OneRender: Regular and Bold collections holding the Japanese, Korean, Simplified Chinese, Traditional Chinese and Hong Kong faces, embedded byte-for-byte.", 0)
    blob = fetch.get(CJK_URL)
    p.archives.append((CJK_URL, sha256(blob)))
    z = zipfile.ZipFile(io.BytesIO(blob))
    for style, weight in (("Regular", 400), ("Bold", 700)):
        name = "NotoSansCJK-" + style + ".ttc"
        data = z.read(name)
        p.files.append((name, CJK_URL, name, sha256(data), data))
        for i, region in enumerate(CJK_REGIONS):
            p.faces.append(("Noto Sans CJK " + region, weight, name, i))
    data = z.read("LICENSE")
    p.files.append(("OFL.txt", CJK_URL, "LICENSE", sha256(data), data))
    return p


def build_emoji(fetch):
    p = Package("Emoji", "EmojiFonts",
                "Noto Color Emoji for OneRender: the upstream CBDT/CBLC color bitmap font (the build with an empty glyf table, which every TrueType reader accepts), embedded byte-for-byte.", 1)
    for name, saved in (("NotoColorEmoji_WindowsCompatible.ttf", "NotoColorEmoji_WindowsCompatible.ttf"), ("LICENSE", "OFL.txt")):
        url = EMOJI_URL.format(name=name)
        data = fetch.get(url)
        p.files.append((saved, url, "-", sha256(data), data))
    p.faces.append(("Noto Color Emoji", 400, "NotoColorEmoji_WindowsCompatible.ttf", 0))
    return p


def all_packages(fetch):
    return build_scripts(fetch) + [build_cjk(fetch), build_emoji(fetch)]


# ---- generated files ----------------------------------------------------------------------------------------------------

CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <Description>{description}</Description>
    <PackageLicenseExpression>(MIT OR Apache-2.0) AND OFL-1.1</PackageLicenseExpression>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../OneRender.Fonts/OneRender.Fonts.csproj" />
  </ItemGroup>

  <!-- The font files, licence and manifest are embedded unmodified; the logical names are fixed so lookup never depends on the build host. -->
  <ItemGroup>
    <EmbeddedResource Include="fonts/*" LogicalName="fonts/%(Filename)%(Extension)" />
  </ItemGroup>

  <!-- The licence also travels as a plain file in the package. -->
  <ItemGroup>
    <None Include="fonts/OFL.txt" Pack="true" PackagePath="licenses/" />
  </ItemGroup>

  <!-- An application that references the package registers it at start-up: the build adds a module initializer to the application. -->
  <ItemGroup>
    <Compile Remove="build/**" />
    <None Include="build/$(PackageId).targets" Pack="true" PackagePath="buildTransitive/" />
    <None Include="build/$(PackageId).AutoRegister.cs" Pack="true" PackagePath="buildTransitive/" />
  </ItemGroup>

</Project>
"""

TARGETS = """<Project>

  <!-- Registers the package's fonts with the engine when the application starts (see OptionalFonts). -->
  <ItemGroup Condition="'$(Language)' == 'C#'">
    <Compile Include="$(MSBuildThisFileDirectory){package_id}.AutoRegister.cs" Visible="false" />
  </ItemGroup>

</Project>
"""

AUTOREG = """// <auto-generated>Added to the application by {package_id}; registers the package's fonts at start-up.</auto-generated>
#nullable enable
namespace OneRenderFontPackages
{{
    internal static class {unique}
    {{
        [global::System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Register() => global::{namespace}.{cls}.Register();
    }}
}}
"""

CLASS = """using OneRender.Fonts;

namespace {namespace};

/// <summary>
/// {summary}
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class {cls}
{{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package {{ get; }} = new(
        typeof({cls}).Assembly,
        "{package_id}",
        {priority},
        [
{faces}
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}}
"""


def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    mode = "wb" if isinstance(data, bytes) else "w"
    with open(path, mode, **({} if isinstance(data, bytes) else {"newline": "\n"})) as f:
        f.write(data)


def stage_package(p):
    d = os.path.join(SRC, p.project)
    fonts = os.path.join(d, "fonts")
    for name, url, member, digest, data in p.files:
        write(os.path.join(fonts, name), data)
    lines = [
        "# Upstream provenance of every shipped file. Columns are tab-separated.",
        "# archive<TAB>url<TAB>sha256   a download that holds several files",
        "# file<TAB>path<TAB>url<TAB>member<TAB>sha256   path is relative to this folder; member is the name inside the archive, or - for a direct download",
        "# Tools: tools/fonts/optional_fonts.py verify",
    ]
    for url, digest in p.archives:
        lines.append("archive\t" + url + "\t" + digest)
    for name, url, member, digest, data in sorted(p.files, key=lambda f: f[0]):
        lines.append("file\t" + name + "\t" + url + "\t" + member + "\t" + digest)
    write(os.path.join(fonts, "MANIFEST.tsv"), "\n".join(lines) + "\n")
    write(os.path.join(d, p.project + ".csproj"), CSPROJ.format(description=p.description))
    faces = []
    sizes = {n: len(data) for n, _, _, _, data in p.files}
    for family, weight, name, index in p.faces:
        extra = ", faceIndex: " + str(index) if index else ""
        faces.append('            new(typeof({cls}).Assembly, "{f}", {w}, FontSlant.Normal, "{n}", {l}{e}),'.format(
            cls=p.cls, f=family, w=weight, n=name, l=sizes[name], e=extra))
    summary = p.description.replace("&", "&amp;")
    write(os.path.join(d, p.cls + ".cs"), CLASS.format(
        namespace=p.namespace, cls=p.cls, summary=summary, package_id=p.package_id, priority=p.priority, faces="\n".join(faces)))
    write(os.path.join(d, "build", p.package_id + ".targets"), TARGETS.format(package_id=p.package_id))
    write(os.path.join(d, "build", p.package_id + ".AutoRegister.cs"), AUTOREG.format(
        package_id=p.package_id, unique=p.cls + "Registration", namespace=p.namespace, cls=p.cls))


def cmd_stage(args):
    fetch = Fetcher(args.cache)
    for p in all_packages(fetch):
        stage_package(p)
        total = sum(len(d) for *_, d in p.files)
        print("staged {:<32} {:>3} files {:>12,} bytes".format(p.project, len(p.files), total))


def cmd_verify(args):
    fetch = Fetcher(args.cache)
    fail = 0
    for p in all_packages(fetch):
        fonts = os.path.join(SRC, p.project, "fonts")
        manifest = {}
        archives = {}
        with open(os.path.join(fonts, "MANIFEST.tsv"), encoding="utf-8") as f:
            for line in f:
                if line.startswith("#") or not line.strip():
                    continue
                cols = line.rstrip("\n").split("\t")
                if cols[0] == "archive":
                    archives[cols[1]] = cols[2]
                else:
                    manifest[cols[1]] = cols[4]
        for url, digest in p.archives:
            if archives.get(url) != digest:
                print("FAIL archive", url, "upstream", digest, "manifest", archives.get(url))
                fail = 1
        for name, url, member, digest, data in p.files:
            path = os.path.join(fonts, name)
            if manifest.get(name) != digest:
                print("FAIL", p.project, name, "manifest differs from upstream")
                fail = 1
            elif not os.path.exists(path) or open(path, "rb").read() != data:
                print("FAIL", p.project, name, "committed bytes differ from upstream")
                fail = 1
            else:
                print("ok  ", p.project, name, digest)
        on_disk = {n for n in os.listdir(fonts) if n != "MANIFEST.tsv"}
        extra = on_disk - {n for n, *_ in p.files}
        if extra:
            print("FAIL", p.project, "files not in the manifest:", sorted(extra))
            fail = 1
    return fail


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["stage", "verify"])
    ap.add_argument("--cache", default=os.path.join(ROOT, "artifacts", "font-cache"))
    args = ap.parse_args()
    sys.exit(cmd_stage(args) if args.command == "stage" else cmd_verify(args))


if __name__ == "__main__":
    main()
