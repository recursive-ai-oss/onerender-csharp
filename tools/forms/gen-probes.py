#!/usr/bin/env python3
"""Writes a probe page of controls (one per row, each followed by a zero-height baseline marker) and a manifest of labels.
Usage: gen-probes.py <name>   -> testdata/forms-probes/<name>/index.html and <name>.labels"""
import sys, os
name = sys.argv[1]
root = os.path.join(os.path.dirname(__file__), '..', '..', 'testdata', 'forms-probes')
rows = []
fonts = [('Arial', s) for s in (10, 13.3333, 16, 20, 30)] + [('serif', 13.3333), ('serif', 20), ('monospace', 13.3333), ('monospace', 20), ('Carlito', 14), ('Caladea', 14)]
if name == 'text':
    for f, s in fonts:
        for n in (1, 2, 10, 20):
            rows.append(('input size=%d font %s %g' % (n, f, s), '<input size=%d style="font:%gpx %s">' % (n, s, f)))
elif name == 'text2':
    for fam in ('Arial', 'serif', 'monospace'):
        for sz in range(8, 41):
            for n in (1, 10, 40):
                rows.append(('input size=%d font %s %d' % (n, fam, sz), '<input size=%d style=\"font:%dpx %s\">' % (n, sz, fam)))
elif name == 'select':
    for s in (10, 13.3333, 20, 30):
        st = 'font:%gpx Arial' % s
        rows.append(('select empty %g' % s, '<select style="%s"></select>' % st))
        rows.append(('select 1opt A %g' % s, '<select style="%s"><option>A</option></select>' % st))
        rows.append(('select 2opt %g' % s, '<select style="%s"><option>One</option><option>Longer option text</option></select>' % st))
        rows.append(('select size3 4opt %g' % s, '<select size=3 style="%s"><option>A</option><option>B</option><option>Cc</option><option>D</option></select>' % st))
        rows.append(('select multiple 2opt %g' % s, '<select multiple style="%s"><option>A</option><option>Bbbb</option></select>' % st))
    rows.append(('select optgroup', '<select><optgroup label="G"><option>A</option></optgroup></select>'))
    rows.append(('select size=4 empty', '<select size=4></select>'))
    rows.append(('select multiple empty', '<select multiple></select>'))
    rows.append(('select width 200', '<select style="width:200px"><option>A</option></select>'))
    rows.append(('select width 200 height 50', '<select style="width:200px;height:50px"><option>A</option></select>'))
    rows.append(('select pad 10', '<select style="padding:10px"><option>A</option></select>'))
    rows.append(('select selected-longer', '<select><option>A</option><option selected>Longest of all</option></select>'))
    rows.append(('select empty-option', '<select><option></option></select>'))
elif name == 'misc':
    for s in (10, 13.3333, 20, 30):
        st = 'font:%gpx Arial' % s
        rows.append(('button Click %g' % s, '<button style="%s">Click</button>' % st))
        rows.append(('input button Go %g' % s, '<input type=button value=Go style="%s">' % st))
        rows.append(('textarea default %g' % s, '<textarea style="%s"></textarea>' % st))
        rows.append(('textarea mono c5 r3 %g' % s, '<textarea cols=5 rows=3 style="font:%gpx monospace"></textarea>' % s))
        rows.append(('checkbox %g' % s, '<input type=checkbox style="%s">' % st))
        rows.append(('radio %g' % s, '<input type=radio style="%s">' % st))
        rows.append(('range %g' % s, '<input type=range style="%s">' % st))
        rows.append(('progress em %g' % s, '<progress style="%s"></progress>' % st))
        rows.append(('meter em %g' % s, '<meter style="%s"></meter>' % st))
    rows.append(('textarea overflow hidden', '<textarea style="overflow:hidden"></textarea>'))
    rows.append(('textarea cols=1', '<textarea cols=1 rows=1></textarea>'))
    rows.append(('textarea text 2 lines rows=1', '<textarea rows=1>a\nb</textarea>'))
    rows.append(('button width 200', '<button style="width:200px">W</button>'))
    rows.append(('button height 60', '<button style="height:60px">H</button>'))
    rows.append(('button two lines', '<button style="width:60px">aa bb cc dd</button>'))
    rows.append(('button block child', '<button><div style="width:50px;height:20px"></div></button>'))
    rows.append(('input height 50', '<input style="height:50px">'))
    rows.append(('input width 100pct in 300 box', '<div style="width:300px"><input style="width:100%"></div>'))
    rows.append(('input border-box 100pct', '<div style="width:300px"><input style="width:100%;box-sizing:border-box"></div>'))
    rows.append(('input display block', '<input style="display:block">'))
    rows.append(('checkbox display block', '<input type=checkbox style="display:block">'))
    rows.append(('input flex item', '<div style="display:flex;width:400px"><input><input style="flex:1"></div>'))
    rows.append(('input flex stretch', '<div style="display:flex;width:400px;height:50px"><input></div>'))
    rows.append(('button flex stretch', '<div style="display:flex;width:400px;height:50px"><button>B</button></div>'))
    rows.append(('input grid item', '<div style="display:grid;grid-template-columns:200px 1fr;width:400px"><input><input></div>'))
    rows.append(('input none appearance', '<input style="appearance:none">'))
    rows.append(('checkbox none appearance', '<input type=checkbox style="appearance:none">'))
    rows.append(('placeholder', '<input placeholder="Placeholder text here">'))
    rows.append(('value long', '<input value="A very long value that does not fit in the default width of the box">'))
    rows.append(('number value', '<input type=number value=12>'))
    rows.append(('search value', '<input type=search value=ab>'))
    rows.append(('legend long', '<fieldset style="width:200px"><legend>A legend that is rather long for the box</legend></fieldset>'))
    rows.append(('legend only', '<fieldset><legend>L</legend></fieldset>'))
    rows.append(('fieldset empty', '<fieldset></fieldset>'))
    rows.append(('fieldset legend float', '<fieldset><legend style="float:left">L</legend>X</fieldset>'))
    rows.append(('fieldset legend align right', '<fieldset><legend align=right>L</legend>X</fieldset>'))
    rows.append(('fieldset legend margin', '<fieldset><legend style="margin:10px;padding:5px;border:3px solid">L</legend>X</fieldset>'))
    rows.append(('fieldset border 10', '<fieldset style="border-width:10px"><legend>L</legend>X</fieldset>'))
    rows.append(('fieldset padding0 border0', '<fieldset style="border:0;padding:0"><legend>L</legend>X</fieldset>'))
    rows.append(('fieldset width auto in inline-block', '<span style="display:inline-block"><fieldset><legend>Wide legend text</legend>X</fieldset></span>'))
    rows.append(('fieldset flex', '<fieldset style="display:flex"><legend>L</legend><span>a</span><span>b</span></fieldset>'))
    rows.append(('label wrap', '<label>Name <input></label>'))
h = ['<!doctype html><html><head><meta charset=utf-8><style>body{margin:8px;font:16px/normal Arial} div.r{margin:6px 0;padding:0;border-bottom:1px dotted #ccc} i.m{display:inline-block;width:1px;height:0}</style></head><body>']
for lab, it in rows:
    h.append('<div class=r>%s<i class=m></i></div>' % it)
h.append('</body></html>')
d = os.path.join(root, name)
os.makedirs(d, exist_ok=True)
open(os.path.join(d, 'index.html'), 'w').write('\n'.join(h))
open(os.path.join(root, name + '.labels'), 'w').write('\n'.join(l for l, _ in rows) + '\n')
print(len(rows))
