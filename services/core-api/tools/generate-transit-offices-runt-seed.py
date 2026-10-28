#!/usr/bin/env python3
"""Generate RUNT-aligned transit office catalog seed for FLIT 2.0 (HU #10659 / B11).

Preferred source (PDN-aligned, Oct 2026 OT remediation):
  services/core-api/tools/data/transit_offices_canonical.csv

Fallback source (legacy RUNT dump, may be absent in sparse checkouts):
  context/traffic_secretaries_example/traffic_secreataries.txt

Also applies code renames from:
  services/core-api/tools/data/transit_offices_code_renames.json
so environments that still have legacy …000 codes converge to Excel …001.

Cleaning rules (TSV fallback only):
- Exclude TEST rows and non-numeric traffic_agency_code.
- Normalize code_dane_municipality to 5 digits (zfill).
- Deduplicate by name: prefer traffic_agency_code ending in '001' (Excel),
  then '000', then lexicographic min.

Output: Persistence/Sql/Ddl/27-HU10659-transit-offices-runt-catalog-seed.sql

Regenerate:
  python services/core-api/tools/generate-transit-offices-runt-seed.py
"""

from __future__ import annotations

import csv
import json
import re
import uuid
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DATA_DIR = Path(__file__).resolve().parent / "data"
CANONICAL_CSV = DATA_DIR / "transit_offices_canonical.csv"
RENAMES_JSON = DATA_DIR / "transit_offices_code_renames.json"
TSV = REPO_ROOT / "context/traffic_secretaries_example/traffic_secreataries.txt"
OUT = (
    REPO_ROOT
    / "services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/27-HU10659-transit-offices-runt-catalog-seed.sql"
)

# Preserve dev/E2E UUIDs used across tests and grants (HU #10133).
FIXED_IDS: dict[str, str] = {
    "11001000": "aaaaaaaa-0001-4000-8000-000000000001",  # Bogotá
    "5001000": "aaaaaaaa-0001-4000-8000-000000000002",  # Medellín
    "76001000": "aaaaaaaa-0001-4000-8000-000000000003",  # Cali
    "8001000": "aaaaaaaa-0001-4000-8000-000000000004",  # Barranquilla
    "68001000": "aaaaaaaa-0001-4000-8000-000000000005",  # Bucaramanga
    "13001000": "aaaaaaaa-0001-4000-8000-000000000006",  # Cartagena
}

# Legacy 5-digit dev codes from 16-HU10133-ot-admin-dev-seed.sql → RUNT traffic_agency_code.
LEGACY_DEV_CODE_UPDATES: list[tuple[str, str]] = [
    ("aaaaaaaa-0001-4000-8000-000000000001", "11001000"),
    ("aaaaaaaa-0001-4000-8000-000000000002", "5001000"),
    ("aaaaaaaa-0001-4000-8000-000000000003", "76001000"),
    ("aaaaaaaa-0001-4000-8000-000000000004", "8001000"),
    ("aaaaaaaa-0001-4000-8000-000000000005", "68001000"),
    ("aaaaaaaa-0001-4000-8000-000000000006", "13001000"),
]

UUID_NS = uuid.UUID("6ba7b810-9dad-11d1-80b4-00c04fd430c8")
FLITDEV_TENANT = "11111111-1111-1111-1111-111111111111"


def sql_str(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def sql_bool(value: bool) -> str:
    return "true" if value else "false"


def parse_bool(raw: str | bool | None) -> bool:
    if isinstance(raw, bool):
        return raw
    s = str(raw or "").strip().lower()
    return s in {"1", "t", "true", "yes", "y"}


def clean_row(raw: dict[str, str]) -> dict[str, str] | None:
    name = (raw.get("name") or "").strip()
    code = (raw.get("traffic_agency_code") or "").strip()
    city = (raw.get("code_dane_municipality") or "").strip()
    dept = (raw.get("department_dane_code") or "").strip()

    if not name or "TEST" in name.upper():
        return None
    if not code or not re.fullmatch(r"[0-9]+", code):
        return None
    if len(code) > 10 or len(name) > 200:
        return None

    if not city or city == "00000":
        city = code[:5] if len(code) >= 5 else code.zfill(5)
    else:
        city = city.zfill(5)

    if not dept:
        dept = city[:2]
    dept = dept.zfill(2)[:2]

    return {
        "id": office_id(code),
        "name": name,
        "code": code,
        "city_code": city,
        "department_code": dept,
        "is_active": True,
    }


def pick_duplicate(group: list[dict[str, str]]) -> dict[str, str]:
    # Excel oficial prioriza …001 sobre …000 cuando hay homónimos.
    ending_001 = [g for g in group if g["code"].endswith("001")]
    if ending_001:
        return min(ending_001, key=lambda g: g["code"])
    ending_000 = [g for g in group if g["code"].endswith("000")]
    if ending_000:
        return min(ending_000, key=lambda g: g["code"])
    return min(group, key=lambda g: g["code"])


def office_id(code: str) -> str:
    return FIXED_IDS.get(code, str(uuid.uuid5(UUID_NS, code)))


def load_renames() -> list[dict[str, str]]:
    if not RENAMES_JSON.exists():
        return []
    data = json.loads(RENAMES_JSON.read_text(encoding="utf-8"))
    out: list[dict[str, str]] = []
    for row in data:
        old_code = str(row.get("old_code") or "").strip()
        new_code = str(row.get("new_code") or "").strip()
        if old_code and new_code and old_code != new_code:
            out.append({"old_code": old_code, "new_code": new_code})
    return out


def load_rows_from_canonical() -> list[dict]:
    with CANONICAL_CSV.open(encoding="utf-8", newline="") as f:
        raw_rows = list(csv.DictReader(f))

    rows: list[dict] = []
    for raw in raw_rows:
        code = (raw.get("code") or "").strip()
        name = (raw.get("name") or "").strip()
        if not code or not name:
            continue
        oid = (raw.get("id") or "").strip() or office_id(code)
        # Keep FIXED_IDS for the six E2E cities even if CSV differs.
        oid = FIXED_IDS.get(code, oid)
        rows.append(
            {
                "id": oid,
                "code": code,
                "name": name,
                "department_code": (raw.get("department_code") or "").strip(),
                "city_code": (raw.get("city_code") or "").strip(),
                "is_active": parse_bool(raw.get("is_active")),
            }
        )
    rows.sort(key=lambda r: r["code"])
    return rows


def load_rows_from_tsv() -> list[dict]:
    with TSV.open(encoding="utf-8") as f:
        raw_rows = list(csv.DictReader(f, delimiter="\t"))

    cleaned = [row for r in raw_rows if (row := clean_row(r))]
    by_name: dict[str, list[dict]] = defaultdict(list)
    for row in cleaned:
        by_name[row["name"].upper()].append(row)

    deduped = [pick_duplicate(group) for group in by_name.values()]
    deduped.sort(key=lambda r: r["code"])
    return deduped


def load_rows() -> tuple[list[dict], str]:
    if CANONICAL_CSV.exists():
        return load_rows_from_canonical(), "tools/data/transit_offices_canonical.csv"
    if TSV.exists():
        return load_rows_from_tsv(), "context/traffic_secretaries_example/traffic_secreataries.txt"
    raise FileNotFoundError(
        f"No catalog source found. Expected {CANONICAL_CSV} or {TSV}"
    )


def render_sql(rows: list[dict], source_label: str, renames: list[dict[str, str]]) -> str:
    lines: list[str] = [
        "-- HU #10659 (DEV) — Catálogo RUNT de organismos de tránsito (B11)",
        "-- ⚠️  DEV/QA: alinea nombres/códigos oficiales (Excel OT) + estado PDN.",
        f"-- Fuente: {source_label}",
        "-- Generado por: services/core-api/tools/generate-transit-offices-runt-seed.py",
        f"-- Filas: {len(rows)}",
        "-- Idempotente: UPSERT por catalogs.transit_offices.code (uq_transit_offices_code).",
        "-- Los 6 UUID fijos (aaaaaaaa-…001–006) se conservan para E2E HU #10133.",
        "-- Incluye renombres …000→…001 y is_active=false para OT Calera legacy 25377001.",
        "",
        "BEGIN;",
        "",
        "SET LOCAL row_security = off;",
        "",
        "-- Migrar códigos ficticios de 5 dígitos (HU #10133) a traffic_agency_code RUNT (8 dígitos).",
        "-- Conserva los UUID fijos referenciados por grants/E2E.",
    ]

    row_by_id = {r["id"]: r for r in rows}
    for legacy_id, runt_code in LEGACY_DEV_CODE_UPDATES:
        row = row_by_id.get(legacy_id)
        if row is None:
            continue
        lines.append(
            f"UPDATE catalogs.transit_offices SET code = {sql_str(runt_code)} "
            f"WHERE id = {sql_str(legacy_id)}::uuid AND code <> {sql_str(runt_code)};"
        )
    lines.append("")

    if renames:
        lines.append("-- Remediación OT 2026-10: códigos Excel (…001) sobre legacy …000.")
        for r in renames:
            lines.append(
                "UPDATE catalogs.transit_offices "
                f"SET code = {sql_str(r['new_code'])}, updated_at = NOW() "
                f"WHERE code = {sql_str(r['old_code'])} "
                f"AND NOT EXISTS (SELECT 1 FROM catalogs.transit_offices x WHERE x.code = {sql_str(r['new_code'])});"
            )
        lines.append("")

    for row in rows:
        lines.append(
            "INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)"
        )
        lines.append("VALUES (")
        lines.append(f"    {sql_str(row['id'])}::uuid,")
        lines.append(f"    {sql_str(row['code'])},")
        lines.append(f"    {sql_str(row['name'])},")
        lines.append(f"    {sql_str(row['department_code'])},")
        lines.append(f"    {sql_str(row['city_code'])},")
        lines.append(f"    {sql_bool(bool(row['is_active']))}")
        lines.append(")")
        lines.append("ON CONFLICT (code) DO UPDATE SET")
        lines.append("    name = EXCLUDED.name,")
        lines.append("    department_code = EXCLUDED.department_code,")
        lines.append("    city_code = EXCLUDED.city_code,")
        lines.append("    is_active = EXCLUDED.is_active;")
        lines.append("")

    # Dev grant: Funza (ejemplo traspaso RUNT) para tenant FLITDEV.
    lines.extend(
        [
            "-- Grant opcional dev: Funza habilitado para FLITDEV (pruebas B11 auto-bind).",
            "INSERT INTO admin.tenant_transit_office_grants (id, tenant_id, transit_office_id, is_enabled, created_at)",
            "SELECT uuidv7(), "
            f"{sql_str(FLITDEV_TENANT)}::uuid, id, true, now()",
            "FROM catalogs.transit_offices",
            "WHERE code = '25286000'",
            "ON CONFLICT (tenant_id, transit_office_id) DO NOTHING;",
            "",
            "COMMIT;",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> None:
    rows, source_label = load_rows()
    renames = load_renames()
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(render_sql(rows, source_label, renames), encoding="utf-8")
    inactive = sum(1 for r in rows if not r["is_active"])
    print(
        f"Wrote {len(rows)} offices ({inactive} inactive, {len(renames)} renames) "
        f"from {source_label} → {OUT.relative_to(REPO_ROOT)}"
    )


if __name__ == "__main__":
    main()
