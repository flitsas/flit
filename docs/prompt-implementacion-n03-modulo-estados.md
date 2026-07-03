# PROMPT — Implementación N 03. MÓDULO ESTADOS (Trámites / Ciclo de vida)

> Copia todo lo que sigue como prompt para Claude Code (u otro agente), ejecutándolo desde la raíz del repo `flit`.

---

Implementa el **N 03. MÓDULO ESTADOS** del ciclo de vida de trámites en FLIT 2.0. El trabajo se divide en **3 historias que deben desarrollarse EN PARALELO** usando subagentes (Task tool) con contratos de interfaz acordados de antemano para evitar conflictos, seguidas de una fase de integración secuencial.

## Contexto del sistema (leer antes de codificar)

- Lee `docs/contexto-funcional-flit.md` completo (arquitectura, convenciones, flujo).
- Backend: `services/core-api/src` — monolito modular .NET 10, **Minimal API + handlers POCO (CQRS manual, sin MediatR)**. Endpoints se mapean en `Flit.Api/Program.cs`. Migraciones EF en `Flit.Infrastructure/Migrations` (una por HU, DDL con snake_case, PK uuid, `row_version`, soft delete).
- Estado actual relevante:
  - `Flit.Tramites.Domain/Enums/ProcedureInstanceStatus.cs` — estados de persistencia actuales (`draft`, `submitted`, `in_review`, `completed`, `cancelled`, `pending_ot`, `approved_ot`, `rejected_ot`).
  - `Flit.Tramites.Domain/Tramites/Services/TramiteStateMachine.cs` — máquina interna existente de 14 estados (`TramiteEstadoInterno`) con transiciones distintas a las nuevas.
  - `Flit.Tramites.Domain/Tramites/Services/TramiteStatusMapper.cs` — mapea estado interno → persistencia.
  - `Flit.Tramites.Domain/Entities/ProcedureInstanceStatusHistory.cs` + su Configuration — historial de transiciones ya existente (`from_status`, `to_status`, `changed_at`).
  - `Flit.Tramites.Domain/Integration/IProcedureStateChangeNotifier.cs` + `Flit.Infrastructure/OtWebhooks/OtWebhookProcedureStateChangeNotifier.cs` — notificación de cambios de estado a webhooks OT.
  - `Flit.Tramites.Application/UseCases/ProcedureInstances/SubmitProcedureInstanceCommand.cs` — gate actual de radicación (documentos obligatorios vía `ChecklistEngine` + biométrica aprobada).
- Frontend: `frontend/` Next.js 16; wizard server-driven en `components/operacion/TramiteWizard.tsx` (`GET /instances/{id}/wizard` devuelve `steps`, `canSubmit`, `blockers`; el front nunca recalcula gates).

## Requerimientos (matriz N 03, `docs/matriz-requerimientos-funcionalidades-faltantes.xlsx`)

- **RF01** — Estados de negocio: `Borrador`, `Anulado`, `Preparado`, `Entregado`, `Aprobado`, `Rechazado`. **Reemplazan** el modelo draft/submitted como estados visibles del trámite.
- **RF02** — Transiciones permitidas (todo lo demás → 422 con código de error):
  - `Borrador → Anulado | Preparado`
  - `Preparado → Entregado`
  - `Entregado → Aprobado | Rechazado`
  - `Rechazado → Borrador | Anulado`
- **RF03** — Gate `Borrador → Preparado`: validación de identidad **aprobada** Y **todos** los documentos obligatorios cargados (reusar `ChecklistEngine` y las validaciones biométricas existentes). El error debe informar la causa exacta.
- **RF04** — `Aprobado` y `Anulado` son **finales**: sin transiciones posteriores ni edición de datos.
- **RF05** — Cada transición se registra en `procedure_instance_status_history` con usuario, fecha, estado origen/destino y **motivo** (agregar campo `reason` si no existe).
- **RNF01** — Transiciones atómicas y seguras ante concurrencia (`row_version` / optimistic locking) y publicación del cambio vía `IProcedureStateChangeNotifier` (webhooks OT).

## Decisión de diseño obligatoria (resolver ANTES de paralelizar)

El repo ya tiene DOS nociones de estado (persistencia de 8 valores + máquina interna de 14). **No crees una tercera capa suelta.** Propuesta a seguir salvo que encuentres una razón fuerte en el código (documenta la decisión como ADR en `services/core-api/docs/adr/`):

1. Introducir enum/const class `TramiteEstado` (los 6 nuevos estados de negocio, en español, persistidos en `procedure_instances.status`) con migración de datos: `draft→borrador`, `submitted→entregado`, `cancelled→anulado`, `approved_ot→aprobado`, `rejected_ot→rechazado`, `in_review/pending_ot→entregado`, `completed→aprobado` (valida contra datos reales del seed antes de fijar el mapeo).
2. Reescribir `TramiteStateMachine` con las transiciones de RF02 y marcar `Aprobado`/`Anulado` como terminales; actualizar `TramiteStatusMapper` o eliminarlo si queda obsoleto.
3. Un único servicio de dominio `TramiteLifecycleService` (o handler `ChangeInstanceStatusCommand`) por el que pasan TODAS las transiciones: valida máquina + gates + concurrencia, escribe historial y dispara notifier en la misma unidad de trabajo (outbox o transacción).

## Plan de ejecución — 3 historias EN PARALELO

Define primero los contratos compartidos (nombres de enum, firma de `TramiteLifecycleService`, DTOs, códigos de error `transicion_no_permitida`, `estado_final`, `identidad_no_aprobada`, `documentos_incompletos`) en un archivo de contratos de dominio, y luego lanza 3 subagentes en paralelo, cada uno con su alcance cerrado:

### HU-1 · MÁQUINA DE ESTADOS (RF01–RF04)
- Enum `TramiteEstado`, nueva `TramiteStateMachine`, `TramiteLifecycleService` con gates (identidad + checklist), bloqueo de edición en estados finales.
- Endpoint `POST /api/v1/tramites/instances/{id}/transition` (body: `toStatus`, `reason`) + integración con los flujos existentes (`finalize-draft`, `submit`, approve/reject de OT) para que usen el servicio único.
- Migración EF de datos de estados + actualización del wizard backend (`GET /wizard`: `canSubmit`/`blockers` reflejan el gate de RF03).
- Tests unitarios exhaustivos de la máquina (todas las transiciones válidas e inválidas, estados finales).

### HU-2 · HISTORIAL (RF05)
- Extender `ProcedureInstanceStatusHistory` con `reason` y `changed_by_user_id` si falta (migración EF propia).
- Escritura del historial SOLO desde `TramiteLifecycleService` (interfaz acordada en contratos).
- Endpoint `GET /api/v1/tramites/instances/{id}/status-history` (paginado, ordenado desc) + evento en `procedure_instance_events`.
- Frontend: timeline de estados en el detalle del trámite (`/tramites/[instanceId]`).
- Tests: cada transición genera exactamente una fila con from/to/usuario/motivo.

### HU-3 · NOTIFICACIONES + CONCURRENCIA (RNF01)
- Concurrencia: transición con `row_version`; conflicto → 409 sin efectos parciales. Test de carrera (dos transiciones simultáneas).
- Publicar cada cambio de estado vía `IProcedureStateChangeNotifier`/`OtWebhookProcedureStateChangeNotifier` con el nuevo vocabulario de estados; garantizar entrega consistente (misma transacción u outbox, siguiendo el patrón de `identity_validation_outbox`).
- Actualizar payload/documentación del webhook OT y el contrato AsyncAPI/OpenAPI en `contracts/` si aplica.
- Tests de integración: transición → notificación disparada exactamente una vez.

### Fase de integración (secuencial, tras las 3 HU)
1. Merge de las 3 ramas/worktrees; resolver el wiring en `Program.cs` y DI.
2. Frontend: badges/etiquetas de estado en listado `/tramites` y wizard con los 6 estados en español; acciones de transición visibles solo cuando la máquina las permite (el backend manda).
3. `dotnet build` + toda la suite de tests backend y frontend (`vitest`) en verde.
4. Verificación end-to-end contra los criterios de aceptación de la matriz N 03 (crear trámite → intentar Preparado sin identidad (falla con causa) → completar gates → Preparado → Entregado → Rechazado → Borrador → … → Aprobado y verificar que queda inmutable, con historial completo y webhook notificado).

## Reglas
- Sigue las convenciones del repo (snake_case BD, migración por HU, Minimal API, handlers POCO, sin MediatR, español en mensajes de negocio).
- No rompas endpoints públicos existentes; si un estado antiguo aparece en respuestas de API, mantén compatibilidad o documenta el breaking change.
- Cero errores de compilación y cero tests rotos al finalizar. Documenta la decisión de estados en un ADR nuevo.
