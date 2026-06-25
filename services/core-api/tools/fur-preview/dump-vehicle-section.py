#!/usr/bin/env python3
"""Extrae celdas y casillas de la sección vehículo del blank FUR."""
from pathlib import Path
import fitz

BLANK = Path(r"d:\Cursor\FLIT\2.0\services\core-api\src\Flit.Infrastructure\Documents\Fur\Templates\fur-formulario-p1-blank.pdf")
GOLDEN_PDF = Path(r"d:\Cursor\FLIT\2.0\services\core-api\artifacts\fur-analysis\fur-preview-matricula-YYY090.pdf")

doc = fitz.open(BLANK)
page = doc[0]

print("=== TEXT LABELS y 80-320 ===")
for block in page.get_text("dict")["blocks"]:
    for line in block.get("lines", []):
        text = "".join(s["text"] for s in line["spans"]).strip()
        if not text:
            continue
        y0 = min(s["bbox"][1] for s in line["spans"])
        if 80 <= y0 <= 320:
            x0 = min(s["bbox"][0] for s in line["spans"])
            print(f"y={y0:6.1f} x={x0:6.1f}  {text[:80]}")

print("\n=== SMALL RECTS (checkbox candidates) y 80-320 ===")
draws = page.get_drawings()
cands = []
for d in draws:
    r = d["rect"]
    if 80 <= r.y0 <= 320 and r.width < 15 and r.height < 15:
        cands.append((round(r.x0, 1), round(r.y0, 1), round(r.width, 1), round(r.height, 1)))
for c in sorted(cands, key=lambda x: (x[1], x[0])):
    print(f"x={c[0]:6.1f} y={c[1]:6.1f} w={c[2]:4.1f} h={c[3]:4.1f}")

print("\n=== INPUT CELLS y 95-220 ===")
cells = []
for d in draws:
    r = d["rect"]
    if 95 <= r.y0 <= 220 and r.width > 30 and 8 < r.height < 25:
        cells.append(
            {
                "x0": round(r.x0, 1),
                "y0": round(r.y0, 1),
                "x1": round(r.x1, 1),
                "y1": round(r.y1, 1),
                "w": round(r.width, 1),
                "h": round(r.height, 1),
            }
        )
seen = set()
for c in sorted(cells, key=lambda x: (x["y0"], x["x0"])):
    key = (c["x0"], c["y0"], c["x1"], c["y1"])
    if key in seen:
        continue
    seen.add(key)
    print(
        f"y0={c['y0']:5.1f} y1={c['y1']:5.1f} "
        f"x0={c['x0']:6.1f} x1={c['x1']:6.1f}  h={c['h']:4.1f}"
    )

# Golden preview X marks
if GOLDEN_PDF.exists():
    print("\n=== GOLDEN PREVIEW X marks y 80-320 ===")
    gdoc = fitz.open(GOLDEN_PDF)
    gp = gdoc[0]
    for block in gp.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            for span in line.get("spans", []):
                text = span["text"].strip()
                if text == "X":
                    x0, y0, x1, y1 = span["bbox"]
                    if 80 <= y0 <= 320:
                        print(f"X at x={x0:6.1f} y={y0:6.1f}")
    gdoc.close()

doc.close()
