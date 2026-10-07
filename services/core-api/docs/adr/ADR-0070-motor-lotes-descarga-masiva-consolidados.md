# ADR-0070: Motor de lotes de descarga masiva de consolidados — tablas propias, reclamo por ítem y partes cifradas con borrado criptográfico

**Fecha**: 2026-10-06
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), PO de la épica #13216, architecture-agent (propuesta)
**Tags**: arquitectura, backend, frontend, seguridad, habeas-data, tramites, consolidado, background-jobs
**Épica / Feature**: #13216 / #13306 (FA1, motor + Gestor). Lo reutilizan #13307 (FA2, Super Admin + cancelación) y #13308 (FB, bandeja OT).
**Diseño completo**: `.claude/state/epica-13216/05-diseno-13306.md` (se publica en la Wiki con el Feature).
**Revisión**: v2 2026-10-07 · v3 2026-10-07 (adenda de schema, ver «Adenda v3») · v4 2026-10-07 (#13307 Super Admin y cancelación, ver «Adenda v4») · v5 2026-10-07 (#13308 bandeja del OT, ver «Adenda v5») · v6 2026-10-07 (subclave por parte, ver «Adenda v6») — sigue en Propuesto.

> **Nota v2 (2026-10-07).** Una regla del usuario cambia cómo se entrega cada ítem: **el lote nunca regenera**. Si el trámite ya tiene consolidado del tipo pedido (`consolidado` o `consolidado_maestro`), se descarga ese tal cual, en cualquier estado y aunque esté desactualizado; el maestro radicado ante Quipux se entrega como hasta ahora. Solo se genera, por el generador oficial, el consolidado de los trámites que no tienen ninguno, incluidos los aprobados o rechazados. Consecuencias en este ADR:
> - el lote deja de usar `EntregarConsolidadoHandler` (reconstruye lo no vigente y en final no genera) y usa un entregador propio «existente o primera generación», apoyado en una guarda aditiva `soloSiNoExiste` (default `false`) en `GenerarConsolidadoHandler` y `GenerarConsolidadoMaestroHandler`;
> - la generación en estado final se hace por el handler, como ya hacen la aprobación del OT y otros flujos de sistema; el guard del gestor (`GeneracionDocumentalGestorGuard`, HU #11051) sigue intacto en el endpoint individual. Excepciones: migrado V1 en final y final sin FUR se omiten;
> - el acotamiento de la cascada al maestro (H1) y el backpressure sobre la cola de #12760 se eliminan: la cascada solo puede darse al generar el primer consolidado de un trámite no final con FUR desactualizado, y la absorbe la cola acotada existente;
> - la idempotencia de reinicio se apoya en la regla misma (el reintento toma el adjunto que dejó la ejecución anterior), no en la bandera de vigencia.
> El resto de la decisión (tablas propias, reclamo por ítem, partes cifradas, borrado criptográfico, auditoría atómica) no cambia.

## Contexto

El Gestor/Radicador necesita descargar en ZIP el consolidado de muchos trámites desde `/tramites`. Las restricciones vienen del código y de ADRs vigentes:

1. No hay Hangfire, Quartz ni colas externas (ADR-0024-telemetria-uso-y-alertas, Aceptado). El patrón es `BackgroundService` con `FOR UPDATE SKIP LOCKED` y configuración en BD (ADR-0059-estandar-procesos-automaticos-periodicos, Propuesto).
2. El lote de #12211 (`admin.standalone_document_batches`) reclama el lote **completo** con un `ClaimTimeout` fijo de 15 min, sin heartbeat (`StandaloneDocumentBatchProcessor.cs:34`). Un lote de horas se re-reclamaría en paralelo, así que copiarlo está vetado. Además es específico de XLSX: plantilla, archivo fuente, tope de 100 filas (CHECK) y schema `admin`.
3. La RLS es decorativa. El worker corre sin JWT, así que el aislamiento por compañía lo tiene que poner la aplicación.
4. `FileManagerAttachmentStorage.Delete` es un **no-op**: el file-manager no expone borrado y el objeto queda hasta su ciclo de vida de 30 días. Además `SaveAsync` carga en memoria el archivo completo. Con eso, «purgar a las 24 h» no se puede cumplir borrando el objeto.
5. La generación oficial vive en `GenerarConsolidadoHandler` y `GenerarConsolidadoMaestroHandler`. La entrega (`EntregarConsolidadoHandler`, HU #12785) reconstruye cuando la vigencia está abajo y en estado final no genera nunca. **v2:** por regla del usuario (2026-10-07) el lote no puede reconstruir ni dejar sin generar un aprobado que no tenga consolidado, así que no usa la entrega; usa los generadores solo para los trámites sin consolidado.
6. Hay una sola instancia de core-api por ambiente, pero el diseño tiene que seguir siendo correcto si se escala.
7. (v2) `GeneracionDocumentalGestorGuard` (HU #11051) bloquea aprobado, anulado y revocado (rechazado no es final) y vive a propósito en el endpoint del gestor, no en el handler, porque los flujos de sistema generan después del estado final. No hay trigger de inmutabilidad por estado sobre la instancia ni sus adjuntos.

## Decisión

Construir un **motor de lotes propio en el schema `tramites`** con estas piezas:
- un lote, sus ítems y sus partes, cada uno en su tabla;
- **reclamo por ítem** con `FOR UPDATE SKIP LOCKED`, lease por ítem mayor que el timeout de ejecución y reparto round-robin entre lotes;
- (v2) entrega **«existente o primera generación»**: si el trámite tiene consolidado del tipo, se toma tal cual (sin mirar estado ni vigencia; maestro radicado primero); si no, se genera por el generador oficial con `force=false`, `userId=null` y `soloSiNoExiste=true`. Nunca se regenera un consolidado existente;
- partes ZIP **cifradas por la aplicación** con una clave por lote (envelope sobre ASP.NET Data Protection). La purga destruye la clave (borrado criptográfico);
- auditoría append-only escrita **en la misma transacción** que crea el lote.

El lote de #12211 no se toca.

## Alternativas consideradas

### Opción 1: Generalizar `StandaloneDocumentBatch` (#12211)

**Pros:** - Hay un solo concepto de «lote» en el repo. - Reutiliza endpoints de polling y el worker ya desplegado en PDN. - Menos tablas.
**Cons:** - El modelo es de XLSX: plantilla, archivo fuente, tope de 100 en CHECK, filas con datos de entrada. Generalizarlo obliga a volver nullables o polimórficas casi todas sus columnas. - Mezcla bounded contexts (`admin` frente a `tramites`). - Exige rehacer su reclamo de lote completo, lo que trae regresión sobre una funcionalidad en PDN y divergencia en las ramas `promote/pdn` (DEP-08). - Su ZIP no se persiste, y aquí persistirlo es obligatorio (H3a).
**Esfuerzo:** L
**Riesgos:** Regresión de #12211 en PDN. Una migración que reescribe una tabla viva.

### Opción 2: Motor propio en `tramites` con reclamo por ítem y partes cifradas (elegida)

**Pros:** - Modelo hecho para el caso: ítem con estado, intentos, lease y snapshot del adjunto; parte con estado propio. - El reclamo por ítem permite reanudar sin doble generación (CF-20) y da equidad entre lotes (CF-21). - La idempotencia la pone el dominio (v2): reprocesar un ítem encuentra el adjunto y lo toma; como máximo una generación por trámite y ninguna en los que ya tenían. - Las estrategias de selección y de entrega, cada una con su clave, dejan a FA2 y a FB conectarse sin rediseñar. - El borrado criptográfico cumple la purga de 24 h aunque el storage no borre.
**Cons:** - Tres tablas operativas, una de auditoría y una de parámetros nuevas. - Código criptográfico nuevo (AES-GCM por bloques sobre la BCL), que pide revisión de seguridad. - Mientras el file-manager no borre, el texto cifrado queda huérfano hasta 30 días.
**Esfuerzo:** L (≈ 30–34 SP de FA1, dentro de lo estimado)
**Riesgos:** Un error en el framing del cifrado deja partes ilegibles; se mitiga con tests de ida y vuelta y vectores fijos. Las copias de seguridad de la BD retienen la clave envuelta durante su ventana de retención.

### Opción 3: Lote completo con lease y heartbeat, ZIP único rearmado al descargar

**Pros:** - Menos estado: no hay tabla de partes ni de ítems persistidos con snapshot. - Su patrón es parecido al de #12211, así que se aprende rápido.
**Cons:** - El heartbeat de lote no da equidad, porque un lote de 5.000 ocupa un slot horas (choca con CF-21). - Rearmar al descargar genera o lee PDF durante el stream y choca con los timeouts de nginx/Next. Tampoco reproduce el contenido (vetado: R3, V4). - Si el heartbeat falla, el lote entero vuelve a empezar.
**Esfuerzo:** M
**Riesgos:** Incumple CF-11, CF-12 y CF-21 tal como los aprobó el PO.

## Tradeoff aceptado

Se aceptan más tablas y un componente criptográfico propio a cambio de tres garantías que las otras opciones no dan juntas:

1. **Ninguna regeneración y como máximo una generación por trámite aunque el proceso caiga a mitad.** Cada ítem tiene su lease y el timeout de ejecución es menor que ese lease. Además (v2), el reintento toma el adjunto que haya, y la guarda `soloSiNoExiste` está en el mismo read que decide generar.
2. **Equidad real entre lotes.** El reclamo es round-robin por `last_claimed_at`.
3. **Purga verificable a las 24 h** sin depender de que el file-manager implemente el borrado. Se destruye la clave del lote y el objeto huérfano deja de poder leerse.

No se generaliza #12211: el ahorro en tablas no compensa el riesgo sobre PDN ni la mezcla de contextos.

## Consecuencias

### Lo que se gana
- Un motor que reutilizan FA2 (origen `superadmin`, cancelación) y FB (origen `ot_bandeja`, tipo `consolidado_maestro`) añadiendo una estrategia, sin tocar las tablas.
- Reanudación tras reinicio y equidad entre lotes, verificables con tests.
- Auditoría Ley 1581 atómica con la creación: si no hay auditoría, no hay lote.
- (v2) El lote no reescribe la documentación de nadie: lo que ya existe se entrega tal cual, incluida la decisión final de aprobados y rechazados. La cascada al otro consolidado queda reducida a un residuo raro (primer consolidado de un trámite no final con FUR desactualizado) que absorbe sin cambios la cola de #12760. No hay backpressure ni cambios en #12760.

### Lo que se pierde

- (v2) El ZIP puede contener PDF que no reflejan cambios posteriores del expediente. Es decisión de producto y se declara en la confirmación.
- (v2) Se añade un parámetro opcional `soloSiNoExiste` a dos generadores compartidos (en PDN). Con el default no cambia nada; lo cubren tests y un test de arquitectura.
- (v2) El lote genera el primer consolidado de trámites en estado final fuera del guard del gestor. Queda encerrado en el entregador con tres invariantes con test (solo si no existe; nunca `force`; en final nunca con FUR faltante). Migrado V1 en final se omite.
- Dos patrones de lote conviven: #12211 (lote completo) y este (por ítem). #12211 queda como deuda declarada: su lote de 100 filas cabe en 15 min con una instancia.
- La descarga de una parte pasa por core-api (descifrado en streaming), no por URL prefirmada. Eso cuesta ancho de banda del API.

### Cambios operacionales
- Nuevo hosted service `ConsolidadoLoteProcessor` con tres carriles: ítems (`item_slots`, 2 por defecto), empaquetado (1 slot) y purga (cada 10 min).
- Los parámetros N, M, `item_slots`, la retención y los timeouts viven en `tramites.consolidado_export_settings` (fila única global). Hoy no tienen UI (R7). (v2: sin umbral de backpressure.)
- nginx: la ruta de descarga de partes necesita `proxy_buffering off` (o `proxy_max_temp_file_size 0`) para no volcar 250 MB a disco temporal.
- Se requiere un directorio temporal con espacio ≥ 2 × M para armar una parte.

## ADRs relacionados

- **ADR-0024-telemetria-uso-y-alertas** (Aceptado): sin Hangfire/Quartz; se respeta.
- **ADR-0059-estandar-procesos-automaticos-periodicos** (Propuesto): BackgroundService + BD + SKIP LOCKED; se sigue.
- **ADR-0032-regeneracion-consolidado-tras-rechazo** (Aceptado): el lote no lo contradice. Esa regeneración sigue ocurriendo por sus vías (pantalla, cola de #12760); el lote solo se abstiene de dispararla y genera por el flujo oficial cuando no hay consolidado.
- **ADR-0029-preview-presigned-get-inline** (Propuesto): **no** se usa para las partes. El objeto está cifrado por la aplicación, y una URL de 10 min no se puede revocar ni auditar por descarga.
- **ADR-0057-almacenamiento-de-archivos-por-ambiente-y-contabo** (Aceptado, `docs/decisions/`): el file-manager es el storage; su `Delete` no-op motiva el borrado criptográfico.
- **ADR-0023-catalogo-global-roles** (Aceptado): el permiso nuevo `consolidado-masivo.download` vive en ese catálogo.
- **ADR-0069-lectura-entre-companias-acotada-y-auditada** (Propuesto): mismo criterio de auditoría append-only sin PII.
- **ADR-0056-generacion-documental-standalone**: #12211, que no se modifica.

## Notas para agentes

- **Database Agent**: cinco tablas en `tramites`, DDL de referencia en §6 del diseño. Excepciones a documentar:
  - `consolidado_export_audit` es append-only, sin FK y sin soft delete;
  - `consolidado_export_settings` es global, sin `tenant_id`;
  - ítems y partes van sin `trg_audit_log` por su alta rotación;
  - el índice único parcial que materializa «1 lote activo por usuario» va sin `tenant_id` a la cabeza.
- **Backend Agent**:
  - (v2) tomar el consolidado existente tal cual; generar **solo** si no existe, por el generador oficial con `force=false`, `userId=null` y `soloSiNoExiste=true`; nunca `EntregarConsolidadoHandler` ni el POST del gestor;
  - (v2) omitir migrado V1 en final y final sin FUR antes de generar;
  - un scope de DI por ítem con el tenant del ítem;
  - el timeout de ítem siempre menor que su lease;
  - nunca generar PDF en la descarga;
  - la creación del lote, sus ítems y la fila de auditoría van en una sola transacción.
- **Frontend Agent**:
  - el seguimiento global se monta en `components/atom/Shell.tsx` y hace polling (no SSE);
  - autodescarga solo de la parte 1 o única, una vez por lote, con un candado entre pestañas;
  - tokens semánticos, sin hex.
- **QA Agent**:
  - matar core-api a mitad de un lote mixto: los que tenían consolidado conservan el mismo adjunto (`id`, `sha256`); los que no, exactamente uno;
  - (v2) consolidado desactualizado, aprobado y rechazado con consolidado → el de BD sin regenerar; aprobado sin consolidado → generado; el POST individual del gestor sigue bloqueado en aprobado;
  - dos lotes concurrentes (uno de 2.000 y otro de 5) en que el de 5 termina primero;
  - N+1 PDF producen 2 partes;
  - otro usuario recibe 404 al pedir la parte;
  - si falla la auditoría, el lote no existe.
- **Security Agent**:
  - revisar el framing AES-256-GCM, el envoltorio de la clave del lote con Data Protection (propósito `Flit.Tramites.ConsolidadoLote.Dek.v1`) y su destrucción en la purga;
  - el filtro auditado se minimiza: las listas pegadas se guardan como conteo;
  - aceptar el riesgo residual del texto cifrado huérfano (≤ 30 días) y de las copias de seguridad de la BD.
- **Infra Agent**:
  - `proxy_buffering off` en `/api/v1/consolidados/lotes/*/partes/*`;
  - volumen temporal de core-api con ≥ 2 × M libres;
  - verificar que la ruta catch-all del gateway cubre `/api/v1/consolidados/*`.

## Adenda v3 (2026-10-07) — schema

> **Estado:** Propuesto (la adenda no cambia el estado; la aceptación es del Líder Técnico humano).
> **Motivo:** cerrar el veredicto `MISSING_1` (A19) del `db-schema-validator` sobre el DDL final del database-agent (`.claude/state/epica-13216/07-schema-13306.md` §3, reporte §6). Documenta las excepciones de convención que el DDL toma y que ni la v1 ni la v2 de este ADR cubrían, más los hallazgos del database-agent que corrigen el DDL de referencia del diseño (§6 de `05-diseno-13306.md`).
> **Insumos:** respuestas del usuario Q1–Q12 (`.claude/state/epica-13216/06-regla-usuario-no-regenerar.md`), en particular Q4 y Q8.
> **Supersedes:** nada. Ningún ADR `Aceptado` se contradice; las excepciones se apoyan en precedentes ya mergeados (DDL 67, 106, 113, 120).

Las excepciones se numeran E1–E5, igual que la cabecera del DDL 133. E1 (settings global) y E2 (auditoría append-only) ya estaban en «Notas para agentes»; aquí se precisan. E3 y E4 se amplían. E5 es nueva.

### A3.1 — E5: lote de Super Admin sin compañía (Q8)

**Decisión.** El usuario eligió Q8 = c): el lote de Super Admin no pertenece a ninguna compañía y las compañías alcanzadas quedan en la auditoría. En el schema:

| Columna | Nulabilidad | Garantía en la base |
|---|---|---|
| `consolidado_export_batches.tenant_id` | NULL **si y solo si** `origin = 'superadmin'` | `ck_consolidado_export_batches_tenant_origin CHECK ((origin = 'superadmin') = (tenant_id IS NULL))` + FK a `identity.tenants` (RESTRICT) cuando no es NULL |
| `consolidado_export_batch_parts.tenant_id` | NULL en partes de lotes `superadmin` | no la escribe la aplicación: la copia del lote el trigger `tr_consolidado_export_batch_parts_tenant` (BEFORE INSERT / UPDATE OF tenant_id, batch_id). Una parte nunca queda imputada a otra compañía |
| `consolidado_export_audit.actor_tenant_id` | NULL **si y solo si** `origin = 'superadmin'` | `ck_consolidado_export_audit_tenant_origin`, mismo bicondicional; `origin` se desnormaliza en la auditoría porque no tiene FK al lote |
| `consolidado_export_batch_items.tenant_id` | **NOT NULL siempre** (también en Super Admin) | es la compañía **del trámite** (CF-15), no la del solicitante |

Complementos:
- **`scope_tenant_id`** guarda el acotamiento por `X-Tenant-Id` del Super Admin (FA2). Solo puede existir en origen `superadmin` (`ck_consolidado_export_batches_scope_origin`). No sustituye a `tenant_id`: «a qué compañía se acotó la selección» y «a qué compañía pertenece el lote» son cosas distintas, y la segunda no existe para el Super Admin.
- **`reached_tenant_ids` es obligatorio en `lote_creado`** (`ck_consolidado_export_audit_created`): contiene las compañías distintas de los trámites congelados (`items.tenant_id`). Va vacío solo si `total_items = 0`. Lleva índice GIN propio. Así, el lote de Super Admin queda trazado ante cada compañía cuyos datos tocó, aunque el lote en sí no tenga compañía (Ley 1581, CF-22).
- **RLS:** en batches, parts e items, la política `tenant_isolation` es `app.is_superadmin = 'true' OR tenant_id = app.current_tenant_id`. En audit es `app.is_superadmin OR actor_tenant_id = actual OR actual = ANY(reached_tenant_ids)` (criterio del DDL 113). **Se declara que la RLS es decorativa en este repo:** no hay `FORCE ROW LEVEL SECURITY` y la app conecta como owner. Las políticas se escriben por convención y para que sigan siendo correctas si algún día se fuerza la RLS. El aislamiento real lo pone la aplicación: las consultas del dueño filtran por `requested_by_user_id = sub` (también las del Super Admin, que no tiene tenant), y el reclamo y la purga entre compañías usan SQL parametrizado en el repositorio, sin `IgnoreQueryFilters()`.

**Alternativas evaluadas.**

| | **(a) NULL acotado por CHECK bicondicional (elegida)** | (b) Tenant «plataforma» ficticio para el Super Admin | (c) `tenant_id` NULL sin CHECK |
|---|---|---|---|
| Pros | Lo exige Q8. La base impide los dos errores: un lote de Gestor sin compañía y un lote de Super Admin imputado a una compañía. Sin datos falsos en `identity.tenants`. | `tenant_id NOT NULL` en todas partes. A4 sin excepción. | Lo más simple. |
| Contras | Excepción a A4/A11 documentada aquí. Los repositorios deben tratar `Guid?`. | Inventa una compañía que aparece en informes, RLS y conteos. Va contra Q8. Hay que excluirla en todos los listados de compañías. | Un fallo de la aplicación deja lotes de Gestor sin compañía. No pasa el validador. |
| Esfuerzo | S | M | S |
| Riesgo | Bajo | Medio (contamina el catálogo de compañías) | Alto (integridad) |

### A3.2 — E3 ampliada: ítems y partes sin borrado lógico; E2 precisada: auditoría append-only

**Decisión.**
- **`consolidado_export_batch_items` y `consolidado_export_batch_parts` no tienen `deleted_at`/`deleted_by`** (ni `trg_audit_log`, ya decidido en v1 por su rotación). Su ciclo de vida es el del lote y se gobierna **por estado**. Ítem: `pendiente → procesando → incluido | omitido`. Parte: `pendiente → empaquetando → cerrada | fallida → purgada`. Al cumplirse la retención, la purga destruye la DEK del lote (borrado criptográfico) y marca `purgada` / `purged_at`. Si alguna vez se borra un lote físicamente, ítems y partes caen con él por `ON DELETE CASCADE`. Ningún flujo de negocio «borra» un ítem o una parte suelta: un borrado lógico sería un estado muerto que ningún código leería.
- **`batches` sí conserva `deleted_at`/`deleted_by`**, y el índice único del lote activo filtra `deleted_at IS NULL`. Es la entidad de negocio que ve el usuario.
- **`consolidado_export_audit` es append-only:**
  - sin FK, para que la auditoría sobreviva al lote, al usuario y a la compañía;
  - sin soft delete y sin `trg_audit_log`;
  - sin trigger de `row_version`: la columna existe por convención A5 y nunca cambia;
  - `UPDATE` y `DELETE` los rechaza `tr_consolidado_export_audit_immutable` con `check_violation`.

  Es el patrón del DDL 113 (`tramites.network_access_audit`, HU #12361). La traza Ley 1581 de ítems y partes vive aquí, no en `audit.audit_logs`.

**Alternativas.** (a) **Estado + purga, sin `deleted_*` (elegida)**: un solo mecanismo de fin de vida, coherente con la máquina de estados, y sin columnas muertas. (b) `deleted_*` en ítems y partes por uniformidad con A5/A6: añade 2 columnas × 20.000 filas por lote que ningún flujo escribe, y obliga a filtrar `deleted_at IS NULL` en los 5 índices parciales sin ganar nada. (c) Borrado físico de ítems y partes en la purga: pierde el detalle de omitidos que se usa en soporte y en los conteos de `lote_finalizado`. P-2 del 07, que propone poner `plate` en NULL en la purga, es compatible con (a) y queda abierta para el security-agent.

### A3.3 — E4 ampliada: índices que no empiezan por `tenant_id`

Lista exacta del DDL 133 (07 §3, §6.1 A11):

| Índice | Columnas / filtro | Por qué no lleva `tenant_id` a la cabeza |
|---|---|---|
| `uq_consolidado_export_batches_active_per_user` | `(requested_by_user_id)` WHERE activo AND `deleted_at IS NULL` | CF-17: 1 lote activo **por usuario**, transversal a compañías. El Super Admin no tiene tenant (E5) |
| `ix_consolidado_export_batches_requested_by_created` | `(requested_by_user_id, created_at DESC)` | Cubre la FK a `identity.users` (A9). Lo usan «mis lotes», `GET actual` y el lote retenido: el dueño se identifica por `sub` |
| `ix_consolidado_export_batches_scope_tenant` | `(scope_tenant_id)` WHERE NOT NULL | Índice de FK (A9) sobre una columna opcional; `scope_tenant_id` es tenant, pero no es la columna `tenant_id` |
| `ix_consolidado_export_batches_transit_office` | `(ot_transit_office_id)` WHERE NOT NULL | Índice de FK (A9) |
| `ix_consolidado_export_batches_claim` | `(last_claimed_at NULLS FIRST, created_at)` WHERE `en_cola`/`en_proceso` | El worker reclama entre **todas** las compañías con round-robin (CF-21). Parcial y pequeño (precedente DDL 106, `ix_standalone_document_batches_pendientes`) |
| `ix_consolidado_export_batches_purge` | `(expires_at)` WHERE `purged_at IS NULL AND expires_at IS NOT NULL` | La purga es global, entre compañías |
| `ix_consolidado_export_batch_parts_claim` | `(created_at)` WHERE `pendiente`/`empaquetando` | Carril de empaquetado global, entre compañías |
| `ix_consolidado_export_batch_items_procedure_instance` | `(procedure_instance_id)` | Índice de FK a `procedure_instances` (A9 prevalece sobre A11) |
| `ix_consolidado_export_batch_items_batch_part` | `(batch_id, part_number)` WHERE `part_number IS NOT NULL` | FK compuesta a parts (A9). Además se lee para empaquetar la parte k |
| `ix_consolidado_export_batch_items_claim` | `(batch_id, position)` WHERE `pendiente`/`procesando` | Reclamo **dentro** de un lote (`FOR UPDATE SKIP LOCKED`). Los ítems de un lote de Super Admin son de varias compañías |
| `ix_consolidado_export_batch_items_unassigned` | `(batch_id, processed_at)` WHERE terminado AND `part_number IS NULL` | Asignación de partes dentro del lote. Mismo motivo |
| `ix_consolidado_export_audit_batch` | `(batch_id)` | Trazar los eventos de un lote, que puede no tener compañía (E5) |
| `ix_consolidado_export_audit_reached_tenant_ids` | GIN `(reached_tenant_ids)` | La consulta «qué lotes tocaron datos de la compañía X» y la política RLS de audit. `tenant_id` como primera columna no aplica a un GIN de arreglo |

Los índices por compañía sí la llevan primero: `ix_consolidado_export_batches_tenant_created`, `ix_consolidado_export_batch_parts_tenant`, `ix_consolidado_export_batch_items_tenant` y `ix_consolidado_export_audit_actor_tenant_occurred`. Los `UNIQUE` `uq_consolidado_export_batch_parts_batch_number` y `uq_consolidado_export_batch_items_batch_instance` empiezan por `batch_id`. Son restricciones de unicidad dentro del lote y cubren su FK.

**Alternativa descartada:** anteponer `tenant_id` a todos. En reclamo y purga el predicado nunca filtra por compañía, así que el índice no se usaría o crecería sin beneficio. En los de usuario rompe el caso Super Admin (tenant NULL) y la regla CF-17, que es transversal.

### A3.4 — E1 precisada: settings global con `id uuid` y fila única por índice `ON ((true))`

**Hallazgo H-1 (database-agent):** `public.trg_audit_log()` declara `v_id uuid` y asigna `v_id := NEW.id` (`services/core-api/src/Flit.Infrastructure/Persistence/Sql/SchemaBootstrap.cs:69-80`). El DDL de referencia del diseño (`id smallint DEFAULT 1` + `CHECK (id = 1)`) **falla** al primer `INSERT` del sembrado y en cada `UPDATE`: no se puede convertir `1` a `uuid`. La migración no se podría aplicar.

**Decisión (corrige el DDL de referencia de §6 del diseño):**
- `id uuid NOT NULL DEFAULT uuidv7()` (A3);
- fila única con `CREATE UNIQUE INDEX uq_consolidado_export_settings_singleton ON tramites.consolidado_export_settings ((true))`, el patrón vigente de `admin.notification_test_settings` (`Persistence/Sql/Ddl/67-HU11365-notification-test-settings.sql:64`) y `admin.quipux_settings`;
- sembrado reproducible `INSERT … SELECT uuidv7() WHERE NOT EXISTS (…)`, que no duplica ni pisa valores ya calibrados;
- tabla **global de plataforma**: sin `tenant_id`, sin RLS y sin soft delete (como el DDL 67). Mantiene `trg_row_version` y `trg_audit_log`, que ya funciona con `uuid`, así que el histórico de calibraciones queda en `audit.audit_logs`;
- en EF Core el índice `((true))` no se puede expresar con `HasIndex`. Vive solo en SQL y se documenta en el `<summary>` de la configuración (como `NotificationTestSettingsConfiguration`).

**Alternativas.** (a) **`uuid` + singleton `((true))` (elegida)**: reutiliza un patrón existente y es compatible con `trg_audit_log`. (b) `smallint` sin `trg_audit_log`: se pierde el histórico de cambios de parámetros que gobiernan un proceso de PDN. (c) Modificar `trg_audit_log()` para aceptar ids no `uuid`: toca una función compartida por decenas de tablas, por un caso que el patrón (a) ya resuelve.

### A3.5 — Grant del permiso `consolidado-masivo.download`

**Q4 del usuario:** grant por defecto a SuperAdmin, admin_tramites, Radicador **y AdminCompany**.

**Restricción verificada:** el trigger `security.tr_role_permissions_same_product` (DDL 120, HU #12964, ADR-0063 borrador) rechaza con `check_violation` que un rol reciba un permiso de un módulo de otro producto. Solo SuperAdmin está exento (`Persistence/Sql/Ddl/120-HU12964-rbac-por-producto.sql:223-252`, función en 223-247 y trigger en 249-252). `AdminCompany` es de producto `plataforma` (misma DDL, l. 36), y `consolidado-masivo` es de `tramites`. Un grant directo a `AdminCompany` en el seeder lanzaría la excepción en `SaveChangesAsync` y **tumbaría el sembrado del catálogo RBAC en el arranque** de los tres ambientes. Por la misma razón, `historial-placa.read` ya movió su grant de AdminCompany a `admin_tramites` (`Security/DevelopmentAuthSeeder.cs:1492-1495`).

**Decisión:**
- Grants directos: `["SuperAdmin", ProductRoleCodes.AdminTramites, "Radicador"]`. **AdminCompany no recibe grant directo.**
- AdminCompany obtiene el permiso por el **espejo AdminCompany → admin_tramites** del DDL 120:
  - backfill §4 (`120-HU12964-rbac-por-producto.sql:143-162`): todo AdminCompany activo recibió `admin_tramites` en la misma empresa;
  - función `security.trg_ura_mirror_admin_tramites()` (l. 169-213) y trigger `tr_ura_mirror_admin_tramites` (AFTER INSERT OR UPDATE OF role_id, deleted_at ON `security.user_role_assignments`, l. 215-218): asignar AdminCompany crea `admin_tramites` (l. 192-199) y quitarlo lo cierra (l. 200-205).
- **Matiz verificado (l. 194-199):** el espejo solo agrega `admin_tramites` si el usuario no tiene ya otro rol de producto `tramites` en esa empresa, porque hay un rol activo por usuario, empresa y producto (`uq_ura_active_user_tenant_product`). Un AdminCompany que en esa empresa ya tenga, por ejemplo, Radicador recibe el permiso por Radicador. Si su rol de Trámites es otro sin grant, no lo recibe. Es el mismo comportamiento que ya tiene `historial-placa.read`, y se resuelve concediéndolo a ese rol desde la pantalla de RBAC. **Se declara; no bloquea.**
- `ot_admin` (producto `tramites`) lo recibe con #13308 (FB).
- **Sembrado:** `DevelopmentAuthSeeder.SeedConsolidadoMasivoPermissionsAsync` (nuevo; copia de `SeedHistorialPlacaPermissionsAsync`, `DevelopmentAuthSeeder.cs:1452`), invocado desde `SeedRbacCatalogAsync` (l. 130-138). Crea el módulo `consolidado-masivo` (sin `ProductCode`, default `tramites`), la acción `consolidado-masivo.download` y los grants, todo idempotente y saltando los roles con `DeletedAt`. Lo gobierna `Seed:RbacCatalog` (`Security/SeedSettings.cs:19-25`), no el nombre del ambiente. `docker-compose.prod.yml:181` fija `Seed__RbacCatalog: ${FLIT_SEED_RBAC_CATALOG:-true}` y el seeder corre después de `Migrate()` (`Flit.Api/Program.cs:206-210`), así que **llega a DEV, QA y PDN** en el arranque. No hay DDL ni migración que inserte en `security.modules`/`security.permissions`, y el Down de la migración no toca el catálogo RBAC.
- Test: `ConsolidadoMasivoPermissionSeedTests` (calcado de `HistorialPlacaPermissionSeedTests`). Debe probar que **ningún** grant va a `AdminCompany`.

**Alternativas.** (a) **Grant a admin_tramites + espejo (elegida)**: cumple Q4 sin violar la regla de producto. (b) Marcar el módulo `consolidado-masivo` como producto `plataforma`: es falso (es una función de Trámites) y abriría el permiso a roles sin acceso a Trámites. (c) Eximir a AdminCompany en `trg_role_permissions_same_product`: reabre lo que cerró la HU #12964 y contradice ADR-0063. **P-1 del 07 queda como confirmación al usuario** de que «AdminCompany vía admin_tramites» satisface su Q4. No cambia el schema.

### A3.6 — Número de DDL y migración

- DDL embebido: **`services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/133-HUxxxxx-consolidado-export-batches.sql`**. Hoy el máximo en `origin/develop` y en todas las ramas remotas es 132 (con 130 duplicado). Idempotente y sin `BEGIN/COMMIT`.
- Migración: **`Migrations/<yyyyMMddHHmmss>_HUxxxxx_ConsolidadoExportBatches.cs`**, clase `HUxxxxx_ConsolidadoExportBatches`, con timestamp mayor que `20261006210116`. Patrón HU #12361 (`20260914120000_HU12361_NetworkAccessAudit.cs:20`): `Up` = `migrationBuilder.Sql(EmbeddedDdl.LoadUp("133-HUxxxxx-consolidado-export-batches.sql"))`; `Down` = SQL inline (07 §3.6), con la advertencia de que revertir destruye la auditoría Ley 1581.
- Procedimiento:
  1. Crear las 5 configuraciones EF y los DbSet.
  2. Ejecutar `dotnet ef migrations add` para alinear el snapshot.
  3. Sustituir el cuerpo de `Up`/`Down`.
  4. Comprobar con `dotnet ef migrations list` que sale `Pending`.
- `HUxxxxx` es el id de la primera HU del Feature #13306.
- **La HU parte de `origin/develop`**. La rama local `develop` está atrasada: le faltan `130-HU13283-identidad-manual.sql` y `20261005205449_HU13283_IdentidadManual`. Partir del local crearía un hueco de numeración y un snapshot desalineado.

### A3.7 — Notas para agentes (adenda)

- **Database Agent:** el DDL 133 de `07-schema-13306.md` §3 es el que se materializa. Con esta adenda, A19 pasa y el validador puede dar `OK_TO_MERGE_DB` sobre el DDL. El PR real se vuelve a validar.
- **Backend Agent:**
  - `TenantId` es `Guid?` en lote, parte y entrada de auditoría;
  - nunca se asigna `parts.tenant_id`: lo fija el trigger;
  - las consultas del dueño van por `sub`, sin `tenant_id = …`;
  - el seeder no incluye AdminCompany.
- **Security Agent:**
  - validar el nivel PII de `items.plate` (`@pii:low`, por consistencia con DDL 47/59) y de `audit.client_ip` (`@pii:medium`);
  - decidir P-2 (`plate` a NULL en la purga);
  - confirmar que la RLS decorativa no es una regresión, porque es la convención vigente.
- **QA Agent:**
  - insertar un lote `superadmin` con `tenant_id` y un lote `tramites` sin él: ambos deben fallar por CHECK;
  - un `lote_creado` sin `reached_tenant_ids` debe fallar;
  - un AdminCompany sin otro rol de Trámites debe poder crear un lote.

## Adenda v4 (2026-10-07) — #13307 Super Admin y cancelación

> **Estado:** Propuesto (la adenda no cambia el estado; la aceptación es del Líder Técnico humano).
> **Motivo:** fijar cómo el motor sirve al Super Admin (origen `superadmin`) y cómo se cancela un lote de cualquier origen, sin rediseñar el motor.
> **Diseño:** `.claude/state/epica-13216/09-diseno-13307.md`.
> **Insumos:** respuestas del usuario Q-1, Q-2, Q-3, Q-4 y Q-8 (`.claude/state/epica-13216/06-regla-usuario-no-regenerar.md`, «Respuestas a los diseños delta de #13307 / #13308»).
> **Supersedes:** nada.

### A4.1 — Origen `superadmin` desde `/tramites`

**Decisión.**
- El mismo `POST /api/v1/tramites/consolidados/lotes` y el mismo resolver (`ListIdsFilteredAsync(Guid?)`). En esta ruta el origen lo decide `TenantEnforcementMiddleware` (`IsSuperAdmin`), nunca el cuerpo.
- `tenant_id` es NULL; `X-Tenant-Id` va a `scope_tenant_id`; el «filtro de compañía» de `/tramites` es la condición `compania` del filtro.
- Guard que falla cerrado: un usuario no SuperAdmin sin tenant → 403 `sin_compania` (defensa en profundidad).
- El lote que el Super Admin crea desde la bandeja de un OT **no** es de origen `superadmin`: es `ot_bandeja` y lo rige la Adenda v5 (A5.5).

**Hallazgo de seguridad (prerrequisito también de #13306, va en #13374).** `/api/v1/tramites/consolidados` debe registrarse en `TenantEnforcementMiddleware.RuntimeScopedRoutes` (`new("/api/v1/tramites/consolidados", RouteMatch.Prefix)`), con test en `TenantEnforcementMiddlewareTests`. Fuera de esa lista, `RequestTenantResolver.FromItems` devuelve `(null, false)`: el lote de un Gestor abarcaría **todas las compañías** (fuga entre compañías, CF-15 de #13306) y el origen Super Admin no se podría distinguir. El registro de la ruta entra como AC de #13374 (F2) y debe estar antes de implementar #13373/#13374.

**Alternativas.** (a) **Mismo endpoint y resolver (elegida)**; (b) `SuperAdminSeleccionResolver` propio: una copia sin comportamiento distinto; (c) endpoint admin propio: duplica el contrato sobre la misma tabla.

### A4.2 — Maestro en modo Super Admin

**Decisión (Q-1 = a).**
- En `/tramites`, `consolidado_maestro` solo se admite para el Super Admin; el Gestor/Radicador pide siempre `consolidado`. (El origen `ot_bandeja` es siempre maestro: Adenda v5.)
- El entregador aplica la regla v2: el radicado ante Quipux primero; si no, el existente tal cual; solo si falta, primera generación con `force=false` y `soloSiNoExiste=true`.
- **`matrizPrecedencia = null`** y **sin** `IQuipuxReadOnlyGuard`, en paridad con la entrega individual actual del Super Admin (`GET …/consolidado/entrega`). No depende del servicio de contexto OT de #13308.
- Las excepciones Q10 (migrado V1 en final) y Q12 (final sin FUR) aplican también al maestro.

**Alternativas.** (a) **Paridad con la entrega del Super Admin (elegida)**; (b) resolver la matriz como la consola OT: depende del servicio que extrae #13308 y no aplica antes de radicar; (c) solo el maestro existente: incumple CF-06.

**Consecuencia.** Si el OT no configuró prelación, el maestro que genera el Super Admin desde `/tramites` sigue el orden por modalidad, igual que hoy en su entrega individual.

### A4.3 — Confirmación con tipo (Q-2 = a)

La confirmación nombra el tipo elegido:
- `consolidado`: el texto aprobado en Q1.
- `consolidado_maestro`: «Se descargará el consolidado maestro que cada trámite tiene guardado, tal como está, aunque no refleje cambios posteriores. Solo se generará el consolidado maestro de los trámites que todavía no tienen uno.» Sin mención a Quipux. Es el **texto único** del maestro para Super Admin y OT (Q-FB3, Adenda v5).

### A4.4 — Proceso multicompañía

- Cada ítem se procesa con `items.tenant_id` (la compañía del trámite) en su propio scope de DI; nunca con `scope_tenant_id` ni con el tenant del lote.
- La revalidación del Super Admin exige que el usuario siga activo, conserve una asignación activa del rol SuperAdmin y que el trámite siga vivo en la compañía congelada; si no, «Acceso revocado».

### A4.5 — Cancelación

**Decisión.** Cancelar es una **transición de estado en BD** en una transacción con `FOR UPDATE` del lote:
- lote → `cancelado` (`finished_at = expires_at = purged_at = now()`, `dek_wrapped = NULL`: borrado criptográfico y cupo liberado);
- ítems vivos → `cancelado`;
- partes `cerrada` → `purgada`; `pendiente`/`empaquetando` → `descartada`;
- auditoría `lote_cancelado` en la misma transacción (falla cerrado: 503).

Los carriles **no se abortan a la fuerza**: el reclamo ya filtra por estado; el ítem consulta el estado antes del entregador; el ítem en vuelo termina y su consolidado queda oficial (CF-09), pero su cierre condicionado por estado actualiza 0 filas; el empaquetado consulta el estado en checkpoints y aborta; la transición a terminal compite por el mismo lock. **Cota aceptada:** como mucho `item_slots` generaciones terminan después de cancelar.

**Contrato** (sirve a los tres orígenes):
- `POST /api/v1/consolidados/lotes/{id}/cancelacion`, solo el solicitante; otros usuarios, incluido el Super Admin → 404;
- exige `consolidado-masivo.download`: **quien perdió el permiso no puede cancelar (403, Q-8 = a)**; su lote se vacía solo, porque la revalidación por ítem omite el resto como «Acceso revocado»;
- idempotente (202); 409 `lote_terminado` si ya terminó.

**UX (Q-3 = b, Q-4 = b).** «Cancelar» actúa **con un solo clic, sin confirmación**. «Descarga cancelada» se muestra **≈ 8 s** y desaparece solo; es terminal, neutro y sin autodescarga.

**Alternativas.** (a) **Estado en BD + checkpoints (elegida)**; (b) además, una señal en memoria que aborte la generación en vuelo: deja estados intermedios y solo vale con una instancia; (c) borrado físico del lote: pierde «Descarga cancelada» y los conteos.

### A4.6 — Schema (delta sobre la Adenda v3; entra en #13368)

- `consolidado_export_batch_parts.status` + `descartada`; `consolidado_export_batch_items.status` + `cancelado`; `ck_consolidado_export_audit_cancelled` (conteos obligatorios en `lote_cancelado`). Sin columnas nuevas: `cancelado` del lote y `lote_cancelado` de la auditoría ya existían.
- Se materializa dentro de #13368 (partes, ítems y auditoría). Si ya se hubiera mergeado, DDL propio con `ALTER … DROP/ADD CONSTRAINT`.

**Alternativas.** (a) **Estados terminales propios (elegida)**; (b) sin DDL, con join al lote en cada reclamo: frágil y deja filas muertas en los índices parciales; (c) reutilizar `fallida`/`omitido`: arrastra el lote a `fallido` y ensucia `omitidos.csv`.

### A4.7 — Permiso y auditoría

- El SuperAdmin pasa `RequirePermission` por el bypass de `PermissionAuthorizationHandler`. En el frontend, `isSuperAdmin ‖ hasPermission`.
- `lote_creado` del Super Admin: `actor_tenant_id NULL` + `reached_tenant_ids` (garantizados por A3.1). Se repite `reached_tenant_ids` en `lote_cancelado` del Super Admin. La condición `compania` se minimiza a conteo en `filter_summary`.

### A4.8 — Notas para agentes (adenda)

- **Database Agent:** materializar A4.6 en #13368.
- **Backend Agent:**
  - registrar `/api/v1/tramites/consolidados` en `RuntimeScopedRoutes` (#13374), con test;
  - cierre de ítem y de parte condicionado por estado (y `claimed_by`);
  - checkpoints de cancelación; una DEK ausente con el lote cancelado no es fallo; la purga ignora los lotes con `purged_at`;
  - `fur_requerido` en final también para el maestro (AC de #13371, ver A5.6).
- **Frontend Agent:**
  - la creación del Super Admin usa las mismas cabeceras que el listado (nunca `tenantHeader()` sin argumento);
  - selector de tipo solo para el SuperAdmin; confirmación con el texto de A4.3;
  - «Cancelar» en el aviso del `Shell`, sin diálogo de confirmación; «Descarga cancelada» ≈ 8 s.
- **QA Agent:**
  - cancelar durante la generación de un ítem: el adjunto queda en el trámite y el ítem no entra a ninguna parte;
  - cancelar durante el empaquetado: ninguna parte descargable, `dek_wrapped IS NULL`;
  - doble cancelación → 202; cancelar un lote completado → 409; otro usuario o el Super Admin → 404; usuario sin el permiso → 403;
  - un lote nuevo tras cancelar se crea sin 409;
  - lote del SA con trámites de dos compañías → un ZIP y `reached_tenant_ids` con las dos;
  - ítem con tenant manipulado → omitido sin generar;
  - Gestor en `/api/v1/tramites/consolidados/lotes` nunca obtiene tenant `null`.
- **Security Agent:** revisar la exportación entre compañías (A4.1), el registro de la ruta en `RuntimeScopedRoutes`, `lote_cancelado` con conteos y `reached_tenant_ids`, y que el 404 frente a no dueños se mantenga en la cancelación.

## Adenda v5 (2026-10-07) — #13308 bandeja del OT

> **Estado:** Propuesto (la adenda no cambia el estado; la aceptación es del Líder Técnico humano).
> **Motivo:** conectar la bandeja del OT al motor (estrategia `ot_bandeja`, tipo `consolidado_maestro`) sin rediseñarlo.
> **Diseño:** `.claude/state/epica-13216/10-diseno-13308.md`.
> **Insumos:** respuestas del usuario Q-FB1 a Q-FB5 (`06-regla-usuario-no-regenerar.md`) y Q11.
> **Supersedes:** nada. No contradice ningún ADR `Aceptado`. Respeta HU #12350 (la bandeja del OT no depende del grant). Precisa A3.5 en cuanto a la HU que siembra el grant de `ot_admin` (A5.4).

### A5.1 — Alcance de acceso del lote OT (Q-FB1 = a)

**Decisión.** El lote `ot_bandeja` usa el **mismo universo que la bandeja**, no el grant:
- `OtClientProcedureRepository.BuildAccessibleQuery`: organismo, estado en `RecibidosPorOrganismo` y vivo.
- Al crear, se resuelve con `ListAccessibleRefsAsync` (filtros y orden de la bandeja, sin paginar). Un ID inyectado que no está en la bandeja queda fuera (AC negativo).
- Por ítem, se revalida con `GetByIdAsync(otTenant, id, ot_transit_office_id)`.
- «Acceso revocado» = el trámite salió de la bandeja, o el solicitante perdió la membresía en el tenant OT, el rol, el permiso `consolidado-masivo.download` o la cuenta.
- El grant no gobierna la visibilidad (HU #12350, AC7): revocar el grant a mitad del lote no excluye los trámites ya entregados.

**Alternativas.** (b) Bandeja ∩ grant solo en el lote: el lote sería más estricto que la bandeja y que la descarga individual. (c) Reintroducir el grant en la consulta base: regresión de HU #12350.

**Consecuencia.** CF-06 de #13308 se reescribe en ADO («grant vigente» → «trámites que el OT ve en su bandeja, revalidados por ítem»).

### A5.2 — Servicio de composición del contexto OT

**Decisión.**
- `Flit.Infrastructure/OtClientProcedures/OtClientProcedureConsolidadoContext` (`ResolverAccesoAsync`, `EjecutarEnContextoClienteAsync`) encapsula, sin HTTP:
  - el acceso del OT con `transitOfficeIdOverride`;
  - el scope transaccional del tenant cliente (`ExecuteInClientTenantScopeAsync`, con compensación post-commit);
  - la precedencia de la matriz documental.
- Lo consumen los endpoints `POST consolidado-maestro` y `GET consolidado/entrega` de `AdminOtEndpoints` (sin cambio de contrato, con test de caracterización previo) y el entregador del lote.
- Vive en Infrastructure porque `Flit.Tramites.Application` y `Flit.Admin.Application` no se referencian entre sí.

**Alternativas.** (b) Helpers y estrategias en `Flit.Api`: un componente del worker quedaría en la capa API. (c) Reimplementar en el entregador: copia la lógica de acceso (DEP-05).

### A5.3 — Entrega por ítem del maestro en la bandeja OT

**Decisión.**
- `OtConsolidadoLoteEntregador` (clave `ot_bandeja`) usa **una transacción por trámite** con el scope del tenant cliente y el OT fijado. Dentro ejecuta el `ConsolidadoLoteEntregador` común con:
  - la precedencia de la matriz del OT;
  - un gancho `antesDeGenerar` = `IQuipuxReadOnlyGuard.ValidateActionAsync(tenantOT, "generar_consolidado_maestro")`, llamado solo si hay que generar. Por Q11 el guard no restringe hoy esa acción (intencional); el lote lo invoca igual que el endpoint.
- La regla v2 aplica igual al maestro: el existente se toma tal cual (el radicado primero); si no existe, se genera con `force=false` y `soloSiNoExiste=true`; se omiten el migrado V1 en final (Q10) y el final sin FUR (Q12).
- Paralelismo: el `item_slots` global del motor, sin carril propio.
- **Confirmación (Q-FB3 = b):** el mismo texto del maestro de A4.3, sin mención a Quipux.

**Alternativas.** (b) Leer fuera de transacción y abrir el scope solo para generar: dos caminos de contexto, sin ganancia, porque `soloSiNoExiste` ya cubre la carrera. (c) Carril propio: contradice D6.

### A5.4 — Permiso en la bandeja OT (Q-FB5 = a)

**Decisión.**
- La ruta nueva `POST /api/v1/admin/ot/consolidados/lotes` exige `RequirePermission("consolidado-masivo.download")` en AND con la `OtModulePolicy` del grupo. Es el primer uso del patrón en `AdminOtEndpoints`; las rutas existentes no cambian.
- Grant directo **solo a `ot_admin`**, en `SeedConsolidadoMasivoPermissionsAsync` **dentro de #13369** (Q-FB4; precisa la mención «con #13308» de A3.5). `ot_admin` es de producto `tramites` (DDL 120), así que `tr_role_permissions_same_product` lo admite, a diferencia de AdminCompany (A3.5). Es inocuo antes de FB: en la ruta del gestor, el tenant OT no tiene trámites propios y el lote sale vacío.
- Los demás roles OT que admite `OtModulePolicy` no reciben el permiso por defecto; se concede por RBAC si hace falta.
- Consulta, descarga y cancelación siguen en `/api/v1/consolidados/lotes/*` (dueño = `sub`).

**Alternativas.** (b) Policy por rol sin slug: incumple CF-09 y choca con las rutas neutras, que exigen el slug. (c) Proteger también las rutas individuales: es la DEUDA-2 (H9), fuera de alcance.

### A5.5 — Lote OT del Super Admin y schema (Q-FB2 = a)

**Decisión.**
- El Super Admin puede crear el lote desde la bandeja de un OT, con `?transitOfficeId` obligatorio. El lote es de origen `ot_bandeja` e imputado al tenant OT: `tenant_id` = tenant OT resuelto (`ResolveOtUserScopeAsync`), que cumple `ck_consolidado_export_batches_tenant_origin`. Como todo lote `ot_bandeja`, usa la matriz del OT y el gancho Quipux (A5.3), a diferencia del maestro de `/tramites` (A4.2).
- Revalidación de ese solicitante: rol SuperAdmin activo (A4.4) en lugar de membresía en el tenant OT; el trámite debe seguir en la bandeja del organismo.
- Sin tablas ni columnas nuevas. `ck_consolidado_export_batches_ot_origin` pasa a bicondicional: `(origin = 'ot_bandeja') = (ot_transit_office_id IS NOT NULL)`, dentro del DDL 133 de #13367.
- El organismo se audita en `filter_summary`; `reached_tenant_ids` = compañías cliente de los ítems.
- La `busqueda` libre se audita solo como `{presente, longitud}`, porque puede contener un número de documento.

### A5.6 — Cambios al contrato del motor, plegados en sus HUs (Q-FB4 = a)

Aditivos; entran como AC de las HUs del motor antes de implementarlas:

| Ref. | HU | Cambio |
|---|---|---|
| R-a | #13370 | `ILoteSeleccionResolver` no se tipa con `TramitesSearchFilter`: carga de filtro por origen (`abstract record LoteFiltro` → `TramitesLoteFiltro` / `OtBandejaLoteFiltro`) y resolución con clave por `origin` |
| R-b | #13371 | `ConsolidadoLoteEntregador` acepta `IReadOnlyList<string>? precedenciaMatriz` y un gancho `antesDeGenerar` que devuelve un motivo de omisión; aplica `fur_requerido` en final también al maestro (Q12) |
| R-c | #13375 | El contexto del ítem lleva `origin`, `batchTenantId` y `otTransitOfficeId`; `IConsolidadoLoteAccessChecker` verifica la membresía contra `batch.tenant_id` (no el tenant del ítem); el entregador se elige por `origin` |
| R-d | #13367 | CHECK bicondicional `ck_consolidado_export_batches_ot_origin` (A5.5) |
| R-e | #13369 | Grant a `ot_admin` en el seeder (A5.4) |

### A5.7 — Notas para agentes (adenda)

- **Database Agent:** aplicar R-d sobre el DDL 133. Nada más.
- **Backend Agent:**
  - test de caracterización del `POST consolidado-maestro` antes de extraer el servicio de contexto;
  - nunca copiar `ApplyListFilters`;
  - guard Quipux solo antes de generar y con el tenant OT;
  - R-a/R-b/R-c/R-e dentro de sus HUs del motor; si alguna ya se implementó sin ellos, el ajuste aditivo va en FB.
- **Frontend Agent:** confirmación con el texto único del maestro (A4.3); el Super Admin en el hub OT envía `?transitOfficeId`.
- **QA Agent:**
  - ID de otro organismo inyectado → fuera;
  - grant revocado a mitad del lote → incluido;
  - trámite movido de organismo o borrado → «Acceso revocado»;
  - `ot_admin` sin el permiso (o con token viejo) → 403; otro rol OT sin grant → 403;
  - Super Admin desde la bandeja de un OT → lote `ot_bandeja` con `tenant_id` del OT y `ot_transit_office_id`;
  - el endpoint individual sin regresión.
- **Security Agent:**
  - primer `RequirePermission` en `AdminOtEndpoints` (AND con `OtModulePolicy`);
  - minimización de `busqueda`;
  - lote OT del Super Admin imputado al tenant OT.

## Adenda v6 (2026-10-07) — subclave por parte en FLZ1

**Decisión del usuario (HU #13372).** Cada parte del lote cifra con su propia clave, derivada con HKDF-SHA256 (RFC 5869, `System.Security.Cryptography.HKDF`):
`clave_parte = HKDF(ikm = DEK, salt = sal, info = "FLZ1" ‖ lote_id (16 B, RFC 4122 big-endian) ‖ part_number (int32 BE), L = 32)`.

- La sal tiene 32 B aleatorios, es nueva en cada intento (un reintento de la misma parte usa otra clave) y viaja en la cabecera: `"FLZ1" ‖ sal` (36 B).
- El nonce pasa a ser `0x00000000 ‖ contador (uint64)`: el prefijo aleatorio de 4 B se elimina porque ya no aporta nada.
- El AAD de cada bloque no cambia.
- La subclave vive en un búfer que se pone a cero en un `finally`, igual que la DEK, que sigue sin salir de `ConsolidadoLoteCipher`.
- Una cabecera truncada (sal incompleta) es `ParteCorrupta`.

**Motivo.** La unicidad del par (clave, nonce) queda garantizada por construcción entre partes y entre reintentos. Antes dependía de un prefijo aleatorio de 32 bits bajo una DEK compartida (colisión ≈ p²/2³³).

**Formato.** Se conserva el nombre FLZ1: no se había publicado ni cifrado nada. Los vectores fijos se regeneraron con una implementación independiente en Python.

## Referencias externas

- NIST SP 800-38D (GCM); construcción STREAM (Hoang, Reyhanitabar, Rogaway, Vizár, 2015) para AEAD por bloques.
- PostgreSQL `SELECT … FOR UPDATE SKIP LOCKED`.
