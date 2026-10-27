# Geometry comparison: tailwind / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 7 | 742 | 21 | 2.830% | 65 | 43 | 210 | 321 | 78 | 4 | 0 | 0 | 1.010% |
| 390x844 | 7 | 720 | 17 | 2.361% | 93 | 39 | 196 | 292 | 79 | 4 | 0 | 0 | 0.000% |
| all | 14 | 1462 | 38 | 2.599% | 158 | 82 | 406 | 613 | 157 | 8 | 0 | 0 | 0.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 14, position+size 0, text-run 0.

Tags with the most mismatches: div 335/350, li 140/152, a 114/138, span 113/114, p 112/112, td 86/86, h2 44/48, th 42/42, h3 40/40, button 38/38
