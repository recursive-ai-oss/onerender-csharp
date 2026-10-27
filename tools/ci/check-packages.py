#!/usr/bin/env python3
"""Checks the packages produced by `dotnet pack` against what a release must contain.

    python3 tools/ci/check-packages.py <directory holding the .nupkg and .snupkg files> [--no-commit-check]

`--no-commit-check` is for a checkout without git metadata (the repository commit is then missing from the package).
Exit status 1 and a list of problems when a package is wrong; 0 when all are fine. Per package it checks:
metadata (id prefix, version, authors, description, licence expression, repository URL, project URL, tags, readme),
contents (README.md that names the package, THIRD-PARTY-NOTICES.md, both licence texts, the assembly and its XML
documentation under lib/net10.0, OFL.txt for every fonts package, nothing else), and that a symbols package exists.
"""
import re
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree

PREFIX = "RecursiveAI."
REPO = "https://github.com/recursive-ai-oss/onerender-csharp"
CODE_LICENSE = "MIT OR Apache-2.0"
FONT_LICENSE = "(MIT OR Apache-2.0) AND OFL-1.1"

problems = []


def fail(pkg, msg):
    problems.append(f"{pkg}: {msg}")


def strip_namespaces(root):
    for e in root.iter():
        e.tag = e.tag.rpartition("}")[2]


def check(path: Path):
    name = path.name
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        nuspec_name = next((n for n in names if n.endswith(".nuspec")), None)
        if nuspec_name is None:
            fail(name, "no nuspec")
            return
        root = ElementTree.fromstring(z.read(nuspec_name))
        strip_namespaces(root)
        meta = root.find("metadata")

        def text(tag):
            e = meta.find(tag)
            return e.text.strip() if e is not None and e.text else ""

        pid = text("id")
        is_font = pid.startswith(PREFIX + "OneRender.Fonts")
        if not pid.startswith(PREFIX + "OneRender"):
            fail(name, f"id '{pid}' does not start with {PREFIX}OneRender")
        want_license = FONT_LICENSE if is_font else CODE_LICENSE
        lic = meta.find("license")
        if lic is None or lic.get("type") != "expression" or (lic.text or "").strip() != want_license:
            fail(name, f"licence expression is '{lic.text if lic is not None else None}', want '{want_license}'")
        if len(text("description")) < 20:
            fail(name, "description missing or too short")
        if text("authors") != "Recursive AI, LLC":
            fail(name, "authors")
        if "Recursive AI, LLC" not in text("copyright"):
            fail(name, "copyright")
        repo = meta.find("repository")
        if repo is None or repo.get("type") != "git" or not (repo.get("url") or "").startswith(REPO):
            fail(name, f"repository url is '{repo.get('url') if repo is not None else None}'")
        elif not repo.get("commit") and "--no-commit-check" not in sys.argv:
            fail(name, "repository commit missing (source link)")
        if not text("projectUrl").startswith(REPO):
            fail(name, "projectUrl")
        if "onerender" not in text("tags"):
            fail(name, "tags")
        if text("readme") != "README.md":
            fail(name, "readme not declared")
        if not re.fullmatch(r"\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?", text("version")):
            fail(name, f"version '{text('version')}'")

        # Contents.
        short = pid[len(PREFIX):]
        allowed = [
            r"\[Content_Types\]\.xml", r"_rels/\.rels", r"package/services/metadata/core-properties/[^/]+\.psmdcp",
            re.escape(nuspec_name), r"README\.md", r"THIRD-PARTY-NOTICES\.md",
            r"licenses/LICENSE-MIT", r"licenses/LICENSE-APACHE",
            re.escape(f"lib/net10.0/{short}.dll"), re.escape(f"lib/net10.0/{short}.xml"),
        ]
        if is_font:
            allowed += [r"licenses/(?:[^/]+/)?OFL\.txt"]
            if short != "OneRender.Fonts":
                allowed += [r"buildTransitive/[^/]+\.(?:targets|cs)"]
        for n in names:
            if n.endswith("/"):
                continue
            if not any(re.fullmatch(a, n) for a in allowed):
                fail(name, f"unexpected file {n}")
        for need in ["README.md", "THIRD-PARTY-NOTICES.md", "licenses/LICENSE-MIT", "licenses/LICENSE-APACHE",
                     f"lib/net10.0/{short}.dll", f"lib/net10.0/{short}.xml"]:
            if need not in names:
                fail(name, f"missing {need}")
        if is_font and not any(n.endswith("OFL.txt") for n in names):
            fail(name, "fonts package without OFL.txt")
        if "README.md" in names:
            readme = z.read("README.md").decode("utf-8")
            if pid not in readme or len(readme) < 200:
                fail(name, "README.md does not describe this package")
        # Public text: nothing internal in what ships.
        for n in ("README.md", nuspec_name):
            if n in names:
                body = z.read(n).decode("utf-8", "replace")
                if re.search(r"/home/|PLAN\.md|DESIGN\.md", body):
                    fail(name, f"{n} mentions an internal path or document")

    symbols = path.with_suffix(".snupkg")
    if not symbols.exists():
        fail(name, "no symbols package (.snupkg)")
    else:
        with zipfile.ZipFile(symbols) as z:
            if not any(n.endswith(".pdb") for n in z.namelist()):
                fail(name, "symbols package holds no .pdb")


def main():
    d = Path(sys.argv[1])
    packages = sorted(d.glob("*.nupkg"))
    if not packages:
        print("no packages found")
        return 1
    for p in packages:
        check(p)
    for p in packages:
        print(f"{p.name}  {p.stat().st_size:>10,} bytes")
    if problems:
        print()
        print("\n".join(problems))
        print(f"{len(problems)} problem(s) in {len(packages)} package(s).")
        return 1
    print(f"{len(packages)} packages checked, no problems.")
    return 0


sys.exit(main())
