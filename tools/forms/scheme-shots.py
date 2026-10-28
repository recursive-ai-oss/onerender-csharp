#!/usr/bin/env python3
"""Writes pages with every native control under a chosen `color-scheme` / `accent-color` / preferred scheme, to be shot in Chrome with shot-page.py.

Each control sits in its own cell of a grid (CELL_W x CELL_H px, 5 columns from (20, 20)), so a script can read pixels at known
coordinates. Usage (on the worker): scheme-shots.py <out-dir> <variant>...   where a variant is
    <name>=<root-style>[@dark]        e.g.  dark=color-scheme:dark   accent=accent-color:#d03060   ld-dark=color-scheme:light dark@dark
(the suffix `@dark` asks for a dark preferred scheme). Prints one shot-page.py spec per page:
    python3 shot-page.py shots $(python3 scheme-shots.py pages "dark=color-scheme:dark" ...) Standard library only.
"""
import os, subprocess, sys

CELL_W, CELL_H, COLS = 260, 44, 4

CONTROLS = [
    ("text", '<input value="Abc">'),
    ("placeholder", '<input placeholder="Ph">'),
    ("textarea", '<textarea style="width:100px;height:28px">Abc</textarea>'),
    ("select", '<select><option>One</option><option>Two</option></select>'),
    ("listbox", '<select multiple style="height:34px"><option selected>One</option><option>Two</option></select>'),
    ("button", '<button>Go</button>'),
    ("checkbox", '<input type=checkbox>'),
    ("checkbox-on", '<input type=checkbox checked>'),
    ("checkbox-ind", '<input type=checkbox id=ind>'),
    ("radio", '<input type=radio>'),
    ("radio-on", '<input type=radio checked>'),
    ("range", '<input type=range value=50>'),
    ("progress", '<progress value=0.5></progress>'),
    ("meter", '<meter value=0.5></meter>'),
    ("meter-low", '<meter min=0 max=10 low=5 high=8 optimum=9 value=2></meter>'),
    ("meter-mid", '<meter min=0 max=10 low=3 high=8 optimum=9 value=5></meter>'),
    ("fieldset", '<fieldset style="margin:0;padding:2px;width:100px;height:14px"><legend>L</legend></fieldset>'),
    ("password", '<input type=password value="abc">'),
    ("input-button", '<input type=button value="Go">'),
    ("readonly", '<input readonly value="Abc">'),
    ("progress-indet", '<progress></progress>'),
    ("color-text", '<span>Text</span>'),
]
DISABLEABLE = {"text", "placeholder", "textarea", "select", "listbox", "button", "checkbox", "checkbox-on", "checkbox-ind", "radio", "radio-on",
               "range", "progress", "meter", "password", "input-button"}


def cells():
    out = []
    for name, html in CONTROLS:
        out.append((name, html))
    for name, html in CONTROLS:
        if name in DISABLEABLE and name not in ("progress", "meter"):
            tag = html.split(" ", 1)[0].split(">", 1)[0]
            out.append(("dis-" + name, html.replace(tag, tag + " disabled", 1)))
    return out


def page(root_style, only=None, focus=False):
    body = []
    for i, (name, html) in enumerate(c for c in cells() if only is None or c[0] == only):
        x, y = 20 + (i % COLS) * CELL_W, 20 + (i // COLS) * CELL_H
        body.append('<div style="position:absolute;left:%dpx;top:%dpx;width:%dpx">%s</div>' % (x, y, CELL_W - 20, html))
    return ("<!DOCTYPE html><html style=\"%s\"><body style=\"margin:0\">%s<script>"
            "document.getElementById('ind') && (document.getElementById('ind').indeterminate=true);%s</script></body></html>" % (root_style, "".join(body), "document.body.querySelector('input,textarea,select,button').focus();" if focus else ""))


def main():
    out = os.path.abspath(sys.argv[1])
    os.makedirs(out, exist_ok=True)
    for spec in sys.argv[2:]:
        name, style = spec.split("=", 1)
        dark = style.endswith("@dark")
        if dark:
            style = style[: -len("@dark")]
        path = os.path.join(out, name + ".html")
        if name.startswith("focus-"):
            open(path, "w").write(page(style, name.split("-", 1)[1].split("~")[0], True))
        else:
            open(path, "w").write(page(style))
        print(path + ("@dark" if dark else ""))


main()
