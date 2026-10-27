# Geometry comparison: onehtml-2026-10a / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 600 | 383033 | 38811 | 10.133% | 227069 | 6677 | 52413 | 33283 | 21742 | 3038 | 0 | 8 | 3.438% |
| 390x844 | 600 | 362242 | 29346 | 8.101% | 221376 | 5758 | 50509 | 30855 | 21312 | 3086 | 0 | 8 | 3.077% |
| all | 1200 | 745275 | 68157 | 9.145% | 448445 | 12435 | 102922 | 64138 | 43054 | 6124 | 0 | 16 | 3.211% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 1050, size-only 1297, position+size 277, text-run 96.

Tags with the most mismatches: div 154857/214010, a 130001/241314, li 87973/188728, span 87676/123518, p 26376/31748, ul 20739/36726, svg 16881/23454, img 16792/20164, use 11167/13196, path 11044/17694
