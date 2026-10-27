# Facts comparison: stratified sample of a static page corpus (aggregate only)

Reference: Chrome 156.0.8078.47 `DOMSnapshot.captureSnapshot` facts; ours: the engine's facts for the same document. 600 pages at 1280x800 and at 390x844, 1,213,384 element and text nodes per viewport, none refused by the mapping, none failed. No page identities are recorded here.

| fact kind | 1280x800 | 390x844 | all |
|---|---|---|---|
| bg.color.exact | 95.338% | 94.671% | 95.012% |
| bg.presence | 94.919% | 95.634% | 95.266% |
| box.borderBox | 12.543% | 9.741% | 11.183% |
| box.exists.element | 93.531% | 93.265% | 93.401% |
| box.exists.text | 68.495% | 66.997% | 67.764% |
| form.inputChecked | 100.000% | 100.000% | 100.000% |
| form.inputValue | 99.982% | 99.982% | 99.982% |
| form.optionSelected | 100.000% | 100.000% | 100.000% |
| form.textValue | 100.000% | 100.000% | 100.000% |
| generated.types | 99.464% | 99.576% | 99.517% |
| paint.order.exact | 63.609% | 66.771% | 65.144% |
| paint.order.relative | 88.754% | 89.517% | 89.125% |
| rect.client | 81.654% | 81.707% | 81.680% |
| rect.offset | 29.682% | 25.462% | 27.634% |
| rect.scroll | 79.966% | 80.117% | 80.039% |
| stacking.context | 99.835% | 99.848% | 99.841% |
| stacking.root | 99.105% | 99.206% | 99.154% |
| style.all | 97.005% | 97.140% | 97.071% |
| text.boxes.count | 98.043% | 97.457% | 97.760% |
| text.boxes.ranges | 99.248% | 98.897% | 99.079% |
| text.layoutText | 99.991% | 99.988% | 99.990% |
| text.opacity | 99.860% | 99.830% | 99.845% |

Box geometry on this set follows the layout engine's own agreement with the browser on real pages (see the geometry differential); text and rect kinds that depend on it are low for the same reason.
