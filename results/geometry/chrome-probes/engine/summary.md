# Geometry comparison: chrome-probes / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: yes

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 9 | 262 | 262 | 100.000% | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 9 | 100.000% |
| 390x844 | 9 | 262 | 262 | 100.000% | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 9 | 100.000% |
| all | 18 | 524 | 524 | 100.000% | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 18 | 100.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 0, position+size 0, text-run 0.
