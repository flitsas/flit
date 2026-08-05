#!/usr/bin/env python3
"""HU #11257 (Feature #11254) — verifica con pymupdf:

    CF-checkboxes — en los seis escenarios de prenda (`16`..`21`, generados por
        `tools/fur-preview/Program.cs`), la "X" cae dentro de la celda de la casilla que le corresponde
        (columna+fila del grid TRAMITE SOLICITADO) y NINGUNA otra casilla del grupo 10-11-12 recibe
        tinta. Los blanks de este FUR no dibujan un rectángulo por casilla — el "checkbox" es la propia
        celda de la tabla (confirmado en `calibrate-prenda-boxes.py`) — así que la contención se verifica
        contra los límites de columna/fila del grid, no contra un rectángulo impreso.
    CF11 — el recuadro OBSERVACIONES declara el literal correcto según la modalidad: constitución
        ("GRAVAMEN / PRENDA A FAVOR DE:") o levantamiento ("LEVANTAMIENTO DE GRAVAMEN A FAVOR DE:"),
        nunca ambos ni ninguno.
    Independencia — `requested_process_1`/`_2` (tipo de trámite) no se ven afectados por la marca de
        prenda: matrícula con prenda sale 1 + 11, no 2 + 11.

Uso:
    1. `dotnet run --project tools/fur-preview/fur-preview.csproj -c Debug` (genera
       `artifacts/fur-analysis/fur-preview-16..21-*.pdf`).
    2. `python3 tools/fur-preview/verify-fur-11257.py` (desde `services/core-api/` o desde
       `tools/fur-preview/`; ruta resuelta relativa a este archivo, igual que `verify-fur-11256.py`).
"""
from __future__ import annotations

import sys
from pathlib import Path

import fitz

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parents[2]  # services/core-api/
ART = ROOT / "artifacts" / "fur-analysis"

# Celdas del grid TRAMITE SOLICITADO (columna del campo que debe marcarse en cada escenario), medidas
# sobre el blank de cada plantilla. x0/x1 = límites de columna del campo; y0/y1 = banda de fila donde
# CUALQUIER casilla del grupo 10-11-12 puede imprimir tinta (para detectar fugas a la fila vecina).
CELLS = {
    "automotor": {
        "row_band": (140.0, 200.0),  # fila 7-12 hasta el arranque de la fila 13-18
        "requested_process_11": (258.07, 311.73),
        "requested_process_12": (314.71, 368.38),
    },
    "maquinaria": {
        "row_band": (118.0, 150.0),  # fila 7-12 (entre las filas 1-6 y el pie de tabla)
        "requested_process_11": (288.36, 353.64),  # rótulo impreso "10" (INSCRIPC. PRENDA)
        "requested_process_12": (356.64, 417.36),  # rótulo impreso "11" (LEVANTA. PRENDA)
    },
    "remolques": {
        "row_band": (118.0, 150.0),
        "requested_process_11": (356.64, 417.36),  # rótulo impreso "11" (INSCRIPC. PRENDA)
        "requested_process_12": (420.36, 490.20),  # rótulo impreso "12" (LEVANTA. PRENDA)
    },
}

SCENARIOS = [
    ("automotor", "16-automotor-prenda-constitucion", "requested_process_11", "Etiqueta constitución"),
    ("automotor", "17-automotor-prenda-levantamiento", "requested_process_12", "Etiqueta levantamiento"),
    ("maquinaria", "18-maquinaria-prenda-constitucion", "requested_process_11", "Etiqueta constitución"),
    ("maquinaria", "19-maquinaria-prenda-levantamiento", "requested_process_12", "Etiqueta levantamiento"),
    ("remolques", "20-remolques-prenda-constitucion", "requested_process_11", "Etiqueta constitución"),
    ("remolques", "21-remolques-prenda-levantamiento", "requested_process_12", "Etiqueta levantamiento"),
]

CONSTITUCION_LITERAL = "GRAVAMEN / PRENDA A FAVOR DE:"
LEVANTAMIENTO_LITERAL = "LEVANTAMIENTO DE GRAVAMEN A FAVOR DE:"


def find_x_marks(pdf_path: Path) -> list[fitz.Rect]:
    doc = fitz.open(pdf_path)
    try:
        page = doc[0]
        marks = []
        for block in page.get_text("dict")["blocks"]:
            for line in block.get("lines", []):
                for span in line.get("spans", []):
                    if span["text"].strip() == "X":
                        marks.append(fitz.Rect(span["bbox"]))
        return marks
    finally:
        doc.close()


def full_text(pdf_path: Path) -> str:
    doc = fitz.open(pdf_path)
    try:
        return doc[0].get_text()
    finally:
        doc.close()


def check_checkbox_containment(fmt: str, pdf_name: str, target_field: str, rows: list, failures: list) -> None:
    pdf_path = ART / f"fur-preview-{pdf_name}.pdf"
    if not pdf_path.exists():
        failures.append(f"{pdf_name}: no existe (¿corriste tools/fur-preview antes?)")
        return

    cells = CELLS[fmt]
    y0, y1 = cells["row_band"]
    marks = [m for m in find_x_marks(pdf_path) if y0 <= m.y0 <= y1]

    target_x0, target_x1 = cells[target_field]
    other_field = "requested_process_12" if target_field == "requested_process_11" else "requested_process_11"
    other_x0, other_x1 = cells[other_field]

    in_target = [m for m in marks if target_x0 - 1 <= m.x0 <= target_x1 + 1]
    in_other = [m for m in marks if other_x0 - 1 <= m.x0 <= other_x1 + 1]

    status_target = "OK (dentro de la celda)" if in_target else "FAIL(sin marca en la celda esperada)"
    status_other = "OK (vacía)" if not in_other else "FAIL(tinta en la casilla contraria)"

    if not in_target:
        failures.append(f"{pdf_name}: {target_field} sin marca dentro de la celda ({target_x0}-{target_x1})")
    if in_other:
        failures.append(f"{pdf_name}: {other_field} (casilla contraria) recibió tinta: {in_other}")

    rows.append((f"{pdf_name} · {target_field}", f"celda=({target_x0:.1f}-{target_x1:.1f})",
                 f"X={[tuple(round(v, 1) for v in m) for m in in_target]}", status_target))
    rows.append((f"{pdf_name} · {other_field} (contraria)", f"celda=({other_x0:.1f}-{other_x1:.1f})",
                 f"X={[tuple(round(v, 1) for v in m) for m in in_other]}", status_other))


def check_cf11_literal(pdf_name: str, expect: str, rows: list, failures: list) -> None:
    pdf_path = ART / f"fur-preview-{pdf_name}.pdf"
    if not pdf_path.exists():
        failures.append(f"{pdf_name}: no existe")
        return
    text = full_text(pdf_path)
    has_constitucion = CONSTITUCION_LITERAL in text
    has_levantamiento = LEVANTAMIENTO_LITERAL in text

    if expect == "constitucion":
        ok = has_constitucion and not has_levantamiento
    else:
        ok = has_levantamiento and not has_constitucion

    status = "OK" if ok else "FAIL"
    if not ok:
        failures.append(
            f"{pdf_name}: literal CF11 incorrecto (constitucion={has_constitucion}, "
            f"levantamiento={has_levantamiento}, esperado={expect})")
    rows.append((f"{pdf_name} · CF11", expect, f"constitucion={has_constitucion} levantamiento={has_levantamiento}",
                 status))


def check_independencia_tipo_tramite(failures: list, rows: list) -> None:
    """Matrícula con prenda ⇒ 1 + 11, no 2 + 11 (verificación explícita de D1)."""
    pdf_path = ART / "fur-preview-16-automotor-prenda-constitucion.pdf"
    if not pdf_path.exists():
        failures.append("16-automotor-prenda-constitucion.pdf: no existe")
        return
    marks = find_x_marks(pdf_path)
    # requested_process_1 = (71.3, 119.2, size 9.9); requested_process_2 = (119.5, 121.1, size 9.8)
    has_1 = any(70 <= m.x0 <= 82 and 118 <= m.y0 <= 131 for m in marks)
    has_2 = any(118 <= m.x0 <= 131 and 119 <= m.y0 <= 133 for m in marks)
    status = "OK (1 marcado, 2 vacío)" if has_1 and not has_2 else "FAIL"
    if not (has_1 and not has_2):
        failures.append(f"Independencia tipo/prenda: has_1={has_1} has_2={has_2} (esperado 1=True, 2=False)")
    rows.append(("Matrícula con prenda ⇒ 1 + 11 (no 2 + 11)", "-", f"has_1={has_1} has_2={has_2}", status))


def main() -> int:
    failures: list[str] = []
    rows: list[tuple] = []

    for fmt, pdf_name, target_field, _label in SCENARIOS:
        check_checkbox_containment(fmt, pdf_name, target_field, rows, failures)
        expect = "constitucion" if target_field == "requested_process_11" else "levantamiento"
        check_cf11_literal(pdf_name, expect, rows, failures)

    check_independencia_tipo_tramite(failures, rows)

    header = f"{'Verificación':55} {'Referencia':22} {'Detalle':45} Estado"
    print(header)
    print("-" * len(header))
    for label, ref, detail, status in rows:
        print(f"{label:55} {str(ref):22} {str(detail):45} {status}")

    if failures:
        print("\nFALLOS:", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1

    print("\nOK — casillas 11/12 contenidas en su celda, contraria vacía, y literal CF11 correcto"
          " en los tres formatos.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
