#!/usr/bin/env python3
"""Dibuja cajas del manifest sobre blank para QA visual."""
from __future__ import annotations

import json
from pathlib import Path

import fitz
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "src/Flit.Infrastructure/Documents/Fur/fur-field-manifest.json"
BLANK = ROOT / "src/Flit.Infrastructure/Documents/Fur/Templates/fur-formulario-p1-blank.pdf"
OUT = ROOT / "artifacts/fur-analysis/manifest-overlay-debug.png"
W, H = 1008, 612


def main() -> None:
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    doc = fitz.open(BLANK)
    page = doc[0]
    mat = fitz.Matrix(W / page.rect.width, H / page.rect.height)
    pix = page.get_pixmap(matrix=mat, alpha=False)
    doc.close()
    img = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
    draw = ImageDraw.Draw(img)
    for f in manifest["fields"]:
        if f.get("type") == "checkbox":
            s = f.get("size", 9)
            draw.rectangle([f["x"], f["y"], f["x"] + s, f["y"] + s], outline="red", width=1)
        else:
            w = f.get("w", 40)
            h = f.get("h", 12)
            draw.rectangle([f["x"], f["y"], f["x"] + w, f["y"] + h], outline="lime", width=1)
        draw.text((f["x"], max(0, f["y"] - 8)), f["id"][:18], fill="blue")
    img.save(OUT)
    print(f"Wrote {OUT}")


if __name__ == "__main__":
    main()
