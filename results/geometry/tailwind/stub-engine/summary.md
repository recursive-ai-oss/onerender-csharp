# Geometry comparison: tailwind / stub-engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 7 | 738 | 0 | 0.000% | 0 | 0 | 0 | 0 | 738 | 0 | 0 | 0 | 0.000% |
| 390x844 | 7 | 716 | 0 | 0.000% | 0 | 0 | 0 | 0 | 716 | 0 | 0 | 0 | 0.000% |
| all | 14 | 1454 | 0 | 0.000% | 0 | 0 | 0 | 0 | 1454 | 0 | 0 | 0 | 0.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 0, position+size 0, text-run 0.

Tags with the most mismatches: div 346/350, li 148/152, a 120/138, span 114/114, p 112/112, td 86/86, h2 48/48, th 42/42, h3 40/40, button 38/38
