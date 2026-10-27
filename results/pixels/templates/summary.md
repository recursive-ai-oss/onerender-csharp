# Pixel similarity: templates

- Chrome: Chrome/156.0.8078.47; fonts: 8768bf2774f6c8e8
- Screenshots are cropped to 4000 CSS px tall; SSIM is on luma, 8x8 windows, stride 4. Information only.

| viewport | pages | mean SSIM | min SSIM | min page | pixels identical (mean) |
|---|---|---|---|---|---|
| 1280x800 | 6 | 0.9880 | 0.9822 | report | 96.08% |
| 390x844 | 6 | 0.9793 | 0.9596 | report | 93.54% |

## Worst pages

| page | viewport | SSIM | differing pixels |
|---|---|---|---|
| report | 390x844 | 0.9596 | 12.22% |
| receipt | 390x844 | 0.9721 | 6.56% |
| statement | 390x844 | 0.9821 | 6.27% |
| report | 1280x800 | 0.9822 | 6.73% |
| certificate | 1280x800 | 0.9829 | 3.16% |
