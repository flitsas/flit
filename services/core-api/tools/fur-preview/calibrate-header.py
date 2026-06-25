#!/usr/bin/env python3
"""Calibra campos del encabezado FUR comparando golden vs blank."""
from __future__ import annotations

import json
from pathlib import Path

import fitz
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
BLANK = ROOT / "src/Flit.Infrastructure/Documents/Fur/Templates/fur-formulario-p1-blank.pdf"
GOLDEN = Path(r"d:\Cursor\FLIT\2.0\FUR.pdf")
W, H = 1008, 612


def render(path: Path) -> np.ndarray:
    doc = fitz.open(path)
    page = doc[0]
    mat = fitz.Matrix(W / page.rect.width, H / page.rect.height)
    pix = page.get_pixmap(matrix=mat, alpha=False)
    doc.close()
    return np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.width, 3)


def blobs(mask: np.ndarray, min_pixels: int = 15) -> list[dict]:
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
                    "x": int(min(xs)),
                    "y": int(min(ys)),
                    "w": int(max(xs) - min(xs)),
                    "h": int(max(ys) - min(ys)),
                    "cx": round(sum(xs) / len(xs), 1),
                    "cy": round(sum(ys) / len(ys), 1),
                    "pixels": len(pts),
                }
            )
    return sorted(found, key=lambda b: (b["y"], b["x"]))


def blank_labels(path: Path, y_max: float = 90) -> list[dict]:
    doc = fitz.open(path)
    page = doc[0]
    out: list[dict] = []
    for block in page.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            text = "".join(s["text"] for s in line.get("spans", [])).strip()
            if not text:
                continue
            x0 = min(s["bbox"][0] for s in line["spans"])
            y0 = min(s["bbox"][1] for s in line["spans"])
            x1 = max(s["bbox"][2] for s in line["spans"])
            y1 = max(s["bbox"][3] for s in line["spans"])
            if y0 <= y_max:
                out.append(
                    {
                        "text": text[:70],
                        "x": round(x0, 1),
                        "y": round(y0, 1),
                        "w": round(x1 - x0, 1),
                        "h": round(y1 - y0, 1),
                    }
                )
    doc.close()
    return sorted(out, key=lambda r: (r["y"], r["x"]))


def main() -> None:
    blank = render(BLANK)
    golden = render(GOLDEN)
    diff = np.abs(golden.astype(np.int16) - blank.astype(np.int16)).max(axis=2)
    mask = diff > 35
    hdr_blobs = [b for b in blobs(mask) if b["y"] < 85]

    labels = blank_labels(BLANK)
    report = {"labels": labels, "golden_header_blobs": hdr_blobs}
    out = ROOT / "artifacts/fur-analysis/header-calibration.json"
    out.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print("BLANK LABELS (y<=90):")
    for r in labels:
        print(f"  y={r['y']:5.1f} x={r['x']:6.1f}  {r['text']}")

    print("\nGOLDEN INK BLOBS (y<85):")
    for b in hdr_blobs:
        print(
            f"  y={b['y']:3} x={b['x']:3} w={b['w']:3} h={b['h']:2} "
            f"cx={b['cx']:6.1f} cy={b['cy']:5.1f} px={b['pixels']}"
        )
    print(f"\nWrote {out}")


if __name__ == "__main__":
    main()
