#!/usr/bin/env python3
"""Writes the four benchmark documents as single self-contained HTML files (style sheets inlined, no external resources).

  invoice.html      the invoice template
  report.html       a long report from the report template's generator (--chapters sets the length; the default, 19 chapters, gives 50 pages)
  table10k.html     one table of 10,000 rows (about 210 pages)
  tailwind.html     the Tailwind marketing page, built with Tailwind 4.3.3 by tools/geomdiff/scripts/build-tailwind.sh

Usage: make-documents.py <out-dir> [--tailwind <dir-with-index.html-and-styles.css>] [--chapters N]
The output is deterministic. The files are generated, never committed.
"""
import argparse
import importlib.util
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
TEMPLATES = os.path.join(REPO, "testdata", "templates")
LINK = '<link rel="stylesheet" href="styles.css">'


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def write(out, name, text):
    with open(os.path.join(out, name), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def inline(folder):
    html = read(os.path.join(folder, "index.html"))
    css = read(os.path.join(folder, "styles.css"))
    if LINK in html:
        return html.replace(LINK, "<style>\n" + css + "</style>")
    # The Tailwind pages link the style sheet in another spelling.
    import re
    return re.sub(r'<link[^>]*href="styles\.css"[^>]*>', lambda m: "<style>\n" + css + "</style>", html, count=1)


def rows(count):
    parts = ["<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'><title>Ledger</title><style>"
             "body{font:12px Liberation Sans}table{border-collapse:collapse;width:100%}"
             "th,td{border:1px solid #999;padding:3px 6px;text-align:left}th{background:#eee}</style></head>"
             "<body><h1>Ledger</h1><table><thead><tr><th>No</th><th>Account</th><th>Description</th><th>Amount</th></tr></thead><tbody>"]
    for i in range(count):
        parts.append("<tr><td>%d</td><td>AC-%d</td><td>Entry number %d for the period</td><td>%d.%d</td></tr>"
                     % (i, i * 7919 % 100000, i, i * 37 % 10000, i % 100))
    parts.append("</tbody></table></body></html>")
    return "".join(parts)


def report(chapters):
    spec = importlib.util.spec_from_file_location("templates_generate", os.path.join(TEMPLATES, "generate.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    tmp = tempfile.mkdtemp(prefix="report-")
    try:
        mod.report(chapter_count=chapters, name="report", root=tmp)
        return inline(os.path.join(tmp, "report"))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--tailwind", help="folder with the built Tailwind page (index.html, styles.css)")
    ap.add_argument("--chapters", type=int, default=19)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    write(a.out, "invoice.html", inline(os.path.join(TEMPLATES, "invoice")))
    write(a.out, "report.html", report(a.chapters))
    write(a.out, "table10k.html", rows(10000))
    if a.tailwind:
        write(a.out, "tailwind.html", inline(a.tailwind))
    else:
        print("no --tailwind folder: tailwind.html not written", file=sys.stderr)
    for n in sorted(os.listdir(a.out)):
        print(n, os.path.getsize(os.path.join(a.out, n)))


main()
