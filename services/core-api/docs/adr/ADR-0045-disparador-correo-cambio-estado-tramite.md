# ADR-0045: El correo de cambio de estado se dispara como sink del outbox y se envía desde su propia cola, nunca desde la transición

**Fecha**: 2026-08-12
**Status**: Aceptado
**Deciders**: Líder Técnico FLIT, Product Owner FLIT (alcance de transiciones notificables), Backend
**Tags**: arquitectura, backend, notificaciones, tramites, outbox

## Contexto

Las plantillas `tramites.aprobado` y `tramites.rechazado` existen, están catalogadas con el disparador `ProcedureStatusChanged` (`Flit.Infrastructure/Notifications/Catalog/NotificationTemplateCatalog.cs:74-83`), tienen composer con variante FLIT y Renting (`Notifications/Tramites/TramiteCambioEstadoEmailComposer.cs:23-79`) y todo el transporte por debajo: `IEmailSender` → `NotificationDeliveryLoggingEmailSender` → `TenantChannelEmailRouter` → SMTP o API Renting. Lo único que falta es el disparador: el comentario del propio composer lo dice — «banco de pruebas; sin disparador productivo aún» (`TramiteCambioEstadoEmailComposer.cs:8`).

Las restricciones que acotan el diseño son cuatro, y todas están verificadas:

1. **Un solo punto de transición.** `TramiteLifecycleService.cs:217-238` encola historial y fila de outbox en la misma unidad de trabajo y confirma con `SaveChangesWithConcurrencyGuardAsync`. No hay una segunda ruta por la que el status cambie.
2. **El despacho ya tiene dueño y es compartido.** `ProcedureStateChangeOutboxProcessor` reclama una fila con `FOR UPDATE SKIP LOCKED`, la despacha a **un** `IProcedureStateChangeNotifier` y sella `published_at`; ante fallo incrementa `attempts` y a los 5 la fila queda en dead-letter (`ProcedureStateChangeOutboxProcessor.cs:138-165,172-196`). Ese notifier hoy es un compuesto de dos sinks —webhook OT y reflejo gRPC a core-ict— que **relanza si cualquiera falla**, de modo que el reintento re-ejecuta también a los que sí habían despachado (`Ict/IctStateReflection.cs:308-333`). El registro del compuesto es «la última gana» (`IctStateReflection.cs:71-82` sobrescribe `AdminInfrastructureExtensions.cs:261-262`): registrar el correo como notifier paralelo **rompe los webhooks OT**.
3. **No existe quién resuelva «los correos de este trámite», y el destinatario no es uno por parte.** `ProcedureInstanceActor.Email` es nullable (`ProcedureInstanceActor.cs:13`) y `ProcedureInstanceParticipant.Email` solo existe si se usó el portal público (`ProcedureInstanceParticipant.cs:13-24`): un trámite llevado íntegramente por el gestor interno puede no tener ningún correo. Además, **requisito del PO (2026-08-12)**: cuando la parte es persona jurídica el aviso va **a la empresa y a su representante legal**; cuando es natural, a la persona. Una parte PJ produce por tanto **dos** destinatarios, no uno.
4. **La bitácora no sirve de llave de idempotencia.** `admin.notification_delivery_logs` no guarda `procedure_instance_id` ni el id del evento (`Persistence/Sql/Ddl/64-notification-delivery-log.sql`), y su `tenant_id` es `NOT NULL`: es evidencia de envío, no control de duplicados.

El requisito duro: **un fallo del correo no puede impedir el webhook del OT ni el reflejo a core-ict, ni dejar la fila del outbox sin sellar**, y un reintento no puede volver a escribirle al cliente.

## Decisión

El correo se engancha como **tercer sink del `CompositeProcedureStateChangeNotifier`**, pero ese sink **no envía**: resuelve destinatarios e **inserta filas en una cola propia** (`tramites.procedure_state_change_email_dispatches`, única por `outbox_id` + destinatario), y un `BackgroundService` dedicado envía desde esa cola por el `IEmailSender` ya existente, con su propio contador de intentos.

## Alternativas consideradas

### Opción 1: Sink que ENCOLA en tabla propia + worker de envío dedicado (RECOMENDADA)

El sink hace tres cosas y ninguna de red: mapea `to_status` a plantilla, resuelve destinatarios y hace `INSERT ... ON CONFLICT DO NOTHING` de una fila por destinatario. El envío (SMTP o API Renting, con sus latencias y sus 5xx) lo hace un worker aparte que reclama filas `pendiente` con el mismo patrón `FOR UPDATE SKIP LOCKED` del outbox de estados.

**Pros:**
- El aislamiento es estructural, no una promesa: lo único que el sink puede fallarle al composite es un `INSERT` en la misma base que ya está confirmando la transición. La red del proveedor de correo queda fuera del camino crítico de OT/ICT.
- La idempotencia es una restricción de la base (`UNIQUE (outbox_id, lower(recipient))`), no un `if`. Cuando el composite relanza porque ICT falló y el outbox reintenta, el sink re-corre y el conflicto lo absorbe Postgres: cero correos duplicados sin escribir una línea de lógica de deduplicación.
- Los reintentos del correo son suyos: cadencia y tope propios, sin consumir el `attempts < 5` compartido y sin que un buzón caído empuje la fila del outbox al dead-letter, matando de paso el webhook OT.
- La cola es la evidencia operativa que hoy no existe: qué trámite, qué transición, a quién, con qué resultado y cuántos intentos — consultable sin cruzar logs.
- Patrón ya establecido en el repo: `IdentityValidationOutboxProcessor`, `IdentityValidationSendRetryProcessor` e `IdentityValidationReconcileProcessor` son exactamente esta forma (`InfrastructureExtensions.cs:672-687`). No se introduce ningún concepto nuevo.

**Cons:**
- Una tabla y un `BackgroundService` más (el sexto). Más superficie que despachar inline.
- Encadena dos colas: el correo llega con dos poll de retraso (hasta ~10 s en el peor caso). Irrelevante para un correo, pero es real.
- La cola nueva hay que purgarla algún día; nadie ha definido retención.

**Esfuerzo:** M
**Riesgos:** que el worker de correo quede sin observabilidad y las filas `fallido` se acumulen sin que nadie mire — se mitiga con log `Critical` al agotar intentos, igual que `StateChangeOutboxLog.DeadLettered`.

### Opción 2: Tercer sink que envía inline, best-effort (nunca lanza)

Mismo enganche en el composite, pero el sink compone y llama a `IEmailSender` en el acto, capturando toda excepción para no contaminar a OT/ICT.

**Pros:**
- Lo más pequeño que puede funcionar: una clase y una línea de registro; sin tabla, sin worker, sin migración.
- El correo sale en el mismo ciclo de despacho, sin latencia añadida.
- La bitácora de envíos (`notification_delivery_logs`) ya deja evidencia de cada intento.

**Cons:**
- **Sigue necesitando la tabla de despachos.** Si el sink no lleva marca propia, el reintento del outbox por un fallo de ICT reenvía el correo: el cliente recibe el mismo aviso dos veces. Sin marca no hay idempotencia posible, y la bitácora no puede dársela (no guarda el evento). El ahorro de la opción se evapora.
- Un fallo transitorio de SMTP pierde el correo para siempre: no hay reintento, porque el sink se traga la excepción a propósito para no arrastrar a OT/ICT. Para un aviso al cliente final eso es una pérdida silenciosa.
- Mete una llamada de red con timeout dentro del bucle que sostiene el `FOR UPDATE` de la fila del outbox: un proveedor lento alarga la ventana del lock y frena el despacho de OT/ICT de las demás filas.
- «Nunca lanza» hay que sostenerlo con disciplina en cada cambio futuro del sink; la Opción 1 lo consigue por construcción.

**Esfuerzo:** S (M si se le añade la marca de idempotencia, que es obligatoria)
**Riesgos:** que se despliegue sin la marca «porque total, ICT casi nunca falla», y el primer incidente de ICT genere una tanda de correos duplicados a clientes.

### Opción 3: Consumidor propio del outbox de estados con columna `email_published_at`

Un segundo `BackgroundService` que reclama sobre la MISMA tabla `procedure_state_change_outbox` con `WHERE email_published_at IS NULL`, columna nueva, y sella su propia marca. No se toca el composite.

**Pros:**
- Aislamiento total desde el primer minuto: dos consumidores independientes, con contadores y marcas separados. Un correo caído no toca al webhook OT ni a ICT.
- No se toca `CompositeProcedureStateChangeNotifier` ni ninguna de las dos rutas de registro, así que no hay riesgo de romper el fan-out existente.
- Idempotencia con la granularidad exacta del evento y sin tabla nueva.

**Cons:**
- Convierte una outbox de un consumidor en una de N consumidores **por columnas**: el tercer consumidor exige `ALTER TABLE` otra vez (`quipux_published_at`, `sms_published_at`…). Es el anti-patrón conocido; la Opción 1 escala añadiendo filas, no columnas.
- La marca es por evento, no por destinatario: un trámite con dos correos (traspaso: comprador y vendedor) donde el segundo falla obliga a elegir entre reenviarle al primero o dar por bueno el fallo del segundo.
- Dos workers compitiendo con `FOR UPDATE SKIP LOCKED` sobre la misma tabla y el índice parcial `ix_procedure_state_change_outbox_unpublished`, que está definido `WHERE published_at IS NULL` y no sirve a la consulta del segundo consumidor: hace falta otro índice parcial.
- La fila del outbox ya no se puede purgar por `published_at`: hay que mirar dos marcas.

**Esfuerzo:** M
**Riesgos:** que el purgado o cualquier consulta operativa existente sobre `published_at IS NULL` empiece a mentir sobre lo que queda pendiente.

### Opción 4: Envío síncrono dentro de `TramiteLifecycleService` (DESCARTADA)

**Cons decisivos:**
- En el punto donde habría que llamar (`TramiteLifecycleService.cs:229-232`) **la transición todavía no está confirmada**: `SaveChangesWithConcurrencyGuardAsync` puede devolver `false` por conflicto de concurrencia. Se le anunciaría al cliente una aprobación que se revierte.
- Mete la latencia del proveedor de correo en el request de aprobar/rechazar del OT, que es interactivo.
- Rompe la propiedad que sostiene todo el diseño de N03: transición confirmada ⇒ exactamente un evento; rollback ⇒ cero eventos. Un correo enviado no se hace rollback.
- Los ~6 envíos síncronos que ya existen lo son porque su disparador **es** el request del usuario (invitación, recuperación de contraseña). Este no: el disparador es un cambio de estado que ya tiene outbox.

**Esfuerzo:** S · **Riesgos:** inaceptables.

## Tradeoff aceptado

Se paga una tabla y un worker (Opción 1) a cambio de tres propiedades que las demás opciones no dan juntas: aislamiento de fallos por construcción, idempotencia por restricción de base y reintentos propios. La Opción 2 solo parece más barata mientras se ignora que la idempotencia también le exige la tabla; con ella, cuesta casi lo mismo y sigue perdiendo correos ante un SMTP intermitente. La Opción 3 aísla igual de bien, pero paga el aislamiento degradando la outbox compartida a un esquema que no admite un cuarto consumidor sin otra migración, y pierde la granularidad por destinatario que un traspaso necesita.

Sobre el composite: **el sink se suma DENTRO de `CompositeProcedureStateChangeNotifier`, nunca como registro paralelo**. Como hay dos rutas de registro y solo una gana (`AdminInfrastructureExtensions.cs:261-262` y `IctStateReflection.cs:73-82`), la composición se centraliza en un único punto que ambas invocan, para que el sink de correo no dependa de si `Ict:StateCallback:Address` está configurado en ese despliegue.

## Resolución de destinatarios (requisito del PO 2026-08-12)

El destinatario **no se deriva del rol del trámite sino del tipo de persona del actor**. Por cada rol notificable (`comprador`, y `vendedor` solo en traspaso) se calcula un conjunto de destinatarios *esperados*:

| Actor | Esperados | Correo de cada uno |
|---|---|---|
| Natural (`PersonType='natural'`) | 1 · `persona` | `Participant` del mismo rol ▸ `actor.Email` |
| Jurídica (`PersonType='juridical'`) | 2 · `empresa` + `representante_legal` | `empresa` = **solo** `actor.Email`; `representante_legal` = **solo** `metadata.representanteLegal.Email` |

Tres reglas, cada una con su razón:

1. **En PJ, `actor.Email` es el correo de la EMPRESA.** Esto **extiende y contradice parcialmente** el precedente vigente `IdentitySubjectResolver.cs:34-37`, que documenta que en PJ el correo es «siempre el del representante legal, nunca el `actor.Email` de la empresa». Ese criterio es correcto **para lo suyo** —la validación biométrica solo puede recibirla quien puede biometrizarse, y un NIT no— pero no aplica a un aviso de estado, que sí va dirigido a la persona jurídica. Los dos criterios conviven: `IdentitySubjectResolver` sigue mandando en identidad; este resolver manda en notificación. **No se copia código de aquel**: se reutiliza su lectura del `metadata` para no crear un cuarto parser del mismo JSON (ya hay tres formas distintas — `ActorRepresentanteLegal` en `ActorsCommand.cs:65-73`, el `MetadataShape` privado de `IdentitySubjectResolver.cs:86` y el `ActorMetadataRl` privado de `FurCommand.cs:1474`, este último **sin campo `Email`**).
2. **`ProcedureInstanceParticipant` no participa en la resolución de una parte PJ.** No distingue PJ de natural, no tiene FK al actor y no dice a quién representa (`ProcedureInstanceParticipant.cs:13-24`). Para un actor PJ, el participante del portal es con toda probabilidad el RL firmando, pero *con toda probabilidad* no es base suficiente: usarlo como respaldo del cupo `representante_legal` significaría escribirle datos del trámite a una persona que el sistema no puede afirmar que sea el RL, y usarlo como respaldo del cupo `empresa` es directamente falso. El fallo seguro (Ley 1581) es dejar el cupo vacío y registrarlo. En persona **natural** sí se usa, con precedencia sobre `actor.Email`: ahí participante y actor son la misma persona por construcción del magic-link.
3. **No se consulta el directorio admin de representantes legales** (`admin.company_legal_representatives`). La empresa actora es un NIT escrito en el wizard, sin FK a `admin.companies` ni a `identity.tenants`: cualquier cruce sería por número de documento y podría emparejar una empresa homónima de otro tenant. La fuente única aquí es `actor.Metadata`.

**Persona jurídica sin `PersonType` (legacy, anterior a la HU #10542).** Se infiere PJ cuando `DocumentType == "NIT"`, reutilizando el criterio que el repo ya aplica al mismo dilema en `FurCommand.cs:1428-1429` (`IsJuridical(PersonType) || DocumentType == "NIT"`). Sin NIT se trata como natural. El fallo residual —una PJ legacy documentada con otro tipo— degrada a «solo llega a `actor.Email`», que en PJ **es el correo de la empresa**: se pierde la copia al RL, nunca se escribe a un tercero.

**Cupo esperado sin correo.** Cada esperado es una fila propia con desenlace propio. Un cupo sin correo se registra como `omitido` y **jamás se sustituye por el otro**: empresa y representante legal son destinatarios jurídicamente distintos, y mandarle al RL el correo que era de la empresa (o al revés) no es una degradación, es escribirle a quien no tocaba. **Un evento parcialmente entregado no es un evento fallido**: no hay desenlace agregado por evento, solo por destinatario. Si el correo faltante se captura después, no hay transición nueva y por tanto no hay correo nuevo — la fila `omitido` es lo que hace visible ese hueco.

**Deduplicación por buzón, no por cupo.** Empresa y RL comparten correo con frecuencia (PYME donde el RL usa el buzón corporativo). La llave `UNIQUE (outbox_id, lower(recipient))` colapsa ese caso a **un** envío; la alternativa —llavear por `(outbox_id, rol, tipo)`— mandaría dos mensajes idénticos al mismo buzón. Consecuencia aceptada: la fila superviviente solo declara uno de los dos cupos. Se mitiga con orden de inserción determinista (por rol `comprador`→`vendedor`, y dentro de cada rol `empresa`→`representante_legal`) y un log que deje constancia del colapso.

## Consecuencias

### Lo que se gana
- El correo de cambio de estado deja de ser una plantilla huérfana y pasa a producción sin tocar ni el transporte ni el enrutamiento por canal ya construidos (Features #11347/#11348/#11349).
- Idempotencia real por destinatario: `UNIQUE (outbox_id, lower(recipient))`. Un reintento del outbox —venga de un fallo de OT o de ICT— no reenvía nada.
- El correo pasa a ser observable por fila (`pendiente|enviado|fallido|omitido`), incluido el caso hoy invisible de «este trámite no tiene ningún correo al que escribir» y el nuevo «la empresa tiene correo pero su representante legal no».
- Queda un segundo punto, además de `IdentitySubjectResolver`, que sabe leer al representante legal del `metadata` del actor — pero **compartiendo el parser**, no duplicándolo: se deja de crecer el número de formas distintas de leer el mismo JSON (hoy tres).

### Lo que se pierde
- El correo ya no es «en el acto»: hasta dos ciclos de poll de 5 s.
- Una tabla más en `tramites` sin política de retención definida, y ahora con hasta 4 filas por evento (comprador PJ y vendedor PJ ⇒ 2 empresas + 2 representantes legales).
- Se pierde la simetría «una parte, un correo»: la evidencia de un traspaso PJ↔PJ hay que leerla como cuatro desenlaces independientes.
- El sexto `BackgroundService` del servicio; el arranque hace un poll más cada 5 s.

### Cambios operacionales
- Migración nueva (DDL `69-…`, envuelta en una `Migration` como `20260811140000_NotificationDeliveryLogRecipientDiverted.cs:24`). Se aplica sola al arrancar.
- Un log `Critical` nuevo al agotar intentos de un despacho de correo: hay que darle destino en la alerta operativa, como ya se hizo con el dead-letter del outbox de estados.
- La variante del cuerpo (FLIT vs Renting) se decide con el **mismo** canal del tenant que usa `TenantChannelEmailRouter.ResolveChannelAsync` (`TenantChannelEmailRouter.cs:191-200`). Se extrae esa resolución a un servicio compartido para que cuerpo y transporte no puedan divergir; si divergieran, un tenant Renting recibiría el cuerpo con marca FLIT enviado por la API de Renting.

### Fuera del alcance de este ADR (decisión del PO humano)
- **Qué transiciones notifican.** Este ADR fija el mecanismo y propone el mapeo mínimo que las plantillas existentes soportan: `*→aprobado` ⇒ `tramites.aprobado`, `*→rechazado` ⇒ `tramites.rechazado`, y **ninguna otra transición notifica** (no hay plantilla para `anulado`, `entregado`, `preparado` ni `borrador`). Ampliar el alcance exige plantillas nuevas y es decisión de producto.
- **Quién recibe.** El PO ya fijó la regla por tipo de persona (empresa + RL en PJ; la persona en natural). Quedan tres flecos suyos: (a) el mandatario se propone **excluido** (es el gestor, no el cliente final); (b) si un actor PJ **legacy sin `PersonType` ni NIT** debe notificarse solo a la empresa —que es lo que hará la inferencia propuesta— o bloquearse; (c) si un evento **parcialmente entregado** (empresa sí, RL no) requiere alguna acción operativa o basta con la fila `omitido`.
- **El asunto dice `— RECHAZADO` en mayúsculas** (`TramiteCambioEstadoEmailComposer.cs:81-82`), aunque el cuerpo no afirme finalidad. Si eso resulta demasiado tajante para un rechazo subsanable, el texto es del PO.
- **El saludo de la variante Renting es nominal y singular** — «**{comprador}**, ¡Es un gusto saludarte!» (`TramiteCambioEstadoEmailComposer.cs:211`). Con comprador PJ saluda a la razón social, también en la copia dirigida al representante legal. Personalizarlo por destinatario exige un campo nuevo en `TramiteCambioEstadoEmailModel` y tocar el composer, sus pruebas y las muestras del banco de pruebas: **mueve el alcance** y no se asume aquí.
- **`rechazado` con subsanación.** Verificado: la subsanación **no es una transición** — la enciende `StartSubsanacionCommand.cs:37` sin tocar `status`, así que no produce fila de outbox ni correo. El correo de rechazo sale una vez, al rechazar, y su texto no afirma que el trámite esté cerrado («Estamos revisando el caso para orientarte en los siguientes pasos», `TramiteCambioEstadoEmailComposer.cs:192`). La re-radicación (`rechazado→entregado`) **no** notifica; un segundo rechazo sí genera un segundo correo, porque es otra fila de outbox y por tanto otro `outbox_id`. Si el PO quiere avisar al cliente de que puede subsanar, eso es una plantilla y un disparador nuevos, no un ajuste de este mecanismo.

## ADRs relacionados

- [ADR-0022] — estados de negocio del ciclo de vida del trámite: el vocabulario que este ADR mapea a plantillas.
- [ADR-0033] — la subsanación es un flag sobre `rechazado`, no un séptimo estado. De ahí que no dispare correo.
- [ADR-0030] — persona/entidad y prevalidación. Introduce las columnas `Person.LegalRep*`, que son un **tercer** concepto de representante legal, distinto del de `actor.Metadata` y del directorio admin y sin sincronizar con ellos. Para la notificación manda el de `actor.Metadata`; este ADR no unifica los tres.
- [ADR-0036] — mandatarios y mandato por OT: el representante legal del `metadata` del actor es el mismo que ese ADR usa como firmante del mandante PJ. El **mandatario** (gestor) no es destinatario de este correo.
- [ADR-0043] — precedente de desacoplar una elegibilidad del canal de notificación; aquí se aplica el criterio inverso y explícito: la **variante del cuerpo** sí depende del canal, y por eso comparte su resolución.
- [ADR-0044] — el desvío de destinatario del canal Renting. Este ADR no lo toca: el correo de trámite pasa por el mismo `IEmailSender` y hereda el desvío y su marca `recipient_diverted`.

## Notas para agentes

- **Database Agent**: crear `tramites.procedure_state_change_email_dispatches` (DDL `69-…`, idempotente con `IF NOT EXISTS`, RLS `tenant_isolation` como sus vecinas de `tramites`), con `recipient_role` (rol en el trámite) y `recipient_kind` (`persona|empresa|representante_legal`) **como columnas separadas**: colapsarlas impide distinguir «el vendedor empresa» de «el representante legal del vendedor». Índices: `UNIQUE (outbox_id, lower(recipient)) WHERE recipient IS NOT NULL` y `UNIQUE (outbox_id, recipient_role, recipient_kind) WHERE recipient IS NULL` —**no** `UNIQUE (outbox_id)`, que solo admitiría un cupo vacío por evento cuando puede haber hasta cuatro—, más índice parcial de cola `(queued_at) WHERE status = 'pendiente'`. FK a la outbox `ON DELETE CASCADE`.
- **Backend Agent**: el sink **jamás** se registra como `IProcedureStateChangeNotifier` suelto — se suma dentro del composite, y la composición se centraliza para cubrir las dos rutas de registro. El sink no hace I/O de red. El worker de envío usa `IEmailSender` (no `IExplicitChannelEmailSender`: esa interfaz tiene una prueba de línea base que falla ante un tercer consumidor, `ExplicitChannelEmailSenderRegistrationTests.cs`). El nombre del destinatario va en `EmailMessage.ToName` (`IEmailSender.cs:25-26`) — razón social en la fila de la empresa, nombre del RL en la suya; el **cuerpo** no cambia entre ambas y por eso el composer no se toca.
- **Frontend Agent**: sin cambios. Ninguna pantalla nueva.
- **QA Agent**: tres casos obligatorios. (1) «ICT falla, correo ya despachado»: la fila del outbox se reintenta y el cliente **no** recibe un segundo correo. (2) «trámite sin ningún correo»: no hay excepción, hay filas `omitido`. (3) «traspaso PJ↔PJ»: cuatro filas, y si empresa y RL comparten buzón, **una sola** salida — no dos mensajes idénticos.
- **Security Agent**: `recipient` es `@pii:medium` (Ley 1581), misma clasificación que `notification_delivery_logs.recipient`; finalidad exclusiva de trazabilidad del envío. La tabla no guarda cuerpo del mensaje. El punto sensible del cambio no es la tabla sino la resolución: **un destinatario mal inferido es una divulgación de datos del trámite a un tercero**, por lo que ningún cupo se rellena con un correo que el sistema no pueda afirmar de quién es (de ahí que `ProcedureInstanceParticipant` no sirva de respaldo en PJ, y que el directorio admin de RL quede fuera).
- **Infra Agent**: un `BackgroundService` más; sin variables de entorno nuevas. El log `Critical` de dead-letter del correo necesita destino en la alerta.

## Referencias externas

- Transactional outbox / chained outbox — patrón ya en uso en `tramites.identity_validation_outbox` y `tramites.procedure_state_change_outbox`.
