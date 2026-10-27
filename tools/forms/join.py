#!/usr/bin/env python3
"""Joins a probe table (probe.sh output on stdin) with the generator's labels: prints one row per control with its box and baseline offset.
Usage: probe.sh ... | join.py <labels-file> [engine-table-file]"""
import sys
def flush():
    if page is None:
        return
    import os
    f = os.path.join(labels_dir, page + ".labels")
    if not os.path.exists(f):
        return
    labels = [l.rstrip("\n") for l in open(f)]
    print("##", page)
    for i, lab in enumerate(labels):
        if i in rows:
            p = rows[i]
            x, y, w, h = map(float, p[3:7])
            print('%-42s %-9s %8.4g x %-8.4g baseline %6.2f' % (lab, p[1], w, h, mark.get(i, 0) - y))


labels_dir = sys.argv[1]
page = None
rows = {}
mark = {}
for line in sys.stdin:
    if line.startswith("# "):
        flush()
        page = line.split()[1]
        rows = {}
        mark = {}
        continue
    p = line.split()
    if len(p) < 7 or not p[0].startswith('0/1/'):
        continue
    parts = p[0].split('/')
    if len(parts) == 4 and parts[3] == '0':
        rows[int(parts[2])] = p
    if p[1] == 'i' and len(parts) == 4:
        mark[int(parts[2])] = float(p[4])
flush()
