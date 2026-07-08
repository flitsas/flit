# ADR-0027 — Organismo de tránsito fijado desde RUNT en traspaso (sin cambio manual)

- **Estado**: Propuesto · 2026-07-08
- **Módulo**: Trámites — Traspaso (TR), paso FUR / organismo de tránsito
- **Requerimientos**: Pendiente B11 (`PendientesFLIT2.0MI-TR.xlsx`): *"Si estoy creando un traspaso no debe permitir cambiar el Organismo de Tránsito, ya que el vehículo tiene un OT asignado"*
- **Relacionado**: C9 (impronta pide OT cuando ya existe) — se beneficia de este ADR al fijar `transit_office_id` desde preflight
- **Decide**: Líder Técnico

## Contexto

En **matrícula inicial** el operador elige libremente el organismo de tránsito donde radicará el
trámite (paso FUR): el modal `OrganismoModal` en `FirmaFurStep.tsx` lista los OT **habilitados**
para la empresa (`GET /api/v1/tramites/transit-offices`) y persiste `transit_office_id`,
`transit_office_code`, `transit_office_name`, `transit_office_city` vía `PATCH field-values`.

En **traspaso**, el vehículo **ya está matriculado** y el RUNT devuelve `organismoTransito`, que
los mappers hidratan como `transit_office_name` (source `consultation`) en el preflight. Sin
embargo:

1. Hoy **no** se resuelve automáticamente el `transit_office_id` del catálogo FLIT.
2. El paso FUR sigue mostrando el botón **"Cambiar"** y permite elegir otro OT habilitado.
3. `PatchFieldValuesHandler` **no distingue** modalidad: cualquier cambio user-side a
   `transit_office_*` se acepta en borrador.

Esto contradice B11 y contribuye a C9 (generar impronta pide seleccionar OT cuando el nombre RUNT
ya está pero falta el `id`).

## Decisión

Para instancias con tipología **`traspaso_standard`**:

1. **Auto-vinculación en preflight (backend):** tras hidratar `transit_office_name` desde RUNT,
   resolver el OT en el catálogo habilitado de la empresa (match por nombre, case-insensitive,
   misma regla que `runtSuggestion` en el frontend) y persistir también `transit_office_id`,
   `transit_office_code` y `transit_office_city` con `Source = consultation`. Si el nombre RUNT
   **no** coincide con ningún OT habilitado, se conserva solo `transit_office_name` (consultation)
   y **no** se inventa un `id`.
2. **Bloqueo de cambio manual (backend autoritativo):** `PatchFieldValuesHandler` rechaza
   cualquier `PATCH` de claves `transit_office_*` iniciado por el usuario (`Source = user`) en
   traspaso. Código de error: `ot_traspaso_no_modificable`. La excepción de post-submit para
   organismo (`IsPostSubmitTransitOfficeKey`) **no aplica** en traspaso.
3. **UI de solo lectura (frontend):** en `modalidad === 'traspaso'`, la sección Organismo no muestra
   "Seleccionar" ni "Cambiar"; no se auto-abre `OrganismoModal`. Se muestra el OT (nombre/código)
   con texto explicativo: *"El organismo proviene del RUNT y no puede modificarse en un traspaso."*
   Si falta `transit_office_id` pero hay nombre RUNT, mostrar aviso de OT no habilitado para la
   empresa (sin ofrecer selector).
4. **Matrícula inicial:** sin cambios — sigue el flujo actual de selección/cambio libre entre OT
   habilitados.

## Alternativas consideradas

### Alternativa A — Auto-bind en preflight + bloqueo back/front (RECOMENDADA)
- (+) Cumple B11 literal; corrige la raíz de C9 al poblar `transit_office_id` temprano.
- (+) Backend como fuente de verdad; UI alineada.
- (+) Matrícula no se toca.
- (−) Si RUNT devuelve nombre que no matchea catálogo, el traspaso queda bloqueado hasta que
  SuperAdmin habilite el grant correcto (comportamiento deseado).
- Esfuerzo: **bajo-medio**. Riesgo: bajo.

### Alternativa B — Solo ocultar botón "Cambiar" en frontend
- (+) Cambio mínimo en UI.
- (−) API directa sigue permitiendo cambiar OT; no cumple B11 de forma segura.
- Esfuerzo: mínimo. Riesgo: **alto**.

### Alternativa C — Permitir cambio solo si OT RUNT no está en grants
- (+) Flexibilidad operativa cuando el nombre RUNT no matchea.
- (−) Contradice B11 ("no debe permitir cambiar"); abre puerta a radicar en OT distinto al del
  vehículo.
- Esfuerzo: medio. Riesgo: medio-alto (regla de negocio ambigua).

## Consecuencias por agente

- **Backend:** resolver OT en `PreflightCommand` (solo traspaso); gate en `PatchFieldValuesHandler`;
  tests xUnit para rechazo de patch y auto-bind exitoso.
- **Frontend:** `FirmaFurStep` / `OrganismoSection` — solo lectura en traspaso; tests vitest.
- **QA:** traspaso con OT RUNT habilitado → sin botón Cambiar; patch directo → 409;
  matrícula → sigue pudiendo elegir/cambiar OT.
- **Security:** reduce manipulación de OT destino en traspaso (integridad del trámite).
- **Infra:** sin migración ni despliegue especial.

## Requisito vs decisión (trazabilidad)

| Pendiente Excel | Decisión ADR |
|-----------------|--------------|
| B11 — No permitir cambiar OT en traspaso | OT fijado desde RUNT; bloqueo user-side back+front |
| C9 (parcial) | Auto-bind de `transit_office_id` en preflight reduce el síntoma |
