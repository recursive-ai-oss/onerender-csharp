# Pixel similarity: chrome-probes

- Chrome: Chrome/156.0.8078.47; fonts: 8768bf2774f6c8e8
- Screenshots are cropped to 4000 CSS px tall; SSIM is on luma, 8x8 windows, stride 4. Information only.

| viewport | pages | mean SSIM | min SSIM | min page | pixels identical (mean) |
|---|---|---|---|---|---|
| 1280x800 | 9 | 0.9998 | 0.9996 | foot | 99.70% |
| 390x844 | 9 | 0.9995 | 0.9990 | foot | 99.05% |

## Worst pages

| page | viewport | SSIM | differing pixels |
|---|---|---|---|
| foot | 390x844 | 0.9990 | 2.66% |
| logical | 390x844 | 0.9990 | 0.30% |
| ifx2 | 390x844 | 0.9995 | 0.28% |
| slash | 390x844 | 0.9996 | 1.96% |
| foot | 1280x800 | 0.9996 | 0.86% |
