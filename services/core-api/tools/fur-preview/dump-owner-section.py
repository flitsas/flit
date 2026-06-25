#!/usr/bin/env python3
"""Analiza sección DATOS DEL PROPIETARIO en blank y FUR.pdf."""
from pathlib import Path
import fitz

BLANK = Path(r"d:\Cursor\FLIT\2.0\services\core-api\src\Flit.Infrastructure\Documents\Fur\Templates\fur-formulario-p1-blank.pdf")
OFFICIAL = Path(r"d:\Cursor\FLIT\2.0\FUR.pdf")
PREVIEW = Path(r"d:\Cursor\FLIT\2.0\services\core-api\artifacts\fur-analysis\fur-preview-matricula-YYY090.pdf")

LABELS = [
    "PRIMER APELLIDO", "SEGUNDO APELLIDO", "NOMBRES",
    "C.C", "NIT", "N.N", "PASAPORTE", "C.EXTRANJ", "T.IDENTI", "NUIP",
    "DIPLOMATICO", "DOCUMENTO", "DIRECCION", "CIUDAD", "TELEFONO",
    "DATOS DEL PROPIETARIO",
]

def labels_in_region(doc, y_min=220, y_max=320, x_max=500):
    page = doc[0]
    out = []
    for block in page.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            text = "".join(s["text"] for s in line["spans"]).strip()
            if not text:
                continue
            y0 = min(s["bbox"][1] for s in line["spans"])
            x0 = min(s["bbox"][0] for s in line["spans"])
            if not (y_min <= y0 <= y_max and x0 < x_max):
                continue
            upper = text.upper()
            for lb in LABELS:
                if lb in upper.replace(".", "").replace("Ó", "O"):
                    x1 = max(s["bbox"][2] for s in line["spans"])
                    y1 = max(s["bbox"][3] for s in line["spans"])
                    out.append({
                        "label": lb,
                        "text": text[:50],
                        "x0": round(x0, 1),
                        "y0": round(y0, 1),
                        "x1": round(x1, 1),
                        "y1": round(y1, 1),
                        "cx": round((x0 + x1) / 2, 1),
                    })
                    break
    return sorted(out, key=lambda r: (r["y0"], r["x0"]))

def value_cells(doc, y_min=248, y_max=320, x_max=500):
    page = doc[0]
    cells = []
    for d in page.get_drawings():
        r = d["rect"]
        if y_min <= r.y0 <= y_max and r.x0 < x_max and r.width > 25 and 8 <= r.height <= 25:
            cells.append({
                "x0": round(r.x0, 1),
                "y0": round(r.y0, 1),
                "x1": round(r.x1, 1),
                "y1": round(r.y1, 1),
                "w": round(r.width, 1),
                "h": round(r.height, 1),
            })
    seen = set()
    out = []
    for c in sorted(cells, key=lambda x: (x["y0"], x["x0"])):
        key = (c["x0"], c["y0"], c["x1"], c["y1"])
        if key in seen:
            continue
        seen.add(key)
        out.append(c)
    return out

def doc_checkboxes(doc, y_min=258, y_max=320, x_max=500):
    page = doc[0]
    boxes = []
    for d in page.get_drawings():
        r = d["rect"]
        if y_min <= r.y0 <= y_max and r.x0 < x_max and 8 <= r.width <= 14 and 6 <= r.height <= 16:
            boxes.append((round(r.x0, 1), round(r.y0, 1), round(r.width, 1), round(r.height, 1)))
    return sorted(set(boxes), key=lambda b: (b[1], b[0]))

def extract_values(doc, needles, y_min=248, y_max=320, x_max=500):
    page = doc[0]
    hits = []
    for block in page.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            for span in line.get("spans", []):
                t = span["text"].strip()
                if not t:
                    continue
                x0, y0, x1, y1 = span["bbox"]
                if not (y_min <= y0 <= y_max and x0 < x_max):
                    continue
                for n in needles:
                    if n.upper() in t.upper():
                        hits.append((n, t[:40], round(x0, 1), round(y0, 1)))
                        break
    return sorted(hits, key=lambda h: (h[3], h[2]))

for name, path in [("BLANK", BLANK), ("OFFICIAL", OFFICIAL), ("PREVIEW", PREVIEW)]:
    if not path.exists():
        print(f"\n=== {name}: not found ===")
        continue
    doc = fitz.open(path)
    print(f"\n=== {name} LABELS ===")
    for r in labels_in_region(doc):
        print(f"  y={r['y0']:5.1f} x={r['x0']:6.1f} cx={r['cx']:6.1f}  {r['text']!r}")
    print(f"\n=== {name} VALUE CELLS ===")
    for c in value_cells(doc):
        print(f"  y0={c['y0']:5.1f} y1={c['y1']:5.1f} x0={c['x0']:6.1f} x1={c['x1']:6.1f}")
    print(f"\n=== {name} DOC CHECKBOXES ===")
    for b in doc_checkboxes(doc):
        cx = b[0] + b[2] / 2
        cy = b[1] + b[3] / 2
        print(f"  x0={b[0]:6.1f} y0={b[1]:5.1f} cx={cx:6.1f} cy={cy:5.1f}")
    if name == "OFFICIAL":
        needles = ["GARCIA", "DANIEL", "AMADO", "1193552679", "CALLE", "FUNZA", "3001234567"]
        print(f"\n=== {name} VALUES ===")
        for h in extract_values(doc, needles):
            print(f"  {h[0]!r:14} x={h[2]:6.1f} y={h[3]:6.1f} text={h[1]!r}")
        print("\n=== OFFICIAL X marks owner section ===")
        for block in doc[0].get_text("dict")["blocks"]:
            for line in block.get("lines", []):
                for span in line.get("spans", []):
                    if span["text"].strip() == "X" and 258 <= span["bbox"][1] <= 320 and span["bbox"][0] < 500:
                        print(f"  X at x={span['bbox'][0]:.1f} y={span['bbox'][1]:.1f}")
    doc.close()
