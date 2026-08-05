#!/usr/bin/env python3
"""HU #11256 — verifica con pymupdf el auto-encaje de `observations` (autoFit opt-in) y la
no-regresión de los sellos de firma en las tres plantillas del FUR (automotor, maquinaria,
remolques).

Compara el render de ANTES (`artifacts/fur-analysis-before-11256/`, código en HEAD de HU #11255)
contra el de DESPUÉS (`artifacts/fur-analysis-after-11256/`, con `FitMultiline` + `autoFit`)
generados por `tools/fur-preview/Program.cs`.

Cubre:
    CF4 — observación corta: el cuerpo reportado por pymupdf es EXACTAMENTE el mismo antes y
          después (si el cuerpo cambió, el passthrough del paso 1 de `FitMultiline` falló).
    CF2/CF3 — observación desmedida (~2.000 car., escenarios 10/11/12): todo el texto queda
          contenido dentro del recuadro declarado en el manifiesto, por arriba y por abajo.
    CF12 — sellos de firma (`vehicle_owner_signature` / `vehicle_buyer_signature`): bbox y cuerpo
          IDÉNTICOS antes y después, en el escenario "NO FIRMADO" (matrícula) y en el escenario con
          sello de identidad de 4 líneas (traspaso, HU #11031/#11035) — el caso que hoy más se
          acerca al borde del recuadro.

Uso:
    python3 tools/fur-preview/verify-fur-11256.py
    (desde `services/core-api/` o desde `tools/fur-preview/`; la ruta se resuelve relativa a este
    archivo, igual que `verify-fur-11254.py`).
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
BEFORE = ROOT / "artifacts" / "fur-analysis-before-11256"
AFTER = ROOT / "artifacts" / "fur-analysis-after-11256"

TOLERANCE = 0.1


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


def spans_matching(pdf_path: Path, source_text: str, near_y: tuple[float, float]) -> list[dict]:
    """
    Todos los spans de texto no vacíos cuyo contenido (sin la elipsis final) es un fragmento
    literal de `source_text` Y cuya `y0` cae dentro de `near_y` (margen amplio alrededor de la
    caja declarada). El filtro combina CONTENIDO + proximidad porque el texto oficial IMPRESO en
    la plantilla blank (p. ej. "MINISTERIO DE TRANSPORTE" en el encabezado) puede coincidir por
    casualidad con un fragmento largo de la observación de prueba; un solo criterio no basta.
    """
    doc = fitz.open(pdf_path)
    try:
        page = doc[0]
        d = page.get_text("dict")
        found = []
        for block in d["blocks"]:
            for line in block.get("lines", []):
                for span in line["spans"]:
                    text = span["text"].strip()
                    if not text:
                        continue
                    candidate = text[:-1].rstrip() if text.endswith("…") else text
                    # Umbral de longitud: una línea envuelta de `observations` trae varias palabras
                    # (>20 caracteres); un span corto ("DE", "LA", "SIN") calzaría como substring de
                    # casi cualquier texto y arrastraría campos ajenos (placa, nombres, ciudades).
                    if not candidate or len(candidate) < 20 or candidate not in source_text:
                        continue
                    y0 = span["bbox"][1]
                    if not (near_y[0] <= y0 <= near_y[1]):
                        continue
                    found.append({"bbox": fitz.Rect(span["bbox"]), "size": span["size"], "text": text})
        return found
    finally:
        doc.close()


# ── CF4 — observación corta: cuerpo y posición idénticos ─────────────────────────────────────
CF4_CHECKS = [
    ("CF4 · observations corta · automotor", "fur-preview-05-automotor-obs-corta.pdf",
     "SIN OBSERVACIONES ADICIONALES"),
    ("CF4 · observations media · maquinaria", "fur-preview-08-maquinaria-obs-media.pdf",
     "VEHICULO VERIFICADO CONTRA EL RUNT"),
    ("CF4 · observations media · remolques", "fur-preview-09-remolques-obs-media.pdf",
     "VEHICULO VERIFICADO CONTRA EL RUNT"),
]

# ── CF12 — sellos de firma: bbox y cuerpo idénticos ───────────────────────────────────────────
CF12_CHECKS = [
    ("CF12 · vehicle_owner_signature (NO FIRMADO) · automotor",
     "fur-preview-13-automotor-no-firmado.pdf", "NO FIRMADO"),
    ("CF12 · vehicle_owner_signature (NO FIRMADO) · maquinaria",
     "fur-preview-14-maquinaria-no-firmado.pdf", "NO FIRMADO"),
    ("CF12 · vehicle_owner_signature (NO FIRMADO) · remolques",
     "fur-preview-15-remolques-no-firmado.pdf", "NO FIRMADO"),
    ("CF12 · vehicle_owner_signature (sello identidad 4 líneas) · automotor traspaso",
     "fur-preview-04-automotor-traspaso.pdf", "Validacion biometrica CC 1000445459"),
    ("CF12 · vehicle_buyer_signature (sello identidad 4 líneas) · automotor traspaso",
     "fur-preview-04-automotor-traspaso.pdf", "Validacion biometrica PAS C27WKYL7"),
]

# ── CF2/CF3 — observación desmedida: caja declarada por formato (manifiesto DESPUÉS) ─────────
OBSERVATIONS_BOX = {
    "fur-preview-10-automotor-obs-desmedida.pdf": (379.9, 472.6, 403.1, 33.0),
    "fur-preview-11-maquinaria-obs-desmedida.pdf": (496.0, 445.0, 490.0, 38.0),
    "fur-preview-12-remolques-obs-desmedida.pdf": (496.0, 417.0, 490.0, 55.0),
}

# Mismo texto que `ObsDesmedida` en tools/fur-preview/Program.cs (HU #11256): se usa para filtrar,
# por CONTENIDO, los spans que pertenecen a `observations` y descartar los de campos vecinos.
OBS_DESMEDIDA_TEXT = (
    "GRAVAMEN / PRENDA A FAVOR DE: BANCO FINANCIERO DE COLOMBIA S.A. - NIT 890900608-1. "
    "VEHICULO VERIFICADO CONTRA EL RUNT SIN NOVEDADES REPORTADAS A LA FECHA DE RADICACION DEL "
    "TRAMITE ANTE EL ORGANISMO DE TRANSITO COMPETENTE. SE ADJUNTA SOAT Y RTM VIGENTES A LA FECHA. "
    "EL PROPIETARIO DECLARA BAJO GRAVEDAD DE JURAMENTO QUE LA INFORMACION SUMINISTRADA EN EL "
    "PRESENTE FORMULARIO ES VERAZ Y COMPLETA, Y QUE EL VEHICULO NO SE ENCUENTRA REPORTADO COMO "
    "HURTADO NI PRESENTA MEDIDAS CAUTELARES VIGENTES ANTE LA AUTORIDAD COMPETENTE. CUALQUIER "
    "INCONSISTENCIA SERA INFORMADA AL ORGANISMO DE TRANSITO PARA LOS FINES PERTINENTES DENTRO DEL "
    "TRAMITE EN CURSO. TRANSFORMACION REGISTRADA CONFORME ADR-0029: CAMBIO DE COLOR DE BLANCO "
    "PERLA A NEGRO MATE, CON SOPORTE FOTOGRAFICO Y CERTIFICADO DE TALLER AUTORIZADO ADJUNTO AL "
    "EXPEDIENTE DIGITAL DEL TRAMITE. EL GESTOR CERTIFICA HABER VERIFICADO FISICAMENTE LA "
    "CORRESPONDENCIA ENTRE EL NUMERO DE MOTOR, EL NUMERO DE CHASIS Y LOS DATOS REGISTRADOS EN EL "
    "SISTEMA RUNT, SIN ENCONTRAR NOVEDADES QUE IMPIDAN LA CONTINUACION DEL TRAMITE SOLICITADO POR "
    "EL INTERESADO ANTE ESTE ORGANISMO DE TRANSITO, DE CONFORMIDAD CON LA NORMATIVIDAD VIGENTE "
    "APLICABLE EN MATERIA DE TRANSITO Y TRANSPORTE TERRESTRE AUTOMOTOR EN EL TERRITORIO NACIONAL "
    "COLOMBIANO, SEGUN LO ESTABLECIDO POR EL MINISTERIO DE TRANSPORTE Y LA SUPERINTENDENCIA DE "
    "TRANSPORTE PARA ESTE TIPO DE TRAMITES DE REGISTRO AUTOMOTOR NACIONAL."
)


def check_cf4(failures: list[str], rows: list[tuple]) -> None:
    for label, pdf_name, needle in CF4_CHECKS:
        try:
            bbox_before, size_before, font_before = find_span(BEFORE / pdf_name, needle)
            bbox_after, size_after, font_after = find_span(AFTER / pdf_name, needle)
        except AssertionError as exc:
            failures.append(f"{label}: {exc}")
            continue

        dx = round(bbox_after.x0 - bbox_before.x0, 3)
        dy = round(bbox_after.y0 - bbox_before.y0, 3)
        size_changed = abs(size_after - size_before) > 1e-6
        status = "OK" if not size_changed else "FAIL(fontSize cambió)"
        if size_changed:
            failures.append(
                f"{label}: cuerpo {size_before}->{size_after} (CF4 exige cuerpo idéntico)")
        rows.append((label, f"{size_before}->{size_after}", f"dx={dx} dy={dy}", status))


def check_cf12(failures: list[str], rows: list[tuple]) -> None:
    for label, pdf_name, needle in CF12_CHECKS:
        try:
            bbox_before, size_before, font_before = find_span(BEFORE / pdf_name, needle)
            bbox_after, size_after, font_after = find_span(AFTER / pdf_name, needle)
        except AssertionError as exc:
            failures.append(f"{label}: {exc}")
            continue

        dx = round(bbox_after.x0 - bbox_before.x0, 3)
        dy = round(bbox_after.y0 - bbox_before.y0, 3)
        identical = (
            abs(dx) <= TOLERANCE and abs(dy) <= TOLERANCE
            and abs(size_after - size_before) <= 1e-6
            and font_after == font_before
        )
        status = "OK (idéntico)" if identical else "FAIL(sello cambió)"
        if not identical:
            failures.append(
                f"{label}: bbox antes={tuple(bbox_before)} después={tuple(bbox_after)}, "
                f"cuerpo {size_before}->{size_after}, font {font_before}->{font_after}")
        rows.append((label, f"{size_before}->{size_after}", f"dx={dx} dy={dy}", status))


def check_cf2_cf3(failures: list[str], rows: list[tuple]) -> None:
    for pdf_name, (x, y, w, h) in OBSERVATIONS_BOX.items():
        after_pdf = AFTER / pdf_name
        # Margen generoso: el objetivo de CF2/CF3 es precisamente detectar tinta que se salga de
        # la caja "un poco" (por arriba o por abajo), así que el margen de búsqueda debe ser mayor
        # que la tolerancia de fallo que se está verificando.
        near_y = (y - 80, y + h + 80)
        obs_spans = spans_matching(after_pdf, OBS_DESMEDIDA_TEXT, near_y)

        if not obs_spans:
            failures.append(f"CF2/CF3 {pdf_name}: no se encontró ningún span de observations")
            continue

        min_x0 = min(s["bbox"].x0 for s in obs_spans)
        max_x1 = max(s["bbox"].x1 for s in obs_spans)
        min_y0 = min(s["bbox"].y0 for s in obs_spans)
        max_y1 = max(s["bbox"].y1 for s in obs_spans)

        # Tolerancia de renderizado: el bbox de pymupdf refleja la tinta real del glifo, y algunos
        # ascendentes (p.ej. "G", "Á") se extienden ~0.5 pt por encima de la línea base declarada.
        # 1.0 pt sigue siendo estricto frente al margen real que deja cada caja (varios puntos).
        eps = 1.0
        within = (
            min_x0 >= x - eps and max_x1 <= x + w + eps
            and min_y0 >= y - eps and max_y1 <= y + h + eps
        )
        status = "OK (contenido en la caja)" if within else "FAIL(tinta fuera de la caja)"
        if not within:
            failures.append(
                f"CF2/CF3 {pdf_name}: caja=({x},{y},{x+w},{y+h}) "
                f"texto=({min_x0},{min_y0},{max_x1},{max_y1})")

        last_span = max(obs_spans, key=lambda s: s["bbox"].y0)
        ends_with_ellipsis = last_span["text"].rstrip().endswith("…")

        rows.append((
            f"CF2/CF3 {pdf_name}",
            f"caja=({x:.1f},{y:.1f})+({w:.1f}x{h:.1f})",
            f"texto=({min_x0:.1f},{min_y0:.1f})-({max_x1:.1f},{max_y1:.1f}) "
            f"cuerpo={last_span['size']:.2f} elipsis={ends_with_ellipsis}",
            status,
        ))


def main() -> int:
    if not BEFORE.exists() or not AFTER.exists():
        print(f"ERROR: faltan {BEFORE} y/o {AFTER}. Generar con tools/fur-preview antes/después.",
              file=sys.stderr)
        return 1

    failures: list[str] = []
    rows: list[tuple] = []

    check_cf4(failures, rows)
    check_cf12(failures, rows)
    check_cf2_cf3(failures, rows)

    header = f"{'Verificación':60} {'Cuerpo':18} {'Detalle':55} Estado"
    print(header)
    print("-" * len(header))
    for label, size_info, detail, status in rows:
        print(f"{label:60} {size_info:18} {detail:55} {status}")

    if failures:
        print("\nFALLOS:", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1

    print("\nOK — CF4 (cuerpo idéntico), CF2/CF3 (contenido en caja) y CF12 (sellos idénticos).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
