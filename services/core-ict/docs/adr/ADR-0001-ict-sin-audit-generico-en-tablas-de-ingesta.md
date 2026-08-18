# ADR-0001 (core-ict) — Quitar la auditoría genérica por fila en las tablas de ingesta de ICT

- **Estado**: Propuesto · 2026-08-13
- **Módulo**: ICT (core-ict) — auditoría de `ict.external_integration_master` / `ict.external_integration_actors`
- **Requerimientos**: throughput del registro por lote (`POST /api/v1/external-transaction/register`); RNF de auditoría (se preserva por otra vía)
- **Decide**: Líder Técnico / Seguridad

## Contexto

El registro de ICT tocó techo en **~25 req/s (~500 filas/s)** en DEV bajo carga (test de 745.960 filas). El host NO estaba ahogado (8 vCPU, load ~0.5). El diagnóstico (EXPLAIN ANALYZE sobre `INSERT` de 20.000 filas en `ict.external_integration_master`, con `ROLLBACK`) aisló la causa:

| Componente | Tiempo (20k INSERT) | % |
|---|---|---|
| Insert base (tabla + 6 índices) | ~905 ms | 35% |
| FK `fk_eim_tenant` | ~141 ms | 5% |
| **Trigger de auditoría `tr_eim_audit`** | **1557 ms** | **~60%** |
| Total | ~2607 ms | 100% |

El trigger `tr_eim_audit` (y su gemelo `tr_eia_audit` en `actors`) ejecuta `public.trg_audit_log()`, que hace `to_jsonb(NEW)` de las **~48 columnas** de cada fila y un `INSERT` en `audit.audit_logs`. Sobre las tablas de ingesta esto **duplica** la escritura de cada registro y, con `synchronous_commit=on` + `max_connections=100` compartidas en el VPS, es el factor dominante del techo. En DEV `audit.audit_logs` llegó a **14 GB / 6,6M filas** (del propio test).

**Hay dos capas de auditoría en FLIT y esta es la genérica, no la de cumplimiento:**

1. **`audit.audit_logs`** (esta) — genérica a nivel de fila, por trigger (`schema/table/record_id/action/old_data/new_data/changed_at`). Forense/debug. En ICT **ni siquiera llena `changed_by`** (no hay actor).
2. **`admin.tenant_config_audit_logs`** — auditoría de **cumplimiento (RNF01)** a nivel de aplicación (usuario, fecha, **IP, operación, resultado**), gobernada por ADR-0024. **No depende de estos triggers.**

Además, `master`/`actors` son **staging de máquina** (el gestor ingresa lotes vía API; el pipeline los muta decenas de veces), no ediciones humanas puntuales, e ICT **ya tiene trazabilidad propia**: `ict.pretramite_events` (timeline sanitizado del ciclo de vida) + `ict.external_integration_process_status` (historial de estados). Y el propio `19-ICT-audit-triggers.sql` **ya excluye** `integration_clients` (volcaría `password_hash`) y los catálogos estáticos — existe el precedente de "excluir por costo/volumen".

## Decisión

**Quitar los triggers de auditoría genérica `tr_eim_audit` y `tr_eia_audit`** de `ict.external_integration_master` y `ict.external_integration_actors` (dejar solo el `DROP TRIGGER IF EXISTS`, sin `CREATE`, en el DDL embebido idempotente — auto-sana los ambientes que ya lo tienen). **Se conservan:**

- `tr_eim_row_version` / `tr_eia_row_version` (concurrencia optimista, costo despreciable).
- `tr_eita_audit` (attachments) y `tr_ptm_audit` (catálogo mapping): bajo volumen, fuera del hot path.
- La auditoría de cumplimiento RNF01 (`admin.tenant_config_audit_logs`) y la trazabilidad ICT (`pretramite_events`, `process_status`), intactas.

**Efecto esperado:** ~2.5× en el registro (~25 → ~55-60 req/s), antes de otras palancas (`synchronous_commit` local, `max_connections`/PgBouncer).

## Alternativas consideradas

### Opción 1 — Excluir `master`+`actors` del audit genérico (RECOMENDADA / esta decisión)
- (+) Elimina el ~60% del costo del registro → **2.5× garantizado** (medido). Cambio mínimo (DDL), idempotente, reversible.
- (+) No toca RNF01; trazabilidad ICT preservada; consistente con la exclusión ya existente de `integration_clients`.
- (−) Se pierde el snapshot genérico fila-a-fila (antes/después) en esas 2 tablas. Mitigado por `pretramite_events` + `process_status`.
- Esfuerzo: **bajo**. Riesgo: **bajo** (postura de auditoría — requiere visto bueno).

### Opción 2 — Auditar solo INSERT/DELETE (quitar el audit de UPDATE)
- (+) Corta los UPDATE (los más frecuentes y pesados, del pipeline).
- (−) **No resuelve el techo del registro**: el `INSERT` sigue auditando (sigue el `to_jsonb` síncrono). Solo alivia el pipeline async.
- Esfuerzo: medio. Riesgo: medio.

### Opción 3 — Auditoría fuera del hot path (CDC / logical decoding)
- (+) Conserva el rastro completo **y** llega a ~2.5× en el registro.
- (−) Capturar `old/new` en caliente es intrínsecamente síncrono salvo leyendo el WAL (Debezium/logical decoding): **infra pesada**, desproporcionada para datos de staging. Variantes "trigger → buffer async" NO llegan a 2.5× (siguen serializando en caliente).
- Esfuerzo: **alto**. Riesgo: medio.

## Consecuencias por agente

- **Backend/DB:** editar `services/core-ict/.../Sql/Ddl/19-ICT-audit-triggers.sql` (DROP sin CREATE en las 2 tablas). Al desplegar, el bootstrapper elimina los triggers en **todos** los ambientes (DEV/QA/PDN) por ser DDL embebido idempotente. Sin migración EF (tablas `ExcludeFromMigrations`). Validar ejecutando el SQL contra Postgres (build verde no prueba DDL).
- **Seguridad:** se retira el snapshot forense genérico en 2 tablas de ingesta. Confirmar que `pretramite_events` + `process_status` cubren la necesidad de trazabilidad de ICT y que RNF01 (`admin.tenant_config_audit_logs`) no se ve afectado. **Este ADR requiere su aceptación.**
- **Infra/Ops:** independiente, hacer `teardown` de los ~14 GB de `audit.audit_logs` del test en `flitdev` (`DELETE ... WHERE schema_name='ict'`, DEV-only) para recuperar espacio. Es higiene, no el fix.
- **QA:** registrar un pre-trámite y verificar que sigue apareciendo su ciclo en `pretramite_events`/estado, y que ya no se generan filas en `audit.audit_logs` para `master`/`actors`.

## Estado y aceptación

Queda en **Propuesto**. Pasa a **Aceptado** solo por PR de aceptación del Líder Técnico/Seguridad (regla FLIT 15). El cambio de DDL se prepara en la rama `fix/ict-audit-ingesta-throughput` **sin push** (lo orquesta Cursor).
