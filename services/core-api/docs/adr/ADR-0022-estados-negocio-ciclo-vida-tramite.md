# ADR-0022 — Estados de negocio del ciclo de vida del trámite (N 03)

- **Estado**: aceptado · 2026-07-02
- **Módulo**: Trámites (runtime de instancias)
- **Requerimientos**: matriz N 03 (RF01–RF05, RNF01)

## Contexto

El repo tenía **dos** nociones de estado desacopladas:

1. `ProcedureInstanceStatus` (8 strings en inglés: `draft`, `submitted`, `in_review`,
   `completed`, `cancelled`, `pending_ot`, `approved_ot`, `rejected_ot`) — lo que realmente se
   persiste en `tramites.procedure_instances.status` y expone la API.
2. `TramiteEstadoInterno` + `TramiteStateMachine` + `TramiteStatusMapper` (14 estados,
   TRAM-12a) — una máquina de dominio **que no está cableada a ningún flujo runtime**
   (verificado: solo la referencian sus propios archivos y `StateMachineTests.cs`).

Además, la lógica de transición estaba **dispersa**: `SubmitProcedureInstanceHandler` hace
draft→submitted (escribe historial y notifica webhook OT), mientras
`OtClientProcedureRepository.TransitionAsync` hace pending_ot→approved_ot/rejected_ot
(escribe historial pero **no** notifica). No hay manejo de `DbUpdateConcurrencyException`
en ningún handler pese a que `row_version` ya es token de concurrencia (trigger
`tr_procedure_instances_row_version`).

## Decisión

**No se crea una tercera capa.** Los 6 estados de negocio de N 03 (en español) pasan a ser
**el único vocabulario**, persistido en `procedure_instances.status`:

`borrador · anulado · preparado · entregado · aprobado · rechazado`

1. **`TramiteEstado`** (`Flit.Tramites.Domain/Tramites/Estados/`) reemplaza a
   `ProcedureInstanceStatus`. Se **eliminan** `TramiteEstadoInterno` y `TramiteStatusMapper`
   (no cableados); `TramiteStateMachine` se **reescribe** sobre `TramiteEstado` con las
   transiciones RF02:
   - `borrador → anulado | preparado`
   - `preparado → entregado`
   - `entregado → aprobado | rechazado`
   - `rechazado → borrador | anulado`
   - `aprobado`, `anulado`: **finales** (RF04 — sin transiciones ni edición de datos).
2. **`ITramiteLifecycleService`** es el único camino de escritura de `status`. Valida máquina
   + gates + concurrencia, y en la **misma unidad de trabajo** registra historial
   (`ITramiteTransitionRecorder`, RF05) y encola la notificación
   (`ITramiteTransitionPublisher`, patrón outbox como `identity_validation_outbox`, RNF01).
   Los flujos existentes (`submit`, approve/reject OT) se integran a este servicio.
3. **Gate `borrador → preparado`** (RF03): identidad **aprobada y vigente** del comprador
   (reusa `BiometricRules`) **y** documentos obligatorios completos (reusa `ChecklistEngine`).
   Errores: `identidad_no_aprobada`, `documentos_incompletos` (causa exacta).
4. **Migración de datos** (validada contra el seed real
   `21-HU10240-analytics-dev-seed.sql`, que siembra `draft`, `submitted`, `in_review`,
   `approved_ot`, `rejected_ot`):

   | Antiguo | Nuevo |
   |---|---|
   | `draft` | `borrador` |
   | `submitted`, `in_review`, `pending_ot` | `entregado` |
   | `completed`, `approved_ot` | `aprobado` |
   | `cancelled` | `anulado` |
   | `rejected_ot` | `rechazado` |

   Aplica a `procedure_instances.status` y a `procedure_instance_status_history`
   (`from_status`/`to_status`). Cualquier SQL con literales de estado (función de refresh de
   analytics, seeds) se actualiza en la misma migración o una posterior.

## Semántica de los flujos existentes

- **`POST /instances/{id}/submit` (radicar)**: equivale a llegar a `entregado`. Desde
  `borrador` ejecuta **dos** transiciones encadenadas en la misma unidad de trabajo
  (`borrador→preparado` con gate RF03, luego `preparado→entregado` con los gates OT ya
  existentes: organismo habilitado, reglas OT); desde `preparado` solo la segunda. Dos filas
  de historial, dos notificaciones. El código de compatibilidad `not_draft` se reemplaza por
  los códigos del módulo.
- **`POST /instances/{id}/finalize-draft`**: NO cambia de estado (sigue sellando
  `draft_finalized_at` dentro de `borrador`); su gate no exige identidad, así que no puede
  mapear a `preparado`.
- **Approve/Reject OT** (`/admin/ot/client-procedures/{id}/approve|reject`): operan sobre
  `entregado → aprobado | rechazado` (antes pending_ot→*_ot). Deben validar con
  `TramiteStateMachine`, registrar historial con motivo y **ahora sí** publicar el cambio.
- **Edición de datos** (field-values, actores, adjuntos, comercial): permitida solo en
  `borrador` (como hoy con draft); los estados finales devuelven `estado_final`.

## Contrato de API

### `POST /api/v1/tramites/instances/{id}/transition`
Body: `{ "toStatus": "<TramiteEstado>", "reason": "<string|null>" }` — `reason` obligatorio
para `anulado` y `rechazado` (`motivo_requerido`).
- 200 → `ProcedureInstanceSummary` (mismo shape del submit).
- 404 `not_found` · **422** `transicion_no_permitida`, `estado_final`, `estado_desconocido`,
  `identidad_no_aprobada`, `documentos_incompletos`, `motivo_requerido` · **409**
  `conflicto_concurrencia`.
- Formato de error: `ProblemDetails` con `title` = **código** y `detail` = mensaje en español
  (el frontend enruta por `title`).

### `GET /api/v1/tramites/instances/{id}/status-history?page=&pageSize=`
- 200 → `{ items: [{ id, fromStatus, toStatus, changedAt, changedByUserId, changedByName, reason }], total, page, pageSize }`
  ordenado por `changedAt` **desc**.

### Webhook OT (`vehicle_state_changed`)
El payload conserva su shape (`from_status`/`to_status`) pero con el **nuevo vocabulario**; la
entrega pasa de inline best-effort a **outbox** (tabla nueva en schema `tramites`, procesador
en background con el patrón de `IdentityValidationOutboxProcessor`).

### Labels frontend (únicos 6 estados visibles)
`borrador→Borrador · anulado→Anulado · preparado→Preparado · entregado→Entregado ·
aprobado→Aprobado · rechazado→Rechazado`

## Breaking change (documentado)

Las respuestas de API que hoy exponen `draft/submitted/...` pasan a exponer el vocabulario en
español. El frontend se actualiza en la misma entrega (tipo `InstanceStatus`, chips de
`TramitesTable`, wizard). No hay consumidores externos del API runtime salvo los webhooks OT,
cuyo cambio de vocabulario queda documentado aquí y en `contracts/`.

## Consecuencias

- (+) Una sola fuente de verdad de estados; transiciones auditables (historial con usuario,
  fecha y motivo) y notificadas de forma consistente (outbox).
- (+) Concurrencia: `row_version` + `DbUpdateConcurrencyException` → 409 sin efectos
  parciales (antes no se manejaba).
- (−) Migración de datos irreversible sin tabla de respaldo (el `Down` restaura el mapeo
  inverso con pérdida de granularidad: `entregado` no distingue `submitted`/`in_review`/
  `pending_ot`). Aceptado: esos matices no eran estados de negocio.
- (−) `StateMachineTests` de la máquina de 14 estados se reescriben (la máquina no estaba en
  uso; el workflow STT y el formato de radicado que viven en ese archivo se conservan).
