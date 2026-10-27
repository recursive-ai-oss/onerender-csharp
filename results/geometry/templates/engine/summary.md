# Geometry comparison: templates / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 6 | 3052 | 15 | 0.491% | 306 | 32 | 622 | 1616 | 461 | 0 | 0 | 0 | 0.508% |
| 390x844 | 6 | 3052 | 9 | 0.295% | 154 | 23 | 682 | 1723 | 461 | 0 | 0 | 0 | 0.508% |
| all | 12 | 6104 | 24 | 0.393% | 460 | 55 | 1304 | 3339 | 922 | 0 | 0 | 0 | 0.508% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 6, size-only 10, position+size 2, text-run 6.

Tags with the most mismatches: td 2800/2800, tr 598/598, p 464/470, strong 380/380, em 344/344, li 314/314, th 270/270, span 170/170, h3 120/120, ul 94/94
