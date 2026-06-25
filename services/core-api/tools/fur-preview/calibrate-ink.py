#!/usr/bin/env python3
"""Localiza tinta en golden vs blank y sugiere coordenadas manifest."""
from __future__ import annotations

import json
from pathlib import Path

import fitz
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "artifacts" / "fur-analysis"
BLANK = ROOT / "src/Flit.Infrastructure/Documents/Fur/Templates/fur-formulario-p1-blank.pdf"
GOLDEN = Path(r"d:\Cursor\FLIT\2.0\FUR.pdf")
PREVIEW = ART / "fur-preview-matricula-YYY090.pdf"
W, H = 1008, 612


def render(path: Path) -> np.ndarray:
    doc = fitz.open(path)
    page = doc[0]
    mat = fitz.Matrix(W / page.rect.width, H / page.rect.height)
    pix = page.get_pixmap(matrix=mat, alpha=False)
    doc.close()
    img = np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.width, 3)
    return img


def ink_mask(a: np.ndarray, b: np.ndarray, thresh: int = 35) -> np.ndarray:
    diff = np.abs(a.astype(np.int16) - b.astype(np.int16)).max(axis=2)
    return diff > thresh


def blobs(mask: np.ndarray, min_pixels: int = 40) -> list[dict]:
    visited = np.zeros(mask.shape, dtype=bool)
    h, w = mask.shape
    found: list[dict] = []
    for y in range(h):
        for x in range(w):
            if not mask[y, x] or visited[y, x]:
                continue
            stack = [(x, y)]
            pts: list[tuple[int, int]] = []
            visited[y, x] = True
            while stack:
                cx, cy = stack.pop()
                pts.append((cx, cy))
                for nx, ny in ((cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1)):
                    if 0 <= nx < w and 0 <= ny < h and mask[ny, nx] and not visited[ny, nx]:
                        visited[ny, nx] = True
                        stack.append((nx, ny))
            if len(pts) < min_pixels:
                continue
            xs = [p[0] for p in pts]
            ys = [p[1] for p in pts]
            found.append(
                {
                    "x": round(min(xs), 1),
                    "y": round(min(ys), 1),
                    "w": round(max(xs) - min(xs), 1),
                    "h": round(max(ys) - min(ys), 1),
                    "cx": round(sum(xs) / len(xs), 1),
                    "cy": round(sum(ys) / len(ys), 1),
                    "pixels": len(pts),
                }
            )
    return sorted(found, key=lambda b: (b["y"], b["x"]))


def main() -> None:
    blank = render(BLANK)
    golden = render(GOLDEN)
    preview = render(PREVIEW)
    g_mask = ink_mask(golden, blank)
    p_mask = ink_mask(preview, blank)
    g_blobs = blobs(g_mask)
    p_blobs = blobs(p_mask)
    report = {
        "golden_blob_count": len(g_blobs),
        "preview_blob_count": len(p_blobs),
        "golden_blobs_top40": g_blobs[:40],
        "preview_blobs_top40": p_blobs[:40],
    }
    out = ART / "ink-blobs.json"
    out.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"golden blobs: {len(g_blobs)}, preview blobs: {len(p_blobs)}")
    print("GOLDEN top blobs:")
    for b in g_blobs[:25]:
        print(f"  y={b['y']:5.1f} x={b['x']:5.1f} w={b['w']:5.1f} h={b['h']:4.1f} px={b['pixels']}")
    print("PREVIEW top blobs:")
    for b in p_blobs[:25]:
        print(f"  y={b['y']:5.1f} x={b['x']:5.1f} w={b['w']:5.1f} h={b['h']:4.1f} px={b['pixels']}")


if __name__ == "__main__":
    main()
