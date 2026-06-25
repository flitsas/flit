#!/usr/bin/env python3
"""Diff FUR.pdf vs blank para ubicar valores propietario."""
from pathlib import Path
import fitz
import numpy as np
from PIL import Image

BLANK = Path(r"d:\Cursor\FLIT\2.0\services\core-api\src\Flit.Infrastructure\Documents\Fur\Templates\fur-formulario-p1-blank.pdf")
OFFICIAL = Path(r"d:\Cursor\FLIT\2.0\FUR.pdf")
OUT = Path(r"d:\Cursor\FLIT\2.0\services\core-api\artifacts\fur-analysis\owner-diff-crop.png")
W, H = 1008, 612

def render(path):
    doc = fitz.open(path)
    page = doc[0]
    mat = fitz.Matrix(W / page.rect.width, H / page.rect.height)
    pix = page.get_pixmap(matrix=mat, alpha=False)
    img = np.array(Image.frombytes("RGB", (pix.width, pix.height), pix.samples))
    doc.close()
    return img

blank = render(BLANK)
off = render(OFFICIAL)
diff = np.abs(off.astype(int) - blank.astype(int)).sum(axis=2)
mask = diff > 45

# crop owner left
y0, y1 = 232, 325
x0, x1 = 140, 490
sub = mask[y0:y1, x0:x1]
Image.fromarray((sub * 255).astype(np.uint8)).save(OUT)

# connected components simple
visited = np.zeros_like(sub, dtype=bool)
blobs = []
h, w = sub.shape
for y in range(h):
    for x in range(w):
        if not sub[y, x] or visited[y, x]:
            continue
        stack = [(y, x)]
        minx = maxx = x
        miny = maxy = y
        n = 0
        while stack:
            cy, cx = stack.pop()
            if cy < 0 or cy >= h or cx < 0 or cx >= w or visited[cy, cx] or not sub[cy, cx]:
                continue
            visited[cy, cx] = True
            n += 1
            minx = min(minx, cx); maxx = max(maxx, cx)
            miny = min(miny, cy); maxy = max(maxy, cy)
            stack.extend([(cy+1,cx),(cy-1,cx),(cy,cx+1),(cy,cx-1)])
        if n > 25:
            blobs.append({
                "x": x0 + minx,
                "y": y0 + miny,
                "w": maxx - minx + 1,
                "h": maxy - miny + 1,
                "cx": x0 + (minx + maxx) / 2,
                "cy": y0 + (miny + maxy) / 2,
                "pixels": n,
            })

blobs.sort(key=lambda b: (b["y"], b["x"]))
print(f"Wrote {OUT}\n=== Owner ink blobs from FUR.pdf ===")
for b in blobs:
    print(f"  x={b['x']:3.0f}-{b['x']+b['w']:3.0f} y={b['y']:3.0f}-{b['y']+b['h']:3.0f} cx={b['cx']:6.1f} cy={b['cy']:5.1f} px={b['pixels']}")
