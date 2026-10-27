#!/usr/bin/env python3
"""Compares two probe tables (Chrome's from probe.sh, ours from `onerender-geomdiff engine-table`) element by element and lists the
boxes outside the tolerance, with the generator's label when there is one.
Usage: cmp.py <chrome.txt> <ours.txt> [labels-dir] [page]"""
import sys, os

def load(f):
    pages, cur = {}, None
    for line in open(f):
        if line.startswith('# '):
            cur = line.split()[1]
            pages[cur] = {}
            continue
        p = line.split()
        if cur is None or len(p) < 4 or not p[0][0].isdigit():
            continue
        box = None if p[3] == '-' else tuple(float(x) for x in p[3:7])
        pages[cur][p[0]] = (p[1], box)
    return pages

chrome, ours = load(sys.argv[1]), load(sys.argv[2])
labels_dir = sys.argv[3] if len(sys.argv) > 3 else None
only = sys.argv[4] if len(sys.argv) > 4 else None
total = bad = 0
for page, cel in chrome.items():
    if only and page != only:
        continue
    if page not in ours:
        continue
    labels = []
    if labels_dir and os.path.exists(os.path.join(labels_dir, page + '.labels')):
        labels = [l.rstrip('\n') for l in open(os.path.join(labels_dir, page + '.labels'))]
    oel = ours[page]
    for path, (tag, cb) in cel.items():
        ob = oel.get(path, (None, None))[1]
        if cb is None and ob is None:
            continue
        total += 1
        ok = False
        if cb and ob:
            ok = True
            for i in (0, 1, 2, 3):
                ext = cb[2] if i in (0, 2) else cb[3]
                if abs(cb[i] - ob[i]) > max(1.0, 0.005 * ext):
                    ok = False
        if not ok:
            bad += 1
            parts = path.split('/')
            lab = labels[int(parts[2])] if labels and len(parts) >= 3 and parts[2].isdigit() and int(parts[2]) < len(labels) else ''
            print('%-6s %-14s %-9s chrome %-34s ours %-34s %s' % (page[:6], path, tag, ' '.join('%g' % x for x in cb) if cb else '-', ' '.join('%g' % x for x in ob) if ob else '-', lab))
print('within tolerance: %d/%d = %.2f%%' % (total - bad, total, 100.0 * (total - bad) / max(1, total)))
