#!/usr/bin/env python3
"""Reads Chrome's computed styles for controls and the system colours under a `color-scheme` (black-box output of getComputedStyle).

Usage (on the worker): scheme-styles.py <root-style>[@dark] ...   Prints one block per variant. Environment: CHROME, FONTCONFIG_FILE.
"""
import json, os, re, subprocess, sys, tempfile

SYSTEM = ("AccentColor AccentColorText ActiveText ButtonBorder ButtonFace ButtonText Canvas CanvasText Field FieldText GrayText Highlight "
          "HighlightText LinkText Mark MarkText SelectedItem SelectedItemText VisitedText ActiveBorder ActiveCaption AppWorkspace Background "
          "ButtonHighlight ButtonShadow CaptionText InactiveBorder InactiveCaption InactiveCaptionText InfoBackground InfoText Menu MenuText "
          "Scrollbar ThreeDDarkShadow ThreeDFace ThreeDHighlight ThreeDLightShadow ThreeDShadow Window WindowFrame WindowText").split()
CONTROLS = [
    ("text", "<input value=a>"), ("text-dis", "<input value=a disabled>"), ("text-ro", "<input value=a readonly>"), ("textarea", "<textarea>a</textarea>"),
    ("textarea-dis", "<textarea disabled>a</textarea>"), ("select", "<select><option>a</option></select>"),
    ("select-dis", "<select disabled><option>a</option></select>"), ("listbox", "<select multiple><option>a</option></select>"),
    ("option", "<select><option id=o>a</option></select>"), ("button", "<button>a</button>"), ("button-dis", "<button disabled>a</button>"),
    ("checkbox", "<input type=checkbox>"), ("radio", "<input type=radio>"), ("range", "<input type=range>"), ("progress", "<progress></progress>"),
    ("meter", "<meter></meter>"), ("fieldset", "<fieldset></fieldset>"), ("legend", "<fieldset><legend>l</legend></fieldset>"),
    ("input-button", "<input type=button value=a>"), ("hr", "<hr>"), ("a", "<a href=#>a</a>"), ("mark", "<mark>a</mark>"),
    ("kbd", "<kbd>a</kbd>"), ("table", "<table><tr><td>a</td></tr></table>"), ("body", ""),
]
PROPS = ["color", "background-color", "border-top-color", "border-right-color", "border-bottom-color", "border-left-color", "border-top-style",
         "border-top-width", "outline-color", "outline-style", "outline-width", "caret-color", "accent-color", "color-scheme", "text-decoration-color",
         "box-shadow", "background-image"]

SCRIPT = """
var out = {sys: {}, ctl: {}};
var S = %s, C = %s, P = %s;
S.forEach(function (n) { var d = document.createElement('div'); d.style.color = n; document.body.appendChild(d); out.sys[n] = getComputedStyle(d).color; d.remove(); });
C.forEach(function (c) {
  var host = document.createElement('div'); host.innerHTML = c[1]; document.body.appendChild(host);
  var el = host.firstElementChild || host; if (c[0] === 'option') el = host.querySelector('option');
  if (c[0] === 'legend') el = host.querySelector('legend');
  var cs = getComputedStyle(el), r = {};
  P.forEach(function (p) { r[p] = cs.getPropertyValue(p); });
  if (c[0] === 'text' || c[0] === 'textarea') { var ph = getComputedStyle(el, '::placeholder'); r['::placeholder color'] = ph.color; var se = getComputedStyle(el, '::selection'); r['::selection color'] = se.color; r['::selection bg'] = se.backgroundColor; }
  out.ctl[c[0]] = r; host.remove();
});
document.documentElement.setAttribute('data-out', JSON.stringify(out));
"""


def run(spec):
    dark = spec.endswith("@dark")
    style = spec[:-5] if dark else spec
    html = ("<!DOCTYPE html><html style=\"%s\"><body>" % style) + "<script>" + SCRIPT % (json.dumps(SYSTEM), json.dumps(CONTROLS), json.dumps(PROPS)) + "</script></body></html>"
    with tempfile.TemporaryDirectory() as d:
        path = os.path.join(d, "p.html")
        open(path, "w").write(html)
        args = [os.environ["CHROME"], "--headless=new", "--no-sandbox", "--disable-gpu", "--dump-dom", "--force-color-profile=srgb"]
        if dark:
            args.append("--blink-settings=preferredColorScheme=1")
        dom = subprocess.run(args + ["file://" + path], check=True, capture_output=True, text=True).stdout
    m = re.search(r"data-out=\"([^\"]*)\"", dom)
    import html as h
    return json.loads(h.unescape(m.group(1)))


for spec in sys.argv[1:]:
    o = run(spec)
    print("=== %s" % spec)
    print("system:", " ".join("%s=%s" % (k, v.replace("rgb(", "").replace(")", "").replace(", ", ",")) for k, v in o["sys"].items()))
    for name, r in o["ctl"].items():
        print("--", name)
        for k, v in r.items():
            print("   %s: %s" % (k, v))
