#!/usr/bin/env python3
"""Centra checkboxes bajo labels de clase y servicio."""
from pathlib import Path
import fitz

BLANK = Path(r"d:\Cursor\FLIT\2.0\services\core-api\src\Flit.Infrastructure\Documents\Fur\Templates\fur-formulario-p1-blank.pdf")

doc = fitz.open(BLANK)
page = doc[0]

targets = ["CAMIONETA", "PARTICUL", "AUTOMOVIL", "MOTOCICLETA", "PUBLICO", "DIPLOMATI"]

print("=== LABEL BBOX ===")
for block in page.get_text("dict")["blocks"]:
    for line in block.get("lines", []):
        text = "".join(s["text"] for s in line["spans"]).strip()
        for t in targets:
            if t in text.upper():
                x0 = min(s["bbox"][0] for s in line["spans"])
                y0 = min(s["bbox"][1] for s in line["spans"])
                x1 = max(s["bbox"][2] for s in line["spans"])
                y1 = max(s["bbox"][3] for s in line["spans"])
                cx = (x0 + x1) / 2
                print(f"{t:12} label x0={x0:6.1f} x1={x1:6.1f} cx={cx:6.1f} y0={y0:6.1f} y1={y1:6.1f}")

print("\n=== CLASS CHECKBOX CELLS y~181-208 ===")
for d in page.get_drawings():
    r = d["rect"]
    if 178 <= r.y0 <= 212 and 9 <= r.width <= 12 and 7 <= r.height <= 11:
        cx = r.x0 + r.width / 2
        cy = r.y0 + r.height / 2
        print(f"  cell x0={r.x0:6.1f} y0={r.y0:6.1f} w={r.width:4.1f} cx={cx:6.1f} cy={cy:6.1f}")

print("\n=== SERVICE CHECKBOX CELLS y~286-295 ===")
for d in page.get_drawings():
    r = d["rect"]
    if 284 <= r.y0 <= 296 and r.width < 2 and r.height > 6:
        # vertical lines - find checkbox columns
        pass

# service cells: full checkbox boxes
boxes = []
for d in page.get_drawings():
    r = d["rect"]
    if 284 <= r.y0 <= 310 and 9 <= r.width <= 12 and 6 <= r.height <= 14:
        boxes.append(r)
seen = set()
for r in sorted(boxes, key=lambda x: (x.y0, x.x0)):
    key = (round(r.x0, 1), round(r.y0, 1))
    if key in seen:
        continue
    seen.add(key)
    cx = r.x0 + r.width / 2
    cy = r.y0 + r.height / 2
    print(f"  cell x0={r.x0:6.1f} y0={r.y0:6.1f} w={r.width:4.1f} h={r.height:4.1f} cx={cx:6.1f} cy={cy:6.1f}")

doc.close()

# Checkbox draw: baseline = field.Y + size*0.85, X char ~7px wide at 11pt
# field.X is left of X; to center under label cx: field.X = label_cx - 3.5
# field.Y from desired baseline: field.Y = baseline - size*0.85
