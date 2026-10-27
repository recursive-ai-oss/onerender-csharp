# Pixel similarity: forms

- Chrome: Chrome/156.0.8078.47; fonts: 8768bf2774f6c8e8
- Screenshots are cropped to 4000 CSS px tall; SSIM is on luma, 8x8 windows, stride 4. Information only.

| viewport | pages | mean SSIM | min SSIM | min page | pixels identical (mean) |
|---|---|---|---|---|---|
| 1280x800 | 4 | 0.9929 | 0.9908 | signup | 97.60% |
| 390x844 | 4 | 0.9871 | 0.9762 | signup | 95.29% |

## Worst pages

| page | viewport | SSIM | differing pixels |
|---|---|---|---|
| signup | 390x844 | 0.9762 | 4.29% |
| checkout | 390x844 | 0.9852 | 4.45% |
| signup | 1280x800 | 0.9908 | 1.37% |
| gallery | 390x844 | 0.9922 | 7.04% |
| checkout | 1280x800 | 0.9925 | 1.86% |
