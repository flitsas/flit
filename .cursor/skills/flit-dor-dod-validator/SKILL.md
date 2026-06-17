---
name: flit-dor-dod-validator
description: Valida work items FLIT contra Definition of Ready (10 Feature / 10 US) o Definition of Done (12 US Resolved / 28 Feature Closed) antes de transiciones de estado. Emite PASS/FAIL/NA con evidencia y veredicto OK_TO_TRANSITION, MISSING_N o BLOCKED. Usar con tech-lead-agent (modo C) o antes de planificación/sprint. Triggers DoR, DoD, Active, Resolved, Closed, flit-dor-dod-validator, modo C.
---

Solo valida; **nunca** ejecuta la transición en Azure DevOps.

## Pre-flight

1. `.cursor/skills/flit-dor-dod-validator/SKILL.md` — criterios DoR/DoD y transiciones
2. `az boards work-item show --id <ID> --output json`

## Conjuntos de criterios

| target_state | Tipo | Conjunto |
|--------------|------|----------|
| Active | Feature | DoR-Feature (10) |
| Active | User Story | DoR-US (10) |
| Resolved | User Story | DoD-US (12) |
| Closed | Feature | DoD-Feature (28) |

Detalle de los 28 de Feature: ver secciones DoR/DoD en este mismo SKILL.

## Checklist

- [ ] Identificar `target_state` y tipo de ítem
- [ ] Evaluar cada criterio con evidencia
- [ ] Contar PASS / FAIL / NA
- [ ] Emitir recomendación
- [ ] Entregar reporte en plantilla

## DoR-Feature (resumen)

Módulo, objetivo, descripción extendida, ≥3 criterios funcionales, sprint siguiente, Area FLIT, tag `DOR`, AssignedTo humano, sin placeholders ni datos sensibles.

## DoR-US (resumen)

Parent Feature Active/Resolved, título `[BACKEND|FRONTEND]`, AC positivo+negativo, Story Points, `Refinement=true`, dependencias, sprint siguiente, tag `DOR`, sin placeholders.

## DoD-US (resumen)

Tests/PR/CI/seguridad/cobertura ≥80%, sin TODOs nuevos, OpenAPI si aplica, migraciones idempotentes, comentario en US, co-authored-by, PR sin threads abiertos.

## Plantilla de salida

Ver `./plantilla-reporte-dor-dod.md`.

## Prohibido

- Ejecutar transición de estado
- FAIL sin evidencia
- Omitir criterios "obvios"
- Marcar NA si hay duda (usar FAIL con nota)
