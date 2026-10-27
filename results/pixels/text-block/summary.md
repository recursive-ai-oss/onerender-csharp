# Pixel similarity: text-block

- Chrome: Chrome/156.0.8078.47; fonts: 8768bf2774f6c8e8
- Screenshots are cropped to 4000 CSS px tall; SSIM is on luma, 8x8 windows, stride 4. Information only.

| viewport | pages | mean SSIM | min SSIM | min page | pixels identical (mean) |
|---|---|---|---|---|---|
| 1280x800 | 18 | 0.9885 | 0.9694 | field-notes | 95.42% |
| 390x844 | 18 | 0.9749 | 0.9504 | field-notes | 91.42% |

## Worst pages

| page | viewport | SSIM | differing pixels |
|---|---|---|---|
| field-notes | 390x844 | 0.9504 | 11.26% |
| essay | 390x844 | 0.9518 | 15.57% |
| email-plain | 390x844 | 0.9558 | 11.63% |
| blog-index | 390x844 | 0.9640 | 8.02% |
| badges-sticky | 390x844 | 0.9640 | 10.33% |
