#!/usr/bin/env python3
from pathlib import Path
import fitz

BLANK = Path(r"d:\Cursor\FLIT\2.0\services\core-api\src\Flit.Infrastructure\Documents\Fur\Templates\fur-formulario-p1-blank.pdf")
OFFICIAL = Path(r"d:\Cursor\FLIT\2.0\FUR.pdf")
W, H = 1008, 612

doc = fitz.open(BLANK)
page = doc[0]

print("=== ALL TEXT y 220-330 x<500 ===")
for block in page.get_text("dict")["blocks"]:
    for line in block.get("lines", []):
        text = "".join(s["text"] for s in line["spans"]).strip()
        if not text: continue
        y0 = min(s["bbox"][1] for s in line["spans"])
        x0 = min(s["bbox"][0] for s in line["spans"])
        if 220 <= y0 <= 330 and x0 < 500:
            x1 = max(s["bbox"][2] for s in line["spans"])
            print(f"y={y0:5.1f} x={x0:6.1f}-{x1:5.1f}  {text[:60]}")

print("\n=== ALL DRAWING RECTS y 245-320 x<500 w>20 ===")
seen = set()
for d in page.get_drawings():
    r = d["rect"]
    if 245 <= r.y0 <= 320 and r.x0 < 500 and r.width > 20:
        key = (round(r.x0,1), round(r.y0,1), round(r.x1,1), round(r.y1,1))
        if key in seen: continue
        seen.add(key)
        print(f"y0={r.y0:5.1f} y1={r.y1:5.1f} x0={r.x0:6.1f} x1={r.x1:6.1f}")

doc.close()

# Official rendered text extraction
if OFFICIAL.exists():
    doc = fitz.open(OFFICIAL)
    page = doc[0]
    mat = fitz.Matrix(W / page.rect.width, H / page.rect.height)
    print("\n=== OFFICIAL scaled text y 220-330 x<500 ===")
    td = page.get_text("dict", matrix=mat)
    for block in td["blocks"]:
        for line in block.get("lines", []):
            text = "".join(s["text"] for s in line.get("spans", [])).strip()
            if not text: continue
            y0 = min(s["bbox"][1] for s in line["spans"])
            x0 = min(s["bbox"][0] for s in line["spans"])
            if 220 <= y0 <= 330 and x0 < 500:
                print(f"y={y0:5.1f} x={x0:6.1f}  {text[:60]}")
    doc.close()
