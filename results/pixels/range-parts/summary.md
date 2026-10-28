# Pixel similarity: range-parts

- Chrome: Chrome/156.0.8078.47; fonts: 8768bf2774f6c8e8
- Screenshots are cropped to 4000 CSS px tall; SSIM is on luma, 8x8 windows, stride 4. Information only.

| viewport | pages | mean SSIM | min SSIM | min page | pixels identical (mean) |
|---|---|---|---|---|---|
| 1280x800 | 6 | 0.9988 | 0.9962 | native | 99.40% |
| 390x844 | 6 | 0.9963 | 0.9879 | native | 98.12% |

## Worst pages

| page | viewport | SSIM | differing pixels |
|---|---|---|---|
| native | 390x844 | 0.9879 | 2.81% |
| dark | 390x844 | 0.9955 | 1.67% |
| native | 1280x800 | 0.9962 | 0.90% |
| inline | 390x844 | 0.9978 | 1.23% |
| custom | 390x844 | 0.9984 | 1.64% |
