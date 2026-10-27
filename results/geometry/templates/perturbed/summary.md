# Geometry comparison: templates / perturbed

- reference: Chrome/156.0.8078.47; ours: capture; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 6 | 3058 | 2796 | 91.432% | 52 | 15 | 19 | 142 | 28 | 6 | 0 | 0 | 91.489% |
| 390x844 | 6 | 3058 | 2796 | 91.432% | 52 | 15 | 19 | 142 | 28 | 6 | 0 | 0 | 91.489% |
| all | 12 | 6116 | 5592 | 91.432% | 104 | 30 | 38 | 284 | 56 | 12 | 0 | 0 | 91.489% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 84, size-only 26, position+size 38, text-run 226.

Tags with the most mismatches: td 244/2800, tr 46/598, p 40/470, strong 34/380, li 32/314, em 24/344, th 24/270, span 14/170, br 8/58, h3 8/120
