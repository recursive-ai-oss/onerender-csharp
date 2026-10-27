#!/usr/bin/env python3
"""Writes the WOFF and WOFF2 test fonts from NotoSansHebrew-Subset-Regular.ttf.

Needs fontTools and brotli (pip install fonttools brotli). The WOFF2 file uses the glyf, loca and hmtx
transforms, so it exercises the whole decoder. The outputs are checked in; run this only to regenerate them.
"""
from fontTools.ttLib import TTFont, woff2

src = "NotoSansHebrew-Subset-Regular.ttf"

font = TTFont(src)
font.flavor = "woff"
font.save(src.replace(".ttf", ".woff"))

font = TTFont(src)
font.flavor = "woff2"
flavor_data = woff2.WOFF2FlavorData()
flavor_data.transformedTables = {"glyf", "loca", "hmtx"}
font.flavorData = flavor_data
font.save(src.replace(".ttf", ".woff2"))
