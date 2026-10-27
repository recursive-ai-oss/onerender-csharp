# Geometry comparison: onehtml-2026-10a / chrome-vs-chrome

- reference: Chrome/156.0.8078.47; ours: capture; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: yes

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 600 | 379995 | 379921 | 99.981% | 40 | 0 | 0 | 34 | 0 | 0 | 0 | 598 | 100.000% |
| 390x844 | 600 | 359156 | 359122 | 99.991% | 0 | 0 | 0 | 34 | 0 | 0 | 0 | 599 | 100.000% |
| all | 1200 | 739151 | 739043 | 99.985% | 40 | 0 | 0 | 68 | 0 | 0 | 0 | 1197 | 100.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 40, size-only 0, position+size 0, text-run 68.

Tags with the most mismatches: path 68/17694, span 40/123518
