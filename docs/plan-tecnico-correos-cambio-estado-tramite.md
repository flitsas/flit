# Plan técnico — Correo al ciudadano cuando su trámite cambia de estado

> Generado: 2026-08-12 · Rama de trabajo: pendiente de crear
> Método: panel de refinamiento (`explore-agent` ×2 → `po-agent` + `architecture-agent` ×2 rondas →
> `tech-lead-agent`), con verificación directa del hilo orquestador sobre los puntos en disputa.
> Diseño formal: `services/core-api/docs/adr/ADR-0045-disparador-correo-cambio-estado-tramite.md`
> (estado **Propuesto**).

---

## 1. El encuadre correcto

Esto **no es «construir el correo del trámite»**. Todo el transporte existe y funciona:

| Pieza | Estado |
|---|---|
| Catálogo de plantillas (8, con id estable) | ✅ existe |
| Plantillas `tramites.aprobado` / `tramites.rechazado`, con variante FLIT y Renting | ✅ existen y se renderizan |
| Enrutamiento por canal de la compañía (`TenantChannelEmailRouter`) | ✅ existe |
| Adaptadores SMTP y API Renting (login, caché de token, mTLS) | ✅ existen |
| Bitácora de envíos (`admin.notification_delivery_logs`) | ✅ existe |
| Banco de pruebas con muestra por canal | ✅ existe |
| **Disparador productivo** | ❌ **no existe** |

El propio código lo declara: *«sin disparador productivo aún»*
(`TramiteCambioEstadoEmailComposer.cs:8`) y *«el handler productivo se conecta en una fase
posterior»* (`NotificationTrigger.cs:22-26`).

**Consecuencia hoy:** ningún actor de ningún trámite —comprador, vendedor, radicador, OT— recibe
jamás un aviso por correo. El ciudadano solo se entera preguntando, y esa pregunta la absorbe el
gestor.

El valor del Feature no está en el volumen de código, sino en las decisiones que conectar ese cable
obliga a tomar.

---

## 2. Decisión de diseño: sink que encola + worker propio

Se evaluaron cuatro opciones. La elegida es **un sink dentro del
`CompositeProcedureStateChangeNotifier` que solo hace `INSERT` en una cola propia, y un
`BackgroundService` separado que envía**.

| | **Op.1 — sink encola + worker (elegida)** | Op.2 — sink envía inline | Op.3 — consumidor propio del outbox | Op.4 — síncrono en el lifecycle |
|---|---|---|---|---|
| Aísla OT/ICT | por construcción | por disciplina | total | no |
| Idempotencia | `UNIQUE (outbox_id, lower(recipient))` | exige la misma tabla | por evento, no por destinatario | n/a |
| Reintento del correo | propio | ninguno: fallo transitorio = correo perdido | propio | n/a |
| Defecto grave | retención por definir | red dentro del `FOR UPDATE` | outbox de N consumidores por columnas | **anuncia antes de confirmar** |

**Por qué no la Opción 2**, que parecía la barata: sin marca propia, un fallo del reflejo a core-ict
hace que el outbox reintente, el composite re-ejecute el sink y **el cliente reciba un segundo
correo**. Y la bitácora no puede servir de marca porque no guarda `procedure_instance_id` ni id de
evento.

**Por qué no la Opción 4**, por una razón concreta y no genérica: en
`TramiteLifecycleService.cs:229-232` **la transición todavía no está confirmada** —
`SaveChangesWithConcurrencyGuardAsync` puede devolver `false` por conflicto en la línea siguiente. Se
le anunciaría al ciudadano una aprobación que se revierte.

### Idempotencia

La llave es `(outbox_id, lower(recipient))`, con `ON CONFLICT DO NOTHING`, **no lógica en C#**.

- No sirve `(procedure_instance_id, to_status)`: un trámite rechazado dos veces **debe** recibir dos
  correos.
- `outbox_id` es exactamente «un aviso de negocio».
- El `INSERT` va **antes** de enviar: se prefiere perder un correo a duplicarlo.

---

## 3. A quién se le escribe

Requisito del PO (2026-08-12): **si el actor es persona jurídica, el correo va a la empresa y a su
representante legal; si es natural, a la persona.**

El cambio de fondo es que **el destinatario deja de derivarse del rol y pasa a derivarse del tipo de
persona**. Un rol produce uno o dos *cupos*.

| Actor | Cupos | De dónde sale el correo |
|---|---|---|
| Natural | `Persona` | `Participant(rol).Email` ▸ `actor.Email` |
| Jurídica | `Empresa` **+** `RepresentanteLegal` | Empresa = **solo** `actor.Email` · RL = **solo** `actor.Metadata.representanteLegal.Email` |

Roles considerados: `comprador` siempre; `vendedor` solo si la modalidad es traspaso. El
`mandatario` queda fuera: es el gestor, no el cliente.

**Por qué `Participant` sale de la ecuación cuando el actor es PJ.** No distingue persona jurídica,
no tiene FK al actor y no dice a quién representa (`ProcedureInstanceParticipant.cs:13-24`). Como
respaldo del cupo *Empresa* sería directamente falso; como respaldo del cupo *RL* sería
*probablemente* cierto — y «probablemente» no basta cuando el error es mandarle datos del trámite a
un tercero (Ley 1581). En persona natural sí se conserva con precedencia, porque ahí participante y
actor son la misma persona por construcción del magic-link.

**Cupo sin correo ≠ error.** Cada cupo tiene desenlace propio: empresa con correo y RL sin él produce
una fila `pendiente` y una `omitido`. **Nunca se sustituye un cupo por el otro** — mandarle al RL el
correo que era de la empresa no es una degradación elegante, es escribirle a un destinatario
jurídicamente distinto del previsto.

---

## 4. Las cinco trampas verificadas

Cada una está confirmada contra el código, no inferida.

**1. El composite de notificadores no siempre existe.**
`CompositeProcedureStateChangeNotifier` solo se registra si hay `Ict:StateCallback:Address`
(`Ict/IctStateReflection.cs:41-46,73-82`). Sin esa clave, el `IProcedureStateChangeNotifier` vigente
es el webhook del OT pelado, registrado en **otro sitio**
(`AdminInfrastructureExtensions.cs:260-262`). Son dos registros donde «la última gana»: engancharse
solo a uno deja el correo muerto en los despliegues sin canal inverso. **Es el riesgo mayor del
Feature y es de inyección de dependencias, no de lógica**: un registro de más silencia los webhooks
sin error de compilación ni test rojo evidente.

**2. Activar la subsanación no dispara nada — y eso es correcto.**
`StartSubsanacionCommand` enciende el flag **sin tocar `status`** y con su propio `SaveChanges`; no
pasa por `TramiteLifecycleService`, no escribe fila de outbox. Su propio comentario lo declara:
*«Activar la subsanación no es una transición»*. El correo de rechazo sale una sola vez, en el
momento del rechazo, cuando el rechazo es real.

> Esto **desmonta** la premisa fuerte con la que el PO exigía una tercera plantilla de subsanación
> como bloqueante. La objeción sobrevive **reducida al asunto**: el cuerpo es neutro e invita a
> contactar, pero el asunto dice `— RECHAZADO` en mayúsculas
> (`TramiteCambioEstadoEmailComposer.cs:81-82`). La tercera plantilla pasa de bloqueante a mejora.

**3. Hay tres «representantes legales» distintos y no están sincronizados.**
El RL embebido en `actor.Metadata` (por trámite), las columnas `Person.LegalRep*` (ADR-0030), y el
directorio `admin.company_legal_representatives` (por compañía, HU #10900/#10932). **Para este
Feature manda el primero.** El directorio admin **no sirve**: la empresa actora de un trámite no es
necesariamente una compañía registrada en FLIT — su NIT es un string capturado en el wizard, sin FK
a `admin.companies` ni a `identity.tenants`.

**4. El precedente que hay que extender hace lo contrario de lo pedido.**
`IdentitySubjectResolver.cs:34-37` documenta que para PJ el correo es *siempre* el del RL y **nunca**
el `actor.Email` de la empresa. Ese criterio es correcto **para lo suyo** (un NIT no se biometriza) y
sigue vigente. Los dos resolvers conviven con dominios disjuntos, y el ADR lo deja escrito para que
nadie «unifique» ambos criterios más adelante creyendo que corrige una inconsistencia.

**5. `PersonType` puede venir `NULL` en trámites legacy.**
Se propone inferir PJ cuando `DocumentType == "NIT"`, que es **exactamente el criterio que el repo ya
aplica al mismo dilema** en `FurCommand.cs:1428-1429`. El fallo residual queda del lado seguro: una
PJ legacy documentada con otra cosa se resuelve como natural y recibe `actor.Email` — que en PJ *es*
el correo de la empresa. Se pierde la copia al RL; nunca se escribe a quien no toca.

---

## 5. Decisiones que son del PO humano

Ninguna la puede cerrar un agente. Ordenadas por impacto en el alcance.

| # | Decisión | Recomendación del panel | Impacto si cambia |
|---|---|---|---|
| 1 | ¿Entra `entregado` («ya quedó radicado»)? | **No en v1** — no tiene plantilla ni texto legal aprobado | Plantilla nueva + copy. El propio PO lo señala como el aviso de mayor valor percibido |
| 2 | ¿Tercera plantilla de subsanación? | **Mejora, ya no bloqueante** (ver trampa 2) | Plantilla + copy + muestras del banco |
| 3 | `PersonType = NULL` legacy | Inferir PJ por `NIT`; degradado silencioso | Alternativa: no notificar esos trámites |
| 4 | Cupos sin correo (`omitido`) | Solo registrar + hacerlos visibles al gestor | Si se quiere acción (reintento manual, captura), es HU aparte |
| 5 | ¿Kill-switch por compañía u OT? | **No** — solo interruptor operativo global | Un modelo de suscripciones no existe y triplica el Feature |
| 6 | Retención de PII de la bitácora | **Feature aparte, inmediatamente después** | Requiere plazo concreto (¿6 meses? ¿2 años?) y qué se hace al vencer |
| 7 | Saludo nominal de la variante Renting | Levantado, no resuelto | `«{comprador}, ¡Es un gusto saludarte!»` (`:211`) saluda a la razón social, también en la copia del RL. La variante FLIT no tiene el problema (`:111-113`) |
| 8 | Asunto `— RECHAZADO` en mayúsculas | Levantado, no resuelto | Ver trampa 2 |
| 9 | Placa vacía en matrícula inicial | Fallback a `ReferenceNumber` | El asunto la incrusta; sin ella queda `— … — APROBADO` |

---

## 6. Alcance

### Entra
1. Disparo del correo en `aprobado` y `rechazado`.
2. Resolución de destinatarios con cupos empresa + RL para PJ.
3. Cola de despacho propia, idempotente por destinatario.
4. Visibilidad del no-envío para el gestor.
5. Kill-switch operativo global.

### Queda fuera
| Qué | Por qué |
|---|---|
| Avisos en `entregado` y `anulado` | Sin plantilla ni texto aprobado. Backlog, `entregado` con prioridad alta |
| Notificar a la oficina de tránsito | Es quien **produce** la decisión; notificarle es ruido |
| Correo al gestor | Ya tiene el board, y ahora la señal en pantalla. Un correo por trámite genera fatiga |
| Suscripción configurable por compañía u OT | El modelo no existe |
| Purga de `notification_delivery_logs` | Feature aparte (decisión 6) |
| Capturar el correo faltante en el wizard | Cambia el flujo de captura. Otro Feature |

---

## 7. Riesgos

1. **Romper OT/ICT en el registro de DI** — trampa 1. Mitigación: composición centralizada + test que
   afirme que el notificador resuelto es un composite con los sinks esperados **en ambas rutas** (con
   y sin `Ict:StateCallback:Address`).
2. **La prueba de no-duplicación no prueba nada en InMemory.** Los índices únicos parciales de
   Postgres no existen ahí, y el processor tiene rama LINQ para tests no relacionales
   (`ProcedureStateChangeOutboxProcessor.cs:85-89,119-132`). Tiene que correr contra Postgres real.
3. **Cuerpo y transporte divergiendo de canal.** La variante del HTML (FLIT vs Renting) y el
   transporte se resuelven hoy por separado; sin resolver compartido puede salir un cuerpo con marca
   FLIT por la API de Renting.
4. **PII creciente sin retención.** La tabla nueva amplía una deuda que ya existe en
   `notification_delivery_logs`. No bloquea la entrega; bloquea dejarlo corriendo indefinidamente.
5. **RLS decorativa.** Como el resto del esquema, la policy no se evalúa (sin `FORCE`, app como
   owner). El aislamiento real es el `WHERE tenant_id` explícito: el worker debe filtrar a mano.

---

## 8. Descomposición

**10 HUs / 36 SP**, partidas en dos Features hermanos porque la regla 5 del `tech-lead-agent` fija un
máximo de 8 HUs por Feature. La descomposición completa —con criterios Gherkin, archivos previstos,
orden de implementación y agrupación en PRs— está en
[`descomposicion-hus-correos-cambio-estado.md`](descomposicion-hus-correos-cambio-estado.md).

| Feature | ADO | HUs | SP |
|---|---|---|---|
| F1 — Disparador y envío del aviso | **#11459** | 7 (#11461-67) | 26 |
| F2 — Control operativo y visibilidad del aviso | **#11460** | 3 (#11468-70) | 10 |

Publicados el 2026-08-12 en **Sprint 3** (el siguiente al activo), estado `New`, tag `DOR`.
**No activar** hasta que el Líder Técnico acepte el ADR-0045 y el PO cierre las decisiones de §5.

Tres cosas de esa descomposición que condicionan la ejecución y no solo la planeación:

1. **≈ 4.085 líneas ⇒ 7 PRs.** No cabe en una, ni siquiera partiendo en dos Features. La estrategia
   acordada («una rama por Feature») sigue siendo viable con PRs secuenciales desde la misma rama,
   porque cada commit deja la rama verde — pero es una decisión que hay que tomar antes de arrancar.
2. **HU-E y HU-G deben desplegarse juntas.** Encolar sin worker es inocuo, pero al llegar el worker
   vacía la cola de golpe: si pasaron días, los clientes reciben avisos de trámites viejos.
3. **La HU peligrosa es la de centralizar los sinks** (trampa 1). Va aislada, en commit propio y
   antes de que exista el sink de correo, para que un fallo se vea en 95 líneas de producción y no
   dentro de 430.

---

## 9. Riesgo de negocio de entregar a medias

**Notificar solo a quien tiene correo, en silencio, es peor que no notificar.** Se crea una
expectativa nueva («FLIT ahora avisa») que se cumple de forma intermitente e invisible: el ciudadano
que no recibe nada asume que su trámite no avanzó, y el gestor no sabe a quién sí le llegó. Por eso
la visibilidad del `omitido` no es un extra — es la condición para que la promesa sea honesta.
