#!/usr/bin/env python3
"""Black-box probe of how Chrome snaps box edges to device pixels.

Renders small pages of boxes on fractional coordinates with headless Chrome (path in $CHROME) and prints, for each case, the
column (or row) run lengths of the painted result, so the rule can be read off. Standard library only.
"""
import os, struct, subprocess, sys, tempfile, zlib

CHROME = os.environ["CHROME"]


def png_rows(data):
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    pos, idat, w, h, ct = 8, b"", 0, 0, 0
    while pos < len(data):
        n, typ = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + n]
        if typ == b"IHDR":
            w, h, depth, ct = struct.unpack(">IIBB", body[:10])
            assert depth == 8
        elif typ == b"IDAT":
            idat += body
        pos += 12 + n
    bpp = {2: 3, 6: 4}[ct]
    raw = zlib.decompress(idat)
    stride = w * bpp
    rows, prev = [], bytearray(stride)
    i = 0
    for _ in range(h):
        f = raw[i]
        line = bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = prev[x]
            c = prev[x - bpp] if x >= bpp else 0
            if f == 1: line[x] = (line[x] + a) & 255
            elif f == 2: line[x] = (line[x] + b) & 255
            elif f == 3: line[x] = (line[x] + ((a + b) >> 1)) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        rows.append([tuple(line[k:k + 3]) for k in range(0, stride, bpp)])
        prev = line
    return rows


def shot(html, w, h, dpr):
    with tempfile.TemporaryDirectory() as d:
        page, out = os.path.join(d, "p.html"), os.path.join(d, "o.png")
        open(page, "w").write("<!DOCTYPE html><body style='margin:0;background:#fff'>" + html)
        subprocess.run([CHROME, "--headless=new", "--no-sandbox", "--disable-gpu", "--hide-scrollbars", "--font-render-hinting=none",
                        "--force-device-scale-factor=%s" % dpr, "--window-size=%d,%d" % (w, h), "--screenshot=" + out, "file://" + page],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        return png_rows(open(out, "rb").read())


NAMES = {(255, 255, 255): ".", (255, 0, 0): "R", (0, 0, 255): "B"}


def line(px):
    return "".join(NAMES.get(p, "?") for p in px)


def box(l, t, w, h, c):
    return "<div style='position:absolute;left:%spx;top:%spx;width:%spx;height:%spx;background:%s'></div>" % (l, t, w, h, c)


CASES = [
    ("left 10.4 width 10.4", box(10.4, 2, 10.4, 4, "#f00"), 1),
    ("left 10.5 width 10", box(10.5, 2, 10, 4, "#f00"), 1),
    ("left 10.6 width 10", box(10.6, 2, 10, 4, "#f00"), 1),
    ("left 10.5 width 0.4", box(10.5, 2, 0.4, 4, "#f00"), 1),
    ("left 0 width 10.5 + left 10.5 width 10.5", box(0, 2, 10.5, 4, "#f00") + box(10.5, 2, 10.5, 4, "#00f"), 1),
    ("left 0 width 10.3 + left 10.3 width 10.3", box(0, 2, 10.3, 4, "#f00") + box(10.3, 2, 10.3, 4, "#00f"), 1),
    ("dpr2 left 10.25 width 10", box(10.25, 1, 10, 2, "#f00"), 2),
    ("dpr2 left 10.2 width 10", box(10.2, 1, 10, 2, "#f00"), 2),
    ("dpr1.5 left 10.2 width 10", box(10.2, 1, 10, 2, "#f00"), 1.5),
]
BORDERS = [("border %spx" % bw, "<div style='position:absolute;left:2px;top:2px;width:10px;height:6px;border:%spx solid #f00;box-sizing:content-box'></div>" % bw, 1)
           for bw in ("0.4", "0.9", "1.0", "1.6", "2.5", "2.99")]
BORDERS += [("dpr2 border %spx" % bw, "<div style='position:absolute;left:2px;top:2px;width:10px;height:6px;border:%spx solid #f00'></div>" % bw, 2) for bw in ("0.4", "0.6", "1.3", "1.6")]
VERT = [("top 10.5 height 10 (rows)", box(2, 10.5, 4, 10, "#f00"), 1), ("top 10.4 height 10.4 (rows)", box(2, 10.4, 4, 10.4, "#f00"), 1)]

for name, html, dpr in CASES + BORDERS:
    rows = shot(html, 40, 12, dpr)
    y = int(2.5 * dpr) if "border" not in name else int(5 * dpr)
    print("%-44s %s" % (name, line(rows[y][: int(30 * dpr)])))
for name, html, dpr in VERT:
    rows = shot(html, 12, 30, dpr)
    print("%-44s %s" % (name, line([rows[y][4] for y in range(24)])))
