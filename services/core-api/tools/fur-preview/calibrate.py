#!/usr/bin/env python3
"""Extrae posiciones de texto en PDFs FUR para calibrar fur-field-manifest.json."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

import fitz

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "artifacts" / "fur-analysis"
GOLDEN = Path(r"d:\Cursor\FLIT\2.0\FUR.pdf")
BLANK = ROOT / "src/Flit.Infrastructure/Documents/Fur/Templates/fur-formulario-p1-blank.pdf"
PREVIEW = ART / "fur-preview-matricula-YYY090.pdf"

# Valores esperados en preview matrícula
MARKERS = [
    "STRIATTOYTTE",
    "FUNDA",
    "25286000",
    "25",  # day - ambiguous
    "6",
    "2026",
    "YYY",
    "090",
    "TESLA",
    "MODELO Y",
    "BLANCO",
    "DANIEL",
    "GARCIA",
    "1193552679",
    "CALLE 1",
    "3001234567",
]


def page_info(doc: fitz.Document, page_no: int = 0) -> dict:
    p = doc[page_no]
    return {"width": p.rect.width, "height": p.rect.height, "rotation": p.rotation}


def find_text(doc: fitz.Document, needles: list[str], page_no: int = 0) -> list[dict]:
    p = doc[page_no]
    hits: list[dict] = []
    for block in p.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            for span in line.get("spans", []):
                text = span["text"].strip()
                if not text:
                    continue
                for needle in needles:
                    if needle.upper() in text.upper():
                        x0, y0, x1, y1 = span["bbox"]
                        hits.append(
                            {
                                "needle": needle,
                                "text": text,
                                "x": round(x0, 2),
                                "y_top": round(y0, 2),
                                "w": round(x1 - x0, 2),
                                "h": round(y1 - y0, 2),
                                "font_size": round(span.get("size", 0), 2),
                            }
                        )
                        break
    return sorted(hits, key=lambda h: (h["y_top"], h["x"]))


def main() -> int:
    paths = {
        "golden": GOLDEN,
        "blank": BLANK,
        "preview": PREVIEW,
    }
    report: dict = {}
    for name, path in paths.items():
        if not path.exists():
            print(f"MISSING {name}: {path}", file=sys.stderr)
            continue
        doc = fitz.open(path)
        report[name] = {
            "path": str(path),
            "pages": doc.page_count,
            "page0": page_info(doc),
            "hits": find_text(doc, MARKERS, 0),
        }
        doc.close()

    out = ART / "calibration-report.json"
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Wrote {out}")
    for name, data in report.items():
        print(f"\n=== {name} {data['page0']} ({len(data['hits'])} hits) ===")
        for h in data["hits"]:
            print(
                f"  {h['needle']!r:16} @ x={h['x']:7.1f} y={h['y_top']:6.1f} "
                f"fs={h['font_size']:4.1f} text={h['text'][:40]!r}"
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
