# Plan — Correo «Asignación de Placa» conectado al flujo de trámites

> Generado: 2026-08-12 · Rama base: `develop` · Estado: **plan, sin implementar**

## 1. Requerimiento

Conectar la plantilla `tramites.asignacion-placa` —hoy huérfana, solo visible en el banco de
pruebas— a un disparador real: **cuando el OT asigna la placa** en un trámite de Matrícula Inicial
que salió por la **ruta de preasignación** (con o sin dígito de preferencia). El trámite está en
`entregado` global y el sub-estado de placa pasa de **Sin asignar → Asignado**. El correo va al
**comprador** e indica que debe comprar el SOAT.

## 2. Punto de partida verificado

Lo que **ya existe y se reutiliza** (mergeado en PR #253, Feature #11459):

| Pieza | Ubicación |
|---|---|
| Plantilla en catálogo (código, no BD) | `NotificationTemplateCatalog.cs:37,85-89` — id `tramites.asignacion-placa`, módulo `Tramites` |
| Composer con variantes FLIT y Renting | `Flit.Infrastructure/Notifications/Tramites/AsignacionPlacaEmailComposer.cs` |
| Resolutor de destinatarios (natural / jurídica / RL) | `Flit.Tramites.Application/Notifications/TramiteNotificationRecipientResolver.cs` |
| Transporte, enrutamiento por canal y bitácora | `TenantChannelEmailRouter`, `NotificationChannelResolver`, `admin.notification_delivery_logs` |
| Patrón de cola + worker (reclamo, reintentos, kill-switch) | `ProcedureStateChangeEmailDispatchProcessor.cs` |

Lo que **no existe** y es el trabajo real:

1. **No hay evento.** `plate_flow_status: preasignado → asignado` se persiste con mutación EF
   directa en `OtClientProcedureRepository.cs:618`. No hay transición de máquina de estados, ni
   fila de historial, ni evento de dominio, ni fila de outbox. Nada de la maquinaria de ADR-0045
   se activa sola.
2. **La cola existente no admite este evento.** `procedure_state_change_email_dispatches.outbox_id`
   es `NOT NULL` con FK a `procedure_state_change_outbox` (`Ddl/69:22-24`). Fabricar una fila
   sintética de outbox haría que `ProcedureStateChangeOutboxProcessor` despachara a los **webhooks
   del OT y al reflejo gRPC de core-ict** un cambio de estado que no ocurrió.
3. **El copy no cumple el requerimiento.** El cuerpo actual no menciona el SOAT y dice *«cuya
   posible placa es»* (`AsignacionPlacaEmailComposer.cs:183-208`), redacción de placa tentativa que
   es falsa una vez la placa está asignada.
4. **La regla de marca Renting está enunciada pero inerte.** `companyRegistered` no existe en el
   código ni en el schema; `811011779` solo vive como `const` y comentario en el composer.

## 3. Decisiones tomadas

| # | Decisión | Quién |
|---|---|---|
| **D1** | Dispara **solo el Flujo B** (el OT asigna desde su consola). El Flujo A —placa que ya venía del RUNT y que el trigger `trg_autoset_plate_flow_status` marca `asignado`— queda fuera. | Usuario |
| **D2** | Destinatarios del rol **comprador**, reutilizando el resolutor sin modificarlo: persona jurídica produce dos cupos (empresa + representante legal), con colapso a un solo envío si comparten buzón. | Usuario |
| **D3** | Se **añade el párrafo del SOAT** y se corrige *«posible placa»* en ambas variantes. | Usuario |
| **D4** | Se enciende **sin extender el guardarraíl de destinatarios al SMTP**. Riesgo aceptado, ver §7 R1. | Usuario |
| **D5** | Enganche en **application sobre el endpoint del OT**, tras el commit y fuera de la transacción; cola propia. | `architecture-agent` |
| **D6** | Marca FLIT/Renting por **`identity.tenants.tax_id` del tenant cliente** normalizado, no por canal. *Asunción adoptada del diseño; confirmable.* | Propuesta |
| **D7** | Reasignar **la misma** placa no reenvía; una placa **distinta** sí. *Asunción adoptada; confirmable.* | Propuesta |
| **D8** | El dígito de preferencia **no** se muestra en el cuerpo (no se pidió). | Propuesta |

**El Flujo A queda excluido sin escribir un solo `if`**: el trigger SQL está guardado por
`plate_flow_status IS NULL` y salta de `NULL` directo a `asignado`; nunca ejecuta la arista
`preasignado → asignado`. Como el enganche vive en el camino de código de esa arista, el Flujo A
no puede alcanzarlo. Es exclusión estructural, no una condición que alguien pueda borrar.

## 4. Diseño elegido

**Alternativas evaluadas** (detalle en ADR-0046):

- **A — Enganchar dentro de `AssignPlateAsync`, en la misma transacción.** Única opción con
  exactly-once real, pero mete resolución de destinatarios en infraestructura y **puede tumbar la
  asignación**: un `Metadata` JSON corrupto dejaría al OT con un 500 sobre una placa válida.
  Blindarlo con `try/catch` fail-open le quita justo la transaccionalidad que la justificaba.
- **B — Use case de application sobre el endpoint del OT.** ✅ **Elegida.**
- **C — Generalizar el outbox con un evento de sub-estado.** Su único argumento —reutilizar
  `outbox_id`— se autodestruye: o contamina el outbox vivo (rompiendo webhooks del OT y reflejo
  ICT), o crea una segunda tabla y entonces ya no reutiliza nada.

**Por qué B:** el requisito duro es que un fallo del correo no revierta la asignación de placa. B lo
cumple *por posición* —el correo ocurre después del commit— y tiene precedente literal a tres
líneas de distancia: `AdminPlateRangesEndpoints.cs:153-174` ya regenera el FUR post-asignación con
`try/catch` y el comentario «un fallo aquí NO revierte la asignación de placa ya persistida».

**Tradeoff aceptado:** una ventana de pérdida si el proceso muere entre el commit y el `INSERT`. Es
auditable con un `LEFT JOIN` (`asignado` sin fila de despacho) y encaja con el criterio de ADR-0045
de hacer visibles los huecos en vez de esconderlos.

**Cola propia** `tramites.plate_assignment_email_dispatches`, gemela de la existente pero sin FK a
outbox. Identidad del evento = `(procedure_instance_id, placa)` — sin outbox, la placa *es* la
noticia. **Condición de fusión escrita en el ADR:** al aparecer el tercer disparador de correo de
trámite, las dos colas se unifican con discriminador. No antes.

## 5. Fases de implementación

`database-agent` y `backend-agent` **no pueden correr en paralelo** sobre `core-api` (regla de
concurrencia del orquestador): las fases 2 y 3 van serializadas.

| # | Fase | Agente | Entregable |
|---|---|---|---|
| 0 | ADR-0046 en `Propuesto` | `architecture-agent` | `services/core-api/docs/adr/ADR-0046-disparador-correo-asignacion-de-placa.md` |
| 1 | Copy definitivo de la plantilla | `backend-agent` | Párrafo SOAT + placa asignada en ambas variantes; campos nuevos **opcionales con default** |
| 2 | Esquema de la cola | `database-agent` | DDL numerado + migración + entidad + mapeo |
| 3 | Encolado al asignar | `backend-agent` | Handler de application, puerto, enqueuer, endpoint delegando |
| 4 | Proyección, marca y worker | `backend-agent` | Projector, `PlateAssignmentBrandResolver`, `BackgroundService` |
| 5 | Evidencias y pruebas | `dev-tester` → `qa-agent` | Tests unitarios por AC + TCs |
| 6 | Review, seguridad e integración | `code-review-agent`, `security-agent`, `integration-agent` | PR ≤ 800 líneas a `develop` |

**Restricción de despliegue (heredada de la HU #11465):** el encolado y el worker **deben ir en el
mismo release**. Encolar sin worker es inocuo, pero al llegar el worker vacía la cola de golpe y los
compradores reciben avisos de placas asignadas días atrás.

## 6. HUs candidatas

Feature padre propuesto: **nuevo** `[NOTIFICACIONES] - Aviso por correo al asignar la placa`. El
#11459 está acotado a *cambio de estado* y este disparador es de **sub-estado**; colgarlo ahí
falsearía su alcance. Alternativa: colgarlo de #10587 (ruta de placa). Decisión de PO / tech lead.

| HU | Tipo | SP | Depende de |
|---|---|---|---|
| Copy definitivo: SOAT y placa asignada | BACKEND | 3 | — |
| Esquema de la cola de despachos de asignación de placa | BACKEND | 3 | — |
| Encolado del aviso al asignar la placa | BACKEND | 5 | cola |
| Proyección del modelo y resolutor de marca FLIT/Renting | BACKEND | 3 | cola |
| Worker de envío de la cola de asignación de placa | BACKEND | 5 | encolado |
| Golden files y regresión del banco de pruebas | BACKEND | 2 | copy |

**Total: 21 SP.** Todas backend; el frontend no se toca.

## 7. Riesgos

| # | Riesgo | Sev. | Tratamiento |
|---|---|---|---|
| **R1** | **Envío real a compradores desde DEV/QA.** El desvío a buzón de control solo existe en el canal Renting; el SMTP de FLIT no lo consulta y los tres ambientes corren con `ASPNETCORE_ENVIRONMENT=Development`, así que `IsProduction()` nunca es `true`. | Alta | **Aceptado por decisión D4.** Freno operativo disponible sin trabajo extra: dejar el kill-switch por tenant apagado en DEV/QA — con el efecto de que ahí tampoco se podrá probar el envío. La corrección de fondo (extender el desvío a todos los canales) queda como ADR aparte que amplíe ADR-0044. |
| R2 | Pérdida silenciosa si el proceso muere entre el commit y el `INSERT`. | Media | Tradeoff aceptado en el ADR; consulta de reconciliación disponible. |
| R3 | La marca Renting **nunca acierta** por comparar `tax_id` sin normalizar (`811011779-1`, `811.011.779`). | Media | Normalizador a dígitos con descarte del dígito de verificación + pruebas de las tres formas; log cuando marca y canal discrepan. |
| R4 | Cambiar la firma de `AsignacionPlacaEmailModel` rompe el banco de pruebas. | Baja | Campos nuevos opcionales con default + regresión del banco en la misma HU. |
| R5 | Usar `otTenantId` en vez de `ClientTenantId` ⇒ canal, kill-switch y RLS equivocados. | Media | Firma del enqueuer que solo acepta `clientTenantId`; prueba con OT y cliente distintos. |
| R6 | Dos colas de correo divergen con el tiempo. | Baja | Condición de fusión escrita en el ADR. |

## 8. Casos de prueba obligatorios

1. Flujo A (placa que ya venía al radicar) **no** genera correo.
2. Compañía con `plate_flow_skip_to_terminado` tampoco.
3. Un fallo del encolado deja la placa asignada y el endpoint responde 200.
4. Revocar y reasignar la **misma** placa no reenvía; una **distinta** sí.
5. Comprador persona jurídica con empresa y representante legal en el mismo buzón ⇒ **un** envío.
6. Trámite sin correo del comprador ⇒ fila `omitido`, sin excepción.

## 9. Qué falta antes de implementar

- Aprobar este plan y el ADR-0046 (queda en `Propuesto`; `Aceptado` es exclusivo del Líder Técnico
  humano).
- Confirmar o corregir las asunciones D6, D7 y D8.
- Decidir el Feature padre y crear las HUs en ADO (gate de activación vigente para cada una).
