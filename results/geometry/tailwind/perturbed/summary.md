# Geometry comparison: tailwind / perturbed

- reference: Chrome/156.0.8078.47; ours: capture; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 7 | 745 | 666 | 89.396% | 17 | 6 | 6 | 28 | 15 | 7 | 0 | 0 | 88.000% |
| 390x844 | 7 | 723 | 646 | 89.350% | 17 | 6 | 6 | 26 | 15 | 7 | 0 | 0 | 87.629% |
| all | 14 | 1468 | 1312 | 89.373% | 34 | 12 | 12 | 54 | 30 | 14 | 0 | 0 | 88.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 16, size-only 6, position+size 12, text-run 36.

Tags with the most mismatches: div 22/350, a 18/138, li 18/152, h3 12/40, span 12/114, button 8/38, p 6/112, td 6/86, h2 4/48, h6 4/6
