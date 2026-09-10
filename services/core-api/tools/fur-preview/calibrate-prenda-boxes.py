#!/usr/bin/env python3
"""HU #11257 (Feature #11254) — calibra las 5 casillas de prenda que faltan en el manifest del FUR:
`requested_process_12` en AUTOMOTOR, y `requested_process_11`/`_12` en MAQUINARIA y REMOLQUES (más la
casilla `10` de bonus, medida en el mismo barrido por si una HU futura la necesita).

Procedimiento (orden fijado por el plan técnico, §4.2):
    1. PRIMARIO — `page.get_drawings()` sobre el PDF blank: buscar rectángulos vectoriales cerca de cada
       rótulo numérico y anclar la declaración al rectángulo real.
    2. RESPALDO — si no hay rectángulo (este script documenta que NO LO HAY: los blanks de este FUR no
       dibujan un cuadrito por casilla, el "checkbox" es simplemente la X que el overlay escribe junto al
       número impreso, igual que las casillas YA calibradas `requested_process_1`/`_2`): medir los
       rótulos «1»/«2» de la MISMA plantilla, calcular el delta contra sus casillas ya calibradas del
       manifest, promediar, y aplicar ese delta a la posición del rótulo objetivo.

Hallazgo que contradice el plan técnico (reportado también en el chat/HU): en el blank de MAQUINARIA la
fila de PRENDA no está en los rótulos 11/12 como en AUTOMOTOR y REMOLQUES, sino en 10/11 — el rótulo "12"
de maquinaria es "DUPLICADO DE PLACAS", no tiene nada que ver con la prenda. Confirmado visualmente
(`crop-maquinaria.png` en el chat) y con texto extraído aquí. El manifest usa el mismo par de IDs
internos (`requested_process_11`=constitución, `requested_process_12`=levantamiento) en los tres
formatos — son un contrato semántico del mapper, no el número impreso en cada plantilla — pero en
MAQUINARIA quedan anclados a las coordenadas de los rótulos impresos "10" y "11" respectivamente.

Uso:
    python3 tools/fur-preview/calibrate-prenda-boxes.py
    (desde `services/core-api/` o desde `tools/fur-preview/`; ruta resuelta relativa a este archivo).

Salida: reporte en consola con método usado (rectángulo real | respaldo por offset) y las coordenadas
finales de cada casilla, más un JSON en `artifacts/fur-analysis/calibration-prenda-boxes.json`.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import fitz

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parents[2]  # services/core-api/
TEMPLATES_DIR = ROOT / "src" / "Flit.Infrastructure" / "Documents" / "Fur" / "Templates"
ART = ROOT / "artifacts" / "fur-analysis"

TEMPLATES = {
    "automotor": TEMPLATES_DIR / "fur-formulario-p1-blank.pdf",
    "maquinaria": TEMPLATES_DIR / "fur-maquinaria-p1-blank.pdf",
    "remolques": TEMPLATES_DIR / "fur-remolques-p1-blank.pdf",
}

# Casillas 1/2 YA calibradas en el manifest (ancla del método de respaldo). AUTOMOTOR no se recalibra
# (su casilla 12 sale por derivación directa desde 11, ya congelada en el manifest — ver §4.2 del plan).
CALIBRATED_1_2 = {
    "maquinaria": {"1": (101.0, 102.0), "2": (170.0, 102.0)},
    "remolques": {"1": (86.0, 101.0), "2": (155.0, 101.0)},
}

# Casilla objetivo -> qué representa en cada plantilla (ROTULO IMPRESO -> significado real). MAQUINARIA
# está desplazada un rótulo respecto de AUTOMOTOR/REMOLQUES (hallazgo de esta HU).
TARGET_LABELS = {
    "maquinaria": {
        "requested_process_11": "10",  # INSCRIPC. PRENDA (constitución) en maquinaria = rótulo impreso "10"
        "requested_process_12": "11",  # LEVANTA. PRENDA (levantamiento) en maquinaria = rótulo impreso "11"
    },
    "remolques": {
        "requested_process_10": "10",  # bonus: DUPLICADO TARJETA DE REGISTRO — no relacionado con prenda
        "requested_process_11": "11",  # INSCRIPC. PRENDA (constitución)
        "requested_process_12": "12",  # LEVANTA. PRENDA (levantamiento)
    },
}

SIZE = 9.0  # tamaño heredado de las hermanas requested_process_1/2 en ambos formatos (task item 4).


def find_number_labels(path: Path) -> dict[str, tuple[float, float, float, float]]:
    """Bbox de los rótulos numéricos '1'..'12' de la fila TRAMITE SOLICITADO (x<490, y en 100-140)."""
    doc = fitz.open(path)
    page = doc[0]
    out: dict[str, tuple[float, float, float, float]] = {}
    for block in page.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            text = "".join(s["text"] for s in line.get("spans", [])).strip()
            if not text or not text.rstrip(".").isdigit():
                continue
            bbox = line["bbox"]
            if bbox[0] >= 490 or not (95 <= bbox[1] <= 190):
                continue
            out[text] = tuple(round(v, 2) for v in bbox)
    doc.close()
    return out


def find_nearby_rect(path: Path, label_bbox: tuple[float, float, float, float]) -> fitz.Rect | None:
    """PRIMARIO: rectángulo vectorial pequeño (3-20pt) cuyo centro cae cerca del centro del rótulo."""
    doc = fitz.open(path)
    page = doc[0]
    lcx = (label_bbox[0] + label_bbox[2]) / 2
    lcy = (label_bbox[1] + label_bbox[3]) / 2
    best: tuple[float, fitz.Rect] | None = None
    for d in page.get_drawings():
        for it in d.get("items", []):
            if it[0] != "re":
                continue
            rect = it[1]
            if not (3 <= rect.width <= 20 and 3 <= rect.height <= 20):
                continue
            cx, cy = (rect.x0 + rect.x1) / 2, (rect.y0 + rect.y1) / 2
            dist = ((cx - lcx) ** 2 + (cy - lcy) ** 2) ** 0.5
            if dist < 25 and (best is None or dist < best[0]):
                best = (dist, fitz.Rect(rect))
    doc.close()
    return best[1] if best else None


def main() -> int:
    report: dict[str, dict] = {}

    for fmt in ("maquinaria", "remolques"):
        path = TEMPLATES[fmt]
        labels = find_number_labels(path)
        anchor1, anchor2 = CALIBRATED_1_2[fmt]["1"], CALIBRATED_1_2[fmt]["2"]
        label1, label2 = labels.get("1"), labels.get("2")
        if label1 is None or label2 is None:
            print(f"ERROR: no se encontraron los rótulos 1/2 en {fmt}", file=sys.stderr)
            return 1

        dx = ((anchor1[0] - label1[0]) + (anchor2[0] - label2[0])) / 2
        dy = ((anchor1[1] - label1[1]) + (anchor2[1] - label2[1])) / 2

        report[fmt] = {"offset": {"dx": round(dx, 3), "dy": round(dy, 3)}, "fields": {}}
        for field_id, printed_label in TARGET_LABELS[fmt].items():
            label_bbox = labels.get(printed_label)
            if label_bbox is None:
                print(f"AVISO: rótulo '{printed_label}' no encontrado en {fmt}; {field_id} se deja fuera.")
                continue

            rect = find_nearby_rect(path, label_bbox)
            if rect is not None:
                method = "rectangulo_real"
                x, y = round(rect.x0, 1), round(rect.y0, 1)
            else:
                method = "respaldo_offset_1_2"
                x = round(label_bbox[0] + dx, 1)
                y = round(label_bbox[1] + dy, 1)

            report[fmt]["fields"][field_id] = {
                "printed_label": printed_label,
                "label_bbox": label_bbox,
                "method": method,
                "x": x,
                "y": y,
                "size": SIZE,
            }

    # AUTOMOTOR — derivación directa desde el hermano ya calibrado (documentado, no medido con drawings:
    # tampoco hay rectángulo vectorial en el blank de automotor).
    auto_label11 = find_number_labels(TEMPLATES["automotor"]).get("11")
    auto_label12 = find_number_labels(TEMPLATES["automotor"]).get("12")
    checkbox11 = (286.9, 170.9)  # ya congelada en el manifest (FurManifestGuardTests baseline)
    delta = (checkbox11[0] - auto_label11[0], checkbox11[1] - auto_label11[1])
    checkbox12 = (round(auto_label12[0] + delta[0], 1), round(auto_label12[1] + delta[1], 1))
    report["automotor"] = {
        "offset": {"dx": round(delta[0], 3), "dy": round(delta[1], 3)},
        "fields": {
            "requested_process_12": {
                "printed_label": "12",
                "label_bbox": auto_label12,
                "method": "derivacion_directa_desde_11",
                "x": checkbox12[0],
                "y": checkbox12[1],
                "size": 10.1,
            }
        },
    }

    ART.mkdir(parents=True, exist_ok=True)
    out_path = ART / "calibration-prenda-boxes.json"
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"Reporte escrito en {out_path}\n")
    for fmt, data in report.items():
        print(f"── {fmt} (offset dx={data['offset']['dx']}, dy={data['offset']['dy']}) ──")
        for field_id, f in data["fields"].items():
            print(f"  {field_id:24} <- rótulo impreso '{f['printed_label']:>2}'  "
                  f"método={f['method']:24} x={f['x']:>6} y={f['y']:>6} size={f['size']}")
        print()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
