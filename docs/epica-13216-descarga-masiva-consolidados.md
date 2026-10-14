# Épica #13216: descarga masiva de consolidados de trámites

> Generado: 2026-10-06 · Actualizado: 2026-10-07 · Estado: **diseño aprobado (ADR-0070 v5, Propuesto), épica descompuesta en 4 Features (#13306, #13366, #13307, #13308) y 28 HUs (#13367–#13394, 105 SP), en implementación** en la rama `feature/AB-13216-descarga-masiva-consolidados`.
> Workflow: `/refine-requirement` completo (panel `po-agent` + `architecture-agent` + `tech-lead-agent` sobre el inventario de `explore-agent`), diseño técnico y descomposición. Las secciones §6, §8 y §9 conservan el registro de esas fases; el estado vigente de cada HU está en ADO.
> **Cómo corregir:** edita la columna **«Decisión final»** de las tablas y los criterios de cada Feature. Lo que no toques se toma como aprobado. Los criterios de §5 son copia de lo publicado en ADO (rev 4, 2026-10-07), con las respuestas Q1–Q12 ya aplicadas.

---

## 1. Resumen

La épica habilita la descarga, en un ZIP, del PDF consolidado de muchos trámites a la vez, desde el listado de trámites. Aplica en tres frentes: Gestor/Radicador, Admin OT y Super Admin FLIT. La generación corre en segundo plano y muestra el progreso. Si la cantidad o el peso lo exigen, el ZIP se entrega en varias partes (aclaración del PO y del LT del 2026-10-06).

**Regla del usuario (2026-10-07): el lote nunca regenera.**
- Si el trámite ya tiene consolidado (o maestro) en BD, se descarga ese, aunque esté desactualizado. En un trámite aprobado o rechazado ese PDF es la decisión final.
- Solo se genera, por el flujo oficial, el de los trámites que no tienen ninguno, incluidos los aprobados o rechazados sin consolidado (caso poco frecuente).
- Aplica igual a `consolidado` y a `consolidado_maestro`.

**Datos de la épica:** creada por el PO el 2026-10-01, asignada a Juan Montoya, prioridad 2. Desde el 2026-10-06 está **Active + DOR**.
**Proceso:** la épica está en el Sprint 10, que es el sprint activo (regla 1). No se movió. Los Features quedaron en la **iteración raíz** porque el Sprint 11 todavía no existe en ADO; hay que moverlos cuando se cree.

### Features en ADO

| ID | Feature | Frente | Criterios | SP estimados | Depende de | Sprint |
|---|---|---|---|---|---|---|
| **#13306** (FA1) | `[TRAMITES]` Descarga masiva para el Gestor/Radicador (motor de lote) | Gestor/Radicador | 22 | ~31–35 (−2 SP por la regla de no regenerar) | — | 11 |
| **#13307** (FA2) | `[TRAMITES]` Descarga masiva para el Super Admin y cancelación del lote | Super Admin + todos (cancelar) | 12 | ~8–10 | #13306 | 11 o 12 |
| **#13308** (FB) | `[ADMIN OT]` Descarga masiva de consolidados maestros desde la bandeja del OT | Admin OT | 9 | ~15–18 (sube: hay que introducir `RequirePermission` en la bandeja) | #13306 | 11 o 12 |

Los tres están en `New`, con tag `DOR`, asignados a Juan Montoya e hijos de #13216; #13307 y #13308 tienen a #13306 como predecesor.
**Orden:** primero #13306; después #13307 y #13308 en paralelo. **#13306 no se promueve a PDN sin #13307**, porque la cancelación es obligatoria.

---

## 2. Hechos del código que condicionan la solución

| # | Hecho | Fuente |
|---|---|---|
| F-a | Hay dos PDF distintos: `consolidado` (gestor) y `consolidado_maestro` (OT). Los dos se guardan como adjuntos del trámite. | `ConsolidadoCommand.cs`, `ConsolidadoMaestroCommand.cs` |
| F-b | La generación falla (409) cuando el trámite no es matrícula ni traspaso, o le falta el FUR o los adjuntos. Por eso un borrador normalmente no se puede consolidar. | `ConsolidadoEndpoints.cs:152` |
| F-c | Generar el **primer** consolidado de un trámite **no final** con el FUR desactualizado regenera el FUR con la fecha del día (ADR-0032) y, si ya existe maestro, lo deja desactualizado y encola su regeneración. Con la regla de no regenerar, es el **único** caso en que el lote re-fecha algo. En estado final el FUR nunca figura como desactualizado. | `ConsolidadoCommand.cs:242-243,535-538`, `FurVigenciaExpediente.cs:30-31`, `ConsolidadoVigenciaTracker.cs:256-297` |
| F-d | El listado tiene un tope de 200 resultados por petición. «Seleccionar todos» exige que el servidor resuelva el lote. La ruta filtrada sí devuelve el total del filtro. | `ListProcedureInstancesFilteredQuery.cs:185`, `ProcedureInstanceEndpoints.cs:197` |
| F-e | Ya existe un patrón de lote asíncrono (HU #12211: tabla, worker y polling). Su ZIP **no se guarda** (se arma al descargar) y su reserva es de 15 min, fija, sin renovación. | `StandaloneDocumentBatchProcessor.cs:34`, `DownloadBatchZipHandler.cs` |
| F-f | No hay Hangfire ni Quartz: el ADR que los rechaza está Aceptado. Los procesos en segundo plano corren dentro de core-api. Hay **una sola instancia** de core-api por ambiente. | `ADR-0024-telemetria-uso-y-alertas.md`, `docker-compose.prod.yml` |
| F-g | El consolidado individual del gestor **no exige permiso**, solo el tenant. | `ConsolidadoEndpoints.cs:20` |
| F-h | La placa puede ser nula en matrícula. El radicado es un consecutivo global único. | `ProcedureInstance.cs`, migración HU #12151 |
| F-i | Ningún listado de trámites tiene hoy selección múltiple. No existe un componente `AlertCard`: el seguimiento global se monta en `components/atom/Shell.tsx`. | `frontend/components/**` |
| F-j | No se midió el tamaño ni el tiempo de generación del consolidado: no hay métricas y no se pudo consultar la BD. | Ver §8 |
| F-k | Estados finales = aprobado, anulado y revocado. **Rechazado no es final.** El guard que impide generar en estado final vive en el endpoint individual del gestor, no en el generador. | `TramiteEstado.cs:175-176,224`, `GeneracionDocumentalGestorGuard.cs:11-18` |
| F-l | Un trámite migrado de la V1 en estado final no se puede generar: el sistema protege el expediente con el que se aprobó. | `ConsolidadoCommand.cs:151-170` |
| F-m | La bandeja del OT no usa `RequirePermission` en ningún endpoint (la protege `OtModulePolicy`). | `AdminOtEndpoints.cs:75-76` |
| F-n | El borrado del file-manager no borra nada (cold storage a 30 días) y la subida carga el archivo entero en memoria. | `FileManagerAttachmentStorage.cs:30-60,130-135` |
| F-o | La protección de solo lectura de Quipux no restringe la generación del maestro: hoy siempre la permite. El usuario lo da por intencional (Q11). | `QuipuxReadOnlyGuard.cs:12-13` |

---

## 3. Puntos críticos

| # | Punto | Riesgo | Cómo quedó resuelto | Decisión final |
|---|---|---|---|---|
| C1 | Regenerar lo no vigente | La descarga re-fecha el FUR (cambia el hash) y desactualiza en cascada el maestro del OT, a escala | **Resuelto por la regla del usuario:** el lote nunca regenera. Solo queda un residuo pequeño (F-c), que absorbe la cola de #12760 tal como está | |
| C2 | Ley 1581 | Exportación masiva de cédulas, firmas y sellos biométricos | Auditoría que no se puede omitir, partes cifradas y privadas, purga a las 24 h | |
| C3 | Fuga entre compañías | El worker corre sin el JWT del usuario y el RLS es decorativo | Contexto guardado al crear el lote, la misma regla de visibilidad del listado, revalidación por ítem y AC negativos | |
| C4 | Reserva fija de #12211 | Un lote largo se procesa dos veces | Tablas propias, reclamo por trámite y reserva mayor que el tiempo máximo de generación. Como mucho una generación por trámite | |
| C5 | Carga sobre la API | La generación de PDF consume mucha CPU y memoria dentro del mismo proceso HTTP | 2 trámites a la vez por instancia y turnos entre lotes. Casi todos los trámites son una lectura, no una generación | |
| C6 | «Sin límite» | No se puede verificar; riesgo de timeouts y de memoria | No se rechaza por cantidad; se parte por N PDF / M MB | |
| C7 | Navegador | Bloquea varias descargas automáticas | Solo la primera parte se descarga sola; botón por parte | |
| C8 | Permisos | El permiso masivo es más estricto que el individual | Se acepta y se declara, más una HU de deuda aparte | |
| C9 | Dependencias | #12753 (permisos por usuario), #12237 (marca blanca: colores y `Shell`), ramas de staging/PDN divergentes en los mismos archivos | Permiso en el catálogo global, tokens del tema, promoción en bloque | |

---

## 4. Decisiones

### 4.1 Primera tanda (aceptada con los valores por defecto)

| # | Tema | Decisión aplicada | Decisión final |
|---|---|---|---|
| Q1 | PDF por frente | Gestor → `consolidado`. Admin OT → `consolidado_maestro` (radicado Quipux: el maestro radicado tal cual). Super Admin → `consolidado` con selector opcional de maestro. Un tipo por lote | |
| Q2 | No consolidables | Se omiten y se listan en `omitidos.csv` (radicado, placa, motivo) | |
| Q3 | Generar en el lote | **Sustituida por la regla del 2026-10-07:** se entrega el que hay en BD aunque esté desactualizado; solo se genera si no existe ninguno, en cualquier estado | |
| Q4 | Usuario sale de la pantalla | El lote sigue en el servidor y el ZIP queda disponible 24 h, solo para quien lo pidió. Sin historial | |
| Q5 | Permiso | Permiso nuevo de descarga masiva, otorgado por defecto a los tres roles | |
| Q6 | Límite | No se rechaza por cantidad. 1 lote activo por usuario. Partición en partes (PO+LT) | |
| Q7 | Exclusiones | «Seleccionar todos» + desmarcar: se envía el filtro + los excluidos y el servidor resuelve | |
| S1 | Selección | Se congela al crear el lote; el acceso se revalida por ítem | |
| S2 | Super Admin multicompañía | Un solo ZIP, sin carpetas por compañía | |
| S3 | Nombres | `consolidados_AAAAMMDD_HHmm.zip` (hora Colombia, al crear el lote); `{radicado}_{placa}.pdf`; `{radicado}_SIN-PLACA.pdf` | |
| S4 | Naranja | Warning = hay ≥1 omitido, durante el proceso o al terminar | |
| S5 | Privacidad | El lote es privado del solicitante, ni siquiera el Super Admin lo ve | |
| S6 | Canales | Sin correo, historial ni notificación fuera de la app | |

### 4.2 Segunda tanda (propuesta del panel)

| # | Tema | Valor por defecto aplicado | Alternativa | Decisión final |
|---|---|---|---|---|
| H1 | Regenerar lo no vigente (C1) | **Sin objeto:** el lote nunca regenera (regla del usuario, 2026-10-07) | — | Resuelta por el usuario |
| H2 | Auditoría | Obligatoria, **en #13306** | En #13307 / ambas en #13306 | |
| H3 | Cancelar lote | Obligatoria, **en #13307**; #13306 no va a PDN sin #13307 | Dentro de #13306 (~+3 SP) | |
| H4 | Partes del ZIP | **Se guardan** al cerrarse, cifradas, y se purgan a las 24 h | Rearmar al descargar | |
| H5 | Descarga de N partes | Se autodescarga **solo la primera** (o la única); botón por parte | Secuencial con un clic | |
| H6 | Acceso revocado durante el lote | Se **omite** con el motivo «acceso revocado» | Respetar lo congelado | |
| H7 | Inicio de las 24 h | **Fin del lote** | Primera descarga | |
| H8 | Doble clic / lote activo | **No se crea otro**: 409 con el id del activo y la UI muestra ese lote | Error genérico | |
| H9 | Permiso más estricto que el individual | **Se acepta y se declara** + HU de deuda aparte | Proteger el individual en esta épica | |
| H10 | Pantalla del Super Admin | **La misma `/tramites`** con filtro de compañía | Página global nueva | |
| H11 | Retención | **Solo el último lote** por usuario; uno nuevo purga el anterior | Cuota en MB | |
| H12 | Sprints | #13306 Sprint 11; #13307 y #13308 Sprint 11 o 12 según capacidad | — | |

### 4.3 Reglas de presentación (resueltas por el PO)

| Tema | Regla | Decisión final |
|---|---|---|
| Estados | `info` = selección, en cola, en proceso · `success` = completado · `warning` = con omitidos (todo omitido → ZIP solo con `omitidos.csv`) · `error` = fallo técnico, sin ZIP, con mensaje de reintento · neutro = cancelado o expirado. Tokens del tema, **sin hex** | |
| Nombre de partes | `consolidados_AAAAMMDD_HHmm_parte-01-de-04.zip` | |
| Manifiesto | Un `omitidos.csv` en **cada parte**, con los omitidos de esa parte | |
| Progreso | Global: procesados / total, con total congelado y procesados = incluidos + omitidos | |
| Descarga anticipada | Las partes solo se descargan cuando termina el lote | |

### 4.4 Parámetros técnicos (propuesta del diseño v2, en BD)

| Parámetro | Propuesta inicial | Rango | Decisión final |
|---|---|---|---|
| N: máximo de PDF por parte | 500 | 1–5.000 | |
| M: máximo de MB por parte | 250 MB | 10–2.048 | |
| Trámites procesados a la vez por instancia | 2 | 1–6 | |
| Espera entre reintentos | 30 s | ≥ 5 s | |
| Retención de las partes | 24 h | fijo por el PO | |

N y M se recalibran con la medición P-1 (§8). Si el p95 del consolidado supera 50 MB, se baja a 1 trámite a la vez.

---

## 5. Features

### #13306 (FA1): `[TRAMITES] - Descarga masiva de consolidados en ZIP para el Gestor/Radicador (motor de lote)`

**Objetivo:** que el Gestor/Radicador descargue en uno o varios ZIP el consolidado de muchos trámites de su compañía desde `/tramites`, sin quedarse esperando. Este Feature construye el motor de lote que reutilizan #13307 y #13308.

**Descripción:**
- El lote se crea con la selección congelada y se procesa en el servidor aunque el usuario salga de la pantalla.
- Se parte en varias partes según N (PDF por parte) y M (MB por parte), los dos configurables en BD.
- Las partes se guardan cifradas en almacenamiento privado durante 24 h.
- Cada exportación queda auditada.
- Restricciones: sin Hangfire, Quartz ni colas externas; nunca se generan PDF durante la descarga del ZIP.
- **El lote nunca regenera** (regla del usuario, 2026-10-07).
- El lote genera en modo sistema: no pide al proveedor externo las improntas que falten.
- Purga a las 24 h por borrado criptográfico; el borrado físico en el file-manager va como HU de deuda aparte, no bloqueante.

**Criterios funcionales**

- [ ] **CF-01** Cada fila de `/tramites` tiene una casilla de selección individual.
- [ ] **CF-02** «Seleccionar todos» selecciona todos los resultados del filtro activo, no solo la página visible, y el servidor resuelve los trámites aunque superen 200.
- [ ] **CF-03** Se pueden combinar la selección individual y «Seleccionar todos» con exclusiones. Se envía el filtro + la lista de excluidos. La selección manual y las exclusiones admiten hasta 10.000 IDs por solicitud; por encima la API responde 422 con un mensaje claro. La selección por filtro no tiene tope.
- [ ] **CF-04** El contador cambia en la misma interacción del clic. Con «Seleccionar todos» muestra el total del filtro calculado por el servidor menos los excluidos, sin estimarlo.
- [ ] **CF-05** «Descargar ZIP» crea un lote en segundo plano y el usuario puede seguir navegando la aplicación.
- [ ] **CF-06** El progreso se muestra como «procesados / total». El total queda fijo al crear el lote y procesados = incluidos + omitidos.
- [ ] **CF-07** Para cada trámite se entrega el consolidado que tiene guardado en BD, aunque esté desactualizado; el lote nunca lo regenera. En un trámite aprobado o rechazado ese PDF es la decisión final del trámite. Solo si el trámite no tiene ningún consolidado se genera por el flujo oficial individual y queda como el oficial, en cualquier estado (incluidos aprobado, rechazado, anulado y revocado). Excepciones, que se omiten con motivo para no crear documentos nuevos sobre un trámite ya decidido: un trámite migrado de la V1 en estado final sin consolidado («Trámite migrado sin consolidado») y un trámite en estado final sin consolidado y sin FUR («El trámite no tiene FUR; no se pudo generar el consolidado»).
- [ ] **CF-08** El lote no modifica ningún trámite que ya tenga consolidado: no crea versión nueva del consolidado, no re-fecha su FUR ni sus certificados y no deja desactualizado su consolidado maestro. Verificación: un trámite con consolidado desactualizado entrega el mismo archivo que tiene en BD (mismo hash) y sus adjuntos no cambian tras el lote. Antes de crear el lote se muestra la confirmación: «Se descargará el consolidado que cada trámite tiene guardado, tal como está, aunque no refleje cambios posteriores. Solo se generará el consolidado de los trámites que todavía no tienen uno.»
- [ ] **CF-09** Los trámites que no se pueden consolidar se omiten sin detener el lote y quedan en `omitidos.csv` (radicado, placa, motivo legible). Un error técnico al generar se reintenta; al agotar los reintentos el trámite se omite con el motivo «No se pudo generar el consolidado, intente de nuevo».
- [ ] **CF-10** El lote no se rechaza por cantidad. Si supera N PDF o M MB, se entrega en varias partes y ninguna supera N ni M. Si un PDF supera M por sí solo, va solo en su propia parte. Verificación: con N configurado, N+1 PDF producen exactamente 2 partes.
- [ ] **CF-11** Con la aplicación abierta en cualquier ruta, al terminar el lote se descarga sola la única parte o la primera. El aviso del lote lista todas las partes con un botón «Descargar» por parte. Si el usuario recarga o vuelve a entrar antes de 24 h desde el fin del lote, ve los botones; después de 24 h ve «Descarga expirada». Descargar una parte no genera PDF.
- [ ] **CF-12** Las partes se guardan cifradas en almacenamiento privado; solo puede bajarlas quien pidió el lote (cualquier otro usuario, incluido el Super Admin, recibe 403/404). Se purgan a las 24 h. Al crear un lote nuevo se purgan las partes del lote anterior del mismo usuario.
- [ ] **CF-13** Nombres:
  - ZIP: `consolidados_AAAAMMDD_HHmm.zip` (hora Colombia, al crear el lote).
  - Partes: `consolidados_AAAAMMDD_HHmm_parte-01-de-04.zip`.
  - PDF: `{radicado}_{placa}.pdf`, y sin placa `{radicado}_SIN-PLACA.pdf`.
  - Cada parte lleva su propio `omitidos.csv`, en UTF-8 con BOM y separador `;` (columnas `radicado;placa;motivo`), con los valores protegidos contra fórmulas.
- [ ] **CF-14** El Gestor recibe el tipo `consolidado`. Un tipo por lote.
- [ ] **CF-15** El lote solo incluye trámites de la compañía del Gestor visibles para él según la misma regla de visibilidad del listado. **AC negativo:** el ID de un trámite de otra compañía inyectado en la solicitud queda fuera del lote.
- [ ] **CF-16** El acceso se revalida por cada trámite al procesarlo. Un trámite al que el usuario perdió acceso después de crear el lote se omite con el motivo «Acceso revocado».
- [ ] **CF-17** Máximo 1 lote activo por usuario. Si pulsa «Descargar ZIP» con un lote en cola o en proceso, no se crea otro: la API responde 409 con el id del lote activo y la UI muestra ese lote con un aviso.
- [ ] **CF-18** La descarga masiva exige el permiso nuevo `consolidado-masivo.download`. El permiso vive en el catálogo global de roles (el que leerá #12753) y se otorga por defecto a SuperAdmin, `admin_tramites`, Radicador y `AdminCompany`. Sin el permiso no se ve «Descargar ZIP» y la API responde 403. **Declarado:** el consolidado individual no exige este permiso.
- [ ] **CF-19** Estados visuales con tokens semánticos del tema, sin hex:
  - `info`: selección, en cola y en proceso.
  - `success`: completado sin omitidos.
  - `warning`: al menos un omitido, en proceso o completado. Si todo se omite, se entrega un ZIP solo con `omitidos.csv`.
  - `error`: fallo técnico del lote, sin ZIP, con el mensaje «No se pudo completar la descarga, intente de nuevo». Libera el cupo del lote activo.
  - Neutro: expirado.
- [ ] **CF-20** El lote sobrevive a un reinicio de core-api y continúa desde el último trámite no procesado. Ningún trámite se genera dos veces dentro del mismo lote, aunque el lote dure horas.
- [ ] **CF-21** Como máximo K trámites de lotes se procesan a la vez por instancia (K configurable en BD). Los lotes que esperan se muestran «En cola». Un lote creado mientras otro más grande está en proceso avanza antes de que el grande termine.
- [ ] **CF-22** Cada lote deja un registro de auditoría que no se puede omitir: usuario, compañía, rol, fecha y hora, tipo de documento, filtro aplicado o cantidad de IDs, y total/incluidos/omitidos. Por cada descarga de parte se registra quién y cuándo. Si la auditoría no se puede registrar, el lote no se crea. La auditoría no guarda el contenido de los PDF.

---

### #13307 (FA2): `[TRAMITES] - Descarga masiva de consolidados para el Super Admin y cancelación del lote`

**Objetivo:** que el Super Admin descargue en lote consolidados de cualquier compañía desde el listado global, y que cualquier solicitante pueda cancelar su lote en curso. Usa el motor de #13306 y depende de él. **#13306 no se promueve a PDN sin #13307.** La regla «el lote nunca regenera» aplica igual al `consolidado_maestro`.

**Criterios funcionales**

- [ ] **CF-01** El Super Admin selecciona trámites de cualquier compañía desde `/tramites` con filtro de compañía *(sujeto a H10)*. Si acota una compañía, solo entran trámites de esa compañía.
- [ ] **CF-02** Selección individual, «Seleccionar todos» con el total resuelto en el servidor, exclusiones y contador funcionan en el listado global igual que en el del Gestor. Se verifican con los mismos casos usando un usuario Super Admin.
- [ ] **CF-03** Antes de crear el lote se elige el tipo: `consolidado` (por defecto) o `consolidado_maestro`. Un tipo por lote.
- [ ] **CF-04** Un lote con trámites de varias compañías produce un solo ZIP, sin carpetas por compañía.
- [ ] **CF-05** Cada trámite se procesa en el contexto de su propia compañía. **AC negativo:** un trámite de la compañía A no se genera con el contexto de la compañía B.
- [ ] **CF-06** Con `consolidado_maestro` se aplica la misma regla de #13306 (CF-07/CF-08): se entrega el maestro guardado en BD aunque esté desactualizado (el radicado ante Quipux, tal cual) y nunca se regenera; solo se genera el de los trámites que no tienen ninguno, en cualquier estado, con las mismas excepciones (migrado de la V1 en estado final; estado final sin FUR). Si esa generación la rechazaría la generación individual del maestro por otra validación, el trámite se omite con el mismo motivo.
- [ ] **CF-07** La partición (N/M), los nombres, `omitidos.csv` por parte, la autodescarga de la primera parte con botón por parte, la retención privada 24 h, la revalidación de acceso por trámite, el lote activo único, los estados por tokens, la supervivencia a reinicios y el tope K funcionan para el Super Admin igual que en #13306 (CF-06 a CF-21).
- [ ] **CF-08** Cada lote del Super Admin queda en la auditoría con los mismos campos y la misma regla que no se puede omitir de #13306 (CF-22). El lote no queda asociado a ninguna compañía y la auditoría registra la lista de compañías alcanzadas.
- [ ] **CF-09** El aviso de un lote en cola o en proceso muestra «Cancelar», sea del listado o de la bandeja del OT. Al cancelar:
  - no se procesan más trámites;
  - no se entrega ZIP y se descartan las partes ya cerradas;
  - se libera el cupo del lote activo;
  - los consolidados ya generados quedan como oficiales (no se revierten);
  - la cancelación queda auditada.
- [ ] **CF-10** Solo quien pidió el lote puede cancelarlo; cualquier otro usuario recibe 403/404.
- [ ] **CF-11** Un lote cancelado se muestra en tono neutro con el texto «Descarga cancelada».
- [ ] **CF-12** La acción exige el permiso de descarga masiva, otorgado por defecto al rol Super Admin.

---

### #13308 (FB): `[ADMIN OT] - Descarga masiva de consolidados maestros en ZIP desde la bandeja del OT`

**Objetivo:** que el Admin OT descargue en uno o varios ZIP los consolidados maestros de su bandeja, respetando el alcance del organismo. Usa el motor de #13306 y depende de él.

**Descripción:** la selección por filtro usa la gramática de filtros de la bandeja del OT, que llega por POST con listas pegadas. Cada trámite se procesa en el contexto de la compañía cliente, con el OT fijado. La regla «el lote nunca regenera» aplica igual al maestro.

**Criterios funcionales**

- [ ] **CF-01** Cada fila de la bandeja del OT tiene una casilla de selección individual.
- [ ] **CF-02** «Seleccionar todos» toma todos los resultados del filtro activo de la bandeja, incluidas las listas pegadas de placa, VIN o radicado. Los resuelve en el servidor el mismo motor de lote de #13306. Se puede combinar con selección individual y exclusiones.
- [ ] **CF-03** El contador cambia en la misma interacción. Con «Seleccionar todos» muestra el total del filtro calculado por el servidor menos los excluidos.
- [ ] **CF-04** «Descargar ZIP» crea un lote en segundo plano con progreso «procesados / total» (total congelado). Al terminar, con la aplicación abierta, se descarga sola la única parte o la primera, y hay un botón por parte. Las partes están disponibles 24 h desde el fin del lote, solo para quien lo pidió. Con N configurado, N+1 PDF producen 2 partes. Un solo lote activo por usuario (409 + se muestra el lote activo).
- [ ] **CF-05** Para cada trámite se entrega el `consolidado_maestro` guardado en BD, aunque esté desactualizado; el lote nunca lo regenera. En un trámite aprobado o rechazado ese PDF es la decisión final; un trámite radicado ante Quipux entrega el maestro radicado tal cual. Solo si el trámite no tiene ningún maestro se genera por el flujo oficial y queda como el oficial, en cualquier estado. Excepciones, que se omiten con motivo: migrado de la V1 en estado final sin maestro y estado final sin maestro y sin FUR. Un trámite que ya tiene maestro no cambia tras el lote (mismo archivo, mismo hash; su FUR y su `consolidado` del gestor no se modifican).
- [ ] **CF-06** Solo entran trámites con un grant vigente hacia el OT, revalidado por trámite al procesarlo. Si el grant se revoca durante el lote, el trámite se omite con el motivo «Acceso revocado». **AC negativo:** el ID de un trámite sin grant inyectado en la solicitud queda fuera.
- [ ] **CF-07** Cuando hay que generar el maestro, el lote aplica las mismas validaciones que la generación individual (guard de acción, Quipux solo lectura); el estado final no impide generar un maestro que no existe, salvo las excepciones de CF-05. Un trámite que no pase las validaciones se omite con motivo; ninguna validación se salta.
- [ ] **CF-08** Los nombres del ZIP, de las partes y de los PDF son iguales a los de #13306 (CF-13). Cada parte lleva su `omitidos.csv` (radicado, placa, motivo). La exportación queda en la auditoría con los mismos campos y la misma regla que no se puede omitir.
- [ ] **CF-09** Estados con tokens `info`, `success`, `warning` y `error`, con la misma semántica que #13306 (CF-19). Exige el permiso de descarga masiva otorgado por defecto al rol Admin OT; sin él, la acción no aparece y la API responde 403.

---

## 6. Diseño técnico de #13306 (v2, pendiente de tu «sí»)

Diseño completo: `.claude/state/epica-13216/05-diseno-13306.md`. ADR: `services/core-api/docs/adr/ADR-0070-motor-lotes-descarga-masiva-consolidados.md` (**Propuesto**).

| # | Decisión | Opción elegida | Decisión final |
|---|---|---|---|
| D1 | Lote de #12211 o propio | **Tablas propias** en `tramites` (lote, ítems, partes, auditoría, parámetros). #12211 no se toca; su reserva de 15 min queda como deuda, bloqueante si se escala core-api | |
| D2 | Reinicio sin doble generación | Reclamo por trámite (`FOR UPDATE SKIP LOCKED`), reserva de 10 min mayor que el tiempo máximo de generación (5 min), turnos entre lotes. Si el proceso cae, el reintento toma el consolidado que dejó la ejecución anterior | |
| D3 | «Seleccionar todos» | Método nuevo `ListIdsFilteredAsync` sobre los mismos filtros y orden del listado, sin el tope de 200. El total se congela al crear el lote. #13308 conecta su propia estrategia para la bandeja | |
| D4 | Partes del ZIP | File-manager con subida en streaming; cada parte cifrada con una clave por lote; descarga autenticada por la API. Purgar = destruir la clave | |
| D5 | Entrega por trámite | **Toma el adjunto existente tal cual**; si no hay, llama al generador oficial con una guarda nueva que solo deja generar si no existe, sin forzar | |
| D6 | Concurrencia | 2 trámites a la vez por instancia; el empaquetado y la purga van aparte | |
| D7 | API y progreso | `POST /api/v1/tramites/consolidados/lotes` (202, o 409 con el id del lote activo); consulta, descarga y cancelación bajo `/api/v1/consolidados/lotes`. Progreso por polling cada 4 s desde `Shell`, sin SSE. Permiso `consolidado-masivo.download` | |
| D8 | Auditoría | Tabla de solo inserción escrita en la misma transacción que crea el lote (si falla, 503 y no hay lote); cada descarga de parte se audita antes del primer byte | |

**Generación en aprobado sin consolidado:** el lote llama al generador, no al endpoint individual, que sigue cerrado en estado final. Tres invariantes con prueba automática lo sostienen: solo genera si no existe, nunca fuerza y en estado final nunca genera si falta el FUR.

**Schema (Fase 2b):** sí hace falta. Cinco tablas nuevas en `tramites` y el permiso nuevo en el sembrado; el siguiente número libre de DDL es el **133**.

### Preguntas abiertas del diseño

| # | Pregunta | Recomendación | Decisión final |
|---|---|---|---|
| Q1 | Texto de confirmación antes de crear el lote: «se descarga el guardado tal cual; solo se generan los que faltan» | Aprobar | **a) Tal cual, sin aviso del FUR** |
| Q3 | ¿El lote pide al proveedor externo las improntas que falten al generar? | No (`userId = null`) | **a) No: modo sistema** |
| Q4 | Permiso por defecto a SuperAdmin, `admin_tramites` y Radicador. ¿Otro rol ve `/tramites`? | Solo los tres | **b) Los tres + `AdminCompany`** |
| Q5 | Borrado criptográfico o borrado físico de las partes | Criptográfico; validar con Security | **c) Criptográfico ahora + HU de deuda para el físico (no bloqueante)** |
| Q6 | `omitidos.csv`: separador `;` con BOM o `,` | `;` con BOM | **a) `;` con BOM** |
| Q7 | Tope de 10.000 IDs en selección manual y exclusiones (422 por encima); sin tope por filtro | Aceptar | **a) Aceptado** |
| Q8 | Compañía del lote de un Super Admin (#13307) | Decidir en #13307 | **c) Sin compañía (NULL) + compañías alcanzadas en la auditoría** → `tenant_id` nullable en el schema de #13306 |
| Q9 | ¿Anulados y revocados sin consolidado también se generan? | Sí | **a) Sí** |
| Q10 | Migrado V1 en estado final sin consolidado | Omitir | **a) Se omite: «Trámite migrado sin consolidado»** |
| Q11 | La protección de Quipux no bloquea la generación del maestro (F-o) | Bug al LT | **c) Se asume intencional; no se registra nada** |
| Q12 | Trámite final sin consolidado y sin FUR | Omitir | **a) Se omite: «El trámite no tiene FUR; no se pudo generar el consolidado»** (también en el maestro) |

---

## 7. Alcance recortado (fuera de esta épica)

| # | Sale | Por qué | Decisión final |
|---|---|---|---|
| R1 | Autodescarga de todas las partes | Los navegadores la bloquean | |
| R2 | Descargar partes antes de que termine el lote | Complejidad sin valor demostrado | |
| R3 | Rearmar el ZIP al descargar | No reproduce el contenido y obliga a generar PDF durante la descarga | |
| R4 | Varios lotes retenidos por usuario | Cuota (H11), coherente con «sin historial» | |
| R5 | Regenerar consolidados desactualizados desde el lote | Regla del usuario (2026-10-07): el lote entrega lo que hay en BD | |
| R6 | Proteger con permiso el consolidado individual | Es otro requerimiento; va como HU de deuda | |
| R7 | UI para configurar N, M y K | Se configuran en BD | |
| R8 | Descarga con la pestaña cerrada o aviso por correo | No hay canal (S6) | |
| R9 | Historial de descargas, otros documentos (FUR suelto, anexos), tipo por trámite, descarga desde el detalle | Fuera desde el borrador inicial | |

---

## 8. Pendientes antes de descomponer en HUs

| # | Pendiente | Responsable | Estado |
|---|---|---|---|
| P-1 | Medir el tamaño real del consolidado para fijar N y M. Consulta de solo lectura, sin PII, para QA o PDN | Humano (Juan) | Abierto |
| P-2 | Generalizar el lote de #12211 o tabla propia; «resolver» de filtros genérico | `architecture-agent` | Resuelto en el diseño (D1, D3) |
| P-3 | Confirmar que el listado devuelve el total real del filtro | Diseño | Resuelto: sí, por la ruta filtrada (F-d) |
| P-4 | Verificar `RequirePermission` en la bandeja del OT | Diseño | Resuelto: no lo usa; #13308 lo introduce (F-m) |
| P-5 | Añadir en el Feature de #12753 un AC espejo para el permiso nuevo | Humano | Abierto |
| P-6 | Coordinar con #12237 (marca blanca) el seguimiento global en `Shell` y los tokens de color | Humano / frontend | Abierto |
| P-7 | Sprint 11 en ADO | Orquestador | No existe: los Features quedaron en la raíz; moverlos cuando se cree |
| P-8 | Presupuestar la promoción a staging/release como bloque: las ramas `promote`/`pdn` divergen en `AdminOtEndpoints`, `ProcedureInstanceEndpoints`, `ConsolidadoVigenciaTracker`, `DataTable.tsx` | Humano / LT | Abierto |
| P-9 | Llevar a ADO las respuestas Q1–Q12 (CF-03/07/08/09/13/18 de #13306, CF-06/08 de #13307, CF-05/07 de #13308) | Orquestador | Hecho (2026-10-07, rev 4) |
| P-10 | Registrar el Bug de Quipux (Q11) | — | Descartado: se asume intencional |
| P-11 | Crear la HU de deuda del borrado físico de partes en `flit-file-manager-minio` (Q5) | Orquestador (al descomponer) | Abierto |
| P-12 | HU de deuda: `RequirePermission` en el consolidado individual (H9) | Orquestador (al descomponer) | Abierto |

Consulta P-1 (si el esquema no es `tramites`, quita el prefijo):

```sql
SELECT tipo, count(*),
       percentile_cont(ARRAY[.5,.95]) WITHIN GROUP (ORDER BY size_bytes) AS p50_p95_bytes,
       max(size_bytes)
FROM tramites.procedure_instance_attachments
WHERE tipo IN ('consolidado','consolidado_maestro')
GROUP BY tipo;
```

---

## 9. Siguientes pasos

1. ~~Crear en ADO los tres Features~~ **Hecho (2026-10-06):** #13306, #13307 y #13308. Regla de no regenerar aplicada en ADO el 2026-10-07.
2. **Aprobar el diseño técnico de #13306** (§6). Q1–Q12 ya están respondidas.
3. **Schema** (Fase 2b): `database-agent` materializa las tablas (DDL 133) y el permiso, validado con `db-schema-validator`.
4. **Descomposición** (`/decompose-feature`) de #13306 → #13307 → #13308 en HUs con AC Gherkin.
5. **Implementación** por HU con los gates habituales: activar la HU, PR a `develop`, review + security, merge con confirmación y reviewer humano.

---

## Anexo: material de trabajo

Carpeta `.claude/state/epica-13216/` (local, gitignored):

| Archivo | Contenido |
|---|---|
| `00-epica-fuente.md` | Texto de la épica |
| `01-brief-explore.md` | Hechos del código |
| `02-borrador-v1-po.md` | Borrador inicial y decisiones de la primera tanda |
| `03a-critica-arquitectura.md` | Crítica de arquitectura |
| `03b-critica-tech-lead.md` | Crítica del tech lead |
| `04-features-v2-po.md` | Reconciliación y v2.1 |
| `05-diseno-13306.md` | Diseño técnico de #13306 (v2) |
| `06-regla-usuario-no-regenerar.md` | Regla del usuario del 2026-10-07 y sus respuestas |
