#!/usr/bin/env python3
"""Writes testdata/forms-themed: native controls under `color-scheme` (light, dark, light dark), with and without `accent-color`,
with `outline: auto` focus rings and in disabled states. Static pages, no script, so Chrome and the engine see the same document.
Run from anywhere:  python3 tools/forms/gen-themed.py   (standard library only; the output is committed)."""
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "testdata", "forms-themed")

CONTROLS = [
    ("text", '<input value="Abc">'),
    ("placeholder", '<input placeholder="Name">'),
    ("password", '<input type=password value="abc">'),
    ("textarea", '<textarea style="width:120px;height:34px">Notes</textarea>'),
    ("select", '<select><option>One</option><option>Two</option></select>'),
    ("listbox", '<select multiple style="height:40px"><option selected>One</option><option>Two</option></select>'),
    ("button", '<button>Save</button>'),
    ("input-button", '<input type=button value="Go">'),
    ("checkbox", '<input type=checkbox>'),
    ("checkbox-on", '<input type=checkbox checked>'),
    ("radio", '<input type=radio name=r>'),
    ("radio-on", '<input type=radio name=s checked>'),
    ("range", '<input type=range value=40>'),
    ("progress", '<progress value=0.6></progress>'),
    ("meter", '<meter value=0.5></meter>'),
    ("meter-low", '<meter min=0 max=10 low=5 high=8 optimum=9 value=2></meter>'),
    ("fieldset", '<fieldset style="margin:0;width:120px"><legend>Group</legend>x</fieldset>'),
]
DISABLEABLE = {"text", "placeholder", "password", "textarea", "select", "listbox", "button", "input-button", "checkbox", "checkbox-on", "radio",
               "radio-on", "range"}


def disabled(html):
    tag = html.split(" ", 1)[0].split(">", 1)[0]
    return html.replace(tag, tag + " disabled", 1)


def section(title, style, controls, extra="", cls=""):
    rows = "".join('<div class=row><span class=name>%s</span>%s</div>\n' % (n, h) for n, h in controls)
    return '<section class="%s" style="%s"><h2>%s</h2>\n%s%s</section>\n' % (cls, style, title, rows, extra)


def page(title, root, sections):
    return ("<!DOCTYPE html>\n<html lang=en style=\"%s\"><head><meta charset=utf-8><title>%s</title>\n<style>\n"
            "body{margin:0;font:13px Arial,sans-serif}\n"
            "section{padding:8px 16px;background:Canvas;color:CanvasText;overflow:hidden}\n"
            "h2{font-size:14px;margin:0 0 6px}\n"
            ".row{display:flex;align-items:center;gap:10px;margin:4px 0}\n"
            ".name{width:90px;flex:none}\n"
            ".ring input,.ring textarea,.ring select,.ring button{outline:auto}\n"
            "</style></head><body>\n%s</body></html>\n") % (root, title, "".join(sections))


def write(name, html):
    d = os.path.join(OUT, name)
    os.makedirs(d, exist_ok=True)
    open(os.path.join(d, "index.html"), "w").write(html)


def main():
    write("light", page("Light", "color-scheme:light", [section("color-scheme: light", "", CONTROLS)]))
    write("dark", page("Dark", "color-scheme:dark", [section("color-scheme: dark", "", CONTROLS)]))
    write("light-dark", page("Light dark", "color-scheme:light dark", [section("color-scheme: light dark", "", CONTROLS)]))
    write("nested", page("Nested", "", [
        section("normal", "", CONTROLS[:8]),
        section("dark subtree", "color-scheme:dark", CONTROLS[:8]),
        section("light under only dark", "color-scheme:only dark", CONTROLS[8:14])]))
    acc = [(n, h) for n, h in CONTROLS if n in ("checkbox-on", "radio-on", "range", "progress", "meter", "checkbox")]
    secs = []
    for scheme in ("light", "dark"):
        for color in ("#d03060", "#ffee00", "#2a9d4a", "#8b8b8b"):
            secs.append(section("%s, accent-color %s" % (scheme, color), "color-scheme:%s;accent-color:%s" % (scheme, color), acc))
    write("accent", page("Accent", "", secs))
    dis = [(n, disabled(h)) for n, h in CONTROLS if n in DISABLEABLE]
    write("disabled", page("Disabled", "", [
        section("disabled, light", "color-scheme:light", dis),
        section("disabled, dark", "color-scheme:dark", dis),
        section("disabled, accent-color ignored", "color-scheme:light;accent-color:#d03060", [c for c in dis if c[0] in ("checkbox-on", "radio-on", "range")])]))
    ring = [(n, h) for n, h in CONTROLS if n in ("text", "textarea", "select", "button", "checkbox-on", "radio-on", "range")]
    write("focus", page("Focus rings", "", [
        section("outline: auto, light", "color-scheme:light", ring, cls="ring"),
        section("outline: auto, dark", "color-scheme:dark", ring, cls="ring")]))


main()
