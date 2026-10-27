# Geometry comparison: forms / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: yes

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 5 | 366 | 366 | 100.000% | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 5 | 100.000% |
| 390x844 | 5 | 366 | 365 | 99.727% | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 4 | 100.000% |
| all | 10 | 732 | 731 | 99.863% | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 9 | 100.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 1, position+size 0, text-run 0.

Tags with the most mismatches: select 1/32
