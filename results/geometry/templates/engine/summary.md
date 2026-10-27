# Geometry comparison: templates / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 6 | 3052 | 232 | 7.602% | 912 | 25 | 542 | 1341 | 0 | 0 | 0 | 1 | 16.751% |
| 390x844 | 6 | 3052 | 172 | 5.636% | 931 | 24 | 548 | 1377 | 0 | 0 | 0 | 1 | 3.046% |
| all | 12 | 6104 | 404 | 6.619% | 1843 | 49 | 1090 | 2718 | 0 | 0 | 0 | 2 | 5.185% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 8, position+size 0, text-run 2.

Tags with the most mismatches: td 2800/2800, tr 598/598, p 412/470, strong 363/380, em 337/344, th 270/270, li 256/314, h3 115/120, ul 94/94, span 63/170
