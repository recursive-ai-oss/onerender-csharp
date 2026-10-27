#!/usr/bin/env python3
"""Compares the raster backend's picture of each template page with a PDF viewer's rendering of the PDF (information only).

Inputs, in the directory given as argv[1] (the output of tools/pdf/run-verapdf.sh with the PixelReferenceTests files beside it):
  <name>.pdf and <name>-<n>.ref.ppm. pdftoppm renders each PDF page at 96 dpi; the content area (page minus margins) is cropped
  and compared by mean SSIM over 7x7 uniform windows on luma (Wang et al. 2004 constants), plus the mean absolute difference.
Prints one line per page. Needs numpy and pdftoppm.
"""
import glob, os, subprocess, sys, tempfile
import numpy as np


def read_ppm(path):
    with open(path, "rb") as f:
        data = f.read()
    parts = data.split(b"\n", 3)
    w, h = map(int, parts[1].split())
    return np.frombuffer(parts[3], dtype=np.uint8)[: w * h * 3].reshape(h, w, 3)


def luma(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def box(a, k):
    c = np.cumsum(np.cumsum(np.pad(a, ((1, 0), (1, 0))), 0), 1)
    return (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)


def ssim(a, b, k=7):
    a, b = luma(a.astype(np.float64)), luma(b.astype(np.float64))
    c1, c2 = (0.01 * 255) ** 2, (0.03 * 255) ** 2
    ma, mb = box(a, k), box(b, k)
    va, vb, cab = box(a * a, k) - ma * ma, box(b * b, k) - mb * mb, box(a * b, k) - ma * mb
    s = ((2 * ma * mb + c1) * (2 * cab + c2)) / ((ma * ma + mb * mb + c1) * (va + vb + c2))
    return float(s.mean())


def main():
    d = sys.argv[1]
    margins = {"certificate": 24}
    for pdf in sorted(glob.glob(os.path.join(d, "*.pdf"))):
        name = os.path.basename(pdf)[:-4]
        if not glob.glob(os.path.join(d, name + "-1.ref.ppm")):
            continue
        m = margins.get(name, 48)
        with tempfile.TemporaryDirectory(dir=d) as t:
            subprocess.run(["pdftoppm", "-r", "96", pdf, os.path.join(t, "p")], check=True)
            for ref in sorted(glob.glob(os.path.join(d, name + "-*.ref.ppm")), key=lambda p: int(p.rsplit("-", 1)[1].split(".")[0])):
                n = int(ref.rsplit("-", 1)[1].split(".")[0])
                rendered = sorted(glob.glob(os.path.join(t, "p-*.ppm")))[n - 1]
                a, b = read_ppm(ref), read_ppm(rendered)
                h, w = a.shape[:2]
                b = b[m : m + h, m : m + w]
                if b.shape != a.shape:
                    print(f"{name} page {n}: size mismatch {a.shape} vs {b.shape}")
                    continue
                diff = float(np.abs(a.astype(int) - b.astype(int)).mean())
                print(f"{name} page {n}: ssim {ssim(a, b):.4f} mean|diff| {diff:.2f}")


main()
