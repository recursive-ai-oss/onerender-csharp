# Geometry comparison: chrome-probes / perturbed

- reference: Chrome/156.0.8078.47; ours: capture; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 9 | 271 | 237 | 87.454% | 7 | 3 | 1 | 10 | 4 | 9 | 0 | 0 | 86.364% |
| 390x844 | 9 | 271 | 237 | 87.454% | 7 | 3 | 1 | 10 | 4 | 9 | 0 | 0 | 86.364% |
| all | 18 | 542 | 474 | 87.454% | 14 | 6 | 2 | 20 | 8 | 18 | 0 | 0 | 86.364% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 12, size-only 2, position+size 2, text-run 18.

Tags with the most mismatches: div 40/318, html 2/18, li 2/46, p 2/14, span 2/24, ul 2/46
