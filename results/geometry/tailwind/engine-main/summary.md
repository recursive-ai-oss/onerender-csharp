# Geometry comparison: tailwind / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 7 | 742 | 12 | 1.617% | 59 | 32 | 234 | 323 | 78 | 4 | 0 | 0 | 0.862% |
| 390x844 | 7 | 720 | 10 | 1.389% | 79 | 34 | 221 | 293 | 79 | 4 | 0 | 0 | 0.000% |
| all | 14 | 1462 | 22 | 1.505% | 138 | 66 | 455 | 616 | 157 | 8 | 0 | 0 | 0.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 14, position+size 0, text-run 0.

Tags with the most mismatches: div 341/350, li 140/152, a 114/138, span 113/114, p 112/112, td 86/86, h2 46/48, th 42/42, h3 40/40, button 38/38
