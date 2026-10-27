#!/usr/bin/env python3
"""Prints the root-cause mismatches of a geomdiff detail file (details/<set>/engine/<page>-<viewport>.json.gz).
Usage: detail.py <file> [--all]"""
import gzip, json, sys
d = json.load(gzip.open(sys.argv[1]))
print(d['page'], d['viewport'], 'elements', d['elements'], 'matched', d['matched'])
for m in d['mismatches']:
    if not m['rootCause'] and '--all' not in sys.argv:
        continue
    print('%-14s %-9s %-16s chrome %-36s ours %s' % (m['path'], m['tag'], m['class'], [round(x, 2) for x in m['chrome']] if m.get('chrome') else None, [round(x, 2) for x in m['ours']] if m.get('ours') else None))
