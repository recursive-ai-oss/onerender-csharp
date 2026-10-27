# Pixel similarity: tailwind

- Chrome: Chrome/156.0.8078.47; fonts: 8768bf2774f6c8e8
- Screenshots are cropped to 4000 CSS px tall; SSIM is on luma, 8x8 windows, stride 4. Information only.

| viewport | pages | mean SSIM | min SSIM | min page | pixels identical (mean) |
|---|---|---|---|---|---|
| 1280x800 | 7 | 0.9725 | 0.9272 | flowbite-components | 92.42% |
| 390x844 | 7 | 0.9506 | 0.8709 | flowbite-components | 88.28% |

## Worst pages

| page | viewport | SSIM | differing pixels |
|---|---|---|---|
| flowbite-components | 390x844 | 0.8709 | 16.73% |
| tw-shop | 390x844 | 0.8754 | 15.05% |
| flowbite-components | 1280x800 | 0.9272 | 10.27% |
| tw-shop | 1280x800 | 0.9595 | 2.40% |
| daisy-app | 1280x800 | 0.9610 | 3.11% |
