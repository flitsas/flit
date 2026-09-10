# Descomposición en HUs — Correo al ciudadano cuando su trámite cambia de estado

> Generado: 2026-08-12 · Plan: `docs/plan-tecnico-correos-cambio-estado-tramite.md`
> Diseño: `services/core-api/docs/adr/ADR-0045-disparador-correo-cambio-estado-tramite.md` (**Propuesto**)
> **Publicado en Azure DevOps el 2026-08-12**, Sprint 3 (el siguiente al activo), estado `New`,
> tag `DOR`. Los ids `HU-A…HU-J` son las etiquetas internas de este documento; la columna **ADO**
> lleva el id real.

---

## Partición en dos Features

La descomposición honesta da **10 HUs / 36 SP**. La regla 5 del `tech-lead-agent` fija un máximo de
8 HUs por Feature, así que se parte en dos hermanos. **No recorta alcance, solo lo agrupa.**

| Feature | ADO | HUs | SP | Contenido |
|---|---|---|---|---|
| **F1 — Disparador y envío del aviso** | **#11459** | HU-A … HU-G (7) | 26 | La capacidad: encolar y enviar |
| **F2 — Control operativo y visibilidad del aviso** | **#11460** | HU-H, HU-I, HU-J (3) | 10 | Kill-switch, panel del gestor, golden file |

F2 depende de F1 salvo HU-J, que es independiente.

---

## Tabla resumen

| Id | ADO | Título | Tipo | SP | Depende de | Líneas est. |
|---|---|---|---|---|---|---|
| HU-A | **#11461** | Esquema de la cola de despachos de correo de cambio de estado | BACKEND | 3 | — | 390 |
| HU-B | **#11462** | Resolución de destinatarios del aviso por tipo de persona | BACKEND | 5 | — | 675 |
| HU-C | **#11463** | Proyección de datos del correo y mapa estado→plantilla | BACKEND | 3 | — | 330 |
| HU-D | **#11464** | Centralizar la composición de sinks de cambio de estado | BACKEND | 3 | — | 350 |
| HU-E | **#11465** | Sink que encola los despachos al aprobar o rechazar | BACKEND | 5 | A, B, C, D | 430 |
| HU-F | **#11466** | Resolutor de canal compartido entre cuerpo y transporte | BACKEND | 2 | — | 230 |
| HU-G | **#11467** | Worker de envío de la cola de avisos | BACKEND | 5 | A, C, F | 520 |
| HU-H | **#11469** | Interruptor operativo de avisos por compañía | FULLSTACK | 3 | A, G | 330 |
| HU-I | **#11470** | El gestor ve a quién no se pudo notificar y por qué | FULLSTACK | 5 | A, G | 650 |
| HU-J | **#11468** | Golden file de las plantillas de trámite | BACKEND | 2 | — | 180 |

**Total ≈ 4.085 líneas.** No cabe en una sola PR (límite FLIT: 800).

---

## HU-A · [BACKEND] – Esquema de la cola de despachos · 3 SP

**Objetivo.** Materializar `tramites.procedure_state_change_email_dispatches` con **la idempotencia
como restricción de base**, para que ninguna HU posterior escriba deduplicación en C#.

Solo esquema: aquí no se inserta ni se lee una fila. Sigue la forma de
`admin.notification_delivery_logs` (DDL 64): RLS `tenant_isolation`, `recipient` etiquetado
`@pii:medium` con finalidad declarada. `recipient_role` y `recipient_kind` van como **columnas
separadas** — colapsarlas impide distinguir «el vendedor empresa» del «representante legal del
vendedor». **Dos** índices únicos parciales, no uno.

```gherkin
Escenario: Un mismo buzón no puede repetirse dentro del mismo evento
  Dado el DDL 69
  Cuando se inspeccionan sus índices
  Entonces existe un índice UNIQUE sobre (outbox_id, lower(recipient)) con WHERE recipient IS NOT NULL
    Y existe un índice UNIQUE sobre (outbox_id, recipient_role, recipient_kind) con WHERE recipient IS NULL

Escenario: Un evento puede registrar hasta cuatro cupos vacíos
  Dado el DDL 69
  Cuando se buscan restricciones de unicidad sobre outbox_id
  Entonces NO existe ninguna restricción UNIQUE cuya única columna sea outbox_id

Escenario: La tabla nace con aislamiento por tenant y el destinatario clasificado
  Dado el DDL 69 embebido en el ensamblado
  Cuando se inspecciona su contenido
  Entonces tiene columnas separadas recipient_role y recipient_kind
    Y habilita ROW LEVEL SECURITY con la política tenant_isolation
    Y la columna recipient lleva COMMENT con la etiqueta @pii: y su finalidad

Escenario: Reaplicar la migración no rompe ni duplica
  Dado el DDL 69
  Cuando se analizan sus sentencias
  Entonces no aparece ningún CREATE TABLE, CREATE INDEX ni ADD COLUMN sin su guarda IF NOT EXISTS
```

**Archivos:** `Ddl/69-tramite-state-email-dispatch.sql`, migración EF,
`Flit.Tramites.Domain/Entities/ProcedureStateChangeEmailDispatch.cs`, su `Configuration`,
`FlitDbContext` + `ModelSnapshot`.

---

## HU-B · [BACKEND] – Resolución de destinatarios por tipo de persona · 5 SP

**Objetivo.** Un único punto que responda «a quién se le escribe este trámite»: en persona jurídica
**dos** destinatarios (empresa y representante legal); en natural, uno.

Introduce `ITramiteNotificationRecipientResolver`, `TramiteEmailRecipient`, `TramiteRecipientGap`,
`TramiteRecipientResolution` y el enum `TramiteRecipientKind (Persona|Empresa|RepresentanteLegal)`.
Reutiliza el parser de RL de `IdentitySubjectResolver` **cambiando solo su visibilidad** — ya hay
tres parsers del mismo JSON y este no puede ser el cuarto. El criterio de identidad (en PJ el correo
es el del RL, nunca el de la empresa) **no cambia** y queda fijado por una regresión: los dos
criterios conviven porque responden preguntas distintas.

```gherkin
Escenario: Una parte persona jurídica produce dos destinatarios distintos
  Dado un actor comprador 'juridical' con Email de empresa
    Y un representante legal en su metadata con correo propio
  Cuando se resuelven los destinatarios
  Entonces se obtienen dos: uno de tipo Empresa con la razón social
    Y otro de tipo RepresentanteLegal con el nombre del representante

Escenario: El cupo del representante legal sin correo NO se rellena con el de la empresa
  Dado un actor 'juridical' con correo de empresa y un representante legal sin correo
  Cuando se resuelven los destinatarios
  Entonces el cupo Empresa queda resuelto
    Y el cupo RepresentanteLegal queda registrado como hueco sin correo
    Y NINGÚN destinatario repite el correo de la empresa para el representante legal

Escenario: El participante del portal no respalda a una parte jurídica
  Dado un actor 'juridical' sin Email y sin representante legal en metadata
    Y un participante del rol comprador con correo
  Cuando se resuelven los destinatarios
  Entonces no se resuelve ninguno para esa parte
    Y el correo del participante no aparece en el resultado

Escenario: Una parte persona natural usa el participante del portal con precedencia
  Dado un actor comprador 'natural' con Email propio y un participante del mismo rol con otro
  Cuando se resuelven los destinatarios
  Entonces se obtiene uno solo, de tipo Persona, con el correo del participante

Escenario: Persona jurídica legacy sin PersonType pero con NIT
  Dado un actor comprador con PersonType nulo y DocumentType 'NIT'
  Cuando se resuelven los destinatarios
  Entonces se trata como jurídica y se esperan los cupos Empresa y RepresentanteLegal

Escenario: El vendedor solo se notifica en traspaso
  Dado un trámite cuya modalidad no es traspaso, con comprador y vendedor con correo
  Cuando se resuelven los destinatarios
  Entonces solo se resuelven los del rol comprador

Escenario: El criterio de identidad no se mueve
  Dado un actor 'juridical' con correo de empresa y representante legal con correo
  Cuando se invoca IdentitySubjectResolver.For sobre ese actor
  Entonces el Email del sujeto sigue siendo el del representante legal
```

> **Es una HU de privacidad disfrazada de HU de lógica.** Un destinatario mal inferido no es un bug
> de correo: es divulgar datos de un trámite a un tercero (Ley 1581). Los dos escenarios negativos
> son los que hay que revisar con lupa, no los positivos.

---

## HU-C · [BACKEND] – Proyección de datos y mapa estado→plantilla · 3 SP

Arma el `TramiteCambioEstadoEmailModel` desde el trámite persistido y decide qué plantilla
corresponde, **sin tocar el composer**. El organismo de tránsito sale de `field_values`
(`transit_office_name`, `transit_office_city` vía `TransitOfficeCity.Legible`) — la misma fuente que
`FurCommand.cs:657-660` — y **no** de `TransitOfficeId`, que es null hasta radicar.

```gherkin
Escenario: Solo aprobado y rechazado tienen plantilla
  Dado el mapa de transiciones a plantillas
  Cuando se consulta 'aprobado' devuelve 'tramites.aprobado'
    Y cuando se consulta 'rechazado' devuelve 'tramites.rechazado'

Escenario: Una transición sin plantilla no inventa ninguna
  Cuando se consulta el destino 'anulado'
  Entonces no devuelve ninguna plantilla y no lanza excepción

Escenario: Un trámite sin placa no rompe la proyección
  Dado un trámite con Plate nulo
  Cuando se proyecta el modelo del correo
  Entonces Placa es cadena vacía y no se lanza excepción
```

---

## HU-D · [BACKEND] – Centralizar la composición de sinks · 3 SP

**Objetivo.** Un único lugar donde se decide qué sinks recibe el fan-out del outbox, **sin cambiar el
comportamiento actual**, para que HU-E pueda sumar el correo sin jugarse los webhooks del OT.

Hoy hay dos registros y gana el último: `AdminInfrastructureExtensions.cs:261-262` mapea el notifier
OT, e `Ict/IctStateReflection.cs:73-82` lo sobrescribe con el compuesto OT+ICT **solo si** existe
`Ict:StateCallback:Address`. Se extrae a `ProcedureStateChangeNotifierRegistration`, invocado por
ambas rutas. Cero cambio funcional observable.

```gherkin
Escenario: Sin canal inverso ICT configurado el fan-out es solo el webhook OT
  Dado un contenedor sin Ict:StateCallback:Address
  Cuando se resuelve IProcedureStateChangeNotifier
  Entonces el fan-out contiene exactamente el sink de webhooks OT

Escenario: Con canal inverso ICT configurado el fan-out son OT e ICT, en ese orden
  Dado un contenedor con Ict:StateCallback:Address definido
  Cuando se resuelve IProcedureStateChangeNotifier
  Entonces contiene exactamente el sink de webhooks OT y el de reflejo ICT

Escenario: Nadie registra el fan-out por fuera del punto centralizado
  Cuando se buscan registros de IProcedureStateChangeNotifier en la infraestructura
  Entonces el único está en ProcedureStateChangeNotifierRegistration

Escenario: Un sink que falla no impide que los demás despachen, pero el outbox reintenta
  Dado un fan-out con un sink que lanza y otro que no
  Cuando se notifica un cambio de estado
  Entonces el sink sano recibe la notificación
    Y la llamada termina lanzando AggregateException para que el outbox reintente
```

---

## HU-E · [BACKEND] – Sink que encola los despachos · 5 SP

Se suma **dentro** del compuesto (vía HU-D). Hace tres cosas y ninguna de red: mapea estado→plantilla
(HU-C), resuelve destinatarios (HU-B) e inserta con `ON CONFLICT DO NOTHING`. Orden determinista
—`comprador`→`vendedor`, y dentro de cada rol `empresa`→`representante_legal`— para que al colapsar
un buzón compartido sobreviva siempre la misma fila.

```gherkin
Escenario: Un reintento del outbox no encola un segundo correo
  Dado un evento cuyos despachos ya se encolaron
  Cuando el mismo evento se vuelve a despachar por un reintento
  Entonces el número de filas de ese outbox_id no cambia

Escenario: Un traspaso jurídica contra jurídica produce cuatro cupos
  Dado comprador y vendedor jurídicos, cada uno con correo de empresa y de representante legal
  Cuando se despacha el evento a 'aprobado'
  Entonces existen cuatro filas, con recipient_kind 'empresa' y 'representante_legal' por rol

Escenario: Empresa y representante legal con el mismo buzón reciben un solo mensaje
  Dado un comprador jurídico cuya empresa y representante legal comparten correo
  Cuando se despacha el evento
  Entonces existe una única fila para ese buzón
    Y queda registro en log del cupo colapsado

Escenario: Un trámite sin ningún correo no lanza excepción
  Dado un trámite cuyos actores no tienen correo y sin participantes
  Cuando se despacha el evento a 'rechazado'
  Entonces existen filas 'omitido' con su cupo declarado
    Y el despacho termina sin excepción

Escenario: Una transición sin plantilla no encola nada
  Cuando se despacha un evento a 'entregado'
  Entonces no se crea ninguna fila de despacho

Escenario: Un fallo del sink de correo no impide el webhook OT
  Dado un fan-out con el sink de correo fallando
  Cuando se despacha el evento
  Entonces el sink de webhooks OT recibió la notificación
```

---

## HU-F · [BACKEND] – Resolutor de canal compartido · 2 SP

Extrae `TenantChannelEmailRouter.ResolveChannelAsync` (`:191-200`) a un `INotificationChannelResolver`
inyectable. **Sin cambio de comportamiento**: mismos defaults (sin tenant resoluble o sin política
operativa ⇒ `FlitSmtp`). Sin esto, el worker resolvería el canal por su cuenta y un tenant Renting
podría recibir el cuerpo con marca FLIT enviado por la API de Renting.

```gherkin
Escenario: Sin tenant resoluble el canal es el SMTP de FLIT
Escenario: Sin política operativa el canal es el SMTP de FLIT
Escenario: El tenant con canal de API mantiene su enrutamiento actual
Escenario: El bypass de los correos de cuenta no se altera
  Dado una plantilla del módulo Security y un tenant con canal 'tenant_api'
  Cuando se envía por el router
  Entonces sale por el SMTP de FLIT
```

> **Refactor con apariencia de trivial.** El router es el paso obligado de los ~6 envíos productivos
> existentes; equivocarse en el default desvía correos de cuenta al canal de un cliente. Su red de
> seguridad es la suite existente del router: si se toca sin dejarla verde, parar.

---

## HU-G · [BACKEND] – Worker de envío de la cola · 5 SP

`BackgroundService` calcado de `ProcedureStateChangeOutboxProcessor` (poll 5 s, `FOR UPDATE SKIP
LOCKED`, rama LINQ para providers no relacionales). Envía por `IEmailSender` — **no** por
`IExplicitChannelEmailSender`, cuya prueba de línea base falla ante un tercer consumidor. Pone el
nombre en `EmailMessage.ToName`: razón social en la fila de empresa, nombre del RL en la suya.
**Reintentos propios**: nunca consume el `attempts` compartido del outbox de estados.

```gherkin
Escenario: El nombre del destinatario distingue empresa de representante legal
  Dado dos filas del mismo evento, una 'empresa' y otra 'representante_legal'
  Cuando el worker procesa un ciclo
  Entonces el mensaje de empresa lleva la razón social
    Y el de representante legal lleva el nombre del representante

Escenario: La variante del cuerpo sigue al canal del tenant
  Dado un tenant con canal 'tenant_api' y una fila pendiente
  Entonces el cuerpo compuesto es la variante Renting

Escenario: Un fallo de envío deja la fila para el próximo ciclo
  Entonces la fila sigue pendiente con attempts incrementado y sin sent_at

Escenario: Agotar los intentos no reintenta más y deja rastro crítico
  Entonces la fila queda 'fallido', se registra un log Critical con el identificador del trámite
    Y un ciclo posterior no vuelve a reclamarla

Escenario: Las filas omitidas nunca se reclaman
```

---

## HU-H · [FULLSTACK] – Interruptor operativo de avisos por compañía · 3 SP

Columna `tramite_state_emails_enabled boolean NOT NULL DEFAULT true` en
`admin.tenant_operational_policies`, expuesta por el mismo camino que
`DocumentosPersonalizadosActivo` y como casilla en `ConfiguracionEmpresaTab`.

**Se evalúa en el worker, no en el sink.** Apagado, las filas quedan `pendiente` sin gastar intentos
y salen al reanudar; evaluarlo en el sink perdería los eventos para siempre.

```gherkin
Escenario: Con el interruptor apagado no sale ningún correo
  Entonces no se envía nada y la fila sigue pendiente con attempts sin incrementar

Escenario: Al reanudar se envía lo acumulado
Escenario: Apagar una compañía no afecta a las demás
Escenario: El valor por defecto no cambia el comportamiento de ninguna compañía existente
  Entonces la columna se agrega con ADD COLUMN IF NOT EXISTS y DEFAULT true
```

> **Decisión de despliegue del PO:** nace en `true` (avisos encendidos para todos al desplegar). Si
> se prefiere activación explícita por compañía, el default pasa a `false`.

---

## HU-I · [FULLSTACK] – El gestor ve a quién no se pudo notificar · 5 SP

Endpoint `GET /instances/{id}/notification-dispatches` siguiendo `StatusHistoryEndpoints.cs` y panel
`AvisosCorreoPanel` con la forma de `IdentityValidationTrackingPanel.tsx`. El correo se devuelve
**enmascarado**: el gestor necesita saber a quién faltó escribirle, no leer la dirección completa.

```gherkin
Escenario: El motivo del hueco es legible
  Dado un despacho 'omitido' por falta de correo del representante legal
  Entonces la fila indica que no había correo para el representante legal

Escenario: El correo se muestra enmascarado
  Entonces la dirección se muestra parcialmente oculta y no aparece completa en la respuesta

Escenario: Nadie consulta los despachos de otra compañía
  Entonces la respuesta es 404 y no revela ningún destinatario

Escenario: Un trámite sin avisos no muestra un panel vacío roto
Escenario: El gestor ve el desenlace de cada destinatario
```

---

## HU-J · [BACKEND] – Golden file de las plantillas de trámite · 2 SP

Cierra un hueco **preexistente**: trámites es el único módulo del catálogo sin golden file (Seguridad
y Analítica sí tienen). Fija asunto y cuerpo de las dos plantillas en sus dos variantes con
`tests/Shared/EmailGolden.cs`.

**Va primero de todo el lote**: es la red que detecta si alguna HU posterior altera sin querer el
cuerpo del correo que se está poniendo en producción. Coste real bajo: los fixtures son de una línea.

---

## Orden de implementación

Cada commit deja la rama compilando y la suite verde: los cuatro primeros no tienen consumidores y
los tres refactores no cambian comportamiento.

| # | HU | Por qué aquí |
|---|---|---|
| 1 | **HU-J** golden | Solo pruebas. Fija el cuerpo actual **antes** de que nada toque la composición |
| 2 | **HU-A** esquema | Tabla sin lectores: compila y no altera runtime |
| 3 | **HU-F** canal compartido | Refactor puro, disponible antes de que el worker lo pida |
| 4 | **HU-D** composición centralizada | El punto peligroso, **aislado**: si rompe OT/ICT se ve en 95 líneas de producción, no dentro de 430 |
| 5 | **HU-B** destinatarios | La HU densa; entra sola |
| 6 | **HU-C** proyección y mapa | Lógica pura sin consumidores |
| 7 | **HU-E** sink | Primer cambio de comportamiento; ya tiene todo lo que necesita |
| 8 | **HU-G** worker | Cierra el circuito |
| 9 | **HU-H** kill-switch | Su comportamiento verificable **es** que el worker no envíe |
| 10 | **HU-I** visibilidad | Pinta lo que las anteriores producen |

### Agrupación en PRs (≤ 800 líneas)

| PR | HUs | Líneas |
|---|---|---|
| PR1 | HU-J + HU-A | 570 |
| PR2 | HU-F + HU-D | 580 |
| PR3 | HU-B | 675 |
| PR4 | HU-C + HU-E | 760 |
| PR5 | HU-G | 520 |
| PR6 | HU-H | 330 |
| PR7 | HU-I | 650 |

---

## Restricciones de despliegue

**HU-E y HU-G deben ir en el mismo release.** Encolar sin worker no envía nada (inocuo), pero al
llegar el worker vacía toda la cola: si pasaron días, los clientes reciben de golpe avisos de
trámites viejos. Es la restricción operativa más importante del lote.

---

## Lo que no se cortó, y por qué

1. **HU-B (675 líneas) no baja más sin empeorar la revisión.** Separar «contratos» de «resolver»
   daría un commit de tipos muertos y otro con 300 líneas de pruebas de golpe.
2. **HU-A no se parte.** DDL sin entidad desalinea el `ModelSnapshot`; entidad sin DDL da un `DbSet`
   sobre una tabla inexistente.
3. **La prueba de no-duplicación contra Postgres real NO entra, y no es un olvido.** El repo **no
   tiene Testcontainers ni Postgres en la suite** (`NotificationDeliveryLogSchemaTests.cs:14-19` lo
   documenta). La idempotencia queda cubierta en tres capas degradadas: aserción estática de los dos
   índices (HU-A), prueba del sink sobre la rama no relacional (HU-E) y **evidencia manual en DEV**
   del caso «ICT falla, correo ya despachado». Introducir Testcontainers afecta al CI, no solo a este
   Feature: es HU de plataforma propia y **no se esconde dentro de una HU de notificaciones**.
   Decisión del Líder Técnico.
4. **No se cortó nada del alcance del PO.** Las cinco condiciones tienen HU y AC.
5. **No se incluyó retención/purga de la tabla nueva.** El ADR la declara sin definir. Radicarla como
   deuda antes de PDN: la tabla crece hasta 4 filas por evento.

---

## Estado DoR

Las 10 HUs pasan los criterios evaluables (título con prefijo, AC positivo y negativo, SP Fibonacci,
dependencias explícitas, sin placeholders ni datos sensibles).

**Cinco criterios no son evaluables** porque dependen de ADO y son idénticos para las 10: Parent
Feature en Active, sprint siguiente al activo, tag `DOR`, `Custom.Refinement='True'`, AssignedTo
humano.

**Dos bloqueos que dependen de personas, no de ADO:**

- **El ADR-0045 está en `Propuesto`.** HU-D y HU-E materializan su decisión estructural. Aceptarlo es
  exclusivo del Líder Técnico humano; ninguna HU debería activarse antes.
- **Los flecos del PO siguen abiertos** (ver tabla de decisiones del plan). Están resueltos *por
  defecto* en los AC de HU-B y HU-I; si el PO decide distinto, HU-B cambia y HU-I con ella.
