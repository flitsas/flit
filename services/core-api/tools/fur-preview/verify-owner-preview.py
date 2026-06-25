#!/usr/bin/env python3
import fitz
from pathlib import Path

PREVIEW = Path(r"d:\Cursor\FLIT\2.0\services\core-api\artifacts\fur-analysis\fur-preview-matricula-YYY090.pdf")
needles = ["AMADO", "GARCIA", "DANIEL", "1193552679", "CALLE", "FUNZA", "3001234567", "X"]

doc = fitz.open(PREVIEW)
page = doc[0]
print("=== PREVIEW owner section values ===")
for block in page.get_text("dict")["blocks"]:
    for line in block.get("lines", []):
        for span in line.get("spans", []):
            t = span["text"].strip()
            if not t:
                continue
            x0, y0, x1, y1 = span["bbox"]
            if y0 > 330 or x0 > 500:
                continue
            for n in needles:
                if n in t.upper():
                    print(f"  {n:14} x={x0:6.1f} y={y0:6.1f}  {t!r}")
                    break
doc.close()
