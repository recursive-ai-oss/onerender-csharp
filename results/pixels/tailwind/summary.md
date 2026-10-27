# Pixel similarity: tailwind

- Chrome: Chrome/156.0.8078.47; fonts: 8768bf2774f6c8e8
- Screenshots are cropped to 4000 CSS px tall; SSIM is on luma, 8x8 windows, stride 4. Information only.

| viewport | pages | mean SSIM | min SSIM | min page | pixels identical (mean) |
|---|---|---|---|---|---|
| 1280x800 | 7 | 0.9908 | 0.9797 | daisy-app | 93.34% |
| 390x844 | 7 | 0.9861 | 0.9673 | daisy-showcase | 91.09% |

## Worst pages

| page | viewport | SSIM | differing pixels |
|---|---|---|---|
| daisy-showcase | 390x844 | 0.9673 | 5.77% |
| tw-article | 390x844 | 0.9684 | 16.59% |
| daisy-app | 1280x800 | 0.9797 | 2.68% |
| tw-article | 1280x800 | 0.9827 | 10.28% |
| daisy-showcase | 1280x800 | 0.9848 | 2.77% |
