# Reporte db-schema-validator

**PR / rama:** [número o rama]
**HU / Feature:** #[id]
**Archivos analizados:** [lista]
**Fecha:** [YYYY-MM-DD]

---

## Veredicto

`OK_TO_MERGE_DB` | `MISSING_N` | `BLOCKED` | `NA`

---

## Resumen

| Sección | PASS | FAIL | NA |
|---------|------|------|-----|
| A — Schema (§16) | n | n | n |
| B — Data access (§15) | n | n | n |
| C — Anti-patterns | — | n bloqueantes | — |

---

## Detalle — Schema (A)

| ID | Criterio | Estado | Evidencia / acción |
|----|----------|--------|-------------------|
| A1 | Schema bounded context | PASS/FAIL/NA | |
| … | | | |

---

## Detalle — Data access (B)

| ID | Criterio | Estado | Evidencia / acción |
|----|----------|--------|-------------------|
| B1 | Domain sin EF Core | PASS/FAIL/NA | |
| … | | | |

---

## Bloqueantes (si aplica)

1. [Descripción + archivo:línea + regla violada + fix sugerido]

---

## Advertencias corregibles (MISSING_N)

1. [Descripción + fix sugerido]

---

## ADR y trazabilidad

- ADR referenciado: [ADR-NNNN o "FALTA — solicitar a architecture-agent"]
- Entidades nuevas: [lista]

---

## Recomendación

[Un párrafo: mergeable desde perspectiva de datos / requiere correcciones / escalar a database-agent]
