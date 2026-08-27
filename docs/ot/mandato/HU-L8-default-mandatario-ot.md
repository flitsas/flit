# HU-L8 — Mandatario por defecto del OT

> Generado: 2026-08-27  
> Registro local (sin work item ADO). Rama: `review/rl-ficha-aislada-escritura-unica`.

## Objetivo

Un organismo puede fijar un **mandatario global por defecto**. Ese default **manda aunque no esté vinculado a la compañía gestora**. El mandato con el que **nace** el OT no cambia (`open`, plantilla genérica, sin firmante persona).

## Cascada (sin elección en el trámite)

| Default OT | Default compañía | Quién se pinta |
|------------|------------------|----------------|
| Sí | Sí | OT |
| No | Sí | Compañía (si está entre candidatos de esa gestora×OT) |
| Sí | No | OT |
| No | No | Vacío (ya no se autoelige el único candidato) |

La elección explícita del wizard (`MandateSignerId`) sigue ganando.

## Datos

- Columna `admin.transit_office_mandate_config.default_mandate_signer_id` (nullable, FK a `mandate_signers`, `ON DELETE SET NULL`).
- Nacimiento: `NULL`.
- Validación al guardar: firmante activo del mismo organismo (oficina primaria o vínculo `mandate_signer_transit_offices`).
