#!/usr/bin/env python3
"""Reads the measurements of one document-gate run and prints the comparison as markdown.

  summarize.py <out-dir> <gate|smoke>

<out-dir> holds onerender.jsonl, chrome.jsonl, weasyprint.jsonl (lines from measure.py and OneRender.ChromePdf), the BenchmarkDotNet JSON under
bdn/ and machine.txt. Medians are over the runs. The verdict for a document is a win when onerender is ahead of Chrome on all of: wall time
of a fresh process, warm in-process time, and peak memory (proportional set size; the resident-size sum is printed beside it).
"""
import glob
import json
import os
import statistics
import sys


DOCS = ["invoice", "report", "table10k", "tailwind"]


def load(path):
    rows = []
    if os.path.exists(path):
        with open(path) as f:
            for line in f:
                line = line.strip()
                if line.startswith("{"):
                    rows.append(json.loads(line))
    return rows


def med(rows, key):
    v = [r[key] for r in rows if key in r]
    return statistics.median(v) if v else None


def bdn(out):
    result = {}
    for p in glob.glob(os.path.join(out, "bdn", "**", "*-report-full-compressed.json"), recursive=True):
        with open(p) as f:
            data = json.load(f)
        for b in data.get("Benchmarks", []):
            name = b["Parameters"].split("=")[-1].strip()
            stats = b["Statistics"]
            mem = b.get("Memory", {})
            result[name] = (stats["Mean"] / 1e6, mem.get("BytesAllocatedPerOperation"))
    return result


def ms(x):
    return "-" if x is None else ("%.0f" % x if x >= 100 else "%.1f" % x)


def mb(kb):
    return "-" if kb is None else "%.0f" % (kb / 1024)


def main():
    out, mode = sys.argv[1], (sys.argv[2] if len(sys.argv) > 2 else "gate")
    ours, chrome, weasy, jit = (load(os.path.join(out, n + ".jsonl")) for n in ("onerender", "chrome", "weasyprint", "onerender-jit"))
    inproc = bdn(out)
    print("# Document gate summary (%s)" % mode)
    if mode == "smoke":
        print("\n**Smoke run: no lock, short job, shared host. Not publishable; the numbers only show where things stand.**")
    print("\nTimes are milliseconds, memory is megabytes. `process` is a fresh process per run (the cold start included; onerender as a NativeAOT executable, and under the JIT for information); `warm` is the converter already running.\n")
    print("| document | pages (ours / Chrome / WeasyPrint) | tool | process ms | warm ms | peak PSS MB | peak RSS-sum MB | allocated MB (warm, ours) |")
    print("|---|---|---|---|---|---|---|---|")
    verdicts = []
    for d in DOCS:
        o = [r for r in ours if r["doc"] == d]
        cc = [r for r in chrome if r["doc"] == d and r["mode"] == "cold"]
        cw = [r for r in chrome if r["doc"] == d and r["mode"] == "warm"]
        w = [r for r in weasy if r["doc"] == d]
        j = [r for r in jit if r["doc"] == d]
        pg = " / ".join(str(int(med(x, "pages"))) if x and med(x, "pages") is not None else "-" for x in (o, cc, w))
        ow = inproc.get(d)
        rows = [
            ("onerender (native)", med(o, "ms"), ow[0] if ow else None, med(o, "pssPeakKb"), med(o, "rssPeakKb"), (ow[1] / 1048576 if ow and ow[1] else None)),
            ("onerender (JIT process)", med(j, "ms"), None, med(j, "pssPeakKb"), med(j, "rssPeakKb"), None),
            ("Chrome", med(cc, "ms"), med(cw, "ms"), med(cc, "pssPeakKb"), med(cc, "rssPeakKb"), None),
            ("WeasyPrint", med(w, "ms"), None, med(w, "pssPeakKb"), med(w, "rssPeakKb"), None),
        ]
        for i, (t, p, wm, pss, rss, al) in enumerate(rows):
            print("| %s | %s | %s | %s | %s | %s | %s | %s |" % (d if i == 0 else "", pg if i == 0 else "", t, ms(p), ms(wm), mb(pss), mb(rss), "-" if al is None else "%.0f" % al))
        (_, op, ow_, opss, _, _), (_, cp, cwm, cpss, _, _) = rows[0], rows[2]
        checks = [("process time", op, cp), ("warm time", ow_, cwm), ("peak memory", opss, cpss)]
        failed = [n for n, a, b in checks if a is None or b is None or a >= b]
        verdicts.append((d, failed))
    print("\n## Verdicts against Chrome\n")
    for d, failed in verdicts:
        print("- %s: %s" % (d, "WIN" if not failed else "behind on " + ", ".join(failed)))
    if os.path.exists(os.path.join(out, "machine.txt")):
        print("\nMachine, versions and load: see `machine.txt`.")


main()
