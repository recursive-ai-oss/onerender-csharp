# Geometry comparison: templates / engine-tables-after

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 6 | 3052 | 1933 | 63.336% | 1118 | 0 | 0 | 1 | 0 | 0 | 0 | 3 | 100.000% |
| 390x844 | 6 | 3052 | 1182 | 38.729% | 1854 | 8 | 0 | 8 | 0 | 0 | 0 | 1 | 97.826% |
| all | 12 | 6104 | 3115 | 51.032% | 2972 | 8 | 0 | 9 | 0 | 0 | 0 | 4 | 98.750% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 631, size-only 2, position+size 0, text-run 2.

Tags with the most mismatches: td 764/2800, p 379/470, strong 331/380, em 318/344, li 237/314, th 225/270, tr 222/598, h3 108/120, ul 85/94, span 47/170
