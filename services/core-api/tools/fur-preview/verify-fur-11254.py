#!/usr/bin/env python3
"""HU #11255 — verifica con pymupdf el desplazamiento aplicado a `observations`
(x -= 2, y -= 5) y `vehicle_serial_number` (y -= 5, x sin cambio) en las tres
plantillas del FUR (automotor, maquinaria, remolques).

Compara el render de ANTES (`artifacts/fur-analysis-before/`) contra el de
DESPUÉS (`artifacts/fur-analysis-after/`) usando pymupdf para localizar el
bbox del span de texto sobreimpreso, nunca contra una expectativa a ojo.

Uso:
    1. Generar la línea base "antes": `git stash` los tres manifiestos del
       FUR (dejarlos en su estado previo a la HU) y correr
       `dotnet run --project tools/fur-preview/fur-preview.csproj -c Debug`;
       copiar `artifacts/fur-analysis/*.pdf` a `artifacts/fur-analysis-before/`.
    2. `git stash pop` para restaurar los manifiestos con los desplazamientos.
    3. Volver a correr `dotnet run --project tools/fur-preview/fur-preview.csproj -c Debug`
       y copiar `artifacts/fur-analysis/*.pdf` a `artifacts/fur-analysis-after/`.
    4. `python3 tools/fur-preview/verify-fur-11254.py` (desde `services/core-api/`
       o desde `tools/fur-preview/`, la ruta se resuelve de forma relativa a
       este archivo).

Sale con código 0 y tabla de deltas si todo cae dentro de tolerancia
(Δ = ±0.1 pt); con código 1 y el detalle del fallo en caso contrario.
"""
from __future__ import annotations

import sys
from dataclasses import dataclass
from pathlib import Path

import fitz

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parents[2]  # services/core-api/
BEFORE = ROOT / "artifacts" / "fur-analysis-before"
AFTER = ROOT / "artifacts" / "fur-analysis-after"

TOLERANCE = 0.1


@dataclass
class SpanCheck:
    """Una verificación de desplazamiento: localizar `needle` en `pdf_name` y
    comparar su bbox entre `before/` y `after/`, esperando (expected_dx, expected_dy)."""

    label: str
    pdf_name: str
    needle: str
    expected_dx: float
    expected_dy: float


CHECKS: list[SpanCheck] = [
    # observations — CF1: x -= 2, y -= 5, las tres plantillas.
    SpanCheck("observations · automotor (obs corta)", "fur-preview-05-automotor-obs-corta.pdf",
              "SIN OBSERVACIONES ADICIONALES", -2.0, -5.0),
    SpanCheck("observations · automotor (obs media)", "fur-preview-06-automotor-obs-media.pdf",
              "VEHICULO VERIFICADO CONTRA EL RUNT", -2.0, -5.0),
    SpanCheck("observations · automotor (obs larga)", "fur-preview-07-automotor-obs-larga.pdf",
              "VEHICULO VERIFICADO CONTRA EL RUNT", -2.0, -5.0),
    SpanCheck("observations · maquinaria", "fur-preview-08-maquinaria-obs-media.pdf",
              "VEHICULO VERIFICADO CONTRA EL RUNT", -2.0, -5.0),
    SpanCheck("observations · remolques", "fur-preview-09-remolques-obs-media.pdf",
              "VEHICULO VERIFICADO CONTRA EL RUNT", -2.0, -5.0),
    # vehicle_serial_number — CF9: y -= 5, x sin cambio. Maquinaria NO tiene la casilla (CF10/AC3).
    SpanCheck("vehicle_serial_number · automotor", "fur-preview-06-automotor-obs-media.pdf",
              "SN-AUTO-000002", 0.0, -5.0),
    SpanCheck("vehicle_serial_number · remolques", "fur-preview-09-remolques-obs-media.pdf",
              "SN-REM-000004", 0.0, -5.0),
]

# AC3/CF10 — maquinaria no debe imprimir el número de serie en ninguna parte de la página,
# aunque el dato venga poblado (MaquinariaData().Vehiculo.NumeroSerie = "SN-420F2-9981").
NO_SERIAL_CHECK = ("fur-preview-08-maquinaria-obs-media.pdf", "SN-420F2-9981")


def find_span(pdf_path: Path, needle: str) -> tuple[fitz.Rect, float, str]:
    """Devuelve (bbox, fontSize, fontName) del primer span cuyo texto contiene `needle`."""
    doc = fitz.open(pdf_path)
    try:
        page = doc[0]
        d = page.get_text("dict")
        for block in d["blocks"]:
            for line in block.get("lines", []):
                for span in line["spans"]:
                    if needle in span["text"]:
                        return fitz.Rect(span["bbox"]), span["size"], span["font"]
        raise AssertionError(f"No se encontró texto que contenga {needle!r} en {pdf_path}")
    finally:
        doc.close()


def find_any_span_with(pdf_path: Path, needle: str) -> bool:
    doc = fitz.open(pdf_path)
    try:
        page = doc[0]
        text = page.get_text("text")
        return needle in text
    finally:
        doc.close()


def main() -> int:
    if not BEFORE.exists() or not AFTER.exists():
        print(f"ERROR: faltan {BEFORE} y/o {AFTER}. Ver el docstring de este script.", file=sys.stderr)
        return 1

    rows: list[tuple[str, float, float, float, float, str]] = []
    failures: list[str] = []

    for check in CHECKS:
        before_pdf = BEFORE / check.pdf_name
        after_pdf = AFTER / check.pdf_name
        try:
            bbox_before, size_before, font_before = find_span(before_pdf, check.needle)
            bbox_after, size_after, font_after = find_span(after_pdf, check.needle)
        except AssertionError as exc:
            failures.append(f"{check.label}: {exc}")
            continue

        dx = round(bbox_after.x0 - bbox_before.x0, 3)
        dy = round(bbox_after.y0 - bbox_before.y0, 3)
        status = "OK"

        if abs(dx - check.expected_dx) > TOLERANCE:
            status = "FAIL(dx)"
        if abs(dy - check.expected_dy) > TOLERANCE:
            status = "FAIL(dy)" if status == "OK" else status + "+FAIL(dy)"
        if abs(size_after - size_before) > 1e-6:
            status += " FAIL(fontSize cambió)"
        if font_after != font_before:
            status += " FAIL(font cambió)"

        if "FAIL" in status:
            failures.append(
                f"{check.label}: dx={dx} (esperado {check.expected_dx}), "
                f"dy={dy} (esperado {check.expected_dy}), "
                f"fontSize {size_before}->{size_after}, font {font_before}->{font_after}")

        rows.append((check.label, dx, check.expected_dx, dy, check.expected_dy, status))

    # AC3/CF10 — maquinaria sin número de serie impreso, antes y después.
    no_serial_name, no_serial_needle = NO_SERIAL_CHECK
    for label, folder in (("antes", BEFORE), ("después", AFTER)):
        found = find_any_span_with(folder / no_serial_name, no_serial_needle)
        status = "FAIL: se imprimió el serial en maquinaria" if found else "OK (no se imprime)"
        rows.append((f"AC3 · maquinaria sin serial ({label})", 0.0, 0.0, 0.0, 0.0, status))
        if found:
            failures.append(f"AC3 maquinaria ({label}): se encontró {no_serial_needle!r} en la página")

    # Tabla de deltas medidos.
    header = f"{'Verificación':50} {'dx':>8} {'dx esp.':>8} {'dy':>8} {'dy esp.':>8}  Estado"
    print(header)
    print("-" * len(header))
    for label, dx, edx, dy, edy, status in rows:
        print(f"{label:50} {dx:>8} {edx:>8} {dy:>8} {edy:>8}  {status}")

    if failures:
        print("\nFALLOS:", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1

    print("\nOK — todos los desplazamientos están dentro de tolerancia (±0.1 pt).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
