#!/usr/bin/env python3
"""Makes variant pages of a tailwind page with injected CSS: variants.py <page-dir> <out-dir> 'css1' 'css2' ..."""
import sys, os, shutil
src, out = sys.argv[1], sys.argv[2]
shutil.rmtree(out, ignore_errors=True)
os.makedirs(out)
variants = [''] + sys.argv[3:]
for i, css in enumerate(variants):
    d = os.path.join(out, 'v%02d' % i)
    shutil.copytree(src, d)
    p = os.path.join(d, 'index.html')
    h = open(p).read()
    if css:
        h = h.replace('</body>', '<style>' + css + '</style></body>', 1)
    open(p, 'w').write(h)
    print('v%02d' % i, css)
