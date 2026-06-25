#!/usr/bin/env python3
"""Celdas y labels propietario en blank + diff con FUR.pdf renderizado."""
from pathlib import Path
import fitz
import numpy as np
from PIL import Image

BLANK = Path(r"d:\Cursor\FLIT\2.0\services\core-api\src\Flit.Infrastructure\Documents\Fur\Templates\fur-formulario-p1-blank.pdf")
OFFICIAL = Path(r"d:\Cursor\FLIT\2.0\FUR.pdf")
W, H = 1008, 612

def render(path):
    doc = fitz.open(path)
    page = doc[0]
    mat = fitz.Matrix(W / page.rect.width, H / page.rect.height)
    pix = page.get_pixmap(matrix=mat, alpha=False)
    img = np.array(Image.frombytes("RGB", (pix.width, pix.height), pix.samples))
    doc.close()
    return img

def cells_owner_blank():
    doc = fitz.open(BLANK)
    page = doc[0]
    rows = []
    # name value row ~248-270
    for d in page.get_drawings():
        r = d["rect"]
        if 246 <= r.y0 <= 272 and r.x0 < 480 and r.width > 20:
            rows.append(("names", r))
        if 268 <= r.y0 <= 290 and r.x0 < 480 and 8 <= r.width <= 14:
            rows.append(("doc_cb", r))
        if 268 <= r.y0 <= 290 and r.x0 < 480 and r.width > 20 and r.height < 20:
            rows.append(("doc_num", r))
        if 305 <= r.y0 <= 320 and r.x0 < 530:
            if r.width > 40:
                rows.append(("contact", r))
    doc.close()
    return rows

print("=== NAME VALUE CELLS (blank) ===")
doc = fitz.open(BLANK)
page = doc[0]
for d in page.get_drawings():
    r = d["rect"]
    if 246 <= r.y0 <= 268 and r.x0 < 480 and r.width > 25 and r.height > 8:
        print(f"  y0={r.y0:.1f} y1={r.y1:.1f} x0={r.x0:.1f} x1={r.x1:.1f} cx={(r.x0+r.x1)/2:.1f}")
doc.close()

print("\n=== DOC TYPE LABELS with bbox ===")
doc = fitz.open(BLANK)
page = doc[0]
doc_labels = ["C.C", "NIT", "N.N", "PASAPORTE", "EXTRANJ", "IDENTI", "NUIP", "DIPLOMAT"]
for block in page.get_text("dict")["blocks"]:
    for line in block.get("lines", []):
        text = "".join(s["text"] for s in line["spans"]).strip()
        if not text: continue
        y0 = min(s["bbox"][1] for s in line["spans"])
        if not (262 <= y0 <= 268): continue
        x0 = min(s["bbox"][0] for s in line["spans"])
        x1 = max(s["bbox"][2] for s in line["spans"])
        if x0 > 480: continue
        if any(k in text.upper() for k in doc_labels):
            print(f"  {text!r:20} cx={(x0+x1)/2:.1f} y={y0:.1f}")
doc.close()

print("\n=== DOC CHECKBOX ROW y~293 ===")
doc = fitz.open(BLANK)
for d in doc[0].get_drawings():
    r = d["rect"]
    if 292 <= r.y0 <= 318 and r.x0 < 480 and 9 <= r.width <= 12 and r.height >= 10:
        print(f"  x0={r.x0:.1f} y0={r.y0:.1f} cx={r.x0+r.width/2:.1f} cy={r.y0+r.height/2:.1f}")
doc.close()

if OFFICIAL.exists():
    blank = render(BLANK)
    off = render(OFFICIAL)
    diff = np.abs(off.astype(int) - blank.astype(int)).sum(axis=2)
    mask = diff > 40
    # owner region
    sub = mask[230:330, 110:530]
    ys, xs = np.where(sub)
    if len(xs):
        print("\n=== FUR.pdf ink blobs owner region (y 230-330) ===")
        # cluster by proximity
        pts = list(zip(xs + 110, ys + 230))
        pts.sort()
        clusters = []
        for x, y in pts:
            merged = False
            for c in clusters:
                if abs(c[0]-x) < 25 and abs(c[1]-y) < 8:
                    c[2] += 1
                    c[0] = (c[0]+x)//2
                    c[1] = min(c[1], y)
                    merged = True
                    break
            if not merged:
                clusters.append([x, y, 1])
        clusters.sort(key=lambda c: (c[1], c[0]))
        for x, y, n in clusters[:40]:
            if n > 30:
                print(f"  ink cx~{x} y~{y} pixels={n}")
