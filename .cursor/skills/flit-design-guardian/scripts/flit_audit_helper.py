#!/usr/bin/env python3
"""
Auditor auxiliar FLIT.

Uso:
  python scripts/flit_audit_helper.py --tokens references/flit_design_tokens.json --files path/to/file.css path/to/file.tsx

El script detecta códigos HEX en archivos frontend y reporta colores no presentes en los tokens FLIT. No sustituye la auditoría visual humana/IA, pero ayuda a bloquear drift cromático.
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any, Iterable

HEX_RE = re.compile(r"#[0-9a-fA-F]{3,8}\b")


def iter_values(obj: Any) -> Iterable[Any]:
    if isinstance(obj, dict):
        for value in obj.values():
            yield from iter_values(value)
    elif isinstance(obj, list):
        for item in obj:
            yield from iter_values(item)
    else:
        yield obj


def normalize_hex(value: str) -> str:
    value = value.strip().lower()
    if re.fullmatch(r"#[0-9a-f]{3}", value):
        return "#" + "".join(ch * 2 for ch in value[1:])
    return value


def load_allowed_colors(tokens_path: Path) -> set[str]:
    tokens = json.loads(tokens_path.read_text(encoding="utf-8"))
    colors: set[str] = set()
    for value in iter_values(tokens):
        if isinstance(value, str):
            for match in HEX_RE.findall(value):
                colors.add(normalize_hex(match))
    return colors


def scan_file(path: Path, allowed: set[str]) -> list[tuple[int, str, str]]:
    findings: list[tuple[int, str, str]] = []
    text = path.read_text(encoding="utf-8", errors="ignore")
    for line_no, line in enumerate(text.splitlines(), start=1):
        for match in HEX_RE.findall(line):
            normalized = normalize_hex(match)
            if normalized not in allowed:
                findings.append((line_no, match, line.strip()))
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description="Auditar colores HEX contra tokens FLIT")
    parser.add_argument("--tokens", required=True, type=Path, help="Ruta al archivo flit_design_tokens.json")
    parser.add_argument("--files", nargs="+", required=True, type=Path, help="Archivos frontend a auditar")
    args = parser.parse_args()

    allowed = load_allowed_colors(args.tokens)
    total_findings = 0

    print("# Auditoría de colores FLIT")
    print(f"Tokens permitidos: {len(allowed)} colores detectados")

    for file_path in args.files:
        if not file_path.exists():
            print(f"\n[ERROR] No existe: {file_path}")
            total_findings += 1
            continue
        findings = scan_file(file_path, allowed)
        if not findings:
            print(f"\n[OK] {file_path}: sin colores HEX no autorizados")
            continue
        print(f"\n[NO APROBADO] {file_path}: {len(findings)} color(es) no autorizado(s)")
        total_findings += len(findings)
        for line_no, color, line in findings:
            print(f"  línea {line_no}: {color} :: {line}")

    if total_findings:
        print("\nVeredicto: No aprobado. Reemplazar colores no autorizados por tokens FLIT o documentar excepción aprobada.")
        return 1

    print("\nVeredicto: Aprobado en auditoría cromática automatizada. Continuar con auditoría visual, UX y accesibilidad.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
