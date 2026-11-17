# ADR-0084: Diagnóstico crea borradores de trámite servidor a servidor; el masivo va por RabbitMQ con consumidor propio de concurrencia acotada y crédito por compañía

**Fecha**: 2026-11-11
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15). Decisiones de producto 4.1–4.6 del usuario del 2026-11-11 (`docs/diagnosticos/plan-ajustes-diagnostico-2026-11-11.md`, Punto 4).
**Tags**: arquitectura, backend, mensajería, RabbitMQ, gRPC, idempotencia, contrapresión, Habeas Data, diagnostico, tramites
**Épica / HU**: #12679 (Derivación, en New) — HUs por crear
**Supersedes**: la decisión **(h) «Handoff firmado a Trámites», opción H1** (token opaco de un solo uso + canje `DerivacionService.CanjearContexto` + wizard con `?ctx=`), de `docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` §4 (h) y de `docs/diagnosticos/plan-pendientes-dictamen-a-flito.md` §2. Nunca se versionó como ADR (D-11 abierta). También retira la tabla `diagnostico.derivaciones` con `token_hash` de ese plan (§5), que no se llegó a migrar.
**Diseño detallado**: `docs/diagnosticos/diseno-crear-tramite-desde-diagnostico.md`

## Contexto

El usuario cerró el Punto 4: «Crear trámite» desde un vehículo con dictamen VIABLE **crea un borrador en Trámites sin abrir el wizard** (4.1), tanto para una placa (síncrono) como para una selección masiva de un lote (en segundo plano, con progreso y resumen, 4.5). El borrador nace con `Origin=diagnostico` y `ExternalRef=vehiculo_id`, el tipo del lote (o el que se elige en un lote Simple), el OT de matrícula del RUNT si está en la lista efectiva de la compañía (4.4), y queda a nombre del usuario que lanzó la acción (4.6).

Requisitos no funcionales del usuario: cola durable (alineada con ADR-0075), concurrencia acotada y configurable hacia core-api, **equidad por compañía** (un lote enorme no acapara), contrapresión ante lentitud o 429/5xx, idempotencia por `(tenant_id, external_ref)`, progreso sin golpear core-api y prueba de carga con objetivo explícito.

Hechos verificados en el código:

- core-api ya crea borradores idempotentes por referencia externa en dos caminos: `IctOrchestrationService.CreateDraftFromIct` (gRPC, `services/core-api/src/Flit.Api/Grpc/IctOrchestrationService.cs`) y el consumidor del portal (`PersonaNaturalCreador`, `services/core-api/src/Flit.Api/PersonaNatural/PersonaNaturalRadicacionHandler.cs`). La red final es el índice único parcial `uq_procedure_instances_tenant_external_ref (tenant_id, external_ref) WHERE external_ref IS NOT NULL AND deleted_at IS NULL` (`Sql/Ddl/40-ICT-procedure-external-ref.sql`). `origin` es `varchar(20)` sin CHECK.
- El OT habilitado se resuelve con `ITransitOfficeResolver.ResolveEnabledByNameAsync` / `ResolveEnabledByIdAsync`, que ya usan la lista efectiva (`IEffectiveTransitOfficeListResolver`).
- Diagnóstico ya llama a core-api por gRPC con token de servicio `svc-diagnostico` (`aud=tramites`, ADR-0070): `CatalogoTiposTramiteGrpc` y `Diagnostico:Tramites:Address` (`docker-compose.prod.yml`). El proyecto `Flit.Tramites.Grpc.Contracts` compila `contracts/proto/flit/tramites/v1/*.proto` y Diagnóstico lo referencia.
- El SDK de mensajería ofrece `AddFlitConsumer` con `Concurrencia`, `Prefetch`, `RetryDelays` y `ProductoresPermitidos` (ADR-0075 D1). `EnqueueJob` prohíbe que un servicio se deje trabajos a sí mismo: hacia la propia cola se usa un **evento** (`Enqueue`) en `flit.diagnostico`.
- ADR-0075 D2 aceptó **perder la equidad entre empresas** en las consultas. Este requisito de producto pide lo contrario para la derivación, así que no se puede copiar tal cual.

## Decisión

**Opción 1.** core-api expone un rpc nuevo e idempotente, `DerivacionTramitesService.CrearBorradorDesdeDiagnostico`, que solo atiende a `svc-diagnostico` y crea el borrador con los casos de uso que ya usan ICT y el portal. Diagnóstico es el dueño de la operación:

1. **Estado en Postgres de Diagnóstico.** `diagnostico.derivacion_operaciones` (cabecera, contadores) y `diagnostico.derivaciones` (un ítem por vehículo, índice único parcial de un ítem vivo por vehículo). El vehículo guarda `tramite_id`/`tramite_numero` al crearse.
2. **Individual síncrono.** `POST …/vehiculos/{vid}/tramite` crea la operación de un ítem y llama al rpc en la misma petición (sin cola).
3. **Masivo encolado con crédito por compañía.** `POST …/diagnosticos/{id}/tramites` inserta la operación e ítems `PENDIENTE` y, en la misma transacción, **libera** como mucho `CreditoPorCompania` ítems de esa compañía publicando por outbox el evento `diagnostico.derivacion.solicitada` (solo ids). Cada vez que un ítem cierra, su transacción libera el siguiente `PENDIENTE` de la misma compañía. Así la cola nunca tiene más de `crédito × compañías activas` mensajes y el FIFO del broker se comporta como un turno rotativo entre compañías.
4. **Consumidor propio** `diagnostico.derivaciones` con `Concurrencia` global configurable (por defecto 4) que revalida (VIABLE, sin trámite), arma la solicitud (descifra el documento del propietario solo aquí) y llama al rpc.
5. **Contrapresión.** Ante `UNAVAILABLE`, `RESOURCE_EXHAUSTED` o `DEADLINE_EXCEEDED`: un freno compartido en proceso (tasa máxima con `TokenBucketRateLimiter` + pausa tras N fallos transitorios seguidos) y reintento con espera creciente por las colas de reintento del SDK (30 s, 2 min, 10 min). Agotado, el ítem queda `FALLIDO` reintentable. core-api, además, se protege con un `ConcurrencyLimiter` en el rpc que responde `RESOURCE_EXHAUSTED` en vez de encolar.
6. **Red de seguridad.** Un reconciliador (60 s) re-publica ítems `ENVIADO` sin progreso tras 15 min y libera crédito huérfano. Es la única lectura entre compañías del módulo, en SQL crudo parametrizado y en la lista blanca de `TomaEntreEmpresasSoloEnColaTests`.
7. **Progreso** sale de los contadores de la operación en Diagnóstico; el front hace polling a Diagnóstico, nunca a core-api.

## Alternativas consideradas

### Opción 1: RabbitMQ con consumidor propio en Diagnóstico, crédito por compañía y rpc síncrono idempotente en core-api (elegida)

**Pros:**
- Un solo rpc sirve al individual (síncrono, 4.6) y al masivo: la regla de creación vive en un único lugar de core-api.
- La equidad por compañía es exacta y barata: el crédito se decide en la transacción que cierra cada ítem, sin poller ni colas por compañía.
- Ningún dato personal viaja por el broker: el evento lleva `operacionId`, `itemId` y `vehiculoId`. El documento va por gRPC interno (el mismo canal que el resto de llamadas a core-api) y se descifra solo al llamar. No hace falta una enmienda de U1 (ADR-0075) ni de R-2 (ADR-0072).
- Reutiliza el SDK (outbox, bandeja, `Concurrencia`, `RetryDelays`, `ProductoresPermitidos`), el cliente gRPC con token de servicio, el patrón de reconciliador de ADR-0075 y, en core-api, los handlers de ICT/portal y el índice único existente.
- core-api no depende de Diagnóstico (no hay llamada de vuelta) y su cambio es acotado (frente T1 pequeño).

**Cons:**
- La concurrencia «global» es por réplica de core-diagnostico (`réplicas × Concurrencia`); hoy hay una réplica por ambiente, con varias hay que dividir el valor.
- Un mensaje en reintento del SDK conserva su crédito: si core-api está caído, las compañías avanzan a la velocidad del reintento (deseado, pero un lote se ve «parado»).
- El freno es en memoria de proceso (no distribuido); con varias réplicas cada una frena por su lado.
- Más piezas en Diagnóstico (despachador, consumidor, reconciliador) que un llamado directo.

**Esfuerzo:** M (≈ 21 SP: core-api 5, Diagnóstico 8, front 5, carga/QA 3)
**Riesgos:**
- Crédito que no se libera (ítem cerrado por fuera del punto común). Se mitiga con un único punto de cierre (`CierreItemDerivacion`) y el reconciliador.
- Doble creación en la ventana entre la respuesta del rpc y el commit del ítem: la evita la idempotencia de core-api por `(tenant_id, external_ref)`; el reintento recibe el mismo trámite (`ya_existia=true`).

### Opción 2: Trabajo hacia core-api (patrón Persona Natural): core-api consume y lee los datos por gRPC a Diagnóstico

Diagnóstico deja `tramites.diagnostico.crear_borrador` (solo ids) con `EnqueueJob`; un consumidor de core-api lee el vehículo con un rpc nuevo de Diagnóstico (`DerivacionService.ObtenerDatosBorrador`), crea y publica `tramites.diagnostico.borrador_resuelto`, que Diagnóstico consume para cerrar el ítem.

**Pros:**
- Contrapresión natural: core-api trae trabajo a su propio ritmo (`Concurrencia` de su consumidor); nunca recibe más de lo que puede.
- Es el patrón ya probado del portal (`RadicacionSolicitadaConsumer`), con DLQ y reintentos del SDK.
- Sin PII en el broker (claim-check por gRPC).

**Cons:**
- El individual síncrono (4.6) necesita además un rpc directo: dos caminos de creación que mantener.
- core-api pasa a depender de Diagnóstico (llamada de vuelta) y Diagnóstico de un evento de core-api: acoplamiento en los dos sentidos y dos consumidores nuevos.
- La equidad por compañía no sale gratis: la cola de core-api es FIFO; habría que mantener igualmente el crédito en Diagnóstico o una cola por compañía.
- El cambio en core-api (otro frente, T1) es más grande: consumidor, cliente gRPC a Diagnóstico, scope nuevo y publicación del resultado.

**Esfuerzo:** L (≈ 29 SP)
**Riesgos:** caída de Diagnóstico deja a core-api reintentando lecturas; mayor superficie de cambio en un servicio de otro frente.

### Opción 3: Tabla de operaciones con trabajador por polling `FOR UPDATE SKIP LOCKED` en Diagnóstico

Como `ColaEvidencias` y `ColaRedaccion`: un `BackgroundService` toma ítems `PENDIENTE` con `SELECT … FOR UPDATE SKIP LOCKED`, eligiendo el más antiguo por compañía con tope de ítems en curso por compañía, y llama al mismo rpc de la opción 1.

**Pros:**
- La equidad se expresa en una sola consulta SQL (ventana por `tenant_id` y conteo de en curso).
- Sin broker: menos piezas de infraestructura y fácil de probar en local.
- Patrón ya presente en el mismo servicio (lease, intentos, reloj inyectado).

**Cons:**
- Contradice el criterio del acta con el LT que ADR-0075 adoptó (asíncrono por RabbitMQ) y la propia RNF «alinear con ADR-0075»; ADR-0075 sustituyó justamente la cola por polling de las consultas.
- Polling constante entre compañías (carga de base aunque no haya trabajo) y latencia de toma igual al intervalo.
- Toda la toma es SQL crudo entre compañías: amplía la excepción de ADR-0072 más allá de una lectura de reconciliación.
- El reintento con espera hay que reimplementarlo (columna `disponible_desde`), cuando el SDK ya lo da.

**Esfuerzo:** M (≈ 19 SP)
**Riesgos:** contención en el índice de la cola en picos; reabre una decisión que el LT ya cerró a favor del broker.

## Tradeoff aceptado

Se elige la opción 1: cumple las seis RNF con la mayor reutilización y el menor cambio en core-api, que es de otro frente. La opción 2 da mejor contrapresión «por construcción», pero duplica el camino de creación (individual) y acopla los dos servicios en ambos sentidos; la protección equivalente se obtiene con el `ConcurrencyLimiter` del rpc más el freno y los reintentos del consumidor. La opción 3 es la más simple, pero reabre la decisión del broker que el LT ya tomó.

A cambio se acepta: concurrencia por réplica (no distribuida), un freno en memoria de proceso, y tres piezas nuevas en Diagnóstico (despachador de crédito, consumidor y reconciliador).

## Consecuencias

### Lo que se gana
- «Crear trámite» individual y masivo, con un único rpc idempotente; doble clic, reintentos y re-entregas nunca duplican.
- Equidad por compañía y techo de carga sobre core-api configurables sin redeploy de core-api.
- Progreso y resumen sin consultas a core-api; «Ver trámite #N» sale del propio vehículo.
- Ningún dato personal nuevo en el broker ni en la outbox.

### Lo que se pierde
- La derivación al **wizard** con contexto (`?ctx=`) y el canje de un solo uso (H1): ya no se construyen. El destino `SANEAMIENTO` de la tabla del plan previo queda sin diseño (se retoma con el Punto 3 del plan de pendientes, si se necesita).
- La reutilización de la consulta RUNT de Diagnóstico en Trámites (R-14) queda **fuera de la primera entrega**: core-api hidrata el borrador con su propia consulta al abrirlo (ver «Pendientes»).

### Cambios operacionales
- **Broker** (`deploy/rabbitmq/definitions.json`): cola `diagnostico.derivaciones` (+ `.retry.1..3`, `.dlq`) atada a `flit.diagnostico` con `diagnostico.derivacion.solicitada`; políticas `ttl-*` 24 h y `dlq-7d` como ADR-0075.
- **core-api**: policy `DiagnosticoDerivacionServicio` (sub `svc-diagnostico`, `aud=tramites`, scope `platform.tramites.derivacion`); el cliente `svc-diagnostico` en core-identity gana ese scope. Config `Tramites:Derivacion:{Habilitada, ConcurrenciaMaxima}` (apagada por defecto).
- **core-diagnostico**: config `Diagnostico:Derivacion:{Habilitada, Concurrencia, Prefetch, CreditoPorCompania, TasaPorSegundo, PausaTrasFallos, PausaSegundos, PlazoRpcSegundos, MaxItemsPorOperacion, Reconciliador}`.
- **Sin cambios** en core-consultas ni en las colas de ADR-0075.

## Pendientes fuera de este ADR

- **R-14 (RUNT precalculado).** core-api ya acepta `PrecomputedVehicleConsultation` en el camino ICT. Reutilizarlo exige que Diagnóstico entregue el `vehicle_snapshot_json` en el formato de `ConsultationReply`, que hoy no guarda. Se decide en una HU aparte con el LT.
- **Canal.** El borrador queda con `channel = tramites` (no hay valor `diagnostico` en `ck_procedure_instances_channel`). Si producto quiere distinguirlo en reportes, es un cambio de DDL + OpenAPI aparte; `origin = diagnostico` ya lo distingue.
- **Objetivo de carga** (propuesto en el diseño, §9): lo ratifica el LT.

## ADRs relacionados

- [ADR-0070] — gRPC entre servicios, REST hacia afuera: el rpc nuevo sigue esta regla.
- [ADR-0072] — aislamiento por tenant: FK compuestas, empresa fijada antes de tocar el ítem; el reconciliador es la excepción acotada.
- [ADR-0075] — consultas por RabbitMQ: mismo SDK, outbox, reintentos y reconciliador. **No** se modifica su D2 (sin cupo en consultas): la equidad aplica solo a la derivación.
- [ADR-0076] — PII cifrada en Diagnóstico: el documento del propietario se descifra solo al armar la solicitud gRPC.
- [ADR-0077] — dictamen por reglas: el veredicto VIABLE es la condición de elegibilidad.

## Notas para agentes

- **Backend Agent (core-api)**: rpc en `contracts/proto/flit/tramites/v1/derivacion.proto`; reutilizar `CreateProcedureInstanceHandler`, `PatchFieldValuesHandler` e `ITransitOfficeResolver` como ICT; autoría = usuario real validado (activo, de la compañía, con `tramites.create` y Crear de la familia); nunca usuario de servicio.
- **Backend Agent (core-diagnostico)**: un único punto de cierre de ítem que actualiza contadores y libera crédito en la misma transacción; el consumidor fija la empresa del sobre antes de tocar la base (`EmpresaDelTrabajo`).
- **Database Agent**: migración EF `P4_DerivacionTramite` con el DDL de referencia del diseño §4; FK compuestas `(tenant_id, …)` RESTRICT.
- **Frontend Agent**: selección y progreso en `frontend-diagnostico`; enlace «Ver trámite #N» a `${TRAMITES_URL}/tramites/{id}?t={tenant}`; sin cambios en `frontend/`.
- **QA Agent**: prueba de carga del diseño §9 (objetivo a ratificar con el LT) + pruebas de idempotencia y equidad.
- **Security Agent**: revisar que ni el evento, ni la outbox, ni los logs lleven documento; scope nuevo y lista de clientes permitidos en core-api.
- **Infra Agent**: `definitions.json`, scope en core-identity y variables nuevas en `docker-compose.prod.yml` (apagadas por defecto).

## Referencias externas

- .NET `System.Threading.RateLimiting` (`TokenBucketRateLimiter`, `ConcurrencyLimiter`): parte del framework compartido de ASP.NET Core; **no añade dependencias**.
- gRPC status codes: `RESOURCE_EXHAUSTED`, `UNAVAILABLE`, `DEADLINE_EXCEEDED`.
