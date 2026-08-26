# ADR-0046: El correo de asignación de placa se dispara en la arista `preasignado → asignado` desde application, con cola propia

**Fecha**: 2026-08-12
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, Product Owner FLIT (destinatarios, copy y aceptación de riesgo), Backend
**Tags**: arquitectura, backend, notificaciones, tramites, placa, outbox
**Extiende**: ADR-0045 — no lo supersede. ADR-0045 sigue rigiendo el correo de cambio de `status`; este ADR cubre un sub-estado que aquella outbox no alcanza.

## Contexto

La plantilla `tramites.asignacion-placa` existe completa —catálogo (`Flit.Infrastructure/Notifications/Catalog/NotificationTemplateCatalog.cs:37,85-89`), composer con variante FLIT y Renting (`Notifications/Tramites/AsignacionPlacaEmailComposer.cs`), modelo y muestra del banco de pruebas— pero es **huérfana**: su único consumidor es `NotificationSampleRenderer`, y `NotificationTrigger.cs:40-44` lo declara sin rodeos — «Declarativo en el catálogo del banco de pruebas; el handler productivo se conecta después».

El requerimiento de negocio: en **matrícula inicial por ruta de preasignación de placa** (el trámite se envía al OT con o sin dígito de preferencia), cuando el **OT asigna la placa** al vehículo, avisar al comprador con los datos de la placa e indicarle que debe comprar el SOAT. El trámite está en `entregado` de forma global, con el sub-estado de placa pasando de «sin asignar» a «asignado».

Cinco restricciones verificadas acotan el diseño:

1. **El evento no es un cambio de `status`.** La arista es el sub-estado `tramites.procedure_instances.plate_flow_status: preasignado → asignado`; el `status` global permanece `entregado`. No hay transición de `TramiteStateMachine`, ni fila de historial, ni fila de outbox, ni evento de dominio, ni MediatR: es una mutación EF directa en `Flit.Infrastructure/Persistence/Repositories/OtClientProcedureRepository.cs:618`, con comentario en el propio código que justifica por qué no se emite transición («evita registrar aristas que la máquina no contempla»). Por tanto **nada de la maquinaria de ADR-0045 se activa sola**, y su mapa de plantillas lo confirma: `TramiteStateChangeTemplateMap` devuelve `null` para cualquier destino que no sea `aprobado` o `rechazado`, y un AC vigente de la HU #11465 dice literalmente que «una transición a *entregado* no encola nada».

2. **La tabla de despachos de ADR-0045 no admite este evento.** `tramites.procedure_state_change_email_dispatches.outbox_id` es `NOT NULL` con FK a `tramites.procedure_state_change_outbox(id)` (`Persistence/Sql/Ddl/69-tramite-state-email-dispatch.sql:22-24`). Sin fila de outbox no hay fila de despacho. Y fabricar una fila de outbox sintética es inaceptable: `ProcedureStateChangeOutboxProcessor` reclama todo lo que tenga `published_at IS NULL` y le haría **fan-out a los webhooks del OT y al reflejo gRPC de core-ict** un cambio de estado que no ocurrió.

3. **Hay una segunda vía a `asignado`, invisible a C#.** El trigger de PostgreSQL `trg_autoset_plate_flow_status` (migración `20260729160000_AutosetPlateFlowFullPlate.cs:25-58`) fija `plate_flow_status='asignado'` al entrar a `status='entregado'` si ya existe field_value `plate`. Es el **Flujo A**: la placa venía del RUNT o del wizard y el OT no asigna nada. Dato decisivo: el trigger está guardado por `NEW.plate_flow_status IS NULL` y salta de `NULL` directo a `asignado` (o a `terminado` si la compañía tiene `plate_flow_skip_to_terminado`); **nunca ejecuta la arista `preasignado → asignado`**.

4. **La regla productiva de la variante Renting está enunciada pero inerte.** El composer documenta que en productivo la variante Renting aplica cuando `companyRegistered == "811011779"` (`AsignacionPlacaEmailComposer.cs:10-12,30`), pero **`companyRegistered` no existe en el código ni en el schema**: la única aparición del NIT en todo `services/core-api/src` es ese `const` y su comentario. La regla necesita origen de datos. Candidato real y verificado: `identity.tenants.tax_id` (`Flit.Infrastructure/Persistence/Entities/Identity/Tenant.cs:11`; `varchar(20) NOT NULL` según `Configurations/Identity/TenantConfiguration.cs:21`).

5. **El endpoint del OT ya orquesta post-asignación y ya asume best-effort.** `Flit.Api/Endpoints/AdminPlateRangesEndpoints.cs:153-174` regenera el FUR y los demás documentos tras asignar, dentro de `ExecuteInClientTenantScopeAsync`, con `try/catch` y comentario explícito: «un fallo aquí NO revierte la asignación de placa ya persistida».

Y una restricción operativa que atraviesa todo: quien invoca el endpoint es el **tenant del OT** (`otTenantId`), pero el dueño del trámite, de la política de canal y del kill-switch es el **tenant cliente** (`accessible.ClientTenantId`).

**Requisito duro**: un fallo del correo **no puede impedir ni revertir la asignación de placa**, que es una operación interactiva del OT sobre inventario ya reservado.

## Decisión

El correo se dispara desde un **handler de application sobre el endpoint del OT**, inmediatamente después de la regeneración de FUR ya existente y **fuera de la transacción de asignación**. El handler no envía: resuelve destinatarios e **inserta filas en una cola propia**, `tramites.plate_assignment_email_dispatches`, idempotente por `(procedure_instance_id, placa, destinatario)`. Un `BackgroundService` dedicado envía desde esa cola por el `IEmailSender` existente, con reintentos y kill-switch propios.

Cinco puntos forman parte de la decisión y no del seguimiento:

**1. Solo dispara el Flujo B.** El enganche vive en el camino de código que ejecuta la arista `preasignado → asignado`, y el trigger SQL del Flujo A nunca la ejecuta (Contexto §3). La exclusión es **estructural, no un `if`**: nadie puede borrarla por descuido. Y debe seguir siéndolo por negocio: en el Flujo A la placa venía del RUNT o del wizard, el comprador ya la conocía antes de radicar, y el correo le anunciaría como novedad un dato que él mismo aportó. Por la misma razón, un trámite con `plate_flow_skip_to_terminado` (que salta a `terminado`) tampoco notifica.

**2. Destinatarios: el rol `comprador`, reutilizando `TramiteNotificationRecipientResolver` sin modificarlo** (decisión del PO, 2026-08-12). Consecuencia asumida: si el comprador es **persona jurídica**, el resolver produce los **dos cupos** —`empresa` (desde `actor.Email`) y `representante_legal` (desde `actor.Metadata`)— tal y como fijó ADR-0045, con sus mismas reglas: ningún cupo se rellena con un correo cuya titularidad el sistema no pueda afirmar, un cupo sin correo se registra como `omitido` y jamás se sustituye por el otro, y cuando empresa y representante legal **comparten buzón el envío se colapsa a uno solo** (llave por destinatario, no por cupo). No se toca el resolver: reutilizarlo tal cual es lo que mantiene una única definición de «a quién se le escribe en un trámite».

**3. Copy: se corrige antes de conectar el disparador, en las dos variantes.** El cuerpo actual dice «cuya posible placa es» (`AsignacionPlacaEmailComposer.cs:194`), redacción de preasignación que es **falsa** una vez la placa está asignada, y **no menciona el SOAT**, que es el propósito del aviso. Se sustituye por la redacción de placa ya asignada y se añade el párrafo indicando al comprador que debe comprar el SOAT, en `ComposeFlit` **y** en `ComposeRenting` (ambas comparten `BuildSharedBody`, así que es un solo punto). Esto es **prerrequisito de la HU de disparo, no seguimiento**: conectar el disparador con el texto actual equivale a enviar un correo incorrecto. Los campos nuevos del modelo se añaden como **opcionales con valor por defecto**, o `NotificationSampleRenderer` y `AsignacionPlacaEmailPreviewSample` dejan de compilar.

**4. Marca del cuerpo: por NIT del tenant cliente, no por canal.** `identity.tenants.tax_id` del tenant cliente, **normalizado a solo dígitos y descartando el dígito de verificación** (`811011779-1`, `811.011.779` y `8110117791` deben coincidir; sin normalizar el `const` no acierta nunca en producción), comparado contra `811011779`. Esto **diverge deliberadamente** de ADR-0045, que ata la variante al canal del tenant para `tramites.aprobado/rechazado`: los dos catálogos declaran criterios distintos y se respetan en vez de uniformarlos por comodidad. Para acotar la divergencia se registra un **log cuando marca y canal discrepan**; el caso peligroso es uno solo —cuerpo con marca FLIT saliendo por la API de Renting— y hoy nadie lo vigila. Se adopta el NIT del **tenant** y no el `documentNumber` del actor comprador porque la marca del cuerpo debe casar con el transporte, y el transporte (`TenantChannelEmailRouter`) se decide por tenant; con el actor, un tenant cualquiera podría emitir cuerpos con marca Renting. **Asunción confirmable**: el `const` heredado no dice cuál de las dos lecturas de `companyRegistered` era la intención original.

**5. Identidad del evento: `(procedure_instance_id, placa)`.** No hay outbox del que colgar la idempotencia, así que **la placa es la noticia**. Consecuencia semántica adoptada: revocar y reasignar **la misma** placa **no** reenvía correo; asignar una placa **distinta** sí. La segunda vez es noticia nueva; la primera no lo es. **Asunción confirmable**, materializada directamente en los índices UNIQUE (no en un `if` de C#). Relacionado: el **dígito de preferencia** (`plate_preferred_last_digit`) **no se muestra** en el cuerpo ni se indica si la placa asignada lo respetó — el requerimiento no lo pide y añadirlo abre la pregunta de qué decir cuando no se respetó.

## Alternativas consideradas

### Opción A: Enganche dentro de `AssignPlateAsync`, en la misma transacción

Un puerto `IPlateAssignmentEmailEnqueuer` (definido en `Flit.Tramites.Application`) inyectado en `OtClientProcedureRepository` e invocado justo después de `entity.PlateFlowStatus = PlateFlowStatus.Asignado;` (`OtClientProcedureRepository.cs:618`), dentro del `ExecuteInClientTenantScopeAsync` y del mismo `SaveChangesAsync`.

**Pros:**
- **Transaccionalidad real**: encolado y sub-estado se confirman o se revierten juntos. Es la misma propiedad que ADR-0045 defiende para `TramiteLifecycleService` («transición confirmada ⇒ exactamente un evento; rollback ⇒ cero eventos»). Ninguna otra opción la da.
- El scope RLS del tenant cliente ya está abierto ahí: el `INSERT` no necesita gimnasia de contexto ni arriesgarse a usar el tenant equivocado.
- Cubre a cualquier futuro llamador del repositorio, no solo al endpoint actual: no se puede puentear.
- Excluye el Flujo A por construcción, igual que la opción recomendada.
- Cero I/O de red en el camino crítico: solo un `INSERT` en la conexión ya abierta.

**Cons:**
- Profundiza la deuda de Clean Architecture: mete resolución de destinatarios —regla de negocio— en la capa de infraestructura. El repositorio ya hace demasiado (reserva inventario, escribe field_values, recarga `row_version` para esquivar el trigger de denormalización) y esto lo empeora.
- **Puede tumbar la asignación de placa.** Si el enqueue lanza —`Metadata` JSON corrupto, timeout, un `NullReferenceException` en el resolver— la transacción se revierte y el OT recibe un 500 al asignar una placa perfectamente válida, con el inventario ya tocado. Evitarlo exige un `try/catch` fail-open explícito que **destruye la transaccionalidad que era su única gran ventaja**.
- Las pruebas del repositorio, ya pesadas, crecen con un doble más.

**Esfuerzo:** M
**Riesgos:** que el fail-open se olvide en un cambio futuro y un actor con `Metadata` mal formado deje al OT sin poder asignar placas — un fallo de correo convertido en incidente de operación.

### Opción B: Use case de application sobre el endpoint del OT (RECOMENDADA)

`AssignPlateToProcedureCommand` + handler en application: llama al repositorio para la asignación y, tras el éxito, encola el correo — exactamente donde hoy vive la regeneración del FUR.

**Pros:**
- Capa correcta: la orquestación (asignar → regenerar documentos → notificar) queda en application, que es su sitio; el repositorio vuelve a ser persistencia.
- **Precedente literal a tres líneas de distancia**: el patrón best-effort post-asignación ya está aceptado, comentado y en producción en el mismo método (`AdminPlateRangesEndpoints.cs:153-174`).
- **Estructuralmente incapaz de tumbar la asignación**: el correo ocurre después del commit, fuera de la transacción, con el mismo `try/catch` que ya envuelve al FUR. Cumple el requisito duro por posición, no por disciplina.
- Testeable con dobles limpios, sin levantar el repositorio ni EF.
- Excluye el Flujo A por construcción (vive en el camino de la arista que el trigger nunca ejecuta).

**Cons:**
- **Ventana de pérdida**: si el proceso muere entre el commit de la asignación y el `INSERT` del despacho, el correo se pierde y nadie se entera. La Opción A no tiene esa ventana.
- Es un refactor: mover orquestación fuera del endpoint toca firma pública, DI y pruebas de endpoint.
- Si mañana alguien llama a `AssignPlateAsync` sin pasar por el handler, no hay correo y nada lo delata.

**Esfuerzo:** L (M con el alcance mínimo que se adopta: añadir el handler sin mover el bloque de FUR)
**Riesgos:** que el refactor arrastre la regeneración de FUR y su comportamiento cambie sin querer — se mitiga acotando explícitamente el alcance.

### Opción C: Generalizar el outbox con un evento de sub-estado de placa

Un `PlateFlowStateChangeEvent` análogo a `ProcedureStateChangeEvent`, con su fila de outbox y su fan-out, para reutilizar el `outbox_id` de la tabla de despachos existente.

**Pros:**
- Uniformidad conceptual: todo cambio de estado —global o de sub-estado— se representaría igual.
- Reutiliza literalmente la tabla de despachos de ADR-0045, sin DDL sobre ella.
- Abre la puerta a que el OT y core-ict escuchen sub-estados en el futuro.

**Cons:**
- **Contamina un outbox vivo.** Si la fila va a `procedure_state_change_outbox`, `ProcedureStateChangeOutboxProcessor` la despacha a los webhooks del OT y al reflejo ICT: se les anuncia un cambio de `status` que no existió. Evitarlo exige columna discriminadora y tocar el `WHERE` de un worker productivo — regresión sobre integraciones externas vivas, por un correo.
- Si en cambio se crea una **segunda** tabla de outbox, entonces `procedure_state_change_email_dispatches.outbox_id` ya no puede referenciarla (una FK apunta a una sola tabla) y **la reutilización, único argumento de la opción, desaparece**.
- Añade dos artefactos antes de la cola de correo: tres saltos hasta el envío, ~15 s de latencia acumulada.
- Sobre-diseño: hoy hay exactamente un consumidor de este evento y ningún consumidor pidiéndolo.

**Esfuerzo:** L
**Riesgos:** romper el webhook del OT o el reflejo ICT — inaceptable como efecto colateral de una notificación.

## Tradeoff aceptado

El desempate no es el purismo de capas sino el requisito duro: **no tumbar la asignación de placa si el correo falla**. La Opción A solo lo cumple renunciando a su propia ventaja: con el `try/catch` fail-open que el requisito le exige, deja de ser transaccional y se queda con la violación de capas **sin la contrapartida**. La Opción B lo cumple por posición.

Se acepta a cambio una **ventana de pérdida** por caída del proceso entre el commit y el `INSERT`. Es el único punto donde A ganaba, y es **auditable**: `plate_flow_status='asignado'` sin fila en la cola de despacho es un `LEFT JOIN` de una línea. Encaja además con el criterio que ADR-0045 ya normalizó — hacer visibles los huecos (filas `omitido`) en vez de esconderlos.

Se acepta también una **segunda cola** en lugar de generalizar la de ADR-0045. La generalización (hacer `outbox_id` nullable, añadir discriminador y reescribir los dos UNIQUE parciales) migra una tabla recién mergeada en el PR #253 y **debilita un invariante recién fijado** —«ningún consumidor posterior escribe deduplicación en C#»— con solo dos disparadores en juego. **Condición explícita de revisión: al aparecer el tercer disparador de correo de trámite, se fusionan las dos colas en una con discriminador.** No antes, y no más tarde.

## Modelo de datos

**No se reutiliza `procedure_state_change_email_dispatches`**: no hay `outbox_id` que poner y la FK es `NOT NULL`. Se crea la tabla hermana `tramites.plate_assignment_email_dispatches`, misma forma y mismos estados, con llave de idempotencia propia:

- `UNIQUE (procedure_instance_id, upper(plate), lower(recipient)) WHERE recipient IS NOT NULL`
- `UNIQUE (procedure_instance_id, upper(plate), recipient_role, recipient_kind) WHERE recipient IS NULL` — cupos vacíos, hasta dos por evento (comprador PJ: empresa + representante legal).
- Índice parcial de cola `(queued_at) WHERE status = 'pendiente'` e índice `(procedure_instance_id)`.
- Columnas gemelas de la tabla vecina: `tenant_id` (= **tenant cliente**), `procedure_instance_id`, `plate`, `recipient` (`@pii:medium`, nullable), `recipient_name`, `recipient_role`, `recipient_kind`, `template_key`, `status` (`pendiente|enviado|fallido|omitido`), `failure_reason`, `attempts`, `queued_at`, `processed_at`, `created_at`.
- **Sin FK a `procedure_state_change_outbox`.** RLS `tenant_isolation` como sus vecinas de `tramites`.

El DDL detallado y su numeración los materializa el `database-agent`.

## Consecuencias

### Lo que se gana
- La plantilla deja de ser huérfana y entra a producción sin tocar el transporte, el enrutamiento por canal, la bitácora `admin.notification_delivery_logs` ni el patrón de worker ya construidos (Features #11347/#11348/#11349/#11459).
- El correo es observable por fila, incluido el caso hoy invisible de «este trámite no tiene correo del comprador» y el de «la empresa tiene correo pero su representante legal no».
- La idempotencia es una restricción de la base, no un `if`: reasignaciones y reintentos no generan correos duplicados.
- Se corrige de paso un texto que hoy sería incorrecto en producción («posible placa», sin SOAT).

### Lo que se pierde
- Una tabla y un `BackgroundService` más (el séptimo): un poll más cada 5 s en el arranque.
- Dos colas de correo de trámite conviviendo hasta la fusión, con el riesgo de que diverjan en comportamiento.
- La exactly-once que solo la Opción A daba: queda la ventana de pérdida por caída del proceso.
- El correo no es «en el acto»: hasta un ciclo de poll de retraso.

### Cambios operacionales
- Migración nueva envuelta en una `Migration` (se aplica sola al arrancar), patrón de `20260811140000_NotificationDeliveryLogRecipientDiverted.cs`.
- Un log `Critical` nuevo al agotar reintentos: necesita destino en la alerta operativa, como el dead-letter del outbox de estados.
- El kill-switch y el canal se leen del **tenant cliente**, nunca del tenant del OT que ejecuta la acción.
- Un log nuevo de discrepancia marca↔canal (Decisión §4).

## ADRs relacionados

- **[ADR-0045]** — mecanismo de correo por cambio de estado del trámite. Este ADR lo **extiende** a un sub-estado que su outbox no cubre, reutilizando su patrón de cola + worker y su `TramiteNotificationRecipientResolver` sin modificarlo. No lo contradice ni lo supersede. Diverge en un solo punto, declarado y acotado: la resolución de la marca del cuerpo (aquí por NIT, allí por canal).
- **[ADR-0022]** — estados de negocio del ciclo de vida del trámite. El sub-estado de placa es **ortogonal** a esa máquina, y por eso este evento no genera fila de outbox ni fila de historial.
- **[ADR-0044]** — envío real vs buzón de control por despliegue. Este ADR **hereda su hueco**: el guardarraíl de destinatarios reales solo existe en el canal Renting. Ver R1 en Riesgos.
- **[ADR-0043]** — precedente de desacoplar una elegibilidad del canal de notificación. Aquí se aplica el criterio en su versión más fuerte: ni siquiera la **variante del cuerpo** depende del canal para esta plantilla.

## Riesgos

| # | Riesgo | Severidad | Tratamiento |
|---|---|---|---|
| R1 | **Envío real a clientes desde DEV/QA.** `RentingRecipientOverride` (ADR-0044) solo existe en el canal Renting; el SMTP de FLIT no lo consulta, y DEV/QA/PDN corren los tres con `ASPNETCORE_ENVIRONMENT=Development`, así que `IsProduction()` nunca es `true`. Un trámite de prueba con el correo real de un comprador **le escribe**. | Alta | **RIESGO ACEPTADO por decisión explícita del usuario (2026-08-12).** Se enciende **sin** extender el guardarraíl al SMTP; el riesgo era conocido y descrito al decidir. Mitigaciones operativas disponibles: dejar el kill-switch por tenant **apagado en DEV/QA** y usar direcciones de prueba en los trámites de esos ambientes. La corrección de fondo —extender el desvío de destinatarios a `TenantChannelEmailRouter` para todos los canales, gobernado por variable explícita de despliegue y no por `IsProduction()`— queda para un **ADR posterior que amplíe ADR-0044**, fuera del alcance de este. |
| R2 | Pérdida silenciosa del correo si el proceso muere entre el commit de la asignación y el `INSERT` del despacho. | Media | Tradeoff aceptado. Auditable con una consulta (`asignado` sin fila de despacho); reconciliador periódico opcional en HU posterior. |
| R3 | **La marca Renting nunca acierta** por comparar `tax_id` sin normalizar (`811011779-1`, `811.011.779`) ⇒ todos los correos salen con marca FLIT, incluidos los de Renting. | Media | Normalizador con pruebas de las tres formas; log de discrepancia marca↔canal. La asunción del origen del NIT (tenant, no actor) se confirma antes de implementar. |
| R4 | Conectar el disparador sin corregir el copy ⇒ correo **incorrecto** en producción («posible placa», sin SOAT). | Media | El copy es **prerrequisito** de la HU de disparo (Decisión §3), no una HU de seguimiento. |
| R5 | Cambiar la firma de `AsignacionPlacaEmailModel` rompe `NotificationSampleRenderer` y `AsignacionPlacaEmailPreviewSample`. | Baja | Campos nuevos **opcionales con default**; prueba de regresión del banco de pruebas en la misma HU. |
| R6 | El refactor del endpoint arrastra la regeneración de FUR y altera su comportamiento. | Baja | Alcance mínimo explícito: **no mover** el bloque de FUR (`AdminPlateRangesEndpoints.cs:153-174`). |
| R7 | Usar `otTenantId` por descuido al leer políticas o al insertar ⇒ kill-switch y canal equivocados, o violación de RLS. | Media | Firma del enqueuer que solo acepta `clientTenantId`; prueba con OT y cliente distintos. |
| R8 | Las dos colas de correo divergen en comportamiento (reintentos, kill-switch, purga) al mantenerse por separado. | Baja | Condición de fusión escrita en el Tradeoff; extraer el reclamo `FOR UPDATE SKIP LOCKED` a una base compartida si sale sin coste. |
| R9 | Ninguna de las dos colas tiene política de retención definida (ya señalado en ADR-0045). | Baja | Se hereda el pendiente; no se resuelve aquí. |

## Notas para agentes

- **Database Agent** — crear `tramites.plate_assignment_email_dispatches` (DDL con el siguiente número libre en `Persistence/Sql/Ddl/`, idempotente con `IF NOT EXISTS`, RLS `tenant_isolation`), con los dos UNIQUE parciales sobre `(procedure_instance_id, upper(plate), …)` y el índice parcial de cola. **Sin FK a `procedure_state_change_outbox`**: no hay evento de outbox que referenciar. `recipient` es `@pii:medium` (Ley 1581), finalidad exclusiva de trazabilidad del envío; la tabla no guarda el cuerpo del mensaje.
- **Backend Agent** — el enqueue va **después** del commit de `AssignPlateAsync` y **envuelto en `try/catch`**: jamás puede propagar al endpoint. El tenant de todo lo relacionado con correo es `ClientTenantId`, **nunca** `otTenantId`. El worker usa `IEmailSender`, **no** `IExplicitChannelEmailSender` (`ExplicitChannelEmailSenderRegistrationTests` tiene una prueba de línea base que falla ante un consumidor nuevo). La ciudad y el organismo de tránsito salen de field_values `transit_office_city` (vía `TransitOfficeCity.Legible`) y `transit_office_name`, **nunca** de `TransitOfficeId`, que es `null` durante todo el wizard. `TramiteNotificationRecipientResolver` se reutiliza **sin modificarlo**. Al conectar el disparador, retirar de `NotificationTrigger.cs:40-44` la nota «el handler productivo se conecta después».
- **Frontend Agent** — sin cambios. Ninguna pantalla nueva.
- **QA Agent** — cinco casos obligatorios. (1) Flujo A —placa presente al radicar— **no** genera correo. (2) Compañía con `plate_flow_skip_to_terminado` tampoco. (3) Un fallo del enqueue deja la placa asignada y el endpoint devuelve 200. (4) Revocar y reasignar **la misma** placa no reenvía; una **distinta** sí. (5) Comprador PJ con empresa y representante legal compartiendo buzón ⇒ **un solo** envío, no dos mensajes idénticos.
- **Security Agent** — el punto sensible no es la tabla sino la resolución del destinatario: **un correo mal inferido es una divulgación de datos del trámite a un tercero**. Aplica íntegro el criterio de ADR-0045: ningún cupo se rellena con un correo cuya titularidad el sistema no pueda afirmar (ni `ProcedureInstanceParticipant` como respaldo en PJ, ni el directorio `admin.company_legal_representatives`). Revisar además R1: se enciende con envío real posible desde DEV/QA por decisión aceptada.
- **Infra Agent** — séptimo `BackgroundService`; sin variables de entorno nuevas. El log `Critical` de dead-letter necesita destino en la alerta. En DEV/QA, dejar el kill-switch por tenant apagado mientras no exista el guardarraíl de SMTP (R1).

## Referencias externas

- Transactional outbox / chained outbox — patrón ya en uso en `tramites.identity_validation_outbox`, `tramites.procedure_state_change_outbox` y `tramites.procedure_state_change_email_dispatches`.
