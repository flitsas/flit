#!/usr/bin/env python3
"""Compara posiciones en FUR.pdf oficial vs blank para sección vehículo."""
from pathlib import Path
import fitz

OFFICIAL = Path(r"d:\Cursor\FLIT\2.0\FUR.pdf")
BLANK = Path(r"d:\Cursor\FLIT\2.0\services\core-api\src\Flit.Infrastructure\Documents\Fur\Templates\fur-formulario-p1-blank.pdf")

def find_x_marks(doc, y_min=80, y_max=320):
    marks = []
    page = doc[0]
    for block in page.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            for span in line.get("spans", []):
                t = span["text"].strip()
                if t == "X":
                    x0, y0, x1, y1 = span["bbox"]
                    if y_min <= y0 <= y_max:
                        marks.append((round(x0, 1), round(y0, 1), round(span.get("size", 0), 1)))
    return sorted(marks, key=lambda m: (m[1], m[0]))

def find_values(doc, needles, y_min=80, y_max=320):
    hits = []
    page = doc[0]
    for block in page.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            for span in line.get("spans", []):
                t = span["text"].strip()
                if not t:
                    continue
                x0, y0, x1, y1 = span["bbox"]
                if not (y_min <= y0 <= y_max):
                    continue
                for n in needles:
                    if n.upper() in t.upper():
                        hits.append((n, t[:40], round(x0, 1), round(y0, 1)))
                        break
    return sorted(hits, key=lambda h: (h[3], h[2]))

if OFFICIAL.exists():
    doc = fitz.open(OFFICIAL)
    print("=== OFFICIAL FUR.pdf X marks ===")
    for m in find_x_marks(doc):
        print(f"  x={m[0]:6.1f} y={m[1]:6.1f} fs={m[2]}")
    print("\n=== OFFICIAL sample values ===")
    needles = ["TESLA", "MODELO", "BLANCO", "CAMIONETA", "SUV", "LRWY", "PARTICUL", "GASOLINA", "ELECTRIC"]
    for h in find_values(doc, needles):
        print(f"  {h[0]!r:12} x={h[2]:6.1f} y={h[3]:6.1f} text={h[1]!r}")
    doc.close()
else:
    print("OFFICIAL not found")

# Checkbox cell centers from blank
print("\n=== BLANK checkbox cells (10x8 approx) ===")
doc = fitz.open(BLANK)
page = doc[0]
boxes = []
for d in page.get_drawings():
    r = d["rect"]
    # checkbox inner cells ~10 wide, ~8 tall
    if 9 <= r.width <= 12 and 7 <= r.height <= 9 and 80 <= r.y0 <= 310:
        boxes.append((round(r.x0, 1), round(r.y0, 1), round(r.width, 1), round(r.height, 1)))
for b in sorted(set(boxes), key=lambda x: (x[1], x[0])):
    cx = b[0] + b[2] / 2
    cy = b[1] + b[3] / 2
    print(f"  cell x0={b[0]:6.1f} y0={b[1]:6.1f} w={b[2]:4.1f} h={b[3]:4.1f}  center=({cx:.1f},{cy:.1f})")

print("\n=== BLANK text input cells (vehicle section) ===")
cells = []
for d in page.get_drawings():
    r = d["rect"]
    if 100 <= r.y0 <= 220 and r.width > 40 and 10 <= r.height <= 20:
        cells.append((round(r.x0,1), round(r.y0,1), round(r.x1,1), round(r.y1,1)))
for c in sorted(set(cells), key=lambda x: (x[1], x[0])):
    print(f"  y0={c[1]:5.1f} y1={c[3]:5.1f} x0={c[0]:6.1f} x1={c[2]:6.1f}")

doc.close()
