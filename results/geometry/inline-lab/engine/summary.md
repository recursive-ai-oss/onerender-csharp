# Geometry comparison: inline-lab / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: yes

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 7 | 155 | 154 | 99.355% | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 6 | 100.000% |
| 390x844 | 7 | 155 | 154 | 99.355% | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 6 | 100.000% |
| all | 14 | 310 | 308 | 99.355% | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 12 | 100.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 0, position+size 0, text-run 2.

Tags with the most mismatches: span 2/98
