#!/usr/bin/env python3
"""Runs a document converter as a child process, once per run, and measures it from outside.

  measure.py --label onerender --docs <dir> --out <dir> [--runs 5] [--doc name]... -- <command> {in} {out}

For every <docs>/<name>.html it runs the command (with {in} and {out} replaced) --runs times, each as a fresh process, and prints one JSON
line per run: wall milliseconds, the peak resident set of the process tree (sampled every 10 ms: rss counts shared pages in each process,
pss divides them), the exact peak of the process itself from the kernel (maxrssKb), the exit code and the page count of the PDF written.
A fresh process includes the runtime start, so run 1 of the smallest document is the cold start.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time


def tree(root):
    parents = {}
    for name in os.listdir("/proc"):
        if name.isdigit():
            try:
                with open("/proc/%s/stat" % name) as f:
                    s = f.read()
                parents[int(name)] = int(s[s.rindex(")") + 2:].split()[1])
            except OSError:
                pass
    pids = {root}
    grew = True
    while grew:
        grew = False
        for pid, ppid in parents.items():
            if pid not in pids and ppid in pids:
                pids.add(pid)
                grew = True
    return pids


def memory(pids, want_pss):
    rss = pss = 0
    for pid in pids:
        try:
            with open("/proc/%d/statm" % pid) as f:
                rss += int(f.read().split()[1]) * 4
            if want_pss:
                with open("/proc/%d/smaps_rollup" % pid) as f:
                    for line in f:
                        if line.startswith("Pss:"):
                            pss += int(line.split()[1])
                            break
        except OSError:
            pass
    return rss, pss


def pages(path):
    try:
        out = subprocess.run(["pdfinfo", path], capture_output=True, text=True, timeout=60).stdout
        m = re.search(r"^Pages:\s+(\d+)", out, re.M)
        if m:
            return int(m.group(1))
    except (OSError, subprocess.SubprocessError):
        pass
    try:
        with open(path, "rb") as f:
            data = f.read()
    except OSError:
        return -1
    best = -1
    for m in re.finditer(rb"/Type\s*/Pages\b[^>]*?/Count\s+(\d+)|/Count\s+(\d+)[^>]*?/Type\s*/Pages\b", data):
        best = max(best, int(m.group(1) or m.group(2)))
    return best


def run(command, in_path, out_path):
    argv = [a.replace("{in}", in_path).replace("{out}", out_path) for a in command]
    start = time.perf_counter()
    p = subprocess.Popen(argv, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    peak_rss = peak_pss = 0
    n = 0
    status = ru = None
    while True:
        pid, status, ru = os.wait4(p.pid, os.WNOHANG)
        if pid:
            break
        rss, pss = memory(tree(p.pid), n % 5 == 0)
        peak_rss = max(peak_rss, rss)
        peak_pss = max(peak_pss, pss)
        n += 1
        time.sleep(0.01)
    wall = (time.perf_counter() - start) * 1000
    p.returncode = 0  # reaped above
    code = os.waitstatus_to_exitcode(status)
    return wall, peak_rss, peak_pss, ru.ru_maxrss, code


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--label", required=True)
    ap.add_argument("--docs", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--runs", type=int, default=5)
    ap.add_argument("--doc", action="append", default=[])
    ap.add_argument("command", nargs=argparse.REMAINDER)
    a = ap.parse_args()
    command = a.command[1:] if a.command and a.command[0] == "--" else a.command
    os.makedirs(a.out, exist_ok=True)
    names = a.doc or sorted(f[:-5] for f in os.listdir(a.docs) if f.endswith(".html"))
    for name in names:
        in_path = os.path.abspath(os.path.join(a.docs, name + ".html"))
        out_path = os.path.abspath(os.path.join(a.out, "%s.%s.pdf" % (name, a.label)))
        for i in range(a.runs):
            wall, rss, pss, maxrss, code = run(command, in_path, out_path)
            print(json.dumps({"tool": a.label, "doc": name, "mode": "process", "run": i + 1, "ms": round(wall, 1), "rssPeakKb": rss,
                              "pssPeakKb": pss, "maxrssKb": maxrss, "exit": code, "pages": pages(out_path)}), flush=True)


main()
