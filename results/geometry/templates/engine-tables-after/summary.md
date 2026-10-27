# Geometry comparison: templates / engine-tables-after

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 6 | 3052 | 1888 | 61.861% | 1120 | 6 | 20 | 18 | 0 | 0 | 0 | 3 | 100.000% |
| 390x844 | 6 | 3052 | 1137 | 37.254% | 1856 | 14 | 20 | 25 | 0 | 0 | 0 | 1 | 74.517% |
| all | 12 | 6104 | 3025 | 49.558% | 2976 | 20 | 40 | 43 | 0 | 0 | 0 | 4 | 98.750% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 629, size-only 4, position+size 0, text-run 2.

Tags with the most mismatches: td 800/2800, p 387/470, strong 331/380, em 318/344, tr 246/598, li 237/314, th 225/270, h3 108/120, ul 85/94, span 47/170
