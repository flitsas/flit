#!/usr/bin/env python3
"""Mapea etiquetas del blank FUR y posiciones overlay para calibrar manifest."""
from __future__ import annotations

import json
import re
from pathlib import Path

import fitz

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "artifacts" / "fur-analysis"
BLANK = ROOT / "src/Flit.Infrastructure/Documents/Fur/Templates/fur-formulario-p1-blank.pdf"
PREVIEW = ART / "fur-preview-matricula-YYY090.pdf"
GOLDEN_IMG = Path(r"d:\Cursor\FLIT\2.0\services\core-api\artifacts\fur-analysis\FUR-page1.png")

LABELS = [
    "ORGANISMO DE TRANSITO",
    "NOMBRE",
    "CIUDAD",
    "CODIGO",
    "FECHA DE TRAMITE",
    "LETRAS",
    "NUMEROS",
    "TRAMITE SOLICITADO",
    "CLASE DE VEHICULO",
    "MARCA",
    "LINEA",
    "COLOR",
    "MODELO",
    "CILINDRAJE",
    "CAPACIDAD",
    "COMBUSTIBLE",
    "CARROCERIA",
    "MOTOR",
    "CHASIS",
    "SERIE",
    "VIN",
    "TIPO DE SERVICIO",
    "PROPIETARIO",
    "COMPRADOR",
    "TELEFONO",
    "OBSERVACIONES",
]


def extract_labels(path: Path) -> list[dict]:
    doc = fitz.open(path)
    p = doc[0]
    out: list[dict] = []
    for block in p.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            text = "".join(s["text"] for s in line.get("spans", [])).strip()
            if not text:
                continue
            upper = re.sub(r"\s+", " ", text.upper())
            for label in LABELS:
                if label in upper:
                    x0 = min(s["bbox"][0] for s in line["spans"])
                    y0 = min(s["bbox"][1] for s in line["spans"])
                    x1 = max(s["bbox"][2] for s in line["spans"])
                    y1 = max(s["bbox"][3] for s in line["spans"])
                    out.append(
                        {
                            "label": label,
                            "text": text[:80],
                            "x": round(x0, 1),
                            "y": round(y0, 1),
                            "w": round(x1 - x0, 1),
                            "h": round(y1 - y0, 1),
                        }
                    )
                    break
    doc.close()
    return sorted(out, key=lambda r: (r["y"], r["x"]))


def extract_values(path: Path, values: list[str]) -> list[dict]:
    doc = fitz.open(path)
    p = doc[0]
    hits: list[dict] = []
    for block in p.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            for span in line.get("spans", []):
                text = span["text"].strip()
                if not text:
                    continue
                for v in values:
                    if v.upper() in text.upper():
                        x0, y0, x1, y1 = span["bbox"]
                        hits.append(
                            {
                                "value": v,
                                "text": text,
                                "x": round(x0, 1),
                                "y": round(y0, 1),
                                "fs": round(span.get("size", 0), 1),
                            }
                        )
                        break
    doc.close()
    return sorted(hits, key=lambda h: (h["y"], h["x"]))


def main() -> None:
    values = [
        "STRIATTOYTTE",
        "FUNDA",
        "25286000",
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
    report = {
        "blank_labels": extract_labels(BLANK),
        "preview_values": extract_values(PREVIEW, values),
    }
    out = ART / "calibration-labels.json"
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Wrote {out}\n")
    print("BLANK LABELS:")
    for r in report["blank_labels"]:
        print(f"  y={r['y']:6.1f} x={r['x']:6.1f}  {r['label']}")
    print("\nPREVIEW VALUES:")
    for r in report["preview_values"]:
        print(f"  y={r['y']:6.1f} x={r['x']:6.1f}  {r['value']!r} -> {r['text']!r}")


if __name__ == "__main__":
    main()
