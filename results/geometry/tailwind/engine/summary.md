# Geometry comparison: tailwind / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 7 | 744 | 206 | 27.688% | 179 | 34 | 118 | 200 | 1 | 6 | 0 | 0 | 1.010% |
| 390x844 | 7 | 722 | 36 | 4.986% | 263 | 53 | 140 | 223 | 1 | 6 | 0 | 0 | 0.000% |
| all | 14 | 1466 | 242 | 16.508% | 442 | 87 | 258 | 423 | 2 | 12 | 0 | 0 | 0.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 2, size-only 16, position+size 3, text-run 21.

Tags with the most mismatches: div 269/350, li 112/152, p 106/112, span 105/114, a 91/138, td 77/86, button 38/38, h2 38/48, h3 37/40, th 35/42
