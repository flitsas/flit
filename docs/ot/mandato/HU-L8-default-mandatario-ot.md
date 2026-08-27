# HU-L8 — Mandatario por defecto del OT

> Generado: 2026-08-27  
> Registro local (sin work item ADO). Rama: `review/rl-ficha-aislada-escritura-unica`.

## Objetivo

Un organismo puede fijar un **mandatario global por defecto**. Ese default **manda aunque no esté vinculado a la compañía gestora**. El **modo** al nacer sigue `open` y sin firmante persona; la plantilla de redacción la fija HU-L10.

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
- Nacimiento: `NULL`. El **modo** al nacer sigue `open`; la **plantilla** pasa a HU-L10 (municipal si el OT es conocido).
- Validación al guardar: firmante activo del mismo organismo (oficina primaria o vínculo `mandate_signer_transit_offices`).
