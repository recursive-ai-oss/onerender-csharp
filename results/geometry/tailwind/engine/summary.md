# Geometry comparison: tailwind / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 7 | 749 | 9 | 1.202% | 85 | 30 | 256 | 357 | 1 | 11 | 0 | 0 | 0.000% |
| 390x844 | 7 | 727 | 7 | 0.963% | 110 | 31 | 241 | 326 | 1 | 11 | 0 | 0 | 0.000% |
| all | 14 | 1476 | 16 | 1.084% | 195 | 61 | 497 | 683 | 2 | 22 | 0 | 0 | 0.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 14, position+size 0, text-run 0.

Tags with the most mismatches: div 345/350, li 144/152, a 118/138, span 113/114, p 112/112, td 86/86, h2 48/48, th 42/42, h3 40/40, button 38/38
