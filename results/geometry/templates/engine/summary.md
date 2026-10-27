# Geometry comparison: templates / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 6 | 3052 | 5 | 0.164% | 404 | 21 | 648 | 1974 | 0 | 0 | 0 | 0 | 0.508% |
| 390x844 | 6 | 3052 | 7 | 0.229% | 220 | 27 | 678 | 2120 | 0 | 0 | 0 | 0 | 0.508% |
| all | 12 | 6104 | 12 | 0.197% | 624 | 48 | 1326 | 4094 | 0 | 0 | 0 | 0 | 0.508% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 12, position+size 0, text-run 0.

Tags with the most mismatches: td 2800/2800, tr 598/598, p 465/470, strong 380/380, em 344/344, li 314/314, th 270/270, span 170/170, h3 120/120, ul 94/94
