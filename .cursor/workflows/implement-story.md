# Workflow: Implement Story

**Objetivo:** Tomar una Historia de Usuario existente en ADO y llevarla desde `New` hasta integrada en `develop` y en estado `Resolved`, con evidencias publicadas.

**Invocación típica:**
```
Implementa la historia #4521
```

---

## Precondiciones

- La HU existe en ADO con estado `New` o `Active`.
- La HU tiene AC en Gherkin.
- La HU tiene Story Points asignados.
- Existe diseño técnico o la HU es lo suficientemente pequeña para no requerirlo.

---

## Fases — resumen

| # | Fase | Agente / Skill | Gate humano |
|---|------|----------------|-------------|
| 1 | Verificar DoR de la HU | `tech-lead-agent` (modo C) | — |
| 2 | Activar HU en ADO | skill `flit-gestion-hu` | **Confirmación explícita del usuario** |
| 3 | Implementar | `backend-agent` / `frontend-agent` según tipo | — |
| 3b | Lint y formato fullstack | `pnpm fix:all:fullstack` | — |
| 3c | Validación de schema/datos (si hay migraciones) | `database-agent` (modo C) + `db-schema-validator` | — |
| 4 | Tests unitarios y evidencias | skill `dev-tester` | — |
| 5 | Review del PR | `code-review-agent` + `security-agent` | — |
| 6 | Integrar PR | `integration-agent` | **Confirmación explícita para el merge** |
| 7 | Marcar HU como Resolved | skill `flit-gestion-hu` | — (automático post-merge DEV) |

---

## Fase 1 — Verificar DoR de la HU

**Agente:** `tech-lead-agent` (modo C)

**Instrucción:**
```
Usa el tech-lead-agent (modo C) para validar el DoR de la HU #[hu_id]
```

**Outputs esperados:**
- Veredicto: `OK_TO_TRANSITION` o `MISSING_N`
- Lista de campos faltantes si aplica

**Si DoR no pasa:** reportar al usuario los campos faltantes y detener el flujo. No continuar hasta que la HU esté completa.

---

## Fase 2 — Activar HU en ADO

**Gate obligatorio — no omitir bajo ninguna circunstancia.**

Mostrar al usuario:
```
⚠️ Voy a activar la HU en Azure DevOps antes de iniciar la implementación.

  HU: #[hu_id] — [título]
  Acción: cambiar estado → Active
  Se publicará un comentario de inicio en Discussion.

¿Confirmas? (sí / no)
```

Si la respuesta es "sí" → ejecutar con skill `flit-gestion-hu` (Motivo A).
Si la respuesta es "no" → detener y consultar cómo proceder.

---

## Fase 3 — Implementar

**Determinar el tipo de la HU:**

| Tipo / alcance | Agente |
|------|--------|
| `[BACKEND]` — schema, migración, catálogos | `database-agent` (modo A/B) |
| `[BACKEND]` — API, use cases, repositorios | `backend-agent` |
| `[FRONTEND]` | `frontend-agent` |
| `[AMBOS]` | `backend-agent` primero, luego `frontend-agent` cuando el backend esté mergeado |

> Si el título o la descripción de la HU menciona *migración*, *schema*, *tabla*, *catálogo* o *DDL* → `database-agent`. Si menciona *endpoint*, *use case* o *repositorio* sobre tablas ya existentes → `backend-agent`.
> Si menciona *FUR*, *Formulario Único*, *casilla*, *Anexo 46* o *Resolución 20233040017145* → consultar `expert-doc-engine` **antes** de cambiar el overlay o el mapper, y aplicar `docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md`.

**Instrucción (ejemplos):**
```
Usa el database-agent (modo B) para implementar la HU #[hu_id] — contexto: diseño ADO Wiki, checklist db-schema-validator
Usa el backend-agent para implementar la HU #[hu_id] — contexto: [AC de la HU, diseño si existe]
```

**Outputs esperados:**
- Branch creado: `feature/AB-[hu_id]-[slug]`
- Código implementado siguiendo la arquitectura del proyecto
- PR abierto con target `develop`
- Título del PR sigue convención FLIT

**Restricción:** El orquestador no debe asignar implementación de código a:
- `architecture-agent` → diseña, no implementa
- `database-agent` → schema/migraciones; no use cases ni controllers
- `qa-agent` → prueba, no implementa
- `code-review-agent` / `security-agent` → revisan, no implementan
- `tech-lead-agent` → coordina, no implementa

---

## Fase 3b — Lint y formato fullstack

**Ejecutor:** orquestador directamente (no requiere invocar un agente)

```bash
pnpm fix:all:fullstack
```

Este comando corre en secuencia:
1. `lint:fix` → ESLint (frontend Next.js) + Ruff fix (Python ML) + dotnet format (core-api)
2. `format` → ESLint/format del frontend + Ruff format (Python ML) + dotnet format (core-api)

**Por qué aquí:** el código ya existe (Fase 3) y los tests aún no han corrido (Fase 4). Ejecutar el fix antes de los tests garantiza que los tests corran sobre código limpio y que el commit no tenga errores de estilo.

**Outputs esperados:**
- Salida del comando sin errores fatales
- Posibles cambios de formato en archivos — normales y esperados
- Si hay errores que el fix no puede resolver automáticamente (ej. errores de tipo en TypeScript o violaciones de reglas no autofix) → reportar al agente implementador para que los corrija antes de continuar

**Si el comando falla:**
- Leer el output del error
- Si es un error de formato/lint corregible → el agente implementador ajusta el código y se vuelve a ejecutar
- Si es un error de configuración del entorno (pnpm no disponible, etc.) → informar al usuario y continuar sin este paso solo si el usuario lo autoriza explícitamente

---

## Fase 3c — Validación de schema y capa de datos (condicional)

**Agente:** `database-agent` (modo C) + skill `db-schema-validator`

**Precondición:** El PR de la HU incluye migraciones, cambios en `Persistence/` o repositorios.

**Instrucción:**
```
Usa el database-agent (modo C) para validar migraciones y repositorios de la HU #[hu_id] — invoca db-schema-validator
```

**Outputs esperados:**
- Reporte PASS/FAIL según checklist `db-schema-validator` (§A schema, §B repositorio)
- Veredicto: `OK_TO_MERGE_DB`, `MISSING_N` o `BLOCKED`

**Si veredicto es BLOCKED:** el flujo se pausa; el implementador corrige antes del review (Fase 5).

**Si no hay cambios de persistencia:** omitir (NA).

---

## Fase 4 — Tests unitarios y evidencias

**Skill:** `dev-tester` (obligatorio al terminar implementación)

**Instrucción:**
```
Usa dev-tester para la HU #[hu_id] — genera tests unitarios desde los AC y publica evidencias en ADO
```

**Outputs esperados:**
- Tests unitarios creados y pasando
- Evidencias publicadas en `Custom.Evidences` de la HU en ADO (un bloque por AC)

Esta fase **no es opcional**. Si el agente implementador no la ejecutó, el orquestador la ejecuta antes de pasar al review.

---

## Fase 5 — Review del PR

Ejecutar el workflow `review-pr.md` con el número del PR creado en Fase 3.

**Outputs esperados:**
- `code-review-agent`: status check `pass` o `fail` con comentario consolidado
- `security-agent`: reporte de seguridad con status `PASS` o `FAIL`

**Si hay bloqueantes:**
- Informar al agente implementador con el detalle de los bloqueantes
- El flujo se pausa hasta que el implementador corrija y actualice el PR
- No reintentar el review hasta que el PR tenga nuevos commits

---

## Fase 6 — Integrar PR

**Gate obligatorio.**

Mostrar al usuario:
```
✅ Code Review: [pass/fail]
✅ Security: [PASS/FAIL]

El PR !N está listo para merge a develop.
¿Confirmas el merge? (sí / no)
Reviewer humano asignado: [nombre o "pendiente de asignar"]
```

Si la respuesta es "sí" → invocar `integration-agent`:
```
Usa el integration-agent para mergear el PR !N del feature #[hu_id]
```

**Outputs esperados:**
- PR mergeado en `develop`
- Registro en ADO (`Custom.Commits`, commentario en Discussion)

---

## Fase 7 — Marcar HU como Resolved

**Skill:** `flit-gestion-hu` (Motivo B) — automático al confirmar merge en DEV.

**Instrucción:**
```
Usa flit-gestion-hu (Motivo B) para marcar la HU #[hu_id] como Resolved — merge en develop confirmado
```

**Outputs esperados:**
- HU en estado `Resolved` en ADO
- Comentario de cierre publicado en Discussion de la HU

**Trazabilidad final:**
```html
<div>[Orchestrator] HU #[hu_id] completada: implementada, revisada, integrada y marcada Resolved.<br/>
PR: ![pr_number] mergeado en develop.<br/>
Siguiente: QA puede iniciar validación cuando lo decida el equipo.</div>
```

---

## Si falla alguna fase

| Situación | Acción |
|-----------|--------|
| DoR no pasa | Reportar campos faltantes; no continuar hasta que se completen |
| Implementación incompleta | Informar al usuario; no avanzar al review |
| Review con bloqueantes | Informar al implementador; pausar hasta corrección |
| Merge rechazado por el usuario | Detener; el flujo queda en espera hasta nueva confirmación |
| `dev-tester` falla | Informar al implementador; los tests son obligatorios antes del review |
