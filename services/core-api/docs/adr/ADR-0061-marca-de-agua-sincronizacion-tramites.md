# ADR-0061: Marca de agua de sincronización de trámites por secuencia global y triggers

**Fecha**: 2026-09-29
**Status**: Propuesto
**Deciders**: David Alejandro Chica Hernandez (PO), Claude Code (agente de implementación). Aceptación: Líder Técnico.
**Tags**: arquitectura, backend, modelo-de-datos, modulo-tramites, integraciones
**Épica / Feature**: #12737 / #13062 (HU #13073, #13074, #13075) · **Contrato**: `docs/integraciones/external-api-tramites-sync.md` (v3.1)

## Contexto

Flito (Épica #12736) lee de FLIT 2 los trámites de todas las compañías por consulta incremental:
cada 5 minutos y a demanda pide «lo que cambió desde mi última lectura» con un cursor. Para eso cada
trámite necesita una marca que:

1. cambie ante **cualquier** modificación del trámite **o de sus datos relacionados** (actores,
   campos del vehículo, historial de estados, adjuntos, datos comerciales);
2. sea **globalmente ordenable** entre compañías, para recorrer el feed con un cursor keyset;
3. no dependa de que cada caso de uso se acuerde de moverla: hoy escriben trámites 113 casos de uso,
   el gRPC de ICT, los webhooks de Kyverum, el portal público, procesos batch y el migrador V1;
4. no rompa la concurrencia optimista de EF, que usa `row_version` como token en
   `procedure_instances`.

`updated_at` no sirve: no se mueve cuando cambia una tabla hija, no es único y dos transacciones
pueden compartir valor.

## Decisión

1. **Versión global por secuencia.** `procedure_instances.sync_version` (bigint) sale de
   `tramites.procedure_sync_seq`, junto con `sync_changed_at`. Las asigna un trigger
   `BEFORE INSERT OR UPDATE` que sobrescribe cualquier valor enviado: la aplicación no puede fijarlas
   (DDL 122).
2. **Propagación desde las tablas hijas por sentencia.** Triggers `AFTER … FOR EACH STATEMENT` con
   transition tables en las cinco tablas hijas hacen un único `UPDATE` del trámite por sentencia
   (DDL 123).
3. **Un sello por transacción.** `sync_xact xid8` guarda la transacción que selló; si la misma
   transacción vuelve a tocar el trámite, se conserva la versión. Un guardado de EF con el trámite y
   varias filas hijas, con o sin los triggers de denormalización del DDL 47, produce un incremento.
4. **Los cambios solo de sincronización no suben `row_version` ni se auditan.** Los triggers
   `tr_procedure_instances_row_version` y `tr_procedure_instances_audit_update` llevan
   `WHEN (NOT tramites.fn_procedure_instance_solo_sync(OLD, NEW))`. Cualquier otro UPDATE se comporta
   como antes. La auditoría del trámite se parte en dos triggers porque un `WHEN` con `OLD` y `NEW`
   no puede ir en un trigger que también dispara en INSERT o DELETE.
5. **Asignación inicial del histórico** en orden de creación, con los triggers de usuario
   desactivados (sin auditoría ni `row_version`) y reservando un bloque de la secuencia (DDL 123).
6. **Índices de la lectura entre compañías sin `tenant_id` al principio** (DDL 124):
   `uq_procedure_instances_sync_version (sync_version)` y tres parciales que empiezan por
   `procedure_instance_id` (alcance «radicado», fecha de aprobación, factura). Es una **excepción
   explícita al criterio A11** del checklist de esquema: la lectura del feed no filtra por compañía;
   un índice con `tenant_id` delante no le sirve. La lectura entre compañías se acota por el ámbito
   exclusivo del servicio externo (HU #13076), no por el índice.

## Alternativas consideradas

### A. `updated_at` mantenido por trigger (descartada)
Más simple, pero no es único ni monótono entre transacciones concurrentes, y ordenar por fecha
obliga a un cursor compuesto `(updated_at, id)` con los mismos problemas de orden de commit. No
resuelve nada que la secuencia no resuelva mejor.

### B. Outbox de cambios escrita por cada caso de uso o por EF (descartada)
Cada ruta de escritura tendría que acordarse de escribirla (o un interceptor de EF, que no ve el
gRPC de ICT, el SQL crudo del migrador ni los batch). Una ruta olvidada pierde cambios en silencio.
Se exploró como canal de aviso en el contrato v2 y se descartó con él (v3 es solo consulta).

### C. Captura de cambios por replicación lógica / CDC (descartada)
Robusta y sin tocar el esquema, pero exige operar un slot de replicación y un consumidor con estado
en un servidor compartido por los tres ambientes. Es infraestructura nueva para un único consumidor
con latencia objetivo de minutos.

## Tradeoff aceptado

- **Orden de commit ≠ orden de secuencia.** Una transacción larga puede confirmar después de otra
  con una versión mayor. La lectura aplica una ventana de estabilidad de 5 s; el riesgo residual de
  una transacción más larga se resuelve en la HU #13076 (ventana por snapshot).
- **Un UPDATE extra del trámite** por sentencia que toca una tabla hija. Es por PK, no sube
  `row_version`, no audita, y se omite si la transacción ya selló el trámite.
- **Los textos de catálogo** (nombre del organismo, de la compañía, del tipo de trámite) no mueven la
  versión: si cambian, el ítem no se reentrega hasta que el trámite cambie. Acordado con Flito.

## Consecuencias

- `procedure_instances` gana tres columnas (`sync_version`, `sync_changed_at`, `sync_xact`) que EF
  **no mapea**: nadie en la aplicación las escribe.
- Los triggers de `row_version` y auditoría del trámite cambian de forma (condición `WHEN` y
  auditoría en dos triggers). Cualquier migración futura que los recree debe conservar el `WHEN`.
- Sigue vigente el comportamiento previo de los triggers de denormalización (DDL 47): suben
  `row_version` y el código recarga el trámite cuando lo necesita (`ConsolidadoVigenciaTracker`,
  `OtClientProcedureRepository.AssignPlateAsync`). Esta decisión no lo empeora ni lo corrige.
- Revertir en PDN tras la asignación inicial es posible (los `Down` existen) pero no recomendable:
  rehacer la asignación vuelve a costar la ventana.

## ADRs relacionados

- ADR-0021 (lecturas por SQL crudo parametrizado): la lectura del feed seguirá ese patrón.
- ADR-0022 / ADR-0059 (vocabulario de estados): el alcance «radicado» se evalúa sobre el historial
  (`preasignacion`, `entregado`).

## Notas para agentes

- No mapear `sync_*` en EF ni escribirlas desde la aplicación.
- Si se agrega una tabla hija nueva cuyos cambios deban llegar a Flito, añadirla a la lista del DDL
  123 (los tres triggers `tr_<tabla>_sync_touch_ins|upd|del`).
- Los trámites migrados desde FLIT 1 (`is_migrated`) llevan versión pero el feed los excluye
  (contrato v3.1).
