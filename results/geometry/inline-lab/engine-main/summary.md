# Geometry comparison: inline-lab / engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 7 | 155 | 0 | 0.000% | 1 | 14 | 1 | 61 | 78 | 0 | 0 | 0 | 0.000% |
| 390x844 | 7 | 155 | 0 | 0.000% | 1 | 14 | 1 | 61 | 78 | 0 | 0 | 0 | 0.000% |
| all | 14 | 310 | 0 | 0.000% | 2 | 28 | 2 | 122 | 156 | 0 | 0 | 0 | 0.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 14, position+size 0, text-run 0.

Tags with the most mismatches: p 108/108, span 98/98, div 18/18, body 14/14, html 14/14, br 12/12, b 8/8, code 4/4, em 4/4, small 4/4
