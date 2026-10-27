# Geometry comparison: tailwind / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 7 | 749 | 55 | 7.343% | 255 | 38 | 153 | 236 | 1 | 11 | 0 | 0 | 0.000% |
| 390x844 | 7 | 727 | 24 | 3.301% | 221 | 35 | 184 | 251 | 1 | 11 | 0 | 0 | 0.000% |
| all | 14 | 1476 | 79 | 5.352% | 476 | 73 | 337 | 487 | 2 | 22 | 0 | 0 | 0.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 14, position+size 2, text-run 0.

Tags with the most mismatches: div 339/350, li 123/152, p 112/112, span 111/114, a 103/138, td 86/86, h2 48/48, th 42/42, h3 40/40, button 38/38
