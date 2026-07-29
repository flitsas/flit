# ADR-0036: Prevalidación restringida a persona natural + tracking de identidad desacoplado del trámite

**Fecha**: 2026-07-28
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, equipo core-api, equipo tramites
**Tags**: arquitectura, backend, frontend, seguridad, tramites, identidad, feature-11004
**Supersedes**: —
**Relacionado**: ADR-0030 (persona-entidad-tenant-prevalidacion), Feature #10864
**HU origen**: Feature #11004 (CF-01, CF-07)

---

## Contexto

El Feature #11004 introduce dos cambios que sientan precedente sobre decisiones ya tomadas en el Feature
hermano #10864:

1. **CF-01 — Cierre a persona natural.** El Feature #10864 (P5, ADR-0030) decidió que la prevalidación
   standalone (`POST /biometric-validations`) admite persona **natural y jurídica** (esta última valida al
   representante legal). Producto ahora determina que la prevalidación **solo** debe admitir persona
   natural — la validación de actores jurídicos queda exclusivamente dentro del flujo de trámite
   (`IdentitySubjectResolver` sobre `ProcedureInstanceActor`, sin cambios). Es una restricción de la
   superficie pública del caso de uso, no un cambio del modelo de datos: `tramites.persons` conserva sus
   columnas `person_type`/`legal_rep_*` para los registros históricos y para no bloquear una reactivación
   futura con un ADR nuevo.

2. **CF-07 — Tracking desacoplado del trámite.** Hoy la única forma de consultar la bitácora de una
   validación de identidad es `GET /instances/{id}/biometric/{validationId}/audit`, que exige un
   `instanceId`. Las prevalidaciones standalone (`ProcedureInstanceId = null`) no tienen ese ancla, así que
   no pueden usar ese endpoint. Además, el único consumidor hoy (`IdentityAuditPanel` en `BiometricStep`)
   está cerrado a `SuperAdmin` — una decisión de UI de soporte técnico, no una política de autorización
   declarada en el backend (todos los endpoints de `BiometricaEndpoints` usan `RequireAuthorization()`
   genérico, sin rol específico).

Ambos cambios requieren una decisión explícita porque alteran un contrato ya usado en producción/QA y
puede haber prevalidaciones jurídicas ya creadas por el Feature #10864.

---

## Decisión

1. **Rechazar `personType = "juridical"` en `IniciarPrevalidacionHandler`** con error de dominio
   `prevalidacion_solo_natural` → HTTP 422, evaluado como el primer guard (antes de tocar `Person`). No se
   modifica el schema ni la entidad `Person`/`ProcedureInstanceBiometricValidation`: los campos
   `person_type`/`legal_rep_*` siguen existiendo para lectura/edición de prevalidaciones jurídicas creadas
   antes de este cambio (vía `EditarPrevalidacionHandler`/`ReenviarPrevalidacionHandler`, que no tocan la
   política de creación). `PrevalidacionForm` (FE) elimina la UI de selección de tipo de persona y el
   bloque de representante legal.

2. **Nuevo endpoint `GET /api/v1/tramites/biometric-validations/{validationId}/audit`**, tenant-scoped,
   **sin** segmento de instancia, que reutiliza `IProcedureInstanceRepository.ListIdentityAuditByValidationAsync`
   (ya usado por el endpoint existente) filtrando solo por `(tenantId, validationId)`. Autorización: la
   misma que ya aplica a todo `BiometricaEndpoints` (`RequireAuthorization()` — cualquier usuario
   autenticado del tenant con acceso al módulo Validaciones/Prevalidaciones). El componente de FE que hoy
   exige `SuperAdmin` (`IdentityAuditPanel`) se generaliza a `IdentityValidationTrackingPanel` sin ese gate.

---

## Alternativas consideradas

### Opción 1: Endpoint de auditoría por `validationId`, tenant-scoped, sin instancia (elegida)

Nuevo endpoint hermano del existente, mismo patrón de autorización que el resto de `BiometricaEndpoints`.
Reutiliza el mismo query de repositorio (`ListIdentityAuditByValidationAsync`) que ya usa el endpoint
por-instancia.

**Pros:**
- Cero cambios de schema; reutiliza 100% la tabla `identity_validation_audit` y el query existente
- Coherente con el resto de endpoints "por id de recurso" que ya expone el módulo (`/{id}/resend`, `/{id}` PATCH)
- Habilita tracking para standalone Y para trámite con una sola superficie de contrato
- Componente de FE se generaliza una sola vez (`IdentityValidationTrackingPanel`) y sirve a los 3 lugares (Validaciones, Prevalidaciones, trámite)

**Contras:**
- Dos endpoints de auditoría coexisten temporalmente (`/instances/{id}/biometric/{validationId}/audit` y el nuevo); el primero se mantiene por el flag `ReferencedFromOtherProcedure` (útil solo en contexto de instancia) y por no romper consumidores existentes
- Requiere quitar el gate SuperAdmin del componente de FE, lo que amplía quién ve la bitácora técnica (mitigado: el backend ya la sanea, sin secretos ni PII cruda)

**Esfuerzo:** S
**Riesgos:** Bajo — el query y la sanitización ya existen; solo cambia el punto de entrada y la autorización de UI.

---

### Opción 2: Generalizar el endpoint existente con `instanceId` opcional

Cambiar la ruta actual a `GET /biometric-validations/{validationId}/audit` (sin `/instances/{id}` como
prefijo) y mantener un solo endpoint, marcando `instanceId` como parámetro de query opcional solo para
compatibilidad retro de FE que aún lo use.

**Pros:**
- Un solo endpoint de auditoría en el largo plazo (sin duplicación temporal)

**Contras:**
- Cambiar la ruta de un endpoint ya versionado en `contracts/openapi/core-api.v1.yaml` y consumido por
  `BiometricStep` es un cambio breaking de contrato (mismo `operationId` no aplica, requiere migración
  coordinada de todos los consumidores)
- El flag `ReferencedFromOtherProcedure` pierde sentido semántico sin `instanceId` fijo — habría que
  redefinirlo o eliminarlo, afectando al consumidor actual (`IdentityAuditPanel`)
- Mayor superficie de cambio para un Feature que busca ser incremental (Fases ≤ 800 líneas por PR)

**Esfuerzo:** M
**Riesgos:** Medio — breaking change de contrato sin beneficio adicional sobre la Opción 1 para el alcance de CF-07.

---

### Opción 3: Sin endpoint nuevo — anclar el tracking de prevalidaciones a un `instanceId` sintético

Generar un `instanceId` "fantasma" o reutilizar `Guid.Empty` para que las prevalidaciones standalone
puedan usar el endpoint existente sin cambios de ruta.

**Pros:**
- Cero cambios de backend

**Contras:**
- Viola el invariante de dominio (`ProcedureInstanceId` es `NULL` para standalone por diseño de ADR-0030);
  introducir un id sintético es un anti-patrón que confunde auditoría/trazabilidad real
- Rompe la semántica de `GetBiometricByIdAsync`/`FindVigenteApprovedByDocumentAsync`, que ya distinguen
  `NULL` explícitamente
- Cualquier reporte o joins futuros sobre `procedure_instances` fallarían silenciosamente con un id falso

**Esfuerzo:** S
**Riesgos:** Alto — corrompe el modelo de datos ya validado en ADR-0030; rechazada.

---

## Tradeoff aceptado

Se elige la **Opción 1** porque reutiliza el query de auditoría ya probado y no toca el modelo de datos
(cumple el requisito de "sin tablas nuevas" del Feature #11004), y porque mantener temporalmente dos rutas
de auditoría (por instancia y por validación) es más barato y más seguro que forzar un contrato único que
rompería `BiometricStep` y el flag `ReferencedFromOtherProcedure`. Para CF-01, rechazar en el handler (no
solo en la UI) es la única opción compatible con la regla de decisión D1 ya cerrada por producto y con la
regla FLIT de no dejar puertas traseras server-side.

---

## Consecuencias

### Lo que se gana
- Tracking de identidad disponible para prevalidaciones standalone sin tocar el modelo de datos
- Un componente de FE (`IdentityValidationTrackingPanel`) reutilizable en Validaciones, Prevalidaciones y
  trámite, reemplazando la duplicación potencial
- Cierre de la puerta trasera de `personType=juridical` en prevalidación a nivel de contrato, no solo de UI

### Lo que se pierde
- Coexisten dos endpoints de auditoría (por instancia y por validación) durante una fase de transición;
  requiere que backend-agent documente en el changelog interno cuál usar en cada contexto nuevo
- Los registros de prevalidación jurídica creados antes de este ADR (si existen en QA/PDN) quedan
  "congelados": visibles y editables (nombre/correo del RL), pero no se podrán volver a crear

### Cambios operacionales
- No hay migración de base de datos que ejecutar
- El componente de FE que hoy es `SuperAdmin`-only pasa a ser visible para cualquier usuario del tenant
  con acceso a `/tramites` (Validaciones/Prevalidaciones/trámite) — el backend ya sanea la bitácora, sin
  secretos ni PII cruda, por lo que el cambio de visibilidad no introduce fuga de datos sensibles

---

## ADRs relacionados

- [ADR-0030] — Entidad Persona a nivel tenant para prevalidaciones de identidad (Feature #10864). Este ADR
  **no la supersede**: el modelo de datos de `tramites.persons` permanece igual; solo se restringe la
  política de creación en el caso de uso de prevalidación standalone.
- [ADR-0022] — Estados de negocio del ciclo de vida del trámite (sin impacto directo).

---

## Notas para agentes

- **Database Agent**: Sin acción — Fase 2b (schema) es **NA** para este Feature. No se crean tablas ni
  columnas; todos los campos usados en el DTO extendido (`Email`, `Attempts`, `MaxAttempts`, `ResendCount`,
  `LastAttemptAt`) ya existen en `procedure_instance_biometric_validations`.

- **Backend Agent**: El guard `prevalidacion_solo_natural` va en `IniciarPrevalidacionHandler.HandleAsync`,
  paso 1, ANTES del upsert de `Person` (para no crear/actualizar una persona con datos jurídicos que luego
  se rechazan). No tocar `EditarPrevalidacionHandler`/`ReenviarPrevalidacionHandler` (siguen operando sobre
  registros jurídicos históricos). El nuevo endpoint de auditoría por `validationId` va en
  `UseCases/ProcedureInstances/` junto a `IdentityAuditQuery.cs`, reutilizando el mismo patrón de
  `GetIdentityAuditHandler` sin el parámetro `instanceId`.

- **Frontend Agent**: Extraer `IdentityAuditPanel` de `BiometricStep.tsx` a un componente compartido
  `IdentityValidationTrackingPanel` en `components/atom/`, parametrizado solo por `validationId`. Quitar
  el gate `useIsSuperAdmin()` de ese componente (D2). Verificar que `useIsSuperAdmin`/`isSuperAdmin` no
  queden imports muertos en `BiometricStep.tsx` si no se usan en otro lugar del archivo.

- **QA Agent**: TC crítico — intentar crear prevalidación con `personType=juridical` debe devolver 422
  `prevalidacion_solo_natural` (antes retornaba 201). Regresión: editar/reenviar una prevalidación jurídica
  PRE-EXISTENTE (fixture) debe seguir funcionando sin cambios. TC de tracking: un operador NO-SuperAdmin
  del tenant puede abrir el tracking de una prevalidación standalone y de una validación de trámite.

- **Security Agent**: Confirmar que el nuevo endpoint de auditoría por `validationId` mantiene el filtro
  `tenantId` como frontera dura (mismo criterio que el endpoint por-instancia: `v.TenantId != tenantId` →
  `not_found`, nunca `403` que confirme existencia cross-tenant). Confirmar que abrir la bitácora a roles
  no-SuperAdmin no expone campos nuevos: la proyección `IdentityAuditEventDto` ya está saneada (sin
  secretos ni PII cruda) y no cambia con este ADR.

- **Infra Agent**: Sin cambios de infraestructura.

---

## Referencias externas

- Diseño técnico completo: `docs/design/FEATURE-11004-mejoras-prevalidacion-tracking-identidad.md`
- ADR-0030: `services/core-api/docs/adr/ADR-0030-persona-entidad-tenant-prevalidacion.md`
