#!/usr/bin/env python3
"""Extrae celdas del encabezado derecho del blank FUR."""
from pathlib import Path
import fitz

BLANK = Path(r"d:\Cursor\FLIT\2.0\services\core-api\src\Flit.Infrastructure\Documents\Fur\Templates\fur-formulario-p1-blank.pdf")

doc = fitz.open(BLANK)
page = doc[0]
draws = page.get_drawings()
rects = []
for d in draws:
    r = d["rect"]
    if r.y1 < 85 and r.x0 > 580:
        rects.append(
            {
                "x0": round(r.x0, 1),
                "y0": round(r.y0, 1),
                "x1": round(r.x1, 1),
                "y1": round(r.y1, 1),
                "w": round(r.width, 1),
                "h": round(r.height, 1),
            }
        )

for r in sorted(rects, key=lambda x: (x["y0"], x["x0"])):
    print(f"y0={r['y0']:5.1f} y1={r['y1']:5.1f} x0={r['x0']:6.1f} x1={r['x1']:6.1f}  h={r['h']:4.1f}")

doc.close()
