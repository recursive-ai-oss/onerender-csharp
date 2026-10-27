# Facts comparison: forms

- reference: Chrome/156.0.8078.47 `DOMSnapshot.captureSnapshot` facts; ours: the engine's facts for the same serialized document
- tolerance for boxes: max(1 px, 0.5% of the reference extent) per axis; rectangles of CSSOM View, paint order, stacking, colors and styles are compared exactly unless the kind says otherwise

| viewport | page captures | refused by the mapping | failed | element and text nodes |
|---|---|---|---|---|
| 1280x800 | 4 | 0 | 0 | 739 |
| 390x844 | 4 | 0 | 0 | 739 |

Agreement per fact kind (agree / compared, rate):

| fact kind | 1280x800 | 390x844 | all |
|---|---|---|---|
| bg.color.exact | 111/119 93.277% | 111/119 93.277% | 222/238 93.277% |
| bg.color.within2 | 111/119 93.277% | 111/119 93.277% | 222/238 93.277% |
| bg.presence | 284/336 84.524% | 284/336 84.524% | 568/672 84.524% |
| box.borderBox | 336/336 100.000% | 335/336 99.702% | 671/672 99.851% |
| box.exists.element | 336/336 100.000% | 336/336 100.000% | 672/672 100.000% |
| box.exists.text | 177/201 88.060% | 156/201 77.612% | 333/402 82.836% |
| form.inputChecked | 11/11 100.000% | 11/11 100.000% | 22/22 100.000% |
| form.inputValue | 78/80 97.500% | 78/80 97.500% | 156/160 97.500% |
| form.optionSelected | 14/15 93.333% | 14/15 93.333% | 28/30 93.333% |
| form.textValue | 7/7 100.000% | 7/7 100.000% | 14/14 100.000% |
| paint.order.exact | 254/336 75.595% | 254/336 75.595% | 508/672 75.595% |
| paint.order.relative | 330/332 99.398% | 330/332 99.398% | 660/664 99.398% |
| rect.client | 336/336 100.000% | 333/336 99.107% | 669/672 99.554% |
| rect.client.within1 | 336/336 100.000% | 335/336 99.702% | 671/672 99.851% |
| rect.offset | 325/336 96.726% | 326/336 97.024% | 651/672 96.875% |
| rect.offset.within1 | 336/336 100.000% | 335/336 99.702% | 671/672 99.851% |
| rect.scroll | 331/336 98.512% | 321/336 95.536% | 652/672 97.024% |
| rect.scroll.within1 | 331/336 98.512% | 323/336 96.131% | 654/672 97.321% |
| stacking.context | 335/336 99.702% | 335/336 99.702% | 670/672 99.702% |
| stacking.root | 335/336 99.702% | 335/336 99.702% | 670/672 99.702% |
| style.all | 26242/26880 97.626% | 26160/26880 97.321% | 52402/53760 97.474% |
| style.uncomputed | 0/5376 0.000% | 0/5376 0.000% | 0/10752 0.000% |
| text.bounds | 177/177 100.000% | 156/156 100.000% | 333/333 100.000% |
| text.boxes.bounds | 177/177 100.000% | 156/156 100.000% | 333/333 100.000% |
| text.boxes.count | 177/177 100.000% | 156/156 100.000% | 333/333 100.000% |
| text.boxes.ranges | 177/177 100.000% | 156/156 100.000% | 333/333 100.000% |
| text.layoutText | 177/177 100.000% | 156/156 100.000% | 333/333 100.000% |
| text.opacity | 336/336 100.000% | 336/336 100.000% | 672/672 100.000% |

Computed-style agreement by property, all viewports (properties below 99.9%):

| property | agree / compared | rate |
|---|---|---|
| appearance | 254/672 | 37.798% |
| width | 483/672 | 71.875% |
| display | 528/672 | 78.571% |
| min-height | 590/672 | 87.798% |
| padding-top | 618/672 | 91.964% |
| font-family | 622/672 | 92.560% |
| border-bottom-color | 630/672 | 93.750% |
| border-left-color | 630/672 | 93.750% |
| border-right-color | 630/672 | 93.750% |
| border-top-color | 630/672 | 93.750% |
| height | 641/672 | 95.387% |
| color | 646/672 | 96.131% |
| outline-color | 646/672 | 96.131% |
| text-decoration-color | 646/672 | 96.131% |
| background-color | 648/672 | 96.429% |
| padding-bottom | 648/672 | 96.429% |
| padding-left | 648/672 | 96.429% |
| padding-right | 648/672 | 96.429% |
| overflow-wrap | 658/672 | 97.917% |
| margin-top | 662/672 | 98.512% |
| overflow-y | 664/672 | 98.810% |
| margin-left | 666/672 | 99.107% |
| line-height | 668/672 | 99.405% |
| min-width | 668/672 | 99.405% |
| opacity | 670/672 | 99.702% |
