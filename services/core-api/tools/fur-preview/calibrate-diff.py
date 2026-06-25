#!/usr/bin/env python3
"""Render preview FUR p1 y compara escala con golden para afinar manifest."""
from __future__ import annotations

import json
from pathlib import Path

import fitz
from PIL import Image, ImageChops, ImageStat

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "artifacts" / "fur-analysis"
PREVIEW = ART / "fur-preview-matricula-YYY090.pdf"
GOLDEN_PDF = Path(r"d:\Cursor\FLIT\2.0\FUR.pdf")
TARGET_W = 1008
TARGET_H = 612


def render_page(path: Path, page_no: int = 0) -> Image.Image:
    doc = fitz.open(path)
    page = doc[page_no]
    scale_x = TARGET_W / page.rect.width
    scale_y = TARGET_H / page.rect.height
    mat = fitz.Matrix(scale_x, scale_y)
    pix = page.get_pixmap(matrix=mat, alpha=False)
    doc.close()
    return Image.frombytes("RGB", (pix.width, pix.height), pix.samples)


def main() -> None:
    preview = render_page(PREVIEW)
    golden = render_page(GOLDEN_PDF)
    preview.save(ART / "preview-page1-1008.png")
    golden.save(ART / "golden-page1-1008.png")

    diff = ImageChops.difference(preview, golden)
    diff.save(ART / "diff-page1.png")
    stat = ImageStat.Stat(diff)
    mean_diff = sum(stat.mean) / len(stat.mean)
    report = {
        "target": [TARGET_W, TARGET_H],
        "mean_rgb_diff": mean_diff,
        "preview_path": str(ART / "preview-page1-1008.png"),
        "golden_path": str(ART / "golden-page1-1008.png"),
        "diff_path": str(ART / "diff-page1.png"),
    }
    (ART / "diff-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
