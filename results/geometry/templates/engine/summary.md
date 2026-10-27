# Geometry comparison: templates / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 6 | 3052 | 173 | 5.668% | 951 | 31 | 556 | 1341 | 0 | 0 | 0 | 1 | 16.751% |
| 390x844 | 6 | 3052 | 138 | 4.522% | 938 | 26 | 564 | 1386 | 0 | 0 | 0 | 1 | 3.046% |
| all | 12 | 6104 | 311 | 5.095% | 1889 | 57 | 1120 | 2727 | 0 | 0 | 0 | 2 | 3.046% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 8, position+size 0, text-run 2.

Tags with the most mismatches: td 2800/2800, tr 598/598, p 421/470, strong 370/380, em 344/344, li 292/314, th 270/270, h3 117/120, ul 94/94, span 64/170
