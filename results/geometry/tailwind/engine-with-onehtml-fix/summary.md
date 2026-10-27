# Geometry comparison: tailwind / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 7 | 744 | 602 | 80.914% | 112 | 15 | 6 | 2 | 1 | 6 | 0 | 2 | 96.795% |
| 390x844 | 7 | 722 | 546 | 75.623% | 106 | 39 | 11 | 13 | 1 | 6 | 0 | 2 | 96.795% |
| all | 14 | 1466 | 1148 | 78.308% | 218 | 54 | 17 | 15 | 2 | 12 | 0 | 4 | 96.795% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 10, size-only 8, position+size 2, text-run 0.

Tags with the most mismatches: div 75/350, li 49/152, p 33/112, h3 16/40, h2 14/48, input 14/28, span 14/114, option 12/12, button 11/38, a 10/138
