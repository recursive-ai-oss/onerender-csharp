# Geometry comparison: templates / stub-engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 6 | 3052 | 0 | 0.000% | 0 | 0 | 0 | 0 | 3052 | 0 | 0 | 0 | 0.000% |
| 390x844 | 6 | 3052 | 0 | 0.000% | 0 | 0 | 0 | 0 | 3052 | 0 | 0 | 0 | 0.000% |
| all | 12 | 6104 | 0 | 0.000% | 0 | 0 | 0 | 0 | 6104 | 0 | 0 | 0 | 0.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 0, position+size 0, text-run 0.

Tags with the most mismatches: td 2800/2800, tr 598/598, p 470/470, strong 380/380, em 344/344, li 314/314, th 270/270, span 170/170, h3 120/120, ul 94/94
