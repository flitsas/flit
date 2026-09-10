#!/usr/bin/env python3
"""Generate catalogs.vehicle_colors seed SQL from SeedData/vehicle-colors.csv."""

from __future__ import annotations

import csv
import uuid
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]  # services/core-api
SRC = (
    REPO
    / "src"
    / "Flit.Infrastructure"
    / "Persistence"
    / "Sql"
    / "SeedData"
    / "vehicle-colors.csv"
)
OUT = (
    REPO
    / "src"
    / "Flit.Infrastructure"
    / "Persistence"
    / "Sql"
    / "Ddl"
    / "55-vehicle-colors-catalog-seed.sql"
)
NS = uuid.UUID("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
BATCH = 250


def esc(value: str) -> str:
    return value.replace("'", "''")


def main() -> None:
    rows: list[tuple[str, str, str, str, str, str]] = []
    with SRC.open(encoding="utf-8", newline="") as handle:
        for raw in csv.DictReader(handle, delimiter=";"):
            code = (raw.get("color_code") or "").strip()
            name = (raw.get("color_description") or "").strip()
            if not code or not name:
                continue
            source_id = (raw.get("id") or "").strip()
            deleted = (raw.get("deleted_at") or "").strip().upper()
            active = "FALSE" if deleted and deleted != "NULL" else "TRUE"
            deleted_sql = "NULL" if active == "TRUE" else "now()"
            color_id = str(uuid.uuid5(NS, f"vehicle-color:{code}"))
            refs = (
                esc(f'{{"source_id":{source_id}}}')
                if source_id.isdigit()
                else "{}"
            )
            rows.append((color_id, code[:20], name[:120], active, refs, deleted_sql))

    lines = [
        "-- Catálogo RUNT de colores de vehículo. Idempotente ON CONFLICT (code).",
        "-- Generado: python services/core-api/tools/generate-vehicle-colors-seed.py",
        "",
    ]
    for i in range(0, len(rows), BATCH):
        chunk = rows[i : i + BATCH]
        lines.append(
            "INSERT INTO catalogs.vehicle_colors "
            "(id, code, name, is_active, external_refs, deleted_at)"
        )
        lines.append("VALUES")
        vals = [
            f"  ('{cid}'::uuid, '{esc(code)}', '{esc(name)}', {active}, "
            f"'{refs}'::jsonb, {deleted_sql})"
            for cid, code, name, active, refs, deleted_sql in chunk
        ]
        lines.append(",\n".join(vals))
        lines.append("ON CONFLICT (code) DO UPDATE SET")
        lines.append("  name = EXCLUDED.name,")
        lines.append("  is_active = EXCLUDED.is_active,")
        lines.append("  external_refs = EXCLUDED.external_refs,")
        lines.append("  deleted_at = EXCLUDED.deleted_at,")
        lines.append("  updated_at = now();")
        lines.append("")

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"wrote {len(rows)} rows -> {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
