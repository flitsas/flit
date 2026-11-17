# Diseño: consultas del Diagnóstico Vehicular por RabbitMQ (R2: core-consultas consume)

> Generado: 2026-10-26 · architecture-agent · Estado: **Propuesto** (lo ratifica el Líder Técnico en el PR)
> ADR: [ADR-0075](../decisions/ADR-0075-diagnostico-consultas-por-rabbitmq.md). Reemplaza la decisión (a) A2 «cola durable
> en Postgres con lease» del plan v2 (§4) y enmienda la excepción A11 de `ix_cola` en ADR-0072.
> Work items: ninguno nuevo. Se trabaja sobre #13727 (motor y cola), #13730 (orquestación RUNT), #13737 (reintentos),
> #13743 (estado del lote), #13746 (reproceso) y #13876 (RUNT por Intempo en core-consultas).
> Fuentes: `.claude/state/diagnostico-rabbitmq/decisiones-usuario.md` y `docs/diagnosticos/acta-reunion-consultas-colas-y-servicios.md`.
> **Revisión 3 (2026-10-26).** Este documento reemplaza por completo la versión R1, en la que core-diagnostico
> consumía la cola y llamaba a core-consultas por gRPC. Esa versión quedó descartada por la brecha 1 del acta: con
> core-consultas caído, las celdas pasaban a ERROR a los ~35 s.
> **Revisión 4 (2026-10-26).** Aplicadas las decisiones U1 a U4 del usuario (ver la tabla de §0). La más visible es U1 =
> (b): **sin cifrado**. Los datos personales viajan en claro y solo lo mínimo, y se elimina la tarea T3 (cifrado de campo).
> **Revisión 5 (2026-10-26, HEAD `63e0fe89c`).** Consolida lo implementado (`git log 3dbb299e7..63e0fe89c`, 37 commits):
> las desviaciones respecto del diseño (§11), la revisión de Habeas Data del security-agent, que salió **BLOQUEADO**, y
> cómo se resolvió cada condición (§12, Dec. 6 a 8), el checklist del LT antes de encender (§13) y la propuesta de
> partición del PR (§7.3). **Estado: implementado, pendiente de PR y de la revisión del LT.** Las anclas `archivo:línea`
> son de `63e0fe89c`; reverificarlas antes de citarlas.

## 0. Decisiones del usuario vigentes

| # | Decisión | Dónde aplica en este diseño |
|---|---|---|
| Pedido | **Todas** las consultas del diagnóstico van por RabbitMQ: RUNT vehículo, RUNT persona (propietario y locatario), SIMIT (vehículo, propietario y locatario), RUES, avalúo e impuesto | §2 Q1 y Q5 |
| **R2** (reemplaza a R1) | **core-consultas consume** los trabajos, llama al proveedor y publica el resultado. **core-diagnostico consume el resultado** y cierra la celda. `diagnostico.consultas` sigue siendo el estado de la celda (el tablero hace polling por `version_tablero`). Si core-consultas está caído, los trabajos esperan en la cola (acta §3.2–§3.4, §4 paso 6) | Todo el documento |
| Brecha 2 | Evento v1 **`diagnostico.lote.terminado`** en `flit.diagnostico` al cerrar el lote: id, empresa y totales por estado, sin PII, publicado en la misma transacción del cierre. Por ahora sin consumidor | Q8 |
| Brecha 3 | **Cola aparte para Express**, con su propio consumidor. Los lotes van en FIFO | Q5 |
| D1 | Opción `Concurrencia` en `AddFlitConsumer` (SDK), con valor por defecto 1. Ahora se aplica a los consumidores de core-consultas (lotes y Express) y al de resultados de core-diagnostico | Q5, T1 |
| D2 | **Sin cupo por empresa** (FIFO del broker). Se acepta perder la equidad entre empresas. Express no compite con los lotes porque tiene cola propia | Q5 |
| D3 | El reconciliador corre cada 60 s: re-publica una vez y después cierra en ERROR (reprocesable) | Q4 y Q6 |
| D4 | Dos tandas de commits con corte directo en `feature/AB-12881-diagnostico-d0-d2`. **A**: todo lo que no enciende consumidores. **B**: encender consumidores y borrar el trabajador de Postgres | §7 y §9 |
| Sin work items | Commits a nombre de las HUs existentes | §7 |
| **U1 = b** | Datos personales **en claro, solo lo mínimo**. A la ida, el `*Request` lleva únicamente lo necesario para consultar: placa o VIN, y tipo y número de documento cuando la fuente lo exige. **Nunca** nombres ni direcciones, ni a la ida ni a la vuelta. **Sin cifrado AES-GCM.** Riesgo aceptado: quien tenga usuario en RabbitMQ puede leer placa, VIN, documentos y el resultado minimizado. Retención (completada tras la revisión de seguridad): `.dlq` 7 días (`dlq-7d`), colas principales 24 h (`ttl-*`, Dec. 6) y outbox de Postgres 24 h tras publicarse (Dec. 7). Registro formal del riesgo en ADR-0075 | Q1, Q2, §10, §12 |
| **U2 = a** | Los reintentos los decide **diagnóstico** (`PoliticaReintentos` 5, 10 y 20 s; re-publica una vez y luego ERROR, según D3). core-consultas consulta **una sola vez** y devuelve OK, o ERROR con el código gRPC. **Efectivo:** 5, 15 y 35 s desde el resultado (§11, fila 2) | Q4 |
| **U3 = a** | La simulación de LOCAL y DEV vive en **core-consultas** (tanda B) | Q6, T10 |
| **U4 = a** | Reconciliador con margen de 30 min y **pausa** si consultas no devuelve resultados en 10 min. Mientras core-consultas esté caído, no duplica trabajos ni pasa celdas a ERROR. **Completada por Dec. 6** (marca de agua FIFO y TTL) | Q6 |
| **Dec. 5 = a** | `diagnostico.lote.terminado` se emite en los **tres** caminos de cierre del lote (automático, `CierrePanel` → INCOMPLETO y `LlenadoManual`), desde el punto común de la transición y en la misma transacción | Q8 |
| **Dec. 6 = a** | Corrige un defecto del reconciliador (republicaba o cerraba celdas que solo esperaban turno en un lote grande): **marca de agua FIFO por cola** (lote y Express) a partir del UUIDv7 del `trabajo_id`, con `ToleranciaOrden` de 15 min. Las 3 colas principales con **TTL de 24 h** por política, que manda lo vencido a la `.dlq`, y `Reconciliador:TtlTrabajo` = 24 h configurable. Pasados TTL + margen, la celda cierra en ERROR reprocesable aunque consultas esté caído (C4) | Q6 (6r-i), §12 |
| **Dec. 7 = a** | Purga **opt-in** de la outbox en el SDK (`Platform:Messaging:RetencionPublicados`, apagada por defecto). Encendida en 24 h en core-diagnostico y core-consultas. Enmienda el control R-2 de ADR-0072 (C1) | §12, ADR-0075 |
| **Dec. 8 = a** | `json-intempo.txt` (muestra real con PII aparente) se movió **sin leerlo** a `context/muestras/` (gitignored) y el plan v2 se actualizó (C2) | §12 |

## 1. Contexto y hechos

**Antes de este cambio** (hasta `3dbb299e7`; el estado actual está en §11). El trabajador de core-diagnostico (`TrabajadorConsultas` → `ProcesadorConsultas`) hace polling de
`diagnostico.consultas` con `FOR UPDATE SKIP LOCKED` y llama a core-consultas por gRPC. Para eso usa `ClienteConsultas`
y `Fuente*Real`, con token `platform.consultas` y la cabecera `x-flit-tenant-id`. La política de reintentos
(`PoliticaReintentos`: 5, 10 y 20 s; `MaxReintentos` 0..3) y la orquestación RUNT → demás (`OrquestadorVehiculo`) viven
en diagnóstico.

**Hechos verificados que condicionan el diseño:**

| # | Hecho | Fuente | Consecuencia |
|---|---|---|---|
| H1 | La decisión (a) A2 **no está versionada como ADR**. Vive en el plan v2 §4, y ADR-0072 la cita en su excepción A11 | `plan-implementacion-diagnostico-vehicular-v2.md:426-437,591`; `ADR-0072:149-160` | ADR-0075 (el siguiente número libre; el más alto actual es 0074) la reemplaza y enmienda ADR-0072 |
| H2 | El consumidor del SDK abre la transacción **antes** del handler y revierte todo lo que este escribió si lanza una excepción. El handler **no recibe el número de intento** | `PlatformConsumer.cs:83,205,228-260` | Lo que deba sobrevivir a un reintento se escribe en un scope propio. El «último intento» lo decide un dato persistido |
| H3 | El SDK consume **un mensaje a la vez** (`ConsumerDispatchConcurrency` = 1, RabbitMQ.Client 7.2.2). `AddHostedService` deduplica por tipo concreto, así que registrar dos veces el mismo `PlatformConsumer<TContext,THandler,TData>` no crea dos consumidores | `PlatformConsumer.cs:106-118` | D1. La cola Express necesita **otro tipo de handler** (una subclase), no una segunda llamada con los mismos tipos |
| H4 | `IPlatformOutbox.EnqueueJob` publica en el exchange del servicio destino, y el usuario del broker necesita ese destino declarado (`usuario-de-servicio.sh <svc> <clave> <destino>`). Cualquier usuario puede leer (`bind`) en cualquier `flit.*`. Nadie puede declarar ni leer colas ajenas | `PlatformOutbox.cs:9-23`; `deploy/rabbitmq/usuario-de-servicio.sh:22-48` | **Cualquier servicio puede atar una cola propia a `flit.consultas` o `flit.diagnostico` y copiar los mensajes.** Los ACL del exchange no protegen el contenido (ver Q1 y Q2) |
| H5 | Una cola la crea el SDK **cuando arranca su consumidor**. Un mensaje publicado en un exchange topic sin cola atada **se descarta** | `PlatformConsumer.cs:136-158` | Las colas de este flujo se declaran **por adelantado** en `deploy/rabbitmq/definitions.json`. Si no, un core-consultas que nunca arrancó después del despliegue haría perder los trabajos (justo lo contrario de acta §3.2) |
| H6 | core-consultas ya tiene `AddFlitOutbox<ConsultasDb>` y las tablas `outbox`/`inbox` (migración `Inicial_EsquemaConOutboxEInbox`). Publica `AvisoKyverumVerify`. **No tiene ningún `AddFlitConsumer`**. Su usuario del broker es `consultas` | brief; `Persistence/ConsultasDb.cs:20-21`; `docker-compose.prod.yml:544` | El consumidor es nuevo, pero no hace falta migración para la bandeja |
| H7 | La medición de consumo `ConsumoRecorder.MedirAsync(ServerCallContext, …)` saca el llamador con `PlatformServiceCaller.From(context)`. `PlatformServiceCaller(ClientId, TenantId)` es un `record` público. La configuración por empresa (proveedor SIMIT con `FinesProviderResolver`) usa `caller.TenantId` | `Grpc/ConsumoRecorder.cs:18-22`; `Grpc/ConsultasGrpcService.cs:85-90`; `PlatformServerInterceptors.cs:9` | Con una sobrecarga `MedirAsync(PlatformServiceCaller, …)` y los métodos `*Core` recibiendo el llamador en vez del `ServerCallContext`, el consumidor reutiliza la medición, los overrides por empresa y la cadena de proveedores tal como están |
| H8 | **Discrepancia con el brief:** en core-consultas no existe `ReutilizacionMismoDia`. Esa clase es de **core-diagnostico** (`Modulos/Consultas/ReutilizacionMismoDia.cs`). core-consultas tiene su propia caché de proveedor (`DesdeCache` en el resumen de consumo) | grep de `MismoDia` en `services/core-consultas` (0 resultados) | La reutilización del mismo día se hace en diagnóstico **antes de publicar**: si hay dato del día, se copia y no viaja ningún mensaje |
| H9 | Diagnóstico usa estas RPC: `RuntIntempoService.ConsultarPorPlaca/ConsultarPorVin` (RUNT), `ConsultarConductor` (RUNT persona), `ConsultarMultas` (SIMIT), `ConsultarRues`, `ConsultarAvaluos`, `ConsultarImpuestoVehicular` y, para el catálogo diario, `ListarDepartamentosImpuesto`. Los mapeos de respuesta (`MapperIntempo` y los demás) parten de los **mensajes proto** de respuesta | `Modulos/Consultas/Fuentes/*Real.cs`; `contracts/proto/flit/consultas/v1/*.proto` | **El contrato del bus reutiliza los mensajes proto existentes**: a la ida va el `*Request` y a la vuelta el `*Response`. Los mapeos de los dos lados no se reescriben |
| H10 | La cabecera del lote tiene `EsExpress`. `RecalculadorEstado` (#13743) recalcula el estado del lote en la transacción de cierre de cada celda, y los estados terminales son `Completado` e `Incompleto` | `CabeceraDiagnostico.cs:30`; `Tablero/RecalculadorEstado.cs:84`; `EstadoDiagnostico.cs` | Q5 (ruteo Express) y Q8 (punto de emisión del evento) |
| H11 | `servicios-plataforma.yml`, el workflow que prueba core-consultas y core-diagnostico, **no tiene servicio rabbitmq**. Con `CI=true` el fixture del SDK falla en vez de saltarse | `.github/workflows/servicios-plataforma.yml:69-82`; `MessagingFixture.cs:37-46` | T12 lo agrega |
| H12 | Los documentos de personas se guardan **cifrados** en diagnóstico (Data Protection con certificado, HU #13751). El gRPC interno de hoy va en h2c **sin TLS** dentro de la red de docker | ADR-0072 §Retención; `Program.cs` (h2c) | Hoy los identificadores ya viajan en claro por la red interna. **Lo nuevo** con el bus es que **quedan guardados** en el broker (volumen, DLQ, consola) |

## 2. Alternativas por punto (Q1–Q8)

### Q1 · Contrato de ida (diagnóstico → core-consultas)

**Datos mínimos.** core-consultas no puede leer la base de diagnóstico, así que el trabajo **tiene que llevar los
identificadores** que exige el proveedor:

| Fuente | Qué viaja en el mensaje |
|---|---|
| RUNT | placa o VIN |
| RUNT persona | tipo y número de documento |
| SIMIT | placa o documento |
| RUES | NIT |
| Avalúo | placa o VIN, modelo, cilindraje, combustible y pasajeros (salen de la RUNT) |
| Impuesto | placa y departamento |

No viajan nombres, direcciones ni nada más del vehículo.

**1.a · Forma del tipo**

| | 1a-i. Un tipo por fuente: `consultas.diagnostico.<fuente>` **(R)** | 1a-ii. Un tipo genérico `consultas.diagnostico.consultar` con la fuente en `data` | 1a-iii. Reutilizar `consultas.consulta.*` genérico para cualquier producto |
|---|---|---|---|
| Pros | La clave de ruteo lleva la fuente, así que mañana se puede separar una cola por fuente (impuesto por scraping, lento) **sin cambiar el contrato**. Un AsyncAPI tipado por fuente. Las métricas de DLQ salen por tipo | Un solo canal en AsyncAPI | Sirve para otros productos |
| Contras | 8 claves por cola (16 contando Express). Se resuelve con un binding comodín `consultas.diagnostico.*` | Aislar una fuente lenta exige cambiar el contrato | Hoy solo hay un consumidor. Sería diseño anticipado (BDUF) |
| Esfuerzo | S | S | M |

Fuentes en la clave: `runt`, `runt_propietario`, `runt_locatario`, `simit_vehiculo`, `simit_propietario`,
`simit_locatario`, `rues`, `avaluo` e `impuesto`. Express: `consultas.diagnostico-express.<fuente>`.

**Payload v1** (sobre común de ADR-0064):

```json
"data": {
  "trabajoId": "uuid",
  "consultaId": "uuid",
  "personaId": null,
  "fuente": "runt",
  "rpc": "RuntIntempoService/ConsultarPorPlaca",
  "solicitud": { "placa": "TST001" }
}
```

- `solicitud` es el **mismo mensaje proto** que hoy arma `Fuente*Real`, serializado en **proto-JSON**
  (`Google.Protobuf.JsonFormatter`) y **en claro** (U1 = b).
- Los constructores solo llenan los campos de identificación. Una prueba de contrato falla si aparece un campo de
  nombre o de dirección.
- La empresa va en el sobre (`tenantId`).
- `consultaId` y `trabajoId` sirven para correlacionar y para la idempotencia.
- `personaId` (agregado al implementar): en las celdas de persona (RUNT persona y SIMIT de propietario y locatario) hay
  **un trabajo por persona**, todos con el mismo `trabajoId`; es la persona de `personas_vehiculo`. En las demás fuentes
  va en `null`. Contrato `consultas-events.v1.yaml` 1.1.0.

**1.b · Datos personales en el bus (Ley 1581, Habeas Data)**

| | 1b-i. Cifrado de campo AES-GCM con llave compartida diagnóstico ↔ consultas (era la recomendada; descartada por el usuario) | 1b-ii. En claro con minimización + TTL de retención **(DECIDIDA, U1 = b)** | 1b-iii. Claim-check de ida (core-consultas pide la solicitud a diagnóstico por gRPC) |
|---|---|---|---|
| Pros | Por H4, cualquier servicio puede atar una cola a `flit.consultas` y copiar mensajes; el cifrado es el **único** control real sobre el contenido. La consola, el volumen y la DLQ guardan texto ilegible. El AAD `tenantId + trabajoId + fuente` **ata el cifrado a la empresa**: un sobre con otra empresa no se descifra. `System.Security.Cryptography.AesGcm` ya viene en .NET, **sin dependencia nueva** | Lo más simple. Es el mismo nivel que el gRPC en h2c de hoy (H12) | No viaja ningún dato personal |
| Contras | Hay una llave que custodiar y rotar en dos servicios (`kid` para la rotación). Para depurar hay que descifrar | Los identificadores quedan **en reposo** en el broker y la DLQ, legibles por cualquier servicio que ate una cola. Es difícil de defender ante una auditoría de Habeas Data | **Acopla core-consultas a diagnóstico**: si diagnóstico cae, consultas no puede trabajar (contradice acta §3.4). Una capacidad de plataforma llamando a un producto |
| Esfuerzo | M | S | M |
| Riesgos | Perder la llave deja mensajes en vuelo ilegibles: se cierran ERROR y se reprocesan | Hallazgo de seguridad | Rompe la independencia |

**Cómo queda con 1b-ii (U1 = b):**

- **Minimización a la ida.** Solo viajan los identificadores de la tabla anterior.
- **Minimización a la vuelta (Q2).** core-consultas **proyecta** el `*Response` a una **lista permitida** por rpc antes de
  publicar (`MinimizadorRespuesta`, corrección C3 de §12): solo viajan los campos que leen los mapeos de diagnóstico; todo
  lo demás, incluido cualquier campo que el proto agregue mañana, no viaja. La primera versión era una lista cerrada de
  campos **a vaciar** (lista negada), y la revisión de seguridad la rechazó.
- **Retención.** Tres controles, todos implementados:
  - `.dlq` de las tres colas: política `dlq-7d`, `message-ttl` de **7 días** (604800000 ms).
  - Colas principales: políticas `ttl-*`, `message-ttl` de **24 h** (86400000 ms) con DLX hacia su `.dlq` (Dec. 6).
  - Outbox de Postgres de diagnóstico y de consultas: purga de lo publicado a las **24 h** (Dec. 7).

  Las colas de reintento tienen TTL corto (segundos o minutos).
- **Acceso.** La consola del broker sigue publicada solo en `127.0.0.1` (túnel SSH). Ningún log lleva `solicitud`,
  `respuesta`, placa, VIN ni documento.
- **Riesgo aceptado por el usuario.** Por H4, quien tenga un usuario en RabbitMQ (cualquier servicio, o el administrador
  del broker) puede atar una cola y **leer placa, VIN, documentos y el resultado RUNT minimizado**.
- **Control compensatorio.** Revisión del security-agent (Habeas Data, Ley 1581) en la tanda B, **antes del PR**. Se
  hizo: salió BLOQUEADO y sus condiciones están resueltas en la rama (§12). No exigió cifrado. El registro del riesgo
  aceptado está en ADR-0075. 1b-i queda como evolución: una v2 del contrato con `solicitud`/`respuesta` cifrados, sin
  cambiar la topología.

### Q2 · Contrato de vuelta (core-consultas → diagnóstico)

| | 2a. Evento de core-consultas `consultas.resultado.diagnostico` con el resultado **minimizado y en claro** (U1 = b) **(R)** | 2b. Claim-check: core-consultas guarda el resultado y diagnóstico lo trae por gRPC con un id | 2c. Trabajo `diagnostico.consulta.resultado` (EnqueueJob hacia `flit.diagnostico`) con el resultado |
|---|---|---|---|
| Pros | **Desacople total**: diagnóstico cierra la celda aunque core-consultas se haya vuelto a caer. Lo publica core-consultas en **su propio exchange** (no necesita destino cruzado). Si mañana otro producto pide, cada uno ata `consultas.resultado.<producto>` sin tocar los permisos de consultas. Reutiliza el `*Response` proto y los mapeos de diagnóstico | Los datos personales nunca entran al broker | El trabajo va al exchange del consumidor (patrón de Notificaciones) |
| Contras | El resultado entra al broker: documentos de propietarios y locatarios, y datos del vehículo. Va sin nombres ni direcciones (minimizado) y en claro, que es el riesgo aceptado de U1 | Diagnóstico depende de core-consultas **al procesar** cada resultado. Tabla nueva, rpc nuevo y limpieza en core-consultas. Si consultas cae, los resultados se reintentan y terminan en DLQ | El usuario `consultas` necesita destino `diagnostico`, y un destino más por cada producto nuevo |
| Esfuerzo | S | L | S |
| Riesgos | Lectura por usuarios del broker (riesgo aceptado de U1) | Acoplamiento y lotes que no cierran | Crecen los permisos cruzados |

**Payload v1 del resultado:**

```json
"data": {
  "trabajoId": "uuid",
  "consultaId": "uuid",
  "personaId": null,
  "fuente": "runt",
  "estado": "OK",
  "grpcStatus": "Unavailable",
  "respuesta": { "...": "proto-JSON del *Response, ya minimizado" },
  "duracionMs": 1234,
  "desdeCache": false
}
```

- `estado` es `OK` o `ERROR`.
- `grpcStatus` solo va cuando `estado = ERROR`. Es el **mismo código** que hoy viaja en el `RpcException`, para que
  diagnóstico aplique el `TraductorErroresConsultas` que ya tiene, sin cambios.
- `respuesta` solo va cuando `estado = OK`. Es el `*Response` en proto-JSON después de pasar por
  `MinimizadorRespuesta` (proyección positiva, §12 C3): sin nombres, direcciones, contacto, texto libre del proveedor ni
  terceros.
- `personaId` repite el del trabajo.
- Nunca viaja `ex.Message` ni el detalle del proveedor.

### Q3 · Identidad, empresa, consumo y reutilización

**Quién publicó y qué empresa es.** Hoy core-consultas confía en el token de servicio (que prueba **quién llama**) y en la
cabecera `x-flit-tenant-id` que pone el llamador (la **empresa** la afirma el servicio, no el token). En el bus se puede
mantener el mismo nivel de confianza:

| | 3a. ACL del broker + `user_id` validado por el broker + lista de productores permitidos **(R)** | 3b. Solo ACL del broker | 3c. Firma HMAC del sobre |
|---|---|---|---|
| Cómo | El publicador del SDK pone `BasicProperties.UserId` = usuario de la conexión, y **RabbitMQ rechaza un `user_id` que no coincida** con el usuario autenticado. En el consumidor, la opción `ProductoresPermitidos` (por ejemplo `["diagnostico"]`) exige que `user_id == producer` del sobre y que el productor esté en la lista; si no, va directo a la DLQ como «productor no autorizado». La empresa del sobre queda en el mismo nivel de confianza que la cabecera `x-flit-tenant-id` de hoy | Solo `flit.consultas` con escritura acotada a los usuarios con destino `consultas` | Firma con una llave por productor |
| Pros | El broker autentica al publicador. Es un cambio pequeño y genérico del SDK | Cero cambios | Independiente del broker |
| Contras | Toca el SDK (publicador y consumidor) | No distingue entre los servicios que tienen permiso (Trámites también tendría destino `consultas` mañana) | Otra llave por servicio. Duplica lo que el broker ya da |
| Esfuerzo | S | — | M |

**Consumo y caché en core-consultas.** El consumidor arma `new PlatformServiceCaller("svc-" + sobre.Producer,
sobre.TenantId)` y llama a los mismos `*Core` y a una sobrecarga `ConsumoRecorder.MedirAsync(PlatformServiceCaller, …)`.
Así se conservan la **medición de consumo** (producto `diagnostico`, evento `consultas.consulta.realizada`), los
**overrides por empresa** (proveedor SIMIT con `FinesProviderResolver`) y la **caché de proveedor** (`DesdeCache`). Hay
una medición por mensaje, igual que hoy hay una por llamada gRPC.

**Reutilización del mismo día** (H8). Se queda en diagnóstico, **antes de publicar**: `PublicadorCeldas` llama a
`ReutilizacionMismoDia.BuscarAsync`. Si encuentra el dato, copia y cierra la celda en la misma transacción, sin mensaje
ni consumo. El reproceso nunca reutiliza (R-1).

### Q4 · Reintentos y caídas

**Caídas de servicio:** las colas son durables, se declaran por adelantado (H5) y los mensajes son persistentes.

| Qué se cae | Comportamiento |
|---|---|
| core-consultas | Los trabajos **esperan** en `consultas.diagnostico*` y no consumen intentos. Las celdas siguen EN_CURSO. Tope: el TTL de 24 h de la cola (Dec. 6); lo vencido pasa a la `.dlq` y el reconciliador cierra la celda en ERROR reprocesable |
| core-diagnostico | Los resultados **esperan** en `diagnostico.consultas-resultados`, con el mismo tope de 24 h |
| El broker | Las dos outbox acumulan y publican cuando vuelve |

**Errores del proveedor (servicio arriba, proveedor caído o con error):**

| | 4a. core-consultas **no reintenta**: publica el resultado ERROR con su `grpcStatus` y diagnóstico decide con `PoliticaReintentos` **(DECIDIDA, U2 = a)** | 4b. core-consultas reintenta con el SDK y publica ERROR al agotar |
|---|---|---|
| Cómo | El consumidor de resultados de diagnóstico, ante un transitorio (`RED`, `TIMEOUT`, `HTTP_5XX`) con reintentos disponibles: (1) en un **scope propio** registra el intento (`intentos_consulta`), `intentos_auto+1`, PENDIENTE y `disponible_desde`; (2) lanza `ReintentoProgramadoException`, y el SDK lleva el **resultado** a `diagnostico.consultas-resultados.retry.n`, configurado en 5, 10 y 20 s = `PoliticaReintentos.Espera(n)`; (3) cuando el mensaje vuelve, ve la celda PENDIENTE con su intento ya registrado y **publica un trabajo nuevo** (`trabajo_id` nuevo) y confirma. Al agotarse, cierra **ERROR con su `tipo_error`** | El consumidor de core-consultas lanza ante un transitorio y el SDK reintenta. En el último intento publica ERROR. Exige que el SDK le pase al handler el número de intento (cambio extra) |
| Pros | **core-consultas queda sin estado**: un mensaje produce una ejecución y un resultado. La política, los intentos auditados por ejecución (`RegistroIntentos`) y `MaxReintentos` siguen en diagnóstico **como hoy**. No hace falta tocar el SDK para exponer el intento. Cada reintento queda medido como consumo en core-consultas | Menos mensajes |
| Contras | Un reintento son dos mensajes (resultado demorado y trabajo nuevo). El retraso usa la cola de reintentos del resultado (es poco intuitivo, así que se documenta) | La política se reparte entre dos servicios. Los intentos intermedios no llegan a diagnóstico. La medición del intento fallido se revierte con la transacción (habría que medir en un scope propio) |
| Esfuerzo | M | M |

**Esperas efectivas (implementado).** El SDK cuenta los intentos **por mensaje**, y cada reintento de la celda es un
resultado nuevo. Por eso un resultado demorado siempre entra por `.retry.1` (5 s) y, si la espera de la celda
(`disponible_desde`) no venció, `ProgramadorReintentos.ReanudarAsync` le da otra vuelta por la cola siguiente
(`ProgramadorReintentos.cs:110`). Desde el resultado transitorio, el trabajo nuevo sale a los **5 s** (reintento 1),
**15 s** (5 + 10, reintento 2) o **35 s** (5 + 10 + 20, reintento 3). Con `Diagnostico:Consultas:MaxReintentos` = 1, el
valor de `appsettings.json`, solo hay un reintento a los 5 s.

**Estados de la celda con el bus.** EN_CURSO = trabajo en vuelo (publicado y sin resultado). PENDIENTE con `trabajo_id` =
reintento programado, a la espera del resultado demorado (`ConsumidorResultados.cs:79`). PENDIENTE diferida = espera a
la RUNT. PENDIENTE sin `trabajo_id` y no diferida = de antes del corte (la publica el reconciliador).

**Lo que nunca muere en silencio:**

- Una excepción inesperada en el consumidor de core-consultas se atrapa y se publica como resultado ERROR con
  `grpcStatus = Internal`, que es definitivo (`EjecutorTrabajo.cs:83`).
- Una ejecución que pasa de `Consultas:Diagnostico:TiempoMaximoSegundos` (90 s, el deadline que diagnóstico ponía a su
  llamada gRPC) se publica como ERROR `DeadlineExceeded`, transitorio (`EjecutorTrabajo.cs:77`).
- Solo una falla de **infraestructura** de core-consultas (por ejemplo, su base caída al confirmar) agota los
  reintentos del SDK (10 s, 1 min, 10 min) y llega a `consultas.diagnostico*.dlq`. Ahí suena la alerta (`alerta-dlq.sh`)
  y la recoge el **reconciliador** de diagnóstico (D3).

### Q5 · Orden RUNT → demás, colas, Express y concurrencia

**Orden.** Sin cambios en la semántica (P2 de la versión anterior):

- Al encolar se publica la RUNT de **todos** los vehículos (D2).
- Cuando el **resultado** de la RUNT cierra la celda en diagnóstico, `OrquestadorVehiculo` libera las diferidas y las
  **publica** en la misma transacción. Los datos que necesitan el avalúo y el impuesto (modelo, cilindraje,
  departamento…) ya están en diagnóstico en ese momento.
- Si la RUNT cierra en ERROR, las demás quedan `BLOQUEADA_POR_RUNT` y no se publica nada.

**Topología** (todas declaradas también en `definitions.json`, H5; las tres con la política `ttl-*` de 24 h y sus `.dlq`
con `dlq-7d`, ambas en prioridad 10):

| Cola | Dueño | Atada a | Clave | Consumidor | Concurrencia / Prefetch |
|---|---|---|---|---|---|
| `consultas.diagnostico` | core-consultas | `flit.consultas` | `consultas.diagnostico.*` | `ConsumidorTrabajosLote` | 8 / 16 |
| `consultas.diagnostico-express` | core-consultas | `flit.consultas` | `consultas.diagnostico-express.*` | `ConsumidorTrabajosExpress` (subclase, H3) | 4 / 8 |
| `diagnostico.consultas-resultados` | core-diagnostico | `flit.consultas` | `consultas.resultado.diagnostico` | `ConsumidorResultados` | 4 / 8 |

**Cómo se marca Express.** `PublicadorCeldas` lee `CabeceraDiagnostico.EsExpress` del lote de la celda (H10) y elige el
prefijo de la clave. No hace falta ninguna columna nueva.

| | 5-i. Dos colas en core-consultas (Express aparte) **(DECIDIDA, brecha 3)** | 5-ii. Una cola con prioridad (`x-max-priority`) |
|---|---|---|
| Pros | Express no espera detrás de un lote de 200 placas aunque no haya cupo (D2). Su concurrencia se dimensiona aparte | Una sola cola |
| Contras | Dos consumidores | La prioridad solo reordena lo que todavía no se entregó: con `Prefetch` 16 un lote ya ocupa los slots. El SDK no expone prioridad |

**Pool de conexiones en core-consultas:** se usan hasta 12 transacciones del SDK abiertas durante las llamadas al
proveedor, más las del gRPC en línea. El handoff de consultas (§9.2 de `docs/suite/handoff-vps-consultas-notificaciones.md`)
recomienda subir el tope de 20 a 30, en el usuario de Postgres y en `Maximum Pool Size`.

### Q6 · Qué cambia en cada servicio

**core-diagnostico:**

- **Se queda:**
  - `diagnostico.consultas` como estado.
  - `EncoladorConsultas`, `OrquestadorVehiculo` y `Reproceso`, ahora publicando.
  - `PoliticaReintentos`, `RegistroIntentos`, `EscritorCeldas`, `RecalculadorEstado`, `ReutilizacionMismoDia` y
    `TraductorErroresConsultas`.
  - Los **mapeos** de respuesta proto → dominio (`MapperIntempo` y equivalentes).
  - `ClienteConsultas` solo para `ListarDepartamentosImpuesto` (catálogo diario, síncrono; acta §3.1).
- **Se transforma:**
  - Cada `Fuente*Real` se parte en dos piezas: `ConstructorSolicitud<fuente>`, que arma el `*Request` y es la parte de
    entrada de cada `EjecutorCelda*`, y `Mapeo<fuente>`, que convierte el `*Response` en el resultado de la celda.
  - `IEjecutorCelda` pasa de «ejecutar» a «construir solicitud / aplicar respuesta».
- **Se borra (tanda B):**
  - `TrabajadorConsultas`, `ProcesadorConsultas` (lote y toma), `OpcionesProcesador` y `ColaConsultas.TomarAsync`.
  - Las llamadas gRPC de `Fuente*Real` y los `Fuente*NoConectada`.
  - `Fuente*Simulada`, `SeleccionAdaptadores` y los fixtures, **si se acepta la opción 6-i** (ver abajo).
  - `RuntIntempoServiceClient` en diagnóstico.
  - **Implementado así.** Del código de producción salieron todos. Las `Fuente*Simulada`, `IFuente*` y dos
    `Fuente*NoConectada` siguen como **dobles de prueba** en `tests/Flit.Diagnostico.Tests/Consultas/Simulacion/`.
    `RegistroAdaptadores.EsNoConectada` devuelve siempre `false` (`RegistroAdaptadores.cs:18`). La columna `lease_hasta` y
    el índice `ix_cola` **se quedan**: la columna ya no la usa nadie (la limpian `EscritorCeldas`, `Reproceso` y
    `LlenadoManual`) y quitarla exige una migración de limpieza, que queda como deuda.
- **Se agrega:**
  - `EmpresaDelTrabajo` (accessor por scope).
  - `PublicadorCeldas`.
  - `ConsumidorResultados`.
  - `ReconciliadorConsultas` (D3).
  - El evento `diagnostico.lote.terminado`.

**core-consultas:**

- **Se agrega:**
  - `Bus/ConsumidorTrabajosDiagnostico` (base) y sus subclases `Lote` y `Express`.
  - `Bus/EjecutorTrabajo`, que según `rpc` llama al `*Core` correspondiente con el llamador del sobre.
  - `Bus/PublicadorResultado`, que hace `Enqueue` de `consultas.resultado.diagnostico`.
  - `Bus/MinimizadorRespuesta`, que proyecta cada `*Response` a la lista permitida de su rpc (U1, C3).
  - El registro de los dos `AddFlitConsumer` con `ProductoresPermitidos = ["diagnostico"]`.
- **Se refactoriza:**
  - Los `*Core` de `ConsultasGrpcService` y de `RuntIntempoGrpcService` reciben `(request, PlatformServiceCaller, ct)`
    en vez del `ServerCallContext`. Los métodos gRPC quedan como envoltorios finos.
  - `ConsumoRecorder.MedirAsync` gana una sobrecarga con el llamador.

**Simulación en local y DEV:**

| | 6-i. La simulación pasa a core-consultas (proveedor simulado por fuente, solo LOCAL y DEV) **(DECIDIDA, U3 = a)** | 6-ii. Se queda en diagnóstico como «transporte simulado» que no publica y resuelve en proceso |
|---|---|---|
| Pros | Un solo camino de punta a punta también en local y DEV: se ejercitan el bus, la minimización y los consumidores. Diagnóstico queda sin nada de proveedores (acta §5). Los fixtures RUNT ya están en proto-JSON | Menos trabajo. Diagnóstico funciona en local sin levantar core-consultas |
| Contras | En local hace falta levantar core-consultas y el broker. Hay que convertir a proto los fixtures de SIMIT, RUES, avalúo, impuesto y RUNT persona | Dos caminos: el de DEV no prueba el de QA y PDN. La simulación de proveedores sigue en el producto |
| Esfuerzo | M | S |

Antes de construir, verificar si core-consultas ya tiene un modo de prueba propio (existe `ValorMockAvaluo`) para
reutilizarlo.

**Migraciones (database-agent):**

- **core-diagnostico** `D5_ConsultasPorBus`:
  - `consultas.trabajo_id uuid NULL`: trabajo vigente. Descarta trabajos y resultados viejos o duplicados, y permite
    reconocer un resultado demorado (Q4).
  - `consultas.republicaciones smallint NOT NULL DEFAULT 0` con `CHECK 0..1` (D3).
  - Sin índice nuevo: el reconciliador usa `ix_cola`.
- **core-consultas:** sin migración. La bandeja ya existe (H6) y no se guardan resultados (2a).

**Reconciliador (D3):** `BackgroundService` cada 60 s en diagnóstico.

| | 6r-i. Margen + **pausa por señal de caída** **(DECIDIDA, U4 = a; completada por Dec. 6)** | 6r-ii. Solo margen fijo largo (por ejemplo, 6 h) |
|---|---|---|
| Cómo | Una celda es candidata si está PENDIENTE no diferida o EN_CURSO, con `trabajo_id` y más de `Margen` (30 min) sin resultado. **No reconcilia** si no llegó **ningún** resultado de core-consultas en los últimos `VentanaVida` (10 min): eso indica que consultas está caído y los trabajos **están esperando** (acta §3.2), no perdidos. Con la señal en verde: re-publica una vez (`republicaciones=1`) y, a la siguiente, cierra ERROR (con el tipo del último intento, o sin tipo como la «falla inesperada»). También publica por primera vez las PENDIENTE no diferidas con `trabajo_id NULL` (las que quedan de antes del corte) | Re-publica cualquier celda que pase de 6 h |
| Pros | No duplica trabajos ni consumo mientras consultas está caído. Detecta pérdidas reales (DLQ, mensajes descartados) en minutos | Simple |
| Contras | Una heurística más. Si hay una pérdida total y consultas está mudo, espera hasta que vuelva a fluir algún resultado | Un lote con una celda perdida queda abierto 6 h. Con consultas caído más de 6 h, se duplica todo |
| Esfuerzo | S | S |

**Defecto encontrado en la revisión y Dec. 6 (implementado en `87610e653`).** Con 6r-i tal como estaba, un lote grande
cuya cola tarda más de 30 min con consultas vivo (por ejemplo, 30.000 trabajos con concurrencia 8) quedaba republicado y
después en ERROR, aunque sus trabajos solo esperaban turno. La corrección:

- **Marca de agua FIFO por cola.** `PulsoConsultas` guarda, aparte para la cola del lote y la del Express, el instante
  de publicación del trabajo más reciente cuyo resultado volvió. Lo saca de los 48 bits altos del UUIDv7 del
  `trabajo_id` (`PulsoConsultas.cs:64`), que `PublicadorCeldas` crea con el reloj inyectado, el mismo de
  `disponible_desde` (`PublicadorCeldas.cs:102`). Una celda solo está **perdida** si su cola ya devolvió un trabajo
  publicado más de `ToleranciaOrden` (15 min) después que el suyo (`ReconciliadorConsultas.cs:177`). Si no, espera su
  turno sin que se duplique ni se cierre nada. La marca es de la réplica y vive en memoria: una réplica recién arrancada
  no tiene marca y no republica.
- **TTL en el broker.** Las 3 colas principales tienen `message-ttl` de 24 h por política (`ttl-*` en
  `deploy/rabbitmq/definitions.json`, prioridad 10, con DLX hacia su `.dlq`).
- **Cierre por vencimiento.** Con el trabajo publicado hace más de `TtlTrabajo` (24 h) + `Margen`, el mensaje ya salió a
  la `.dlq` y no puede duplicarse: la celda se cierra en ERROR reprocesable aunque consultas esté caído
  (`ReconciliadorConsultas.cs:171`, evento de log 137287).
- **Configuración.** `Diagnostico:Consultas:Reconciliador` = `Intervalo` 60 s, `Margen` 30 min, `VentanaVida` 10 min,
  `ToleranciaOrden` 15 min, `TtlTrabajo` 24 h y `Maximo` 200 (`OpcionesReconciliador.cs:37,43`). Se valida al arrancar:
  `TtlTrabajo > Margen` y `1 min ≤ ToleranciaOrden < TtlTrabajo`. El TTL de la política y `TtlTrabajo` **deben ser
  iguales**; `ReconciliadorTests.El_TTL_por_defecto_es_el_de_la_politica_del_broker_y_lo_vencido_va_a_la_DLQ` falla si
  el valor por defecto y `definitions.json` no coinciden.

### Q7 · Infraestructura

| | 7a. Destino cruzado solo diagnóstico → consultas **(R)** | 7b. Destinos cruzados en los dos sentidos (vuelta como trabajo, 2c) |
|---|---|---|
| Pros | El usuario `consultas` no cambia (publica en su propio exchange). Solo `diagnostico` se crea con destino `consultas` | Simétrico |
| Contras | Ninguno relevante | Más permisos, y crecen con cada producto |

Cambios:

- **Broker:**
  - `deploy/rabbitmq/usuario-de-servicio.sh diagnostico <clave> consultas`, recreando el usuario de diagnóstico.
  - En `definitions.json`: las 3 colas y sus bindings, la política `dlq-7d` (`message-ttl` de 604800000 ms) sobre las
    tres `.dlq` y las tres `ttl-*` (86400000 ms, DLX hacia su `.dlq`) sobre las principales, todas en prioridad 10
    (retención de U1 y Dec. 6).
  - `usuario-de-servicio.sh` rechaza los nombres reservados `flit` (su patrón abarcaría todos los `flit.*`) y `guest`,
    como servicio y como destino (B7, `5da57f840`).
  - Inventario de usuarios del broker para la revisión de Habeas Data. Quien tenga un usuario puede leer los mensajes
    (riesgo aceptado de U1).
- **Compose:** `RABBITMQ_URL_DIAGNOSTICO` pasa a obligatoria con `:?`. `RABBITMQ_URL_CONSULTAS` no cambia (B3 propone
  pasarla también a `:?`; no aplicado).
- **CI:** `servicios-plataforma.yml` suma el servicio `rabbitmq:4.1-management-alpine` (usuario `flit`/`flit-prueba`, 5672
  y 15672 publicados, healthcheck de los dos listeners), `FLIT_TEST_RABBITMQ` y `FLIT_TEST_RABBITMQ_ADMIN` (H11, C5/M5).
  Las pruebas de broker T12 usan la API de administración para crear un vhost desechable. Con `CI=true` no se saltan.
  El escaneo de secretos y de código (gitleaks y semgrep) ya corre en `.github/workflows/security-scan.yml`.
- **Local:**
  - `docker compose -f deploy/rabbitmq/docker-compose.local.yml up -d`.
  - Usuarios con `COMPOSE="docker compose -f deploy/rabbitmq/docker-compose.local.yml"`.
  - Con 6-i, core-consultas también corre en local con su simulación.

### Q8 · `diagnostico.lote.terminado` y AsyncAPI

**Punto de emisión (implementado, Dec. 5):** `RecalculadorEstado.AplicarTransicionAsync`
(`Modulos/Tablero/RecalculadorEstado.cs:82`), el punto común de la transición del lote. Por ahí pasan los **tres**
caminos de cierre:

- el automático, al cerrar la última celda (`RecalcularAsync`, `RecalculadorEstado.cs:72`);
- el cierre del panel, que deja el lote INCOMPLETO (`Modulos/Tablero/CierrePanel.cs:57`);
- el llenado manual del backoffice (`Modulos/Consultas/LlenadoManual.cs:149`).

Cuando la transición llega a `Completado` o `Incompleto`, hace `Enqueue` en la **misma transacción**. Si un reproceso o
un llenado manual reabre el lote y lo vuelve a cerrar, se emite **otra vez**. Los consumidores deduplican por
`(diagnosticoId, version)`. El diseño original solo cubría el camino automático (`TrasCerrar`).

| | 8a. Totales por estado de celda y por vehículo **(R)** | 8b. Solo id y estado (el consumidor consulta el resto) |
|---|---|---|
| Pros | Autocontenido para notificar o reportar. Sin PII | Mínimo |
| Contras | Hay que mantener los totales | Obliga a un rpc o REST de vuelta |

**Payload v1:**

```json
"data": {
  "diagnosticoId": "uuid",
  "estado": "COMPLETADO",
  "esExpress": false,
  "version": 42,
  "terminadoEn": "fecha ISO",
  "vehiculos": 200,
  "celdas": { "COMPLETADO": 1500, "ERROR": 80, "NA": 20 },
  "erroresPorTipo": { "TIMEOUT": 3 }
}
```

- `estado` es `COMPLETADO` o `INCOMPLETO`.
- `version` es el `version_tablero` en el momento del cierre.
- `erroresPorTipo` usa los códigos de `tipo_error`.
- La empresa va en el sobre. Por ahora sin consumidor.

**AsyncAPI** (los 3 contratos):

- `contracts/asyncapi/consultas-events.v1.yaml`, archivo existente:
  - canales de trabajo `consultas.diagnostico.{fuente}` y `consultas.diagnostico-express.{fuente}`;
  - evento `consultas.resultado.diagnostico`.
  - Sube a la versión menor siguiente (quedó en **1.1.0**, con `personaId` obligatorio y anulable en los dos mensajes).
- `contracts/asyncapi/diagnostico-events.v1.yaml`, archivo nuevo: `diagnostico.lote.terminado`.
- **Fixtures de contrato** compartidos en `contracts/fixtures/bus/`: un JSON por mensaje, que validan las pruebas de los
  dos servicios.

## 3. Decisión (combinación)

**1a-i + 1b-ii + 2a + 3a + 4a + 5-i + 6-i + 6r-i + 7a + 8a**, con D1–D4, U1–U4 y Dec. 5–8 vigentes:

1. Diagnóstico publica por outbox, en la misma transacción de la celda, un trabajo `consultas.diagnostico[-express].<fuente>`
   con el `*Request` proto **en claro y mínimo** (solo identificadores).
2. core-consultas lo consume con concurrencia configurable, autentica al productor por `user_id` validado, ejecuta
   **una sola vez** los `*Core` existentes con el llamador del sobre (medición, overrides y caché intactos) y publica en
   su exchange `consultas.resultado.diagnostico`: el `*Response` **minimizado** (sin nombres, direcciones ni contacto), o
   el `grpcStatus`.
3. Diagnóstico consume el resultado, aplica los mapeos y la política de reintentos de siempre, cierra la celda, libera
   las diferidas publicándolas y, al cerrar el lote, emite `diagnostico.lote.terminado`.
4. Un reconciliador con pausa por señal de caída y marca de agua FIFO por cola cubre las pérdidas reales. Las colas
   principales vencen a las 24 h y lo vencido se cierra en ERROR reprocesable (Dec. 6).
5. La outbox de los dos servicios purga lo publicado a las 24 h (Dec. 7).

**Por qué:**

- Es lo único que cumple el acta en las caídas: los trabajos esperan en la cola.
- Reutiliza casi todo: los proto, los `*Core`, la medición, la política, el orquestador y los mapeos.
- Cada servicio publica en su propio exchange o con un único destino cruzado.
- Minimiza los datos personales y los retiene poco tiempo, sin el costo de custodiar una llave (U1 = b).

**Lo que se paga:**

- Dos cambios pequeños al SDK compartido: concurrencia y `user_id` validado.
- **Riesgo aceptado:** cualquier usuario del broker puede leer placa, VIN, documentos y el resultado minimizado. Se
  compensa con los controles de §12 y queda registrado en ADR-0075 («Registro del riesgo aceptado U1»).
- La enmienda del control R-2 de ADR-0072: el documento va en claro en la outbox hasta 24 h después de publicarse.
- El refactor de `*Core` en core-consultas.
- La simulación pasa a core-consultas.

## 4. Diagrama de secuencia

```mermaid
sequenceDiagram
  autonumber
  actor U as Usuario (lote / Express)
  participant D as core-diagnostico (API + outbox)
  participant DDB as Postgres diagnostico
  participant MQ as RabbitMQ (vhost flit)
  participant C as core-consultas (ConsumidorTrabajos Lote/Express)
  participant P as Proveedor (Intempo, Verifik/SIMIT, RUES, Fasecolda, impuestos)
  participant R as ConsumidorResultados (diagnóstico)
  participant T as Tablero (polling version_tablero)

  U->>D: confirmar vehículos
  D->>DDB: TX: celdas PENDIENTE (RUNT ya, resto Diferida)<br/>¿dato del mismo día? → copia y cierra sin mensaje<br/>si no: trabajo_id + outbox EnqueueJob consultas.diagnostico[-express].runt {solicitud mínima en claro}
  D-->>MQ: OutboxPublisher → flit.consultas (user_id=diagnostico)
  Note over MQ,C: si core-consultas está caído, el trabajo ESPERA en consultas.diagnostico (cola pre-declarada)
  Note over MQ: colas principales con TTL 24 h → .dlq (dlq-7d)
  MQ->>C: entrega (Concurrencia 8 lote / 4 express)
  C->>C: user_id (o x-flit-user-id en un reenvío propio) == producer ∈ ProductoresPermitidos · parsea el *Request (proto-JSON)
  C->>P: *Core(request, caller(svc-diagnostico, tenant)) · MedirAsync en savepoint · override SIMIT por empresa · caché
  P-->>C: respuesta o error (una sola llamada, tope 90 s; sin reintentos en consultas)
  C->>MQ: TX consultas: consumo + outbox Enqueue consultas.resultado.diagnostico {OK + respuesta proyectada a la lista permitida | ERROR + grpcStatus} + bandeja → ack
  Note over MQ,R: si core-diagnostico está caído, el resultado ESPERA en diagnostico.consultas-resultados
  MQ->>R: entrega (Concurrencia 4)
  R->>R: pulso + marca de agua de su cola (UUIDv7) · Fijar(empresa del sobre) · ¿trabajo_id vigente? si no → ack sin efecto
  alt ERROR transitorio y PoliticaReintentos permite
    R->>DDB: scope propio: intento + intentos_auto+1 + PENDIENTE
    R-->>MQ: ReintentoProgramado → resultado a .retry.n (5/10/20 s)
    MQ->>R: vuelve el resultado demorado (otra vuelta si la espera no venció: efectivo 5/15/35 s)
    R->>DDB: TX: trabajo_id nuevo + outbox EnqueueJob (mismo destino) + bandeja
  else final (OK, definitivo o agotado)
    R->>DDB: TX: Mapeo<fuente>(respuesta) · EscritorCeldas.Cerrar · version_tablero
    opt la celda era RUNT
      R->>DDB: OrquestadorVehiculo: libera diferidas → trabajo_id + outbox (avalúo/impuesto con datos de la RUNT)
    end
    opt el lote quedó COMPLETADO/INCOMPLETO
      R->>DDB: outbox Enqueue diagnostico.lote.terminado {totales, sin PII}
    end
    R->>DDB: bandeja + COMMIT → ack
  end
  T->>D: GET tablero?desde=version
  Note over D: Reconciliador 60 s: perdida = > 30 min y su cola ya devolvió trabajos publicados > 15 min después (marca FIFO)<br/>con resultados en los últimos 10 min → re-publica 1 vez → ERROR · publicada hace > 24 h + 30 min → ERROR (vencida)<br/>Outbox de los dos servicios: purga de lo publicado a las 24 h
```

## 5. Contratos

- **OpenAPI `diagnostico.v1.yaml`:** sin cambios.
- **Proto:** sin cambios. El bus reutiliza los `*Request`/`*Response` existentes.
- **AsyncAPI:** Q8.
- **gRPC en línea de core-consultas:** sigue igual para Trámites y para los demás productos (acta §3.1).

## 6. Archivos a crear, modificar o borrar

La columna «T» indica la tanda (A/B).

### SDK compartido — `services/core-api/src/Flit.Platform.Sdk`

| T | Acción | Archivo | Qué |
|---|---|---|---|
| A | Modificar | `Messaging/PlatformConsumer.cs` | `PlatformConsumerOptions.Concurrencia` (ushort, por defecto 1) → `ConsumerDispatchConcurrency` (D1). `ProductoresPermitidos` (lista, vacía por defecto): si tiene elementos, exige `BasicProperties.UserId == envelope.Producer` y que el productor esté en la lista; si no se cumple, el mensaje va a la DLQ como `productor_no_autorizado` |
| A | Modificar | `Messaging/PlatformMessagingExtensions.cs` | Validar `Concurrencia >= 1` y `Prefetch >= Concurrencia` |
| A | Modificar | `Messaging/RabbitMqEventPublisher.cs` | `BasicProperties.UserId` = usuario de la URI de la conexión |
| A | Modificar | `../../tests/Flit.Platform.Sdk.Tests/Messaging/ConsumerTests.cs`, `OutboxTests.cs` | Concurrencia, productor permitido o rechazado, `user_id` puesto por el publicador |

| A | Modificar | `Messaging/PlatformConsumer.cs` (`VerificacionProductor`, `:391`) | **Desviación aceptada.** Al reenviar a `<cola>.retry.n` o a la DLQ, el consumidor no puede conservar el `user_id` ajeno (el broker lo rechaza): pone el suyo y lleva el verificado en la cabecera `x-flit-user-id`, que solo cuenta cuando el `user_id` es el del propio consumidor |
| B | Crear | `Messaging/OutboxPurgeService.cs` | Dec. 7 (C1): con `Platform:Messaging:RetencionPublicados` (`PlatformMessagingOptions.cs:32`), purga por lotes de 500 las filas ya publicadas antes de ahora − retención, al arrancar y cada hora. Nunca toca las pendientes. Sin la opción no se registra; un valor no positivo impide arrancar |
| B | Crear | `../../tests/Flit.Platform.Sdk.Tests/Messaging/OutboxRetencionTests.cs` | Purga opt-in |

Sin cifrado de campo (U1 = b): no se crean `CifradoDeCampo`, `AddFlitCifradoDeCampo` ni `CifradoDeCampoTests`.

### core-diagnostico — `services/core-diagnostico/src/Flit.Diagnostico.Api`

| T | Acción | Archivo | Qué |
|---|---|---|---|
| A | Crear | `Plataforma/EmpresaDelTrabajo.cs` | Accessor por scope que decora el HTTP (H7 de la versión anterior). `ProcesadorConsultas` ya lo usa en la tanda A |
| A | Crear | `Modulos/Consultas/Bus/TrabajoConsultaDiagnostico.cs` | Record del `data` de ida, más las claves `consultas.diagnostico[-express].<fuente>` y el mapa fuente → rpc |
| A | Crear | `Modulos/Consultas/Bus/ConstructoresSolicitud.cs` (uno por fuente o un archivo con 7 clases) | Arma el `*Request` proto con los datos de la celda y del vehículo (extraído de `EjecutorCelda*` y `Fuente*Real`). Descifra los documentos con la protección de documentos existente |
| A | Crear | `Modulos/Consultas/Bus/PublicadorCeldas.cs` | Reutilización del mismo día **solo desde la tanda B** (en A no cambia nada en ese camino), `trabajo_id`, `*Request` en proto-JSON en claro y mínimo, prefijo según Express y `EnqueueJob` |
| A | Modificar | `Modulos/Consultas/EncoladorConsultas.cs` | Publica la RUNT de **todos** los vehículos nuevos |
| A | Modificar | `Modulos/Consultas/OrquestadorVehiculo.cs` | `LiberarCeldasDelVehiculoAsync` publica las liberadas |
| A | Modificar | `Modulos/Consultas/Reproceso.cs` | `trabajo_id` nuevo y publicación |
| A | Modificar | `Modulos/Tablero/RecalculadorEstado.cs` | `Enqueue` de `diagnostico.lote.terminado` al llegar a `Completado` o `Incompleto` |
| A | Crear | `Modulos/Tablero/LoteTerminado.cs` | Record del evento y cálculo de los totales (EF dentro de la empresa) |
| A | Modificar | `Modulos/Trabajos/ProcesadorConsultas.cs` | Usa el `DiagnosticoDb` del scope con `EmpresaDelTrabajo` (para que la outbox y la liberación compartan contexto). Nada más cambia en la tanda A |
| A | Modificar | `Modulos/Consultas/CeldaConsulta.cs`, `Persistence/Configuraciones/ConsultaConfig.cs`, `Persistence/Migrations/<ts>_D5_ConsultasPorBus*` | database-agent |
| A | Modificar | `Program.cs` / `Modulos/Modulos.cs` | Registrar `EmpresaDelTrabajo` (con `Replace` del accessor), `PublicadorCeldas` y los constructores |
| B | Crear | `Modulos/Consultas/Bus/ConsumidorResultados.cs` | `IEventConsumer<ResultadoConsultaDiagnostico>`: empresa, `trabajo_id`, parseo del `*Response` (proto-JSON), `Mapeo<fuente>`, política (Q4, U2) y cierre |
| B | Crear | `Modulos/Consultas/Bus/ResultadoConsultaDiagnostico.cs`, `ReintentoProgramadoException.cs`, `OpcionesBus.cs` | Contrato de vuelta, señal de control y `Diagnostico:Consultas:Bus {ConsumidorHabilitado, Concurrencia=4, Prefetch=8}` |
| B | Crear | `Modulos/Consultas/Mapeos/*.cs` | La parte de respuesta de cada `Fuente*Real` (incluye `MapperIntempo`) |
| B | Crear | `Modulos/Trabajos/ReconciliadorConsultas.cs` | 6r-i |
| B | Modificar | `Modulos/Trabajos/ColaConsultas.cs` | Queda solo `BuscarHuerfanasAsync` (SQL entre empresas, ids). Comentario a ADR-0075 |
| B | Modificar | `Modulos/Consultas/IEjecutorCelda.cs`, `EjecutorCelda*.cs`, `EjecutorPorPersonas.cs` | Pasan a «construir solicitud / aplicar respuesta» |
| B | Borrar | `Modulos/Trabajos/TrabajadorConsultas.cs`, `Modulos/Trabajos/OpcionesProcesador.cs`, `Modulos/Trabajos/ProcesadorConsultas.cs` (su núcleo pasa a `ConsumidorResultados`) | Ya no hay polling ni llamadas |
| B | Borrar | `Modulos/Consultas/Fuentes/Fuente*Real.cs`, `Fuente*NoConectada.cs`, `Fuente*Simulada.cs`, `SeleccionAdaptadores.cs`, `Fixtures/*` (se mueven a core-consultas, U3), `IFuente*.cs` | Se conserva `ClienteConsultas` solo para `ListarDepartamentosImpuesto` |
| B | Modificar | `appsettings.json`, `appsettings.Development.json.example` | `Diagnostico:Procesador` y `Simulacion` desaparecen. Entran `Diagnostico:Consultas:Bus` y `Reconciliador {Intervalo=60s, Margen=30min, VentanaVida=10min}`; `ToleranciaOrden` (15 min) y `TtlTrabajo` (24 h) quedan con su valor por defecto en código. `Platform:Messaging:RetencionPublicados` = `1.00:00:00` (`appsettings.json:67`). `Diagnostico:Consultas:Address` y `DeadlineSegundos` se quedan para `ListarDepartamentosImpuesto` |
| B | Crear | `Modulos/Consultas/Bus/PulsoConsultas.cs`, `ProgramadorReintentos.cs`, `Modulos/Trabajos/OpcionesReconciliador.cs` | Señal de vida y marca de agua FIFO (Dec. 6), reintentos del bus (Q4) y opciones del reconciliador |

### core-consultas — `services/core-consultas/src/Flit.Consultas.Api` (todo en la tanda B)

| T | Acción | Archivo | Qué |
|---|---|---|---|
| B | Crear | `Bus/TrabajoConsultaDiagnostico.cs`, `Bus/ResultadoConsultaDiagnostico.cs` | Records espejo, validados contra `contracts/fixtures/bus/` |
| B | Crear | `Bus/ConsumidorTrabajosDiagnostico.cs` (+ `ConsumidorTrabajosLote`, `ConsumidorTrabajosExpress`) | Parsea el `*Request`, arma el llamador, `EjecutorTrabajo` y `PublicadorResultado`. **Una sola llamada**. Nunca lanza por errores del proveedor (Q4, U2) |
| B | Crear | `Bus/EjecutorTrabajo.cs` | Despacha `rpc` → `*Core(request, caller, ct)` y convierte `RpcException` en `grpcStatus` |
| B | Crear | `Bus/MinimizadorRespuesta.cs` | Proyección positiva por rpc: lista permitida de campos por mensaje proto y de claves de `Campo`; lo que no está en la lista no se publica (U1, C3; §12) |
| B | Crear | `Bus/PublicadorResultado.cs` | `Enqueue("consultas.resultado.diagnostico", …)` con la respuesta minimizada en proto-JSON |
| B | Modificar | `Grpc/ConsultasGrpcService.cs`, `ConsultasGrpcService.Documentos.cs`, `RuntIntempo/RuntIntempoGrpcService.cs` | Los `*Core` reciben `PlatformServiceCaller` y `CancellationToken`, y los métodos gRPC quedan como envoltorios |
| B | Modificar | `Grpc/ConsumoRecorder.cs` | Sobrecarga `MedirAsync(PlatformServiceCaller, …)` |
| B | Crear | `Simulacion/*` (6-i) | Proveedor simulado por fuente con los fixtures proto (los RUNT se mueven desde diagnóstico), solo con `Consultas:Simulacion:Habilitada` en LOCAL o DEV |
| B | Modificar | `Program.cs`, `ServicioSettings.cs`, `appsettings*.json` | Dos `AddFlitConsumer` (`consultas.diagnostico` 8/16 y `consultas.diagnostico-express` 4/8, `ProductoresPermitidos=["diagnostico"]`) y `Consultas:Simulacion` (solo LOCAL y DEV). Implementado además: `Consultas:Diagnostico:TiempoMaximoSegundos` = 90 y `Platform:Messaging:RetencionPublicados` = `1.00:00:00` (`appsettings.json:19,37`) |
| B | Crear | `Bus/OpcionesConsumidoresDiagnostico.cs` | `Consultas:Diagnostico`: Lote 8/16, Express 4/8, `TiempoMaximo` y `ProductoresPermitidos`, que **no se puede vaciar** (vacía vuelve a `["diagnostico"]`, `:74`) |

### Pruebas

| T | Archivo | Qué |
|---|---|---|
| A | `core-diagnostico/tests/.../Plataforma/EmpresaDelTrabajoTests.cs` | Accessor |
| A | `.../Consultas/OutboxDePrueba.cs` | Lee las filas de la outbox y parsea el `data` |
| A | `.../Consultas/EncoladoTests.cs`, `BloqueoPorRuntTests.cs`, `ReprocesoTests.cs` | Publicación |
| A | `.../Consultas/ConstructoresSolicitudTests.cs` | Un `*Request` por fuente, igual al que hoy arma `Fuente*Real` |
| A | `.../Contratos/TrabajoConsultaDiagnosticoContratoTests.cs`, `LoteTerminadoContratoTests.cs` | Contra los fixtures. El trabajo **no lleva nombres ni direcciones**; el lote terminado no lleva PII |
| B | `core-consultas/tests/.../Bus/MinimizadorRespuestaTests.cs` | Cada `*Response` publicado queda sin nombres, direcciones ni contacto |
| A | `.../Tablero/LoteTerminadoTests.cs` | Emisión y re-emisión |
| A | `.../Persistencia/EsquemaD2Tests.cs` | Columnas |
| B | `.../Consultas/ResultadosTests.cs`, `ReconciliadorTests.cs`, `TomaPorTrabajoTests.cs` | Reemplazan a `ColaConcurrenciaTests` y `LeaseTests` |
| B | `.../Consultas/MotorDePrueba.cs` → `BusDePrueba` | Drena la outbox de ida, simula a consultas y entrega los resultados |
| B | `core-consultas/tests/.../Bus/*Tests.cs` | Consumidor, ejecutor, medición y overrides con el llamador del sobre, productor no autorizado y errores convertidos en `grpcStatus` |
| B | `.../Bus/ConsultasDiagnosticoBrokerTests.cs` (en los dos servicios) | Broker real: punta a punta, consultas caído (el trabajo espera y se procesa al volver), diagnóstico caído, Express no espera al lote, DLQ y reconciliador. Vhost desechable por la API de administración. En diagnóstico la topología sale de `definitions.json` (`Bus/DefinicionesBroker.cs`, B5); en consultas todavía se declara a mano, con `dlq-7d` y **sin** las `ttl-*` (B5 del lado de consultas, pendiente) |
| B | `core-api/tests/Flit.Platform.Sdk.Tests/Messaging/OutboxRetencionTests.cs`, `core-diagnostico/tests/.../Bus/RetencionOutboxTests.cs`, `core-consultas/tests/.../Bus/ConfiguracionConsumidoresTests.cs` | Purga opt-in del SDK y retención encendida en los dos servicios (Dec. 7) |
| B | `core-consultas/tests/.../Bus/MinimizadorRespuestaTests.cs` | Guarda por `Descriptor`: `Ningun_campo_del_proto_queda_sin_clasificar` (`:130`) recorre cada mensaje alcanzable del `*Response` y falla si un campo del proto no está clasificado como permitido o prohibido; con todos los campos llenos, solo sale la lista permitida (C3) |

### Contratos, infraestructura y documentación

| T | Archivo | Qué |
|---|---|---|
| A | `contracts/asyncapi/consultas-events.v1.yaml`, `contracts/asyncapi/diagnostico-events.v1.yaml` (nuevo), `contracts/asyncapi/README.md` | Q8 |
| A | `contracts/fixtures/bus/{trabajo-consulta-diagnostico,resultado-consulta-diagnostico,lote-terminado}.v1.json` | Fixtures de contrato |
| B | `deploy/rabbitmq/definitions.json` | 3 colas, bindings, la política `dlq-7d` y las tres `ttl-*` (Dec. 6) |
| B | `deploy/rabbitmq/usuario-de-servicio.sh` | Rechaza `flit` y `guest` (B7) |
| B | `docker-compose.prod.yml` | Diagnóstico con `:?` |
| B | `.env.prod.example` | Comentario de obligatoria en `RABBITMQ_URL_DIAGNOSTICO` |
| B | `.github/workflows/servicios-plataforma.yml` | Servicio `rabbitmq:4.1-management-alpine`, `FLIT_TEST_RABBITMQ` y `FLIT_TEST_RABBITMQ_ADMIN` (C5/M5) |
| B | `.github/workflows/despliegue-config.yml`, `.github/scripts/test_despliegue_diagnostico.py` | Aceptan la `RABBITMQ_URL_DIAGNOSTICO` obligatoria (`e4fb2e082`) |
| B | `docs/suite/handoff-vps-diagnostico.md`, `docs/suite/handoff-vps-consultas-notificaciones.md` | Usuario con destino, colas, `dlq-7d` sin vuelta atrás (B1), `ttl-*` (cambiarlas por ambiente, comprobarlas y volver atrás), pool y vuelta atrás |
| B | Revisión del security-agent (sin archivo de código) | Hecha: BLOQUEADO, condiciones resueltas (§12) |
| B | `docs/decisions/ADR-0072-diagnostico-aislamiento-por-tenant.md` | Enmienda A11 (el dueño pasa a ser el reconciliador), fila de aplicabilidad y nota de la enmienda de R-2 (Dec. 7) |
| B | `docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` | Nota: (a) reemplazada por ADR-0075; las muestras de Intempo apuntan a `context/muestras/` (Dec. 8) |

**Fuera de alcance:**

- La cola del ZIP (`ColaEvidencias`). Es de una sola empresa por fila y no tiene proveedor externo.
- El servicio de impuestos externo (§8, acta §3.6).
- Los cambios de UI.

## 7. Estimación (SP Fibonacci por tarea) y tandas

**Estado: implementado, pendiente de PR y de la revisión del LT.** Tandas A y B cerradas, más las correcciones de la
revisión de seguridad. Sin push.

| # | T | Tarea | HU existente | SP | Agente | Estado y commits |
|---|---|---|---|---|---|---|
| T1 | A | SDK: `Concurrencia` (D1) | #13727 | 2 | backend | ✅ `03a5d1be1` |
| T2 | A | SDK: `user_id` validado y `ProductoresPermitidos` | #13727 | 2 | backend | ✅ `ba6e139d9` (con la desviación de `x-flit-user-id`, §11) |
| T4 | A | AsyncAPI (3 contratos) y fixtures | #13727, #13743 | 2 | backend | ✅ `969f27ec3` |
| T5 | A | Migración diagnóstico `trabajo_id` y `republicaciones` | #13727 | 1 | database | ✅ `8f625d252` |
| T6 | A | Diagnóstico: `EmpresaDelTrabajo`, constructores de solicitud, `PublicadorCeldas` y publicación en encolado, liberación y reproceso | #13727, #13730, #13746 | 5 | backend | ✅ `6e35b89df`, `86c76a5ab`, `75d8705fe` |
| T7 | A | Evento `diagnostico.lote.terminado` | #13743 | 2 | backend | ✅ `63590d688`, `422fc9528` (Dec. 5) |
| T8 | B | core-consultas: consumidores Lote/Express (una sola llamada, U2), `*Core` con llamador, `MedirAsync`, `MinimizadorRespuesta` y resultado | #13876, #13727 | 8 | backend | ✅ `51f3c75ec`, `e2cd0803c`, `3e51aea60`, `0fffcb6c3` |
| T9 | B | Diagnóstico: `ConsumidorResultados`, reintentos (Q4), mapeos y borrado de trabajador, procesador y llamadas gRPC | #13727, #13737 | 8 | backend | ✅ `be1860a90`, `0f83b338a`, `8fe01eab7`, `d2d439362` |
| T10 | B | Simulación en core-consultas (6-i) y borrado de las simuladas de diagnóstico | #13727 | 5 | backend | ✅ `88bfe0d4b` |
| T11 | B | Reconciliador con pausa (D3, 6r-i) | #13727 | 3 | backend | ✅ `931875618`; rehecho con Dec. 6 en `87610e653` |
| T12 | B | Pruebas con broker real en los dos servicios y CI con rabbitmq | #13727 | 5 | backend / qa | ✅ `eff3d9329`, `0aca43189`, `042d091b9`, `ded5af1d1`; CI con la consola en `7cb910a5b` |
| T13 | B | `definitions.json` (colas y política de 7 días en las DLQ), usuarios, compose, handoffs, ADR-0072 y revisión de Habeas Data del security-agent antes del PR | #13727 | 3 | infra / architecture / security | ✅ `5a88c9238`, `b46184660`, `64dc6cb8d`, `e4fb2e082`; revisión hecha (BLOQUEADO → §12) |
| | | **Total** (A = 14 · B = 32) | | **46** | | |

T3 (cifrado de campo, 3 SP) **se elimina** por U1 = b. Los números T1–T13 se conservan para no romper las referencias.

**Correcciones de la revisión de seguridad** (trabajo no estimado en la tabla; va a nombre de las mismas HUs):

| Condición | Qué se hizo | Commits |
|---|---|---|
| C1 (Dec. 7) | Purga opt-in de la outbox en el SDK; 24 h en diagnóstico y consultas | `5ddf484ba`, `de67ee4b4`, `101230f57` |
| C2 (Dec. 8) | `json-intempo.txt` a `context/muestras/`; el plan v2 apunta ahí | `b54574d5a` |
| C3 | `MinimizadorRespuesta` como proyección positiva por rpc | `9221c813a` |
| C4 + defecto del reconciliador (Dec. 6) | `ttl-*` de 24 h en las colas principales; marca de agua FIFO y cierre por vencimiento | `15d517131`, `87610e653` |
| C5 / M5 | CI con `rabbitmq:4.1-management-alpine`, 15672 y `FLIT_TEST_RABBITMQ_ADMIN` | `7cb910a5b` |
| B1 | Handoff: sin `clear_policy` de `dlq-7d` como vuelta atrás; cómo cambiar, comprobar y revertir las `ttl-*` | `63e0fe89c` |
| B5 (diagnóstico) | Las pruebas de broker de diagnóstico leen la topología de `definitions.json` | `15d517131` |
| B7 | `usuario-de-servicio.sh` rechaza `flit` y `guest` | `5da57f840` |

**Pruebas.** Regresión combinada sobre `63e0fe89c`, con PostgreSQL y RabbitMQ reales y sin omitidas: core-diagnostico
772/772, core-consultas 271/271 y SDK 95/95 (este último con `FLIT_TEST_RABBITMQ_CONSULTAS`, así que también corren
`PermisosPorServicioTests` y `ConsumidorConUsuarioDeServicioTests`).

**Tanda A (cerrada).** Ningún consumidor nuevo se encendió y el trabajador de Postgres siguió ejecutando. Solo se
escribían filas en la outbox de diagnóstico y el evento de lote terminado. Los trabajos a `flit.consultas` se descartaban
porque la cola todavía no existía (H5). Entre A y B no se desplegó nada, y **A no se puede desplegar sin B** (§13).

**Tanda B (cerrada).** Es el corte: consumidores encendidos y trabajador de Postgres borrado.

### 7.1 Tamaño del cambio

`git diff --shortstat 3dbb299e7..63e0fe89c`: **37 commits, 209 archivos, +13.618 / −3.385** (unas 17.000 líneas). Sin
contar los documentos sin trackear (este diseño, ADR-0075 y los demás de `docs/diagnosticos/`).

| Área | Archivos | Líneas |
|---|---|---|
| SDK (`services/core-api`) | 8 | +704 / −21 |
| Contratos (`contracts/`) | 6 | +260 / −2 |
| Infra (`deploy/`, `.github/`, compose, `.env.prod.example`) | 8 | +121 / −17 |
| Documentos trackeados (`docs/`: handoffs, ADR-0072, plan v2) | 4 | +315 / −16 |
| core-consultas, código (`Grpc` + `RuntIntempo` 182/70 · `Bus` 637 · `Simulacion` 599 · resto 56) | 27 | +1.474 / −71 |
| core-consultas, pruebas | 11 | +2.479 |
| core-diagnostico, código (incluye 1.576 líneas generadas de la migración `D5_ConsultasPorBus`) | 92 | +4.176 / −2.282 |
| core-diagnostico, pruebas | 70 | +4.751 / −1.639 |

**Bloqueo previo, ajeno a este cambio.** La rama `feature/AB-12881-diagnostico-d0-d2` lleva **127 commits sobre
`develop`** (690 archivos, +98.325 / −239): D0–D5, F10 y este cambio. Nada de eso está en `develop`. Cualquier
partición de este cambio va **encima** de los PR de su base, y la base necesita su propia estrategia de partición.

### 7.2 Restricciones de orden y de despliegue

| Pieza | ¿Se puede desplegar sola? | Por qué |
|---|---|---|
| SDK | Sí | Retrocompatible: `Concurrencia` = 1, `ProductoresPermitidos` vacía y retención apagada por defecto |
| Contratos | Sí | Sin efecto en tiempo de ejecución |
| Infra (`definitions.json`, script, CI) | Sí, con el checklist de §13 | Las colas sin consumidor quedan vacías. El `:?` del compose exige `RABBITMQ_URL_DIAGNOSTICO` en **todos** los `.env` antes de cualquier despliegue |
| core-consultas | Sí, antes que diagnóstico | Sus consumidores no reciben nada hasta que diagnóstico publique. El refactor de los `*Core` toca el gRPC en línea de Trámites |
| core-diagnostico tanda A | **No** | Sin el usuario con destino `consultas`, la outbox recibe `ACCESS_REFUSED`. Con las colas declaradas y consultas encendido, el trabajador de Postgres y el bus consultarían **dos veces** |
| core-diagnostico tanda B | Solo con consultas, la infra y el broker listos | Es el corte |

Orden de merge obligatorio: **SDK → contratos → infra → core-consultas → core-diagnostico (A y B juntas) → documentos.**

### 7.3 Partición del PR (regla 9, PR ≤ 800 líneas) — **para decisión del LT**

| | P1. Pila por capa y servicio (8 PR) | P2. Un PR por frontera de despliegue (4 PR) | P3. Un solo PR con excepción formal |
|---|---|---|---|
| Cómo | 1) SDK (~725). 2) Contratos + infra + CI (~400). 3) consultas: refactor de `*Core` y `ConsumoRecorder`, sin cambio de comportamiento (~250 + pruebas `Grpc`). 4) consultas: `Bus` + sus pruebas (~3.100). 5) consultas: simulación (~600 + pruebas). 6) diagnóstico tanda A (~3.350, de ellas ~1.576 generadas). 7) diagnóstico tanda B (~8.600). 8) Documentos: diseño, ADR-0075, ADR-0072, revisión. Cada PR se arma con `cherry-pick` de los commits de su área, que ya son atómicos por servicio | 1) Plataforma: SDK + contratos + infra + CI (~1.100). 2) core-consultas entero (~4.000). 3) core-diagnostico entero (~12.800). 4) Documentos | Los 37 commits en un PR, revisados commit a commit (cada uno lleva su HU y su prueba) |
| Pros | 4 de 8 PR quedan bajo 800. Cada PR es revisable por un especialista (SDK, infra, consultas, diagnóstico). El orden de merge sigue §7.2. Si algo falla, se revierte una capa | Cada PR es una unidad que se despliega sola y coherente. Pocos PR y un orden simple. Poco trabajo de armado | Cero trabajo de partición. Refleja D4 (corte directo). `develop` nunca queda a medias |
| Contras | Los PR 4, 6 y 7 siguen pasando de 800 aunque se separen las pruebas, y separarlas dejaría `develop` sin cobertura entre merges: necesitan excepción. Entre 6 y 7, `develop` tiene diagnóstico en tanda A: si cada merge a `develop` despliega en DEV, 6 y 7 tienen que entrar el mismo día y sin despliegue en medio, o ir como un solo PR. Ocho revisiones | Los PR 2 y 3 necesitan excepción, y el 3 (~12.800) es difícil de revisar a fondo. Reversión de grano grueso | ~17.000 líneas: viola la regla 9 de lleno. La revisión humana real es improbable. Reversión de todo o nada |
| Esfuerzo | M (cherry-pick y 8 revisiones) | S | S |
| Riesgos | Reordenar commits: el PR del SDK lleva también `5ddf484ba` (retención), que en la rama va después de commits de diagnóstico. El SDK no depende de ellos, pero cada PR armado se compila y se prueba antes de abrirlo. Estado intermedio en DEV | Revisión superficial del PR de diagnóstico | Defectos que pasan la revisión |

**Recomendación: P1**, con la excepción de la regla 9 pedida solo para los PR 4, 6 y 7, y con 6 y 7 mergeados juntos
(o fundidos en uno si `develop` despliega en cada merge). Es la que mejor se acerca a la regla 9 y deja que cada
especialista revise su capa. Además, sigue el orden de despliegue de §7.2 sin estados prohibidos. P2 es la alternativa
razonable si el LT prefiere menos PR a cambio de uno muy grande. **La decisión es del LT**; nada de esto está armado.

## 8. Alineación con el acta

| Acta | Punto | Estado | Cómo lo cubre el diseño / qué queda abierto |
|---|---|---|---|
| §3.1 | Síncrono en línea por gRPC, con caché | ✅ | El gRPC de core-consultas sigue para Trámites y demás. Diagnóstico solo usa gRPC para el catálogo diario de departamentos. La caché de proveedor no cambia |
| §3.2 | Asíncrono: si el servicio está caído, el trabajo espera | ✅ | Colas durables **pre-declaradas** (H5) y mensajes persistentes. El reconciliador se pausa mientras consultas está caído (U4 = a): no duplica trabajos ni pasa celdas a ERROR |
| §3.3 | Lotes por RabbitMQ, sin bucle de llamadas directas | ✅ | Un trabajo por celda por outbox. Diagnóstico no llama a ningún proveedor |
| §3.3 | «Recibe o consulta el resultado al finalizar» | ✅ | Resultado por celda (evento) y `diagnostico.lote.terminado` (brecha 2) |
| §3.3 | Evitar la «cola en memoria» que perdía avance | ✅ | Estado en `diagnostico.consultas` + broker durable + bandeja + reconciliador |
| §3.4 | Independencia y aislamiento de fallos | ✅ | Q4: cada caída deja los mensajes esperando. Diagnóstico no depende de consultas para cerrar (2a) |
| §3.5 | Integraciones en el servicio de Consultas | ✅ | Diagnóstico pierde `Fuente*Real` y también las simuladas, que pasan a core-consultas (U3 = a) |
| §3.5 | SIMIT «directa mediante Verifik» | ⚠️ | El código elige el proveedor **por empresa** (`FinesProviderResolver` con el override de la empresa). **Ratificar con el LT** si Verifik es fijo para diagnóstico o si vale el override |
| §3.5 | Consulta «Intempo» pendiente | ⚠️ | Ya está implementada en core-consultas (`RuntIntempoService`, HU #13876) y es la fuente RUNT de diagnóstico. Falta **confirmar el nombre y el alcance** con el LT (§6.1) |
| §3.6 | Impuestos como servicio independiente; usar el actual | ❌ pendiente externo | El servicio actual **no está en el repo**. core-consultas expone `ConsultarImpuestoVehicular` y `ListarDepartamentosImpuesto`, y `Fuentes:ImpuestoDisponible=false` en los tres ambientes. Encaje propuesto: **core-consultas como fachada** hacia ese servicio. Diagnóstico ya pasa por consultas (`consultas.diagnostico.impuesto`). Pendiente: **URL, contrato, autenticación**, tiempos y si es scraping (eso afecta la concurrencia de su cola; 1a-i permite aislarla) |
| §4.1–4.2 | Carga, validación y aceptación del lote | ✅ | Sin cambios |
| §4.3 | Enviar solicitudes a RabbitMQ | ✅ | `PublicadorCeldas` (outbox, misma transacción) |
| §4.4 | El responsable toma trabajos cuando tiene capacidad | ✅ | Consumidores de core-consultas con `Concurrencia`/`Prefetch` (D1) |
| §4.5 | RUNT, SIMIT, avalúo e impuesto | ✅ / ⚠️ | Todas las fuentes. Impuesto depende de §3.6 |
| §4.6 | Consumidor no disponible → pendientes | ✅ | Q4 |
| §4.7 | Diagnóstico obtiene la respuesta o la notificación de fin | ✅ | Q2 (por celda) y Q8 (lote) |
| §4.8 | La interfaz muestra avance sin bloquear | ✅ | Tablero con polling incremental, sin cambios |
| §6.1 | Nombre y alcance de Intempo | ⚠️ | Ver §3.5 |
| §6.2 | Quién consume y quién llama a Consultas | ✅ | R2 |
| §6.3 | Cómo se informa el fin | ✅ | Resultado por celda + `diagnostico.lote.terminado` |
| §6.4 | Contratos, colas, exchanges, claves y versionado | ✅ (Propuesto) | Q1, Q2, Q5 y Q8, en AsyncAPI v1 |
| §6.5 | Reintentos, confirmaciones, tiempos, fallidos y DLQ | ✅ | Q4 (U2 = a): los reintentos los decide diagnóstico (efectivos 5, 15 y 35 s) y core-consultas consulta una sola vez, con tope de 90 s. SDK con confirmaciones del publicador y ack manual. Colas principales con TTL de 24 h, DLQ con alerta y TTL de 7 días |
| §6.6 | Idempotencia y recuperación | ✅ | Bandeja + `trabajo_id` + reconciliador |
| §6.7 | Concurrencia, prioridades y límites por lote o empresa | ⚠️ | Concurrencia ✅; prioridad Express ✅ (cola propia); límite por empresa ❌ **a propósito** (D2). Se reabre si aparece la necesidad |
| §6.8 | ¿Optimizar o reemplazar el servicio de impuestos? | ❌ fuera de alcance | Decisión posterior del LT |
| §6.9 | Alternativa al scraping | ❌ fuera de alcance | Ídem |
| §6.10 | Presentar el flujo al equipo | ⚠️ | No es técnico. Este documento y su diagrama sirven de insumo |
| §7 | Topología | ✅ | Q5 |
| §7 | Contenido de los mensajes | ✅ | Q1, Q2, Q8 |
| §7 | Garantía de entrega | ✅ | Al menos una vez + idempotencia |
| §7 | Política de caché | ⚠️ | Sin cambios: mismo día en diagnóstico y caché de proveedor en consultas. No se rediseñó |
| §7 | Persistencia del progreso | ✅ | Celda como estado |
| §7 | Callback, evento o polling | ✅ | Eventos entre servicios y polling solo en la UI |
| §7 | Observabilidad | ⚠️ | Métrica `flit.messaging.dead_lettered`, `alerta-dlq.sh` y consumo medido. Faltan tableros y alertas de profundidad de cola |
| §7 | Seguridad entre servicios | ⚠️ | ACL + `user_id` validado + productores permitidos (Q3). Datos personales en claro y solo lo mínimo, sin nombres ni direcciones (U1 = b), con retención de 24 h en colas y outbox y de 7 días en las DLQ. **Riesgo aceptado:** quien tenga usuario en RabbitMQ lee placa, VIN, documentos y el resultado minimizado. Revisión del security-agent hecha (§12); queda la ratificación del LT y la decisión sobre M4 |
| §7 | Fuentes ofrecidas por ambiente | ⚠️ | Ninguna fuente queda «no conectada»: en QA y PDN SIMIT, RUES y avalúo **pasan a ofrecerse** (§11). **Ratificar con el LT** |
| §7 | Tecnología del servicio de impuestos | ❌ | Externo (§3.6) |

## 9. Encargos de la tanda A

> **Histórico.** Así se encargó la tanda A; ya está cerrada (§7). Lo que cambió al implementarla está en §11.

Reglas comunes:

- Commits `HU<id>: …` en `feature/AB-12881-diagnostico-d0-d2`, sin push.
- Una sola compilación por tanda, rojo → verde con filtro y la regresión al final.
- Staging por archivo.
- Detener `Flit.Api` antes de compilar (bloquea `bin` del SDK).
- Ningún consumidor nuevo registrado en esta tanda.

### 9.1 database-agent (primero; T5, 1 SP, HU #13727)

| Archivo | Qué |
|---|---|
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Consultas/CeldaConsulta.cs` | `Guid? TrabajoId` y `short Republicaciones` |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Persistence/Configuraciones/ConsultaConfig.cs` | `trabajo_id uuid NULL`, `republicaciones smallint NOT NULL DEFAULT 0` y `ck_consultas_republicaciones CHECK (republicaciones BETWEEN 0 AND 1)`, con comentarios de columna que citen ADR-0075. Sin índice |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Persistence/Migrations/<ts>_D5_ConsultasPorBus.cs` (+ Designer y snapshot) | `dotnet ef migrations add D5_ConsultasPorBus` |
| `services/core-diagnostico/tests/Flit.Diagnostico.Tests/Persistencia/EsquemaD2Tests.cs` | **Rojo:** tipo, nulabilidad y defecto de las columnas, y que el CHECK rechace 2. **Verde:** la migración |
| `docs/decisions/ADR-0072-diagnostico-aislamiento-por-tenant.md` | Fila de aplicabilidad de `consultas`: las dos columnas nuevas (técnicas, sin PII) |

core-consultas no necesita migración.

Filtro: `dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter "FullyQualifiedName~EsquemaD2Tests"`.
Commit: `HU13727: columnas trabajo_id y republicaciones en diagnostico.consultas para las consultas por RabbitMQ (ADR-0075)`.

### 9.2 backend-agent (T1, T2, T4, T6 y T7; 13 SP)

**A1 · SDK `Concurrencia` (T1, HU #13727)**

- `services/core-api/src/Flit.Platform.Sdk/Messaging/PlatformConsumer.cs`: `public ushort Concurrencia { get; set; } = 1;`
  y en `StartAsync`, `ConsumerDispatchConcurrency = consumer.Concurrencia`.
- `PlatformMessagingExtensions.cs`: validar `Concurrencia >= 1` y `Prefetch >= Concurrencia`.
- `services/core-api/tests/Flit.Platform.Sdk.Tests/Messaging/ConsumerTests.cs`, **casos en rojo**:
  - con `Concurrencia=2`, dos handlers entran a la vez (barrera, 10 s);
  - con el valor por defecto, como máximo uno a la vez;
  - `Prefetch < Concurrencia` falla al registrar.

Commit: `HU13727: opción Concurrencia en AddFlitConsumer (ConsumerDispatchConcurrency, por defecto 1)`.

**A2 · SDK productor validado (T2, HU #13727)**

- `RabbitMqEventPublisher.cs`: `props.UserId` = usuario de la URI de `Platform:Messaging:ConnectionString`.
- `PlatformConsumer.cs`: `PlatformConsumerOptions.ProductoresPermitidos` (lista vacía por defecto, sin cambio de
  comportamiento). Si tiene elementos y `delivery.BasicProperties.UserId != envelope.Producer`, o el productor no está en
  la lista, el mensaje va a la DLQ con motivo `productor_no_autorizado`, sin reintentos, y suma la métrica.
- `OutboxTests.cs`, rojo: el mensaje publicado lleva `user_id` igual al usuario de la conexión.
- `ConsumerTests.cs`, rojos:
  - productor permitido → se procesa;
  - productor fuera de la lista → DLQ sin llamar al handler;
  - `user_id` distinto del `producer` → DLQ;
  - lista vacía → comportamiento de hoy.

Commit: `HU13727: el publicador del SDK pone user_id (validado por RabbitMQ) y el consumidor puede exigir productores permitidos`.

(A3, el cifrado de campo, se eliminó por U1 = b.)

**A4 · Contratos (T4, HU #13727 y #13743)**

- `contracts/asyncapi/consultas-events.v1.yaml`:
  - canales de trabajo `consultas.diagnostico.{fuente}` y `consultas.diagnostico-express.{fuente}`, con la enumeración
    de las 9 fuentes;
  - evento `consultas.resultado.diagnostico`, con `additionalProperties: false`;
  - `solicitud` y `respuesta` en proto-JSON y en claro. La descripción declara la minimización (sin nombres ni
    direcciones) y la retención de 7 días en las DLQ.
- `contracts/asyncapi/diagnostico-events.v1.yaml` (nuevo): `diagnostico.lote.terminado`.
- `contracts/asyncapi/README.md`.
- `contracts/fixtures/bus/*.v1.json`, los 3 fixtures.
- Validar con `node contracts/asyncapi/validate.cjs`.

Commit: `HU13727: AsyncAPI de los trabajos y resultados de consultas del diagnóstico y del evento diagnostico.lote.terminado`.

**A5 · Publicación en diagnóstico (T6, HU #13727 · #13730 · #13746)**

1. **Empresa.** `Plataforma/EmpresaDelTrabajo.cs` más el registro con `Replace` del accessor después de la
   autenticación de plataforma.
   - `ProcesadorConsultas.EjecutarAsync` fija la empresa y usa el `DiagnosticoDb` del scope; se borra `EmpresaDeLaCelda`.
   - Rojos en `EmpresaDelTrabajoTests`: sin nada, fijada, HTTP, `Fijar(Guid.Empty)` y una segunda empresa distinta.
   - Regresión: la aserción de empresa de `ColaConcurrenciaTests`.
2. **Constructores.** `Bus/ConstructoresSolicitud.cs`, uno por fuente, con el `*Request` extraído de `Fuente*Real` y
   `EjecutorCelda*`, sin cambiar todavía los ejecutores.
   - Rojo en `ConstructoresSolicitudTests`: para cada fuente, el `*Request` que arma el constructor es igual
     (`Equals` de proto) al que hoy envía `Fuente*Real` con el mismo vehículo, y solo trae campos de identificación
     (placa o VIN, y tipo y número de documento cuando la fuente lo exige). Los documentos se descifran del
     almacenamiento con la protección de documentos existente.
3. **Publicador.** `Bus/TrabajoConsultaDiagnostico.cs` y `Bus/PublicadorCeldas.cs`:
   - `trabajo_id = Guid.CreateVersion7()`;
   - `solicitud` = el `*Request` en proto-JSON (`JsonFormatter`), **en claro** (U1 = b);
   - la clave es `consultas.diagnostico-express.<fuente>` si `EsExpress`, y `consultas.diagnostico.<fuente>` si no;
   - `outbox.EnqueueJob(clave, 1, tenantId, data)`.
   - La reutilización del mismo día **no se toca en esta tanda**: la sigue haciendo el procesador.
   - Rojo en `TrabajoConsultaDiagnosticoContratoTests`: el `data` serializado coincide con el fixture en claves y
     formato, y **no contiene nombres ni direcciones**. Se siembra el vehículo y las personas con nombre y dirección
     conocidos, se buscan esos valores (y las claves `nombre*`/`direccion*`) en el JSON, y no deben aparecer.
4. **Encolado.** En `EncoladorConsultas`, publicar la RUNT de **todos** los vehículos creados en la llamada.
   - Rojos en `EncoladoTests`:
     - 7 vehículos → 7 trabajos `…runt` con `trabajoId == celda.trabajo_id`, y ninguna diferida publicada;
     - un lote Express → la clave lleva `-express`;
     - re-encolar no duplica;
     - un rollback no deja mensajes.
5. **Liberación.** En `OrquestadorVehiculo.LiberarCeldasDelVehiculoAsync`, publicar las liberadas.
   - Rojos en `BloqueoPorRuntTests`:
     - RUNT completada → un trabajo por cada diferida liberada, en la misma transacción;
     - RUNT en ERROR → ninguno;
     - el avalúo liberado lleva en su `*Request` el modelo y el cilindraje de la RUNT.
6. **Reproceso.** Publicar con un `trabajo_id` nuevo.
   - Rojo en `ReprocesoTests`: 1 trabajo y `trabajo_id` distinto del anterior.

Commits por paso: `HU13727:` para 1–4, `HU13730:` para 5 y `HU13746:` para 6.

**A6 · `diagnostico.lote.terminado` (T7, HU #13743)**

- `Modulos/Tablero/LoteTerminado.cs`: el record y los totales (EF dentro de la empresa).
- `RecalculadorEstado.TrasCerrar` hace el `Enqueue` cuando la transición llega a `Completado` o `Incompleto`.
- `Tablero/LoteTerminadoTests.cs`, rojos:
  - al cerrar la última celda, un evento con los totales correctos;
  - el lote sigue EN_PROCESO → ningún evento;
  - reproceso y nuevo cierre → segundo evento con una `version` mayor;
  - `LoteTerminadoContratoTests`: coincide con el fixture y sin PII (ni placas ni documentos).

Commit: `HU13743: evento diagnostico.lote.terminado (totales por estado, sin PII) en la transacción de cierre del lote`.

**Comandos esperados de la tanda A:**

```bash
dotnet build services/core-diagnostico/Flit.Diagnostico.slnx            # una vez; arrastra el SDK
dotnet test services/core-api/tests/Flit.Platform.Sdk.Tests --filter "FullyQualifiedName~ConsumerTests|FullyQualifiedName~OutboxTests"
dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter "FullyQualifiedName~EmpresaDelTrabajoTests|FullyQualifiedName~ConstructoresSolicitudTests|FullyQualifiedName~ContratoTests|FullyQualifiedName~EncoladoTests|FullyQualifiedName~BloqueoPorRuntTests|FullyQualifiedName~ReprocesoTests|FullyQualifiedName~LoteTerminadoTests|FullyQualifiedName~ColaConcurrenciaTests|FullyQualifiedName~EsquemaD2Tests"
node contracts/asyncapi/validate.cjs
# Regresión al final, una vez (el motor de Postgres sigue igual):
dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter "FullyQualifiedName~Consultas|FullyQualifiedName~Tablero|FullyQualifiedName~Aislamiento|FullyQualifiedName~Contratos|FullyQualifiedName~Plataforma"
dotnet test services/core-consultas/tests --filter "FullyQualifiedName~Grpc"   # el SDK cambió: core-consultas sigue verde sin tocarse
```

Las pruebas del SDK necesitan RabbitMQ: levantar `deploy/rabbitmq/docker-compose.local.yml` o definir
`FLIT_TEST_RABBITMQ`.

**Salida de la tanda A:**

- Todo lo anterior en verde.
- El motor de Postgres sin cambios de comportamiento.
- Filas de outbox en los 3 puntos de publicación, más el evento de lote.
- Ningún consumidor nuevo registrado.

## 10. Riesgos residuales

- **Datos personales en claro en el broker y en la outbox (riesgo aceptado, U1 = b).**
  - Qué queda expuesto: quien tenga un usuario en RabbitMQ (cualquier servicio con `bind` a `flit.*`, o el
    administrador del broker) puede leer placa, VIN, documentos y el resultado minimizado. Quien lea las bases
    `diagnostico` y `consultas` ve lo mismo en la outbox durante 24 h.
  - Mitigaciones: minimización a la ida y proyección positiva a la vuelta, TTL de 24 h en las colas, DLQ purgadas a
    7 días, outbox purgada a 24 h, consola solo en `127.0.0.1` y nada de esto en los logs.
  - Control: la revisión del security-agent se hizo (§12). El registro formal del riesgo está en ADR-0075 y lo acepta el
    LT al pasarlo a Aceptado.
  - Evolución posible: cifrado de campo (1b-i) en una v2 del contrato, sin cambiar la topología.
- **M4 sin aplicar.** Mientras no se restrinja la lectura por exchange, cualquier usuario de servicio puede atar una
  cola a `flit.consultas`. Es la mayor parte del riesgo de U1.
- **Textos de proveedor que siguen viajando.** `detalle_propiedad` (RUNT), `EstadoVigencia.crudo` (RUES) y
  `tipo_documento` en texto (RUNT) pasan la lista permitida porque diagnóstico los lee. Propuesta abierta: normalizarlos
  a catálogo en core-consultas (§12).
- **Vencimiento a las 24 h.** Un trabajo que espera más que el TTL (lote muy grande o consultas caído más de un día)
  sale a la `.dlq` y la celda cierra en ERROR reprocesable. Si se quitan las `ttl-*` sin subir `TtlTrabajo`, el
  reconciliador cierra en ERROR celdas cuyo trabajo sigue en la cola, y su resultado tardío se descarta con el consumo ya
  pagado (handoff de diagnóstico §2.2.1).
- **Marca de agua en memoria y por réplica.** Tras un reinicio, hasta que vuelva un resultado, la réplica no republica
  nada (lado seguro); solo cierra lo vencido por TTL.
- **Doble consumo del proveedor.** La ventana va de la respuesta al commit en core-consultas (al menos una vez). Es la
  misma de hoy.
- **Sin equidad entre empresas en los lotes** (D2, aceptado). Express queda protegido por su propia cola.
- **Refactor de `*Core` en core-consultas.** Toca el servicio que también atiende a Trámites en línea. Se mitiga con la
  regresión de `Grpc` de core-consultas y con envoltorios que no cambian el contrato.
- **Heurística del reconciliador.** Si hay una pérdida total con consultas mudo, espera a que vuelva a fluir algún
  resultado o a que venza el TTL.

## 11. Implementación real frente al diseño

Desviaciones que reportaron los agentes al implementar, verificadas contra el código en `63e0fe89c`:

| # | Tema | Diseño | Implementado | Dónde |
|---|---|---|---|---|
| 1 | Estados de la celda | PENDIENTE mientras el trabajo está en vuelo | **EN_CURSO** = trabajo en vuelo. **PENDIENTE con `trabajo_id`** = reintento programado, que espera su resultado demorado | `PublicadorCeldas.cs:102-108`; `ConsumidorResultados.cs:79` |
| 2 | Esperas de reintento | 5, 10 y 20 s | El SDK cuenta intentos por mensaje: el resultado demorado entra siempre por `.retry.1` y da vueltas hasta que vence la espera de la celda. Efectivo: **5, 15 y 35 s**. Con `MaxReintentos` = 1 (el valor de `appsettings.json`), solo 5 s | `PoliticaReintentos.cs:41-45`; `ProgramadorReintentos.cs:36,110`; `Modulos.cs:120` |
| 3 | Celdas de persona | Un trabajo por celda | `personaId` en el contrato: **un trabajo por persona**, con el mismo `trabajoId`. AsyncAPI 1.1.0 | `PublicadorCeldas.cs:117-121`; `consultas-events.v1.yaml:155` |
| 4 | `diagnostico.lote.terminado` | Desde `TrasCerrar` (cierre automático) | En los **3 caminos** (automático, `CierrePanel` → INCOMPLETO y `LlenadoManual`), desde el punto común de la transición (Dec. 5) | `RecalculadorEstado.cs:82`; `CierrePanel.cs:57`; `LlenadoManual.cs:149` |
| 5 | Tope de una ejecución en consultas | No estaba | `Consultas:Diagnostico:TiempoMaximoSegundos` = 90 → ERROR `DeadlineExceeded` (transitorio). Una excepción inesperada → `Internal` (definitivo) | `OpcionesConsumidoresDiagnostico.cs:42`; `EjecutorTrabajo.cs:77,83` |
| 6 | `ProductoresPermitidos` en consultas | `["diagnostico"]` | Además **no se puede vaciar**: una lista vacía en la configuración vuelve a `["diagnostico"]` | `OpcionesConsumidoresDiagnostico.cs:74` |
| 7 | `user_id` en los reenvíos | El broker valida el del productor | Al reenviar a `.retry.n` o a la DLQ, el consumidor pone **su propio** `user_id` (el broker rechaza uno ajeno) y guarda el verificado en `x-flit-user-id`, que solo cuenta cuando el `user_id` es el del propio consumidor. Probado contra broker real | `PlatformConsumer.cs:391-430` (`VerificacionProductor`) |
| 8 | Fuentes ofrecidas | Sin cambio | Ninguna fuente queda «no conectada»: `EsNoConectada` es siempre `false`. En QA y PDN, **SIMIT, RUES y avalúo pasan a ofrecerse** en `fuentes_disponibles`; el impuesto sigue fuera mientras la réplica de departamentos esté vacía (C3). **Para ratificar con el LT** | `RegistroAdaptadores.cs:18`; `FuentesDisponibles.cs:27-29` |
| 9 | Persona sin documento consultable | No previsto | Se resuelve **al publicar**: entra al payload en ERROR definitivo `ESTADO_NO_OK` (como antes del bus) y no deja la celda esperando al reconciliador | `EjecutorPorPersonas.cs:174`; `PublicadorCeldas.cs:113` |
| 10 | Medición de consumo en el bus | `MedirAsync` con el llamador | Mide en un **savepoint** dentro de la transacción del consumidor: si Postgres rechaza la fila, vuelve al savepoint, suelta solo sus dos entradas y el resultado se publica igual | `Grpc/ConsumoRecorder.cs:97` (`0fffcb6c3`) |
| 11 | Minimización a la vuelta | Lista cerrada de campos a vaciar | **Proyección positiva** por rpc (C3) | `Bus/MinimizadorRespuesta.cs:34-97` |
| 12 | Reconciliador | Margen + pausa | Además, marca de agua FIFO por cola y cierre por TTL (Dec. 6) | Q6 |
| 13 | Retención | `dlq-7d` | Además, `ttl-*` de 24 h (Dec. 6) y purga de la outbox a 24 h (Dec. 7) | Q1 |
| 14 | Simulación de diagnóstico | Se borra | Sale del código de producción; las `Fuente*Simulada` quedan como dobles de prueba | `tests/Flit.Diagnostico.Tests/Consultas/Simulacion/` |
| 15 | Columna `lease_hasta` | Se borra con el procesador | Se queda sin uso (deuda: migración de limpieza). `HasComment` de `ConsultaConfig` y la documentación de `EstadoCelda` y `DisparadorIntento` todavía hablan del procesador | `CeldaConsulta.cs:57` |

**Diagnóstico ya no llama por gRPC a las fuentes.** El único rpc que le queda hacia core-consultas es
`ListarDepartamentosImpuesto` (catálogo diario, global), por `ClienteConsultas`
(`Modulos/Consultas/Fuentes/ClienteConsultas.cs`), con `Diagnostico:Consultas:Address` y `DeadlineSegundos`.

**No cubierto por T12:** los dos servicios reales en un mismo proceso (cada lado usa un doble del otro), el ACL real del
usuario `consultas` al publicar resultados y, en core-consultas, la topología leída de `definitions.json` (B5 de ese lado).

## 12. Revisión de Habeas Data del security-agent (Ley 1581)

**Resultado: BLOQUEADO** sobre `3dbb299e7..ded5af1d1`. Dos condiciones bloqueantes (C1, C2) y tres condiciones para el
PR (C3 a C5), más hallazgos M y B. Todas las condiciones están resueltas en la rama. Falta la **re-revisión** del
security-agent sobre `63e0fe89c`.

| # | Hallazgo | Resolución | Commits |
|---|---|---|---|
| C1 | La outbox de Postgres guardaba para siempre los payloads ya publicados, con documentos en claro. Contradice el control R-2 de ADR-0072 («el documento nunca va en eventos del outbox») | **Dec. 7.** Purga opt-in en el SDK (`Platform:Messaging:RetencionPublicados`, apagada por defecto; `OutboxPurgeService` borra por lotes lo publicado antes de ahora − retención, nunca lo pendiente). **24 h** en core-diagnostico y core-consultas (`appsettings.json:67` y `:19`); apagada en los demás servicios. **Enmienda explícita de R-2** en ADR-0075, con nota en ADR-0072 | `5ddf484ba`, `de67ee4b4`, `101230f57` |
| C2 | `json-intempo.txt` con PII aparente dentro de `docs/` | **Dec. 8.** Movido sin leerlo a `context/muestras/` (gitignored, bloqueado en `permissions.deny`); el plan v2 apunta ahí | `b54574d5a` |
| C3 | La minimización de la vuelta era una lista negada: un campo nuevo del proto viajaría | Proyección **positiva** por rpc en `MinimizadorRespuesta`: lista permitida por mensaje y por clave de `Campo`; los mapas y los mensajes sin regla no se publican. Guarda por `Descriptor`: `MinimizadorRespuestaTests.Ningun_campo_del_proto_queda_sin_clasificar` falla si un campo del proto no está clasificado (M1/M2) | `9221c813a` |
| C4 | Sin TTL en las colas principales; y defecto del reconciliador (republicaba o cerraba celdas que solo esperaban turno) | **Dec. 6.** `ttl-*` de 24 h con DLX a la `.dlq` en las 3 colas, prioridad 10; marca de agua FIFO por cola (UUIDv7, `ToleranciaOrden` 15 min); `Reconciliador:TtlTrabajo` = 24 h configurable; pasado TTL + margen, ERROR reprocesable aunque consultas esté caído | `15d517131`, `87610e653` |
| C5 / M5 | El CI no podía correr las pruebas de broker (sin la API de administración) | `rabbitmq:4.1-management-alpine`, 15672 y `FLIT_TEST_RABBITMQ_ADMIN` en `servicios-plataforma.yml`. gitleaks y semgrep ya corren en `.github/workflows/security-scan.yml` | `7cb910a5b` |
| B1 | El handoff ofrecía `clear_policy dlq-7d` como vuelta atrás | Quitado: `dlq-7d` no tiene vuelta atrás; se cambia por PR revisado. Las `ttl-*` sí (y entonces se sube `TtlTrabajo`) | `63e0fe89c` |
| B5 | Las pruebas de broker copiaban la topología a mano | En diagnóstico la leen de `definitions.json`. **En consultas sigue copiada a mano**, con `dlq-7d` y sin `ttl-*` (pendiente menor) | `15d517131` |
| B7 | `usuario-de-servicio.sh` aceptaba `flit` y `guest` | Los rechaza, como servicio y como destino | `5da57f840` |

**Recomendaciones NO aplicadas, para decisión del LT o del usuario:**

| # | Recomendación | Efecto si no se aplica |
|---|---|---|
| M4 | Restringir la lectura por exchange por servicio (hoy `usuario-de-servicio.sh` da `read` sobre `flit.*`) | Cualquier usuario de servicio puede atar una cola a `flit.consultas` y copiar trabajos y resultados. Es la exposición principal de U1 |
| M6 | Quitar las claves de usuarios demo de `docs/diagnosticos/pruebas-validacion-diagnostico-local.md` (sin trackear) **antes de commitearlo** | Credenciales en el repo |
| B2 | Documentar que `Consultas__Diagnostico__ProductoresPermitidos` no se define en QA ni en PDN | Una lista mal puesta abriría la cola a otro productor (la lista vacía ya no es posible) |
| B3 | Pasar `RABBITMQ_URL_CONSULTAS` y `RABBITMQ_ADMIN_PASSWORD` a `:?` en el compose | Un `.env` incompleto arranca con un valor vacío en vez de fallar |
| B4 | Topic permissions para el usuario `diagnostico` (escribir solo `consultas.diagnostico*` en `flit.consultas`) | Diagnóstico podría publicar cualquier clave en `flit.consultas`, incluida `consultas.resultado.diagnostico` (la rechaza `ProductoresPermitidos` de la cola de resultados) |
| B6 | El SDK manda `ex.Message` en la cabecera `x-flit-error` al reenviar a la DLQ (`PlatformConsumer.cs:253`) | Un mensaje de excepción con datos podría quedar en la DLQ hasta 7 días |
| — | Normalizar a catálogo, en core-consultas, los textos de proveedor que siguen viajando: `detalle_propiedad`, `EstadoVigencia.crudo` y `tipo_documento` en texto | Texto libre del proveedor en el bus; hoy pasan la lista permitida porque diagnóstico los lee |

## 13. Checklist del LT antes de encender (VPS / GitOps)

| # | Paso | Cómo comprobarlo |
|---|---|---|
| 1 | **No desplegar la tanda A sin la B.** core-consultas y la infra van antes que diagnóstico, o en la misma ventana | Orden de §7.2 |
| 2 | `RABBITMQ_URL_DIAGNOSTICO` definida en **todos** los `.env` y en el sealed secret de cada ambiente (el compose la exige con `:?` aunque el perfil de diagnóstico esté apagado) | `docker compose -f docker-compose.prod.yml config` sin error |
| 3 | `rabbitmqctl import_definitions /etc/rabbitmq/definitions.json` | Las 3 colas existen antes de arrancar los servicios |
| 4 | `deploy/rabbitmq/usuario-de-servicio.sh diagnostico "$CLAVE" consultas` (con la misma clave de hoy, no hay que tocar el `.env`) | `list_permissions -p flit`: `diagnostico` escribe en `flit.consultas` |
| 5 | `rabbitmqctl list_policies -p flit`: `dlq-7d` y las tres `ttl-*` en prioridad 10, y **ninguna política manual** con mayor prioridad cuyo patrón alcance estas colas o sus `.dlq` | `list_queues -p flit name policy effective_policy_definition` muestra la `ttl-*` en cada principal y `dlq-7d` en cada `.dlq` |
| 6 | `rabbitmqctl list_bindings -p flit` de `flit.consultas`: **solo** las 3 colas del flujo (más las que ya existían para otros eventos de consultas) | Una cola desconocida atada a `flit.consultas` es una copia de los datos: se borra tras confirmarlo |
| 7 | Consola del broker publicada solo en `127.0.0.1` (túnel SSH) | `docker compose ps` / `ss -ltn` en la VPS |
| 8 | Pool de Postgres de core-consultas de 20 a 30 (usuario y `Maximum Pool Size`) | Handoff de consultas §9.2 |
| 9 | `Consultas__Diagnostico__ProductoresPermitidos` y `Consultas__Simulacion__Habilitada` **no definidos** en QA ni en PDN | `docker compose exec core-consultas env` |
| 10 | El TTL de las políticas y `Diagnostico__Consultas__Reconciliador__TtlTrabajo` **iguales** (24 h por defecto) | Log de arranque de core-diagnostico, evento 137280 (`… TTL <valor>`) |
| 11 | Inventario de usuarios del broker: los 6 de servicio y el administrador | `list_users` y `list_permissions -p flit` (ADR-0075, registro de U1) |
| 12 | Ratificar las fuentes que pasan a ofrecerse en QA y PDN (SIMIT, RUES y avalúo, §11 fila 8) y su habilitación en el contrato de cada compañía | Pantalla de contrato del backoffice |

Pasos y vuelta atrás detallados: `docs/suite/handoff-vps-diagnostico.md` (§2.2, §2.2.1, §2.3) y
`docs/suite/handoff-vps-consultas-notificaciones.md` (§9).
