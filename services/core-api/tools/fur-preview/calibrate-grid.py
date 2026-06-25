#!/usr/bin/env python3
"""Detecta líneas de grilla en blank FUR y sugiere celdas de datos."""
from __future__ import annotations

import json
from collections import defaultdict
from pathlib import Path

import fitz

ROOT = Path(__file__).resolve().parents[2]
BLANK = ROOT / "src/Flit.Infrastructure/Documents/Fur/Templates/fur-formulario-p1-blank.pdf"
OUT = ROOT / "artifacts" / "fur-analysis" / "grid-lines.json"


def cluster(values: list[float], tol: float = 1.5) -> list[float]:
    if not values:
        return []
    values = sorted(values)
    groups: list[list[float]] = [[values[0]]]
    for v in values[1:]:
        if abs(v - groups[-1][-1]) <= tol:
            groups[-1].append(v)
        else:
            groups.append([v])
    return [sum(g) / len(g) for g in groups]


def main() -> None:
    doc = fitz.open(BLANK)
    page = doc[0]
    page_rect = {"width": page.rect.width, "height": page.rect.height}
    xs: list[float] = []
    ys: list[float] = []
    for d in page.get_drawings():
        for item in d["items"]:
            if item[0] == "l":
                p1, p2 = item[1], item[2]
                if abs(p1.y - p2.y) < 0.5:
                    ys.append((p1.y + p2.y) / 2)
                if abs(p1.x - p2.x) < 0.5:
                    xs.append((p1.x + p2.x) / 2)
    doc.close()

    x_lines = cluster(xs, 1.0)
    y_lines = cluster(ys, 1.0)
    report = {
        "page": page_rect,
        "x_lines": [round(x, 1) for x in x_lines],
        "y_lines": [round(y, 1) for y in y_lines],
        "x_count": len(x_lines),
        "y_count": len(y_lines),
    }
    OUT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Wrote {OUT} — {len(x_lines)} vertical, {len(y_lines)} horizontal lines")
    print("Y lines (first 25):", report["y_lines"][:25])
    print("X lines (first 25):", report["x_lines"][:25])


if __name__ == "__main__":
    main()
