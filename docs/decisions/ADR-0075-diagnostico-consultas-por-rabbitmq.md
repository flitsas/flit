# ADR-0075: Las consultas del Diagnóstico Vehicular van por RabbitMQ; core-consultas consume los trabajos y publica el resultado, y la celda queda como estado

**Fecha**: 2026-10-26
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15). Decisiones de producto del usuario del 2026-10-26, contrastadas con el acta de reunión con el LT (`docs/diagnosticos/acta-reunion-consultas-colas-y-servicios.md`).
**Tags**: arquitectura, backend, mensajería, RabbitMQ, outbox, seguridad, Habeas Data, diagnostico, consultas
**Épica / HU**: #12881 · trabajo sobre #13727, #13730, #13737, #13743, #13746 y #13876, sin work items nuevos
**Supersedes**: la decisión **(a) A2 «cola durable por vehículo en Postgres con lease + `FOR UPDATE SKIP LOCKED`»** del plan v2 (`docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` §4). Nunca se versionó como ADR.
**Enmienda** ADR-0072 (Propuesto) en dos puntos: la excepción A11 de `ix_cola` (el dueño pasa a ser el reconciliador) y el control **R-2** de la retención de datos personales (el documento puede estar en claro en la outbox hasta 24 h después de publicarse; ver «Enmienda del control R-2 de ADR-0072»).
**Diseño**: `docs/diagnosticos/diseno-consultas-por-rabbitmq.md` (revisión 5)
**Estado de la implementación**: implementado en `feature/AB-12881-diagnostico-d0-d2` (`3dbb299e7..63e0fe89c`, 37 commits, sin push), pendiente de PR, de la re-revisión del security-agent y de la revisión del LT.

## Contexto

El motor de consultas de `core-diagnostico` hacía polling de `diagnostico.consultas` y llamaba a core-consultas por gRPC,
celda por celda. El acta con el Líder Técnico fija tres criterios:

- los lotes se procesan de forma asíncrona por RabbitMQ, sin un bucle de llamadas directas (§3.3);
- si el servicio que atiende una consulta está caído, **los trabajos esperan en la cola** (§3.2, §3.4, §4 paso 6);
- las integraciones con proveedores viven en el servicio de Consultas (§3.5).

Una primera versión de este ADR (R1) tenía a diagnóstico como consumidor de su propia cola, llamando a core-consultas.
**No cumplía el segundo criterio**: con core-consultas caído, las celdas pasaban a ERROR a los ~35 s.

Restricciones verificadas:

- **SDK.** La transacción se abre antes del handler, el handler no recibe el número de intento y se consume un mensaje a
  la vez.
- **Broker.**
  - Una cola la crea su consumidor al arrancar; si no existe, el broker descarta el mensaje.
  - Cualquier usuario puede leer (`bind`) en cualquier `flit.*`, así que los ACL no protegen el contenido.
  - Los trabajos hacia otro servicio exigen declarar el destino.
- **core-consultas.** Ya tiene outbox y bandeja, pero ningún consumidor. Su medición y sus overrides por empresa
  dependen del llamador, hoy sacado del contexto gRPC.

## Decisión

**R2: core-consultas consume y core-diagnostico cierra.**

1. **Ida.** Por cada celda ejecutable, diagnóstico publica por outbox, en la misma transacción que cambia la celda, un
   trabajo `consultas.diagnostico.<fuente>` (o `consultas.diagnostico-express.<fuente>` si el lote es Express) con
   `EnqueueJob` hacia `flit.consultas`.
   - El `data` lleva `trabajoId` (UUIDv7), `consultaId`, `personaId`, `fuente`, `rpc` y la **solicitud proto
     existente** (`*Request`) en proto-JSON, **en claro y con lo mínimo**: placa o VIN, y tipo y número de documento
     cuando la fuente lo exige. Nunca nombres ni direcciones (U1 = b).
   - En las celdas de persona se publica **un trabajo por persona**, con el mismo `trabajoId` (`personaId` distinto). Una
     persona sin documento consultable no publica: queda en ERROR `ESTADO_NO_OK` al publicar.
   - Se publica la RUNT de todos los vehículos al encolar, las demás al cerrar la RUNT, y la celda reprocesada. La celda
     queda EN_CURSO mientras el trabajo está en vuelo.
   - Si hay dato del mismo día, se copia y no se publica.
2. **Ejecución.** core-consultas consume con dos colas declaradas por adelantado en `definitions.json`:
   - `consultas.diagnostico` (lotes, FIFO, `Concurrencia` 8, `Prefetch` 16);
   - `consultas.diagnostico-express` (`Concurrencia` 4, `Prefetch` 8).

   El consumidor exige que el `user_id` validado por el broker sea el `producer` del sobre y esté en
   `ProductoresPermitidos` (`["diagnostico"]`, que la configuración no puede vaciar). Ejecuta los mismos `*Core` del gRPC
   con `PlatformServiceCaller("svc-diagnostico", tenant)`, así que conserva la medición de consumo (en un savepoint), el
   proveedor por empresa y la caché. Consulta **una sola vez** (U2 = a), con un tope de 90 s
   (`Consultas:Diagnostico:TiempoMaximoSegundos`; vencido, `DeadlineExceeded`). Una excepción inesperada se publica como
   `Internal`, definitivo.
3. **Vuelta.** core-consultas publica en **su propio exchange** el evento `consultas.resultado.diagnostico`: `OK` con el
   `*Response` **proyectado a la lista permitida de su rpc** (solo lo que leen los mapeos de diagnóstico), o `ERROR` con
   el `grpcStatus`. **No reintenta**.
4. **Cierre.** Diagnóstico consume el resultado en `diagnostico.consultas-resultados` (`Concurrencia` 4, solo del
   productor `consultas`). Aplica `TraductorErroresConsultas`, `PoliticaReintentos` y los mapeos que ya tiene.
   - Un transitorio con reintentos disponibles queda auditado, la celda pasa a PENDIENTE con su `trabajo_id`, el
     resultado espera en la cola de reintentos del SDK (5, 10 y 20 s; efectivo 5, 15 y 35 s desde el resultado) y genera
     un trabajo nuevo.
   - Al agotarse, la celda cierra **ERROR con su `tipo_error`**.
   - Al cerrar la RUNT, se publican las diferidas.
   - Al cerrar el lote (`Completado` o `Incompleto`), se emite `diagnostico.lote.terminado` (v1, totales sin PII), en la
     misma transacción, desde el punto común de la transición: cierre automático, cierre del panel y llenado manual
     (Dec. 5).
5. **Red de seguridad.** Un reconciliador de 60 s revisa las celdas con trabajo y sin resultado tras 30 min.
   - Solo están **perdidas** si su cola (lote o Express) ya devolvió un trabajo publicado más de 15 min después que el
     suyo. La marca de agua FIFO sale del UUIDv7 del `trabajo_id` (Dec. 6).
   - Una perdida se republica una vez y, a la siguiente, se cierra en ERROR reprocesable.
   - Se **pausa** cuando no llegó ningún resultado en los últimos 10 min, porque eso indica que consultas está caído y
     los trabajos están esperando: no duplica ni pasa celdas a ERROR (U4 = a).
   - Pasados `TtlTrabajo` (24 h) + margen desde la publicación, el mensaje ya venció en el broker y la celda se cierra en
     ERROR reprocesable aunque consultas esté caído (Dec. 6).
6. **Retención.** Las 3 colas principales tienen `message-ttl` de 24 h con DLX a su `.dlq` (políticas `ttl-*`); las
   `.dlq`, 7 días (`dlq-7d`); ambas en prioridad 10. La outbox de diagnóstico y la de consultas purgan lo publicado a las
   24 h (Dec. 7).
7. **SDK.** Gana tres opciones retrocompatibles: `Concurrencia` (por defecto 1), `ProductoresPermitidos` con `user_id`
   validado (en los reenvíos a reintento o DLQ, el consumidor pone su propio `user_id` y lleva el verificado en
   `x-flit-user-id`) y `RetencionPublicados` (apagada por defecto). Sin cifrado de campo (U1 = b).
8. **Simulación.** La de LOCAL y DEV vive en core-consultas (U3 = a). Diagnóstico queda sin adaptadores de proveedor y
   ya no llama por gRPC a ninguna fuente; solo conserva `ListarDepartamentosImpuesto`, el catálogo diario.

## Decisiones del usuario (2026-10-26)

| # | Decisión |
|---|---|
| Pedido | Todas las consultas del diagnóstico van por RabbitMQ |
| R2 (reemplaza a R1) | core-consultas consume los trabajos y publica el resultado. core-diagnostico consume el resultado y cierra la celda. La celda sigue siendo el estado |
| Brecha 2 | Evento v1 `diagnostico.lote.terminado` en `flit.diagnostico` al cerrar el lote, en la misma transacción, sin consumidor por ahora |
| Brecha 3 | Cola aparte para Express con su propio consumidor. Los lotes van en FIFO |
| D1 | Opción `Concurrencia` en `AddFlitConsumer` (por defecto 1). Se aplica a los consumidores de core-consultas y al de resultados |
| D2 | Sin cupo por empresa. Se acepta perder la equidad entre empresas en los lotes |
| D3 | Reconciliador cada 60 s: re-publica una vez y luego ERROR (reprocesable) |
| D4 | Dos tandas de commits con corte directo en `feature/AB-12881-diagnostico-d0-d2`. A: SDK, contratos, migración, publicación y evento de lote, sin consumidores. B: consumidores, reconciliador, simulación en consultas, infraestructura y borrado del trabajador de Postgres. **Las dos están cerradas** |
| U1 = b | Datos personales **en claro, solo lo mínimo**. Ni a la ida ni a la vuelta viajan nombres o direcciones. **Sin cifrado AES-GCM.** Riesgo aceptado: quien tenga usuario en RabbitMQ lee placa, VIN, documentos y el resultado minimizado. Retención: DLQ 7 días, colas 24 h (Dec. 6) y outbox 24 h (Dec. 7). La revisión de Habeas Data del security-agent se hizo (BLOQUEADO, condiciones resueltas). Registro formal en «Registro del riesgo aceptado U1» |
| U2 = a | Reintentos en diagnóstico (`PoliticaReintentos` 5, 10 y 20 s; efectivo 5, 15 y 35 s; con `MaxReintentos` = 1, solo 5 s). Re-publica una vez y luego ERROR, según D3. core-consultas consulta una vez y devuelve OK o ERROR con el código gRPC |
| U3 = a | La simulación de LOCAL y DEV vive en core-consultas (tanda B) |
| U4 = a | Reconciliador con margen de 30 min y pausa si consultas no devuelve resultados en 10 min. Completada por Dec. 6 |
| Dec. 5 = a | `diagnostico.lote.terminado` en los tres caminos de cierre (automático, `CierrePanel` → INCOMPLETO, `LlenadoManual`), desde el punto común de la transición |
| Dec. 6 = a | Marca de agua FIFO por cola (lote y Express) a partir del UUIDv7 del `trabajo_id`, `ToleranciaOrden` = 15 min. TTL de 24 h en las 3 colas principales por política, con DLX a la `.dlq`, y `Reconciliador:TtlTrabajo` = 24 h configurable. Pasados TTL + margen, ERROR reprocesable aunque consultas esté caído |
| Dec. 7 = a | Purga opt-in de la outbox en el SDK (`Platform:Messaging:RetencionPublicados`), apagada por defecto. 24 h en diagnóstico y consultas. Enmienda R-2 de ADR-0072 |
| Dec. 8 = a | `json-intempo.txt` movido sin leer a `context/muestras/` (gitignored) |

## Alternativas consideradas

### Opción 1: R1 — diagnóstico consume su propia cola y llama a core-consultas por gRPC

**Pros:**
- Un solo servicio que cambiar. core-consultas queda intacto.
- Sin datos personales en el broker (el mensaje solo lleva ids).
- La política y la orquestación quedan en un único lugar.

**Cons:**
- **No cumple el acta**: con core-consultas caído, los reintentos se agotan en ~35 s y las celdas quedan en ERROR.
- Diagnóstico sigue llamando a la capacidad celda por celda, ahora desde un consumidor (§3.3).
- Mantiene la conexión de base abierta durante la llamada gRPC.

**Esfuerzo:** L
**Riesgos:** lotes enteros en ERROR durante cada despliegue o caída de core-consultas.

### Opción 2: R2 con datos mínimos en claro y retención acotada (elegida, U1 = b)

**Pros:**
- Cumple §3.2–§3.4: cada caída deja los mensajes esperando y ningún servicio depende de otro para avanzar.
- Reutiliza los `*Request`/`*Response` proto, los `*Core`, la medición, los overrides por empresa, la caché, la
  política, el orquestador y los mapeos.
- Un solo destino cruzado (diagnóstico → consultas). La vuelta usa el exchange propio de consultas, que escala a otros
  productos sin permisos nuevos.
- Sin llaves que custodiar ni rotar. Los mensajes se pueden leer al depurar. Es el mismo nivel de exposición en tránsito
  que el gRPC interno en h2c de antes.

**Cons:**
- Los identificadores (placa, VIN, documentos) y el resultado minimizado quedan **en reposo y en claro** en el broker
  (hasta 24 h en las colas y 7 días en la DLQ) y en la outbox de Postgres (24 h). Cualquier usuario del broker puede atar
  una cola y leerlos.
- Tres cambios al SDK compartido.
- Refactor de los `*Core` de core-consultas, que también atiende a Trámites en línea.
- La simulación pasa a core-consultas: en local hay que levantar ese servicio y el broker.
- Un reintento son dos mensajes (resultado demorado y trabajo nuevo).
- Se pierde la equidad entre empresas en los lotes (D2).
- Un trabajo que espera más de 24 h vence y su celda cierra en ERROR reprocesable.

**Esfuerzo:** L (46 SP estimados: tanda A = 14, tanda B = 32; más las correcciones de la revisión de seguridad, no
estimadas)
**Riesgos:**
- Hallazgo de Habeas Data por los datos en claro. Se mitiga con la minimización, la retención acotada y la consola solo
  local; la revisión del security-agent se hizo y sus condiciones están resueltas.
- Doble consumo del proveedor en la ventana entre la respuesta y el commit (la misma de antes).

### Opción 3: R2 con cifrado de campo AES-GCM (llave compartida diagnóstico ↔ consultas)

**Pros:**
- Es el único control efectivo sobre el contenido cuando cualquier usuario del broker puede atar una cola a un exchange.
- El AAD `tenant|trabajo|fuente` ata el mensaje a la empresa.
- `System.Security.Cryptography.AesGcm`, sin dependencia nueva.

**Cons:**
- Una llave que custodiar, respaldar y rotar en dos servicios. Si se pierde, los mensajes en vuelo quedan ilegibles.
- Un cambio más al SDK (+3 SP). Depurar exige descifrar.

**Esfuerzo:** L (49 SP)
**Riesgos:** mala configuración de la llave entre servicios.

El claim-check (core-consultas guarda el resultado y diagnóstico lo trae por gRPC) también se evaluó y se descartó: hace
que diagnóstico dependa de consultas al cerrar cada celda, lo que contradice §3.4. Detalle en el diseño, Q2.

## Tradeoff aceptado

Se elige la opción 2. Es la que cumple el acta en caídas e independencia con la mayor reutilización. A cambio se acepta:

- tener los datos mínimos **en claro** (sin nombres ni direcciones) en el broker y en la outbox con retención acotada, en
  vez de custodiar una llave compartida. El riesgo queda registrado abajo («Registro del riesgo aceptado U1»);
- tocar el SDK compartido con tres opciones retrocompatibles (sin cambio para Notificaciones, Trámites ni core-api);
- refactorizar los `*Core` de core-consultas detrás de envoltorios gRPC que no cambian el contrato;
- soltar el límite por empresa en los lotes, mientras Express queda protegido con su propia cola.

## Enmienda del control R-2 de ADR-0072

**Qué decía R-2** (ADR-0072, «Retención de datos personales», controles compensatorios): «El documento nunca va en logs,
eventos del outbox, DTO de listado ni prompt del LLM».

**Qué cambia.** Para los trabajos y resultados de este ADR, **el documento sí va en la outbox**: en claro, en
`diagnostico.outbox` (trabajos con el documento de propietarios y locatarios, y el NIT para RUES) y en `consultas.outbox`
(resultados con los documentos que devuelve el proveedor). Se conserva **hasta 24 h después de publicarse**; la purga
del SDK lo borra (`Platform:Messaging:RetencionPublicados` = `1.00:00:00` en los dos servicios). Lo pendiente de publicar
no se purga nunca. Sigue prohibido en logs, DTO de listado y prompt del LLM, y en cualquier **otro** evento de la outbox.

**Por qué.**

- core-consultas no puede leer la base de diagnóstico. Para que los trabajos esperen en la cola cuando consultas está
  caído (acta §3.2), el mensaje tiene que llevar el identificador que el proveedor exige.
- La outbox es lo que garantiza que el trabajo sale si y solo si la celda cambió (misma transacción). Sacar el documento
  del outbox obligaría a un claim-check, que se descartó por acoplar los servicios (Q2).
- El cifrado de campo (opción 3) se descartó en U1 = b.
- La purga acota la exposición al tiempo que el mensaje necesita para publicarse, con margen para depurar un incidente
  del mismo día. Antes de esta enmienda no había ninguna purga: la outbox guardaba los payloads publicados para siempre
  (hallazgo C1 del security-agent).

**Alcance.** Solo core-diagnostico y core-consultas encienden la purga. En los demás servicios queda apagada y R-2 sigue
igual. Un servicio nuevo que ponga datos personales en su outbox necesita su propia enmienda.

## Registro del riesgo aceptado U1 (Ley 1581 de 2012)

**Decisión:** U1 = b, aceptada por el usuario el 2026-10-26. La acepta formalmente el Líder Técnico al pasar este ADR a
Aceptado.

**Qué datos.** Identificadores en claro, sin nombres, direcciones, teléfonos ni correos:

- **Ida** (`*Request`): placa o VIN; tipo y número de documento de propietarios y locatarios (NIT para RUES); datos
  técnicos del avalúo (modelo, cilindraje, combustible, pasajeros); departamento del impuesto; ids internos (`trabajoId`,
  `consultaId`, `personaId`, empresa en el sobre).
- **Vuelta** (`*Response` proyectado a la lista permitida, `services/core-consultas/src/Flit.Consultas.Api/Bus/MinimizadorRespuesta.cs`):

  | rpc | Lo único que viaja |
  |---|---|
  | `RuntIntempoService/ConsultarPorPlaca`, `ConsultarPorVin` | `resultado`, `tipo_error`, `http_status`. Vehículo: `plate`, `vin`. Información general: placa, VIN, tipo de servicio, clase, organismo de tránsito, marca, línea, modelo, cilindraje y combustible. Datos técnicos: pasajeros sentados. Propietarios actuales: `id_tipo_documento`, `tipo_documento`, `no_documento`, `tipo_propiedad`, `detalle_propiedad` y sus locatarios (`tdoc`, `ndoc`). `lessee[]`: `document_type`, `document_number` |
  | `ConsultasService/ConsultarConductor` (RUNT persona) | Proveedor, chequeos (clave y estado) y solo los campos `person_license_status` y `person_license_categories` (clave y `valor_texto`) |
  | `ConsultasService/ConsultarMultas` (SIMIT) | Proveedor, semáforo, chequeos (clave y estado) y comparendos (número, fecha, valor, estado y organismo) |
  | `ConsultasService/ConsultarRues` | Proveedor, chequeos, los campos `rues_estado`, `rues_fecha_renovacion` y `rues_ultimo_ano_renovado`, y de cada matrícula mercantil el estado (`valor`, `crudo`) y la fecha de renovación. Sin NIT, razón social ni representantes |
  | `ConsultasService/ConsultarImpuestoVehicular` | Proveedor, chequeos, monto total y vigencias (año, valor, pagada) |
  | `ConsultasService/ConsultarAvaluos` | Valor sugerido, fuente principal y de cada fuente su clave, estado, valor y muestras (sin el mensaje del proveedor) |

  Nunca viajan `valor_json`, mapas clave-valor, texto libre del proveedor ni ningún campo que el proto agregue sin
  clasificarlo: `MinimizadorRespuestaTests.Ningun_campo_del_proto_queda_sin_clasificar` falla en ese caso. Sí viajan
  tres textos de proveedor que diagnóstico lee (`detalle_propiedad`, `EstadoVigencia.crudo` y `tipo_documento`); su
  normalización a catálogo queda propuesta.

**Dónde y cuánto tiempo.**

| Lugar | Retención | Control |
|---|---|---|
| Colas principales (`consultas.diagnostico`, `consultas.diagnostico-express`, `diagnostico.consultas-resultados`) | Hasta consumirse; máximo 24 h | Políticas `ttl-*` (86400000 ms, DLX a la `.dlq`), prioridad 10 |
| Colas `.retry.<n>` | Segundos o minutos | TTL del SDK |
| Colas `.dlq` | 7 días | Política `dlq-7d` (604800000 ms), prioridad 10, sin vuelta atrás por `clear_policy` |
| Volumen del broker | Lo mismo que las colas | Mismos TTL |
| Outbox de Postgres (`diagnostico.outbox`, `consultas.outbox`) | 24 h tras publicarse | `Platform:Messaging:RetencionPublicados` (excepción explícita a R-2 de ADR-0072) |

**Quién puede verlos.**

- El administrador del broker. La consola está publicada solo en `127.0.0.1` (túnel SSH).
- Cualquier usuario de servicio del vhost `flit` puede atar una cola a `flit.consultas` (lectura sobre `flit.*`) hasta
  que se aplique M4. Inventario esperado: **6 usuarios de servicio** (`tramites`, `consultas`, `notificaciones`,
  `plataforma`, `diagnostico` y `comparendos`, uno por `RABBITMQ_URL_*` de `docker-compose.prod.yml`) **más el
  administrador**.
- Quien lea las bases `diagnostico` y `consultas` (outbox, 24 h).
- `root` de la VPS.

**Controles compensatorios.**

- Minimización de ida (constructores de solicitud, prueba de contrato sin nombres ni direcciones) y de vuelta
  (proyección positiva con guarda por `Descriptor`).
- `user_id` validado por el broker + `ProductoresPermitidos` (no vaciable en consultas), también en los reenvíos
  (`x-flit-user-id`).
- Aislamiento por empresa en el consumidor: la empresa del sobre se fija antes de tocar la celda (ADR-0072).
- Logs sin PII: ids, fuente y tipo de error; nunca solicitud, respuesta, placa, VIN, documento ni `ex.Message`.
- Retención: `dlq-7d`, `ttl-*` y purga de la outbox.
- Consola en loopback y un usuario por servicio; `usuario-de-servicio.sh` rechaza `flit` y `guest`.
- Revisión periódica: `rabbitmqctl list_users`, `list_permissions -p flit` y `list_bindings -p flit` de
  `flit.consultas` (solo deben aparecer las colas conocidas).

**Evolución prevista.** Cifrado de campo como v2 del contrato (opción 3) si cambia el inventario de usuarios del broker,
si una auditoría lo exige o si los datos salen de la VPS.

**Responsable.** Acepta el riesgo el Líder Técnico FLIT (humano). Mantiene los controles Infra/plataforma. Revisión en
cada alta de usuario del broker y al menos cada 6 meses.

## Consecuencias

### Lo que se gana
- Tolerancia a fallos de punta a punta: core-consultas, core-diagnostico o el broker pueden caerse sin perder trabajo
  durante hasta 24 h.
- Diagnóstico queda sin integraciones de proveedor, también sin las simuladas (acta §3.5, §5).
- Recuperación por redelivery e idempotencia por bandeja + `trabajo_id`. Desaparece el polling de 2 s entre empresas.
- Fin de lote notificable (`diagnostico.lote.terminado`) para futuros consumidores (correo, reportes).
- La outbox de los dos servicios deja de crecer sin límite.

### Lo que se pierde
- La equidad entre empresas en los lotes (D2).
- El detalle síncrono del error: diagnóstico ve el `grpcStatus`, no la excepción (igual que antes, sin `ex.Message`).
- La comodidad de correr diagnóstico en local sin core-consultas ni el broker.
- La outbox como registro histórico de lo publicado en diagnóstico y consultas (solo 24 h).

### Cambios operacionales
- **Broker.**
  - Usuario `diagnostico` con destino `consultas`.
  - `definitions.json` declara `consultas.diagnostico`, `consultas.diagnostico-express` y
    `diagnostico.consultas-resultados` con sus bindings, la política `dlq-7d` en sus `.dlq` y las `ttl-*` en las
    principales, todas en prioridad 10. Una política manual con más prioridad que alcance estas colas las anula.
  - El TTL de las `ttl-*` y `Diagnostico:Consultas:Reconciliador:TtlTrabajo` deben coincidir.
- **Acceso al broker.** Inventario de usuarios y consola solo en `127.0.0.1`, porque cualquier usuario puede leer los
  mensajes (riesgo aceptado de U1).
- **Compose.** `RABBITMQ_URL_DIAGNOSTICO` pasa a obligatoria (`:?`) en todos los ambientes.
- **Pools de base.** core-consultas de 20 a 30 conexiones (12 consumidores concurrentes más el gRPC en línea).
- **CI.** `servicios-plataforma.yml` suma `rabbitmq:4.1-management-alpine` (5672 y 15672), `FLIT_TEST_RABBITMQ` y
  `FLIT_TEST_RABBITMQ_ADMIN`.
- **Configuración.**
  - `Diagnostico:Procesador` y `Diagnostico:Simulacion` desaparecen.
  - Entran `Diagnostico:Consultas:Bus`, `Diagnostico:Consultas:Reconciliador` (`Intervalo`, `Margen`, `VentanaVida`,
    `ToleranciaOrden`, `TtlTrabajo`, `Maximo`), `Consultas:Diagnostico` (`Lote`, `Express`, `TiempoMaximoSegundos`,
    `ProductoresPermitidos`), `Consultas:Simulacion` (solo LOCAL y DEV) y `Platform:Messaging:RetencionPublicados` en
    los dos servicios.
  - En QA y PDN no se definen `Consultas__Diagnostico__ProductoresPermitidos` ni `Consultas__Simulacion__Habilitada`.
- **Fuentes ofrecidas.** Ninguna fuente queda «no conectada»: en QA y PDN, SIMIT, RUES y avalúo pasan a ofrecerse en
  `fuentes_disponibles`. A ratificar con el LT.
- **Despliegue.** La tanda A de diagnóstico no se despliega sin la B. Checklist completo en el diseño, §13.
- **Sin cambios.** La cola del ZIP de evidencias.

## Pendientes fuera de este ADR

- **Impuestos (acta §3.6).** El servicio actual no está en el repo. Falta su URL, su contrato, su autenticación y si es
  scraping. Encaje propuesto: core-consultas como fachada, con diagnóstico publicando `consultas.diagnostico.impuesto`.
- **SIMIT.** El acta dice Verifik y el código elige el proveedor por empresa (`FinesProviderResolver`). A ratificar con el
  LT.
- **Intempo.** Ya implementado en core-consultas (HU #13876). Falta confirmar el nombre y el alcance (acta §6.1).
- **Recomendaciones del security-agent sin aplicar**, para decisión del LT o del usuario: M4 (restringir la lectura por
  exchange por servicio), M6 (claves de usuarios demo en `docs/diagnosticos/pruebas-validacion-diagnostico-local.md`),
  B2, B3, B4, B6 y la normalización a catálogo de los textos de proveedor que siguen viajando. Detalle en el diseño, §12.
- **Partición del PR** (regla 9). Tres opciones con recomendación en el diseño, §7.3. Decide el LT.

## ADRs relacionados

- [ADR-0064] — eventos y trabajos por outbox, bandeja, reintentos y DLQ. Este ADR agrega `user_id` validado,
  `ProductoresPermitidos` y la retención opt-in de la outbox al SDK, y configura las esperas del consumidor de resultados
  en 5, 10 y 20 s.
- [ADR-0065] — consultas externas como capacidad de plataforma. core-consultas pasa a atender también por cola.
- [ADR-0070] — gRPC entre servicios. Se mantiene para lo que es en línea; los lotes salen del gRPC.
- [ADR-0072] — aislamiento por empresa. Se enmiendan A11 (el dueño es el reconciliador) y R-2 (documento en claro en la
  outbox hasta 24 h tras publicarse). Los consumidores fijan la empresa del sobre como «job sin usuario».
- [ADR-0059] — procesos periódicos in-process. Lo sigue el reconciliador.

## Notas para agentes

- **Backend Agent.** Ningún consumidor lanza por errores del proveedor:
  - core-consultas publica un resultado ERROR;
  - diagnóstico solo lanza `ReintentoProgramadoException` o errores de infraestructura.

  Fijar la empresa es la primera línea. Ni placa, ni VIN, ni documento, ni `solicitud`/`respuesta`, ni `ex.Message` en
  logs. Los consumidores Lote y Express son **tipos distintos** (el SDK deduplica por tipo). Un campo nuevo en un
  `*Response` que diagnóstico necesite se agrega a la lista permitida de `MinimizadorRespuesta` **y** se clasifica en su
  prueba; sin eso no viaja.
- **Database Agent.** Diagnóstico: `consultas.trabajo_id uuid NULL` y `republicaciones smallint NOT NULL DEFAULT 0`
  (CHECK 0..1), sin índice. core-consultas: sin migración. Deuda: `lease_hasta` y el `HasComment` de `ConsultaConfig`
  siguen hablando del procesador borrado; se limpian con una migración aparte.
- **Frontend Agent.** Sin cambios.
- **QA Agent.** Pruebas de contrato contra `contracts/fixtures/bus/` en los dos servicios. Las de broker real cubren las
  caídas de cada servicio, Express frente al lote, la DLQ y el reconciliador. En CI no se saltan. Pendiente: que las de
  core-consultas lean la topología de `definitions.json`, como ya hacen las de diagnóstico.
- **Security Agent.** La revisión de Habeas Data se hizo (BLOQUEADO; C1–C5 resueltas). Falta la **re-revisión** sobre
  `63e0fe89c` antes del PR. Si exige cifrado, la opción 3 entra como v2 del contrato.
- **Infra Agent.** Checklist del diseño §13: colas pre-declaradas con `dlq-7d` y `ttl-*`, usuario con destino, compose
  `:?`, pools, inventario de usuarios y rabbitmq con la consola en el CI.

## Referencias externas

- RabbitMQ — Validated User-ID: https://www.rabbitmq.com/docs/validated-user-id
- RabbitMQ — Consumer Acknowledgements and Publisher Confirms: https://www.rabbitmq.com/docs/confirms
- RabbitMQ — Dead Letter Exchanges, TTL y políticas: https://www.rabbitmq.com/docs/dlx · https://www.rabbitmq.com/docs/ttl
- RFC 9562 — UUID versión 7 (marca de tiempo en los 48 bits altos)
- Ley 1581 de 2012 (Colombia), artículo 4 literales f (acceso y circulación restringida), g (seguridad) y h
  (confidencialidad)
