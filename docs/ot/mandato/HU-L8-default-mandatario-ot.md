# HU-L8 — Mandatario por defecto del OT

> Generado: 2026-08-27  
> Registro local (sin work item ADO). Rama: `review/rl-ficha-aislada-escritura-unica`.

## Objetivo

Un organismo puede fijar un **mandatario global por defecto** (persona natural). Si la empresa que radica tiene otro default en ese OT, **ese gana**. El default del OT aplica cuando no hay default cliente×OT, **aunque no esté vinculado a la compañía gestora**. El **modo** al nacer sigue `open` y sin firmante persona; la plantilla de redacción la fija HU-L10.

## Cascada (sin elección en el trámite)

El mandatario es siempre una **persona natural**.

| Default cliente×OT | Default OT | Quién se pinta |
|--------------------|------------|----------------|
| Sí (candidato de esa gestora×OT) | Sí o no | Cliente×OT |
| No | Sí | OT (aunque no esté en candidatos de la gestora) |
| No | No | Vacío (ya no se autoelige el único candidato) |

La elección explícita del wizard (`MandateSignerId`) sigue ganando.

## Datos

- Columna `admin.transit_office_mandate_config.default_mandate_signer_id` (nullable, FK a `mandate_signers`, `ON DELETE SET NULL`).
- Nacimiento: `NULL`. El **modo** al nacer sigue `open`; la **plantilla** pasa a HU-L10 (municipal si el OT es conocido).
- Validación al guardar: firmante activo del mismo organismo (oficina primaria o vínculo `mandate_signer_transit_offices`).
