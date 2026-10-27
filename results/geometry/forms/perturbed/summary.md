# Geometry comparison: forms / perturbed

- reference: Chrome/156.0.8078.47; ours: capture; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 4 | 340 | 309 | 90.882% | 5 | 2 | 3 | 13 | 4 | 4 | 0 | 0 | 91.139% |
| 390x844 | 4 | 340 | 309 | 90.882% | 5 | 2 | 3 | 13 | 4 | 4 | 0 | 0 | 91.139% |
| all | 8 | 680 | 618 | 90.882% | 10 | 4 | 6 | 26 | 8 | 8 | 0 | 0 | 91.139% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 10, size-only 2, position+size 4, text-run 16.

Tags with the most mismatches: label 12/110, input 8/160, button 6/34, div 6/134, option 6/70, select 4/32, a 2/10, body 2/8, h1 2/6, html 2/8
