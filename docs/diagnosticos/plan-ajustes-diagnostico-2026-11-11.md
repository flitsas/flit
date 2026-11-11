# Plan de ajustes — Módulo Diagnóstico Vehicular

> Generado: 2026-11-11 · Estado: **en refinamiento** (punto por punto; no implementar hasta cerrar todos)
> Fuente inicial: `docs/diagnosticos/mejoras.txt` + descripción del usuario por punto.
> Código de referencia: `origin/develop` → `services/core-diagnostico/` (Domain + Api) y app propia `frontend-diagnostico/` (no `frontend/app`). Anclas `archivo:línea` del brief de explore-agent del 2026-11-11: reverificar antes de citar en HUs.

## Cómo se usa este archivo

Cada punto se refina por separado: descripción del usuario → hechos del código → decisiones (una pregunta a la vez) → alcance y criterios → triaje (Caso A/B/C) → agentes y estimación.
Cuando todos estén cerrados, se agrupan en Feature(s)/HUs en ADO y se implementan.

## Pendientes transversales

- **Pendiente LT (PR #823):** `PLATE_PATTERN` y el fixture compartido de core-diagnóstico no aceptan la placa de motocarro `123ABC` (ver punto 7).
- Leer `docs/diagnosticos/plan-pendientes-dictamen-a-flito.md` antes de refinar el punto 4 (posible diseño previo del paso a trámite).

## Índice

| # | Punto (según mejoras.txt) | Estado |
|---|---|---|
| 1 | Configuración del producto por cualquier super admin (sin restricción por tipo de compañía) | **refinado** (el usuario pasó al punto 2 sin objetar; supuesto 1.3 abierto a ajuste) |
| 2 | Multas: bloqueantes para el trámite (reglas del dictamen) | **refinado** |
| 3 | Reintentos: 3 automáticos cada 2 min y luego manual (por placa y para todo el lote) | **refinado** |
| 4 | Crear trámite con dictamen favorable (individual y masivo) | **refinado** (requiere diseño de arquitectura) |
| 5 | Nombres técnicos → nombres entendibles por el usuario | **refinado** |
| 6 | Identificador de compañía como selector en backoffice | **refinado** |
| 7 | Pruebas con placas especiales (maquinaria, motos, carros, motocarros) | **refinado** |
| 8 | Índice del ZIP masivo → Excel o CSV | **refinado** |
| 9 | Revisión de diseño: iconos, tablas y filtros | **diferido al final** (decisión del usuario 2026-11-11) |

## Consolidado (puntos 1–8)

| # | Punto | Caso | Estimación | Depende de |
|---|---|---|---|---|
| 1 | Configuración del backoffice por cualquier SuperAdmin | C | 5 SP | Visto bueno de seguridad y del LT (acceso entre compañías) |
| 2 | Multas bloqueantes (catálogo v2) | C | 3 SP | Dictamen de `expert-doc-engine` |
| 3 | Reintentos automáticos y manuales | C | 8 SP | — |
| 4 | Crear trámite individual y masivo | C (grande) | ≈ 21 SP | 2, 3 · ADR nuevo · lado core-api · épica #12679 |
| 5 | Rótulos de negocio (diccionario único) | C liviano | 5 SP | 2 |
| 6 | Selector de compañía en backoffice | C liviano | 2 SP | 1 |
| 7 | Motocarro + matriz por clase + QA con placas reales | C (+ posible B) | 8 SP | Visto bueno del LT · placas reales · dictamen RTM/SOAT |
| 8 | Índice del ZIP en Excel | C liviano | 3 SP | 5 (y 2/4 para sus columnas) |
| | **Total** | | **≈ 55 SP** | |

**Orden propuesto de implementación:**
1. **Dictamen normativo** (`expert-doc-engine`): paz y salvo SIMIT en traspaso (punto 2) y RTM/SOAT por clase (punto 7). Va primero porque condiciona las reglas.
2. **Punto 2** (catálogo v2) → base de 4, 5 y 8.
3. **Puntos 1 + 6** (backoffice; misma área y misma política). En paralelo con 2.
4. **Punto 3** (reintentos). Independiente; en paralelo.
5. **Punto 5** → **punto 8** (rótulos, luego índice).
6. **Punto 7** (tras el visto bueno del LT; la matriz usa las reglas del punto 2).
7. **Punto 4.** El diseño de `architecture-agent` puede arrancar desde ya en paralelo; la implementación va al final porque necesita 2 y 3.

**Agrupación propuesta en ADO (a confirmar al crear):**

| Feature | Puntos |
|---|---|
| Reglas del dictamen: multas bloqueantes | 2 (+ Bug de RTM si se confirma, 7.4) |
| Backoffice abierto a SuperAdmin | 1, 6 |
| Reintentos de consultas | 3 |
| Lenguaje de negocio y descargas | 5, 8 |
| Placas especiales | 7 |
| Crear trámite desde el diagnóstico | 4 (reusar épica #12679; pasa por `/refine-requirement`) |

**Gates antes de implementar:** Features y HUs creados en ADO (sprint siguiente al activo, tag DOR, SP Fibonacci, asignado a un humano) · activación de cada HU con tu «sí» · visto bueno del LT en motocarro y en el acceso entre compañías · ADR del punto 4 en Propuesto, aceptado por el LT.

---

## Punto 1 — Configuración por cualquier super admin

**Descripción del usuario:** Levantar la restricción de tipo de compañía para poder configurar el producto. Cualquier usuario con rol **super admin, de cualquier compañía**, puede acceder a la configuración y aplicar cambios.
**Situación actual (código):**
- El backoffice (`/backoffice/*`) exige el permiso `diagnostico.backoffice.ver` **y** `tenant_type = FLIT`. El SuperAdmin cuenta como permiso, pero si su compañía no es FLIT tampoco entra (`Plataforma/PoliticaBackoffice.cs:8,26`).
- La configuración del cliente (`GET/PUT /configuracion`, contrato) **solo** pide permisos `diagnostico.configuracion.*`; ahí no se revisa el tipo de compañía y el SuperAdmin pasa siempre (`Plataforma/Autorizacion.cs:23`, `Permisos.cs:15`).
- Por tanto el filtro por tipo de compañía vive **solo en el backoffice**, que trabaja sobre todas las compañías (`?empresa=`).

**Decisiones:**

| # | Pregunta | Respuesta |
|---|---|---|
| 1.1 | ¿Sobre qué compañías actúa un SuperAdmin no FLIT en el backoffice? | **c) Sobre todas, igual que FLIT** (decisión del usuario 2026-11-11; se le advirtió de la fuga entre compañías) |
| 1.2 | ¿Qué secciones se abren? | **a) Solo configuración**: contrato, fuentes obligatorias, intentos y listado/consulta de diagnósticos. **Override del dictamen y llenado manual siguen exclusivos de FLIT.** |
| 1.3 | ¿Usuarios no FLIT sin rol SuperAdmin pero con el permiso `diagnostico.backoffice.ver`? | **No entran** (por defecto, según la descripción: «cualquier usuario con rol super admin»). FLIT conserva el acceso por permiso. |

**Regla resultante:**
- Secciones de configuración: entra un SuperAdmin de cualquier compañía, **o** un usuario de compañía FLIT con `diagnostico.backoffice.ver`.
- Override del dictamen y llenado manual: regla actual (compañía FLIT + permiso), sin cambios.

**Alcance / criterios de aceptación:**
1. Un SuperAdmin de una compañía no FLIT ve la entrada «Backoffice» en el menú de `frontend-diagnostico` y abre resumen, contrato, diagnósticos (listado, grilla, historial), fuentes obligatorias e intentos de **cualquier** compañía.
2. Ese mismo usuario puede guardar cambios en contrato, fuentes obligatorias e intentos (PUT → 200).
3. Ese mismo usuario no ve las acciones de override del dictamen ni de llenado manual; si llama a esos endpoints directamente recibe **403**.
4. Un usuario no FLIT sin rol SuperAdmin sigue recibiendo **403** en `/backoffice/*`, aunque tenga el permiso `diagnostico.backoffice.ver`.
5. FLIT no cambia: su SuperAdmin y sus usuarios con el permiso conservan todo el acceso, override y llenado manual incluidos.
6. Los tokens de servicio siguen sin pasar.
7. Cada cambio de configuración hecho desde el backoffice registra el usuario y la compañía del actor (verificar si la auditoría ya existe; si no, sumarla).

**Triaje:** Caso **C** (cambia permisos y comportamiento observable; no hay work item) · **Agentes:** `backend-agent` (separar en `PoliticaBackoffice` la política de configuración de la de operación FLIT y aplicarla por endpoint), `frontend-agent` (menú, guardas de ruta y ocultar override/llenado manual en `frontend-diagnostico`), `security-agent` (revisión Habeas Data) · **Estimación:** 5 SP (BE 3 + FE 2)

**Riesgos / dependencias:**
- **Acceso entre compañías (Ley 1581):** con 1.1=c, el SuperAdmin de cualquier compañía cliente ve y edita contrato, diagnósticos e historial de las demás. Requiere el visto bueno de `security-agent` (Habeas Data) y del Líder Técnico antes del merge, y queda documentado en la HU.

## Punto 2 — Multas bloqueantes

**Descripción del usuario:** Si el vehículo tiene multas, es **bloqueante** para el trámite (va relacionado con las reglas del dictamen).
**Situación actual (código):**
- El catálogo es **código, no tabla**: las severidades están fijas en `CatalogoReglasDictamen.Definiciones` (versión 1, familia TRASPASO: STANDARD, UNILATERAL, TRANSFERENCIA_DE_DOMINIO; 14 reglas × 3 tipos = 42 filas). `reglas_dictamen` sigue sin migración. Veredicto: `BLOQUEADO` si alguna regla tiene efecto `BLOQUEA`, si no `VIABLE` (`EvaluadorDictamen`).
- `DisparaSimit` **mezcla** en una sola regla multas (comparendos > 0, sin paz y salvo, acuerdo de pago con saldo) con estado de licencia (`Suspendida`/`Cancelada`). Hay 3 reglas: `SIMIT_VEHICULO`, `SIMIT_PROPIETARIO` (no se evalúa en leasing) y `SIMIT_LOCATARIO` (en leasing **sin documento** del locatario se emite igual: «no se pudo consultar»).
- Además `INFRACCION_RUNT` (infracciones que registra el RUNT) y `LICENCIA_CONDUCTOR` (licencia suspendida/vencida/cancelada o sin certificado de aptitud), ambas como advertencia.
- Si la consulta SIMIT no está disponible se emite `FUENTE_PENDIENTE` (advierte).
- En el catálogo `Domain/Dictamen/CatalogoReglasDictamen.cs:36-42` solo **bloquean** `SOAT_NO_VIGENTE` y `RTM_NO_VIGENTE`. `SIMIT_VEHICULO/PROPIETARIO/LOCATARIO`, `INFRACCION_RUNT`, `LIMITACION`, `GRAVAMEN`, `PRENDA`, `IMPUESTO`, etc. **advierten**.
- La regla SIMIT se dispara con `CantidadComparendos > 0 || PazYSalvo == false || Suspendida || Cancelada`. Con leasing, el SIMIT del propietario no se evalúa y se evalúa el del locatario (`EvaluadorDictamen.cs:40,107,151`).
- Docs: `ficha-catalogo-reglas-dictamen.md`, `evaluador-reglas-dictamen-v1.md`.

**Decisiones:**

| # | Pregunta | Respuesta |
|---|---|---|
| 2.1 | ¿Las multas de quién bloquean? | **a) Vehículo + propietario; en leasing, locatario en lugar del propietario** (usuario 2026-11-11). Confirmar el requisito normativo con `expert-doc-engine` al cerrar el punto. |
| 2.2 | ¿Separar multas de licencia en la regla SIMIT? | **Separar.** La regla de multas (comparendos, sin paz y salvo, acuerdo con saldo) **BLOQUEA**. La licencia (suspendida/cancelada según SIMIT, y `LICENCIA_CONDUCTOR` del RUNT: suspendida, vencida, cancelada o sin certificado) queda **solo informativa (ADVIERTE), en ningún caso bloquea**. *Corrección del usuario 2026-11-11: primero eligió «c + la licencia suspendida/vencida bloquea» y luego lo corrigió.* |
| 2.3 | (Cancelada / certificado de aptitud) | Sin objeto tras la corrección de 2.2: ninguna condición de licencia bloquea. |
| 2.4 | ¿Y si no se pudo consultar el SIMIT (error, sin respuesta, leasing sin documento del locatario)? | **a) Bloquea**: si falta cualquiera de las consultas SIMIT exigidas por 2.1, el dictamen no sale VIABLE (motivo «no se pudo verificar multas»); se libera cuando la consulta responde (reintento o llenado manual FLIT). |
| 2.5 | ¿Acuerdos de pago? | **c) Nunca bloquean** (informativos). Bloquean solo comparendos pendientes y falta de paz y salvo. Ojo: si SIMIT reporta «sin paz y salvo» por un acuerdo, bloquea por esa vía. |
| 2.6 | ¿Dictámenes ya emitidos? | **Supuesto por defecto:** no se reevalúan; conservan la versión 1 del catálogo. Los nuevos se emiten con la versión 2. |

**Regla resultante (catálogo v2, familia TRASPASO):**

| Regla | Dispara cuando | Efecto |
|---|---|---|
| Multas del vehículo | SIMIT vehículo: comparendos > 0 o sin paz y salvo | **BLOQUEA** |
| Multas del propietario | SIMIT propietario (no leasing): comparendos > 0 o sin paz y salvo | **BLOQUEA** |
| Multas del locatario | SIMIT locatario (leasing): comparendos > 0 o sin paz y salvo | **BLOQUEA** |
| Multas sin verificar | Falta una de las consultas SIMIT anteriores (error, sin respuesta, leasing sin documento del locatario) | **BLOQUEA** |
| Licencia (SIMIT) | Suspendida o cancelada | ADVIERTE |
| `LICENCIA_CONDUCTOR` (RUNT) | Suspendida, vencida, cancelada o sin certificado de aptitud | ADVIERTE (sin cambio) |
| Acuerdo de pago | Acuerdo con saldo pendiente | ADVIERTE |
| `INFRACCION_RUNT` | Infracciones en RUNT | ADVIERTE (sin cambio) |

**Alcance / criterios de aceptación:**
1. Un vehículo con comparendos pendientes en SIMIT obtiene dictamen **BLOQUEADO**, con un motivo de multas propio del vehículo.
2. Lo mismo con el propietario (no leasing) y con el locatario (leasing). En leasing no se evalúan las multas del propietario.
3. Sin paz y salvo en SIMIT → BLOQUEADO, aunque la cantidad de comparendos sea 0.
4. Si falta alguna consulta SIMIT exigida → BLOQUEADO («no se pudo verificar multas»). Cuando la consulta responde sin multas, el dictamen pasa a VIABLE.
5. Licencia suspendida, vencida o cancelada, o sin certificado → aparece como advertencia y **no** cambia el veredicto.
6. Acuerdo de pago con saldo, sin comparendos y con paz y salvo → advertencia; el veredicto sigue VIABLE.
7. Los textos de las reglas (pantalla y PDF) ya no dicen «Es una advertencia» en las reglas que bloquean, y separan multas de licencia.
8. Tests del evaluador para cada fila de la tabla anterior.

**Triaje:** Caso **C** (cambia el veredicto que ve el cliente; sin work item) · **Agentes:** `expert-doc-engine` (Modo B: confirmar el paz y salvo SIMIT exigido en traspaso a vendedor/locatario), `backend-agent` (catálogo v2 + `EvaluadorDictamen`), `frontend-agent` solo si la UI muestra fijo el texto «advertencia» · **Estimación:** 3 SP (BACKEND)

**Riesgos / dependencias:**
- Con el punto 4, este bloqueo es lo que impide crear trámites con multas.
- El override del dictamen sigue exclusivo de FLIT (punto 1.2).
- Actualizar `ficha-catalogo-reglas-dictamen.md` en el mismo PR.

## Punto 3 — Reintentos

**Descripción del usuario:** Ajustar el flujo de reintentos: **3 intentos automáticos cada 2 min**; después, reintento **manual individual por placa** y **masivo para todo el lote** de las placas que quedaron pegadas.
**Situación actual (código):**
- Estados de la celda (`Domain/Consultas/EstadoCelda.cs`): `PENDIENTE → EN_CURSO → COMPLETADO | ERROR | NA`. Desde `ERROR` se puede volver a `PENDIENTE` (reproceso) o pasar a `COMPLETADO` (llenado manual).
- `PoliticaReintentos`: el máximo es **configuración global de la app** (`MaxReintentos`, 0..3, por defecto 1), no por compañía. La espera crece 5 s × 2^(n-1). `ProgramadorReintentos` (Bus) difiere la celda con `DisponibleDesde`.
- `ReconciliadorConsultas` (cada 60 s, ADR-0075) es la red de seguridad para mensajes perdidos. Una celda sin resultado tras 30 min solo cuenta como perdida si la cola FIFO ya avanzó más de 15 min por encima de ella. La primera vez la republica; la segunda la cierra en `ERROR`. Si el mensaje vence en el broker (24 h + 30 min) también queda en `ERROR`. Si core-consultas está caído no toca nada. **Una celda puede quedar horas en `PENDIENTE`/`EN_CURSO` sin pasar a `ERROR`.**
- `GET /backoffice/consultas/{id}/intentos` es solo auditoría de intentos (FLIT), no configuración.
- Reintento automático por celda (vehículo × fuente), solo para errores transitorios (`ClasificadorErrores`). Espera creciente 5 s × 2^(n-1); el máximo global es configurable entre 0 y 3, **1 por defecto** (`Domain/Consultas/PoliticaReintentos.cs:24`). Al agotarse queda en `ERROR` reprocesable.
- Reproceso manual **solo individual por celda**: `POST /consultas/{cid}/reprocesar` (`EndpointsReproceso.cs:13`). **No hay** reproceso por placa ni para el lote.
- Las consultas viajan por RabbitMQ (`PublicadorCeldas`, `ReconciliadorConsultas`, ADR-0075). Doc: `diseno-consultas-por-rabbitmq.md`.

**Decisiones:**

| # | Pregunta | Respuesta |
|---|---|---|
| 3.1 | ¿A qué fallos aplican los 3 automáticos cada 2 min? | **a) Solo transitorios.** `MaxReintentos` = 3 por defecto y espera **fija de 2 min** (en lugar de 5/10/20 s). Los definitivos siguen yendo directo a `ERROR`. |
| 3.2 | ¿Qué es una placa «pegada»? | **a)** Alguna celda en `ERROR` **o** en `PENDIENTE`/`EN_CURSO` sin respuesta por **más de 10 min**. El reintento manual de una celda en curso **descarta la respuesta tardía** del trabajo anterior (para no pisar ni duplicar resultado). |
| 3.3 | ¿El reintento manual reinicia los automáticos? | **a) Sí**: cada reintento manual reinicia el ciclo de 3 automáticos cada 2 min (`intentos_auto` vuelve a 0). |
| 3.4 | Alcance del manual (por defecto, no objetado) | Por placa: solo sus celdas pegadas (las completas no se repiten). Masivo: todas las placas pegadas del lote, un solo botón. Permiso: el actual `ConsultasReprocesar`. El dictamen se recalcula solo al llegar la respuesta. |

**Alcance / criterios de aceptación:**
1. Una consulta con error transitorio se reintenta sola hasta **3 veces**, con **2 min** entre intentos. Agotados los 3, queda en `ERROR`. El número de reintentos y la espera quedan como configuración global (por defecto 3 y 2 min).
2. Un error definitivo pasa a `ERROR` sin reintentos automáticos.
3. En el tablero, cada placa pegada (definición 3.2) muestra la acción **«Reintentar placa»**. Al usarla, sus celdas pegadas pasan a `PENDIENTE` y las completas no se tocan.
4. El lote muestra **«Reintentar pegadas (N)»** cuando N > 0. Reencola todas las celdas pegadas del lote y se oculta o queda deshabilitado cuando N = 0.
5. La acción es idempotente: pulsar dos veces seguidas no duplica consultas, porque una celda recién reencolada lleva menos de 10 min y no cuenta como pegada.
6. Si llega tarde la respuesta del trabajo anterior de una celda reintentada, se descarta. La celda queda con el resultado del trabajo nuevo.
7. Tras un reintento manual la celda vuelve a tener sus 3 automáticos.
8. Al completarse las consultas reintentadas, el dictamen de la placa se recalcula (enlaza con 2.4: «multas sin verificar» se libera).
9. Sin permiso `ConsultasReprocesar`, ambas acciones quedan ocultas y los endpoints responden 403.

**Triaje:** Caso **C** · **Agentes:** `backend-agent` (`PoliticaReintentos` con espera fija configurable, endpoints de reintento por placa y por lote, criterio «pegada», descarte por `trabajo_id`; **verificar** si el consumidor de resultados ya descarta los `trabajo_id` viejos, que el reconciliador ya republica con id nuevo), `frontend-agent` (acciones en el tablero) · **Estimación:** 8 SP (BE 5 + FE 3)

**Riesgos / dependencias:**
- Más reintentos significa más consultas pagas al proveedor; vigilar costo en lotes grandes.
- El reconciliador sigue igual (red de seguridad). El manual solo adelanta lo que hoy esperaría 30 min o más.
- Actualizar `diseno-consultas-por-rabbitmq.md` en el mismo PR.

## Punto 4 — Crear trámite con dictamen favorable

**Descripción del usuario:** _pendiente_
**Situación actual (código):**
- **No existe** paso de dictamen a trámite en core-api: no hay ExternalRef, `Origin='diagnostico'` ni token opaco, ni individual ni masivo. El front solo tiene un enlace `tramitesUrl` en el shell (`frontend-diagnostico/components/DiagnosticoShell.tsx:75`).
- El plan v2 recomendaba un token opaco de un solo uso + `Origin='diagnostico'`/`ExternalRef` (sin implementar).
- **Diseño previo ya escrito:** `plan-pendientes-dictamen-a-flito.md` §2 «Derivación a trámite» (D7, **épica #12679 en New**). Es solo **individual** y abre el **wizard** de Trámites con contexto:
  - Tabla `diagnostico.derivaciones` con token SHA-256, TTL de 10 min y un solo canje. Endpoint `POST /vehiculos/{vid}/derivaciones` → `{url, expira_en}`; 409 si el vehículo ya originó un trámite.
  - gRPC `DerivacionService.CanjearContexto`. Trámites crea el borrador con `Origin=diagnostico` y `ExternalRef=vehiculo_id`. La fila muestra «Ver trámite #N».
  - Ya existe en código: el permiso `diagnostico.derivacion.crear` y la capacidad `derivar` (el backoffice no la tiene).
  - Dependencias abiertas: el **frente T1** (core-api + wizard de `frontend/`: aceptar `?ctx=`, canjear y precargar), el **ADR (h)** (Propuesto, falta que lo acepte el LT) y la **D-14 del PO**.
  - **El caso masivo no está diseñado.**
- Antes de crear HUs, verificar en ADO qué hay bajo la épica #12679 (si ya existen HUs, el flujo es `/implement-story`).

**Decisiones:**

| # | Pregunta | Respuesta |
|---|---|---|
| 4.1 | ¿Qué produce «Crear trámite»? | **b) Borrador en Trámites, tanto individual como masivo, sin abrir el wizard.** Diagnóstico crea el borrador servidor a servidor (`Origin=diagnostico`, `ExternalRef=vehiculo_id`), con placa, tipo, datos del vehículo, propietario y RUNT; el usuario lo completa después desde Trámites. **Reemplaza** el mecanismo token/canje/wizard de `plan-pendientes-dictamen-a-flito.md` §2 → el ADR (h) debe reescribirse o quedar sustituido por uno nuevo. |
| 4.2 | ¿Qué placas entran en el masivo? | **a) Selección del usuario, sin preselección.** Casillas por fila + «seleccionar todas» (las elegibles del lote, también de la página/filtro actual), «quitar selección» y contador de seleccionadas. Solo se pueden marcar placas VIABLE sin trámite creado; las demás aparecen deshabilitadas con el motivo. |
| 4.3 | Tipo de trámite del borrador (por defecto) | Lote Con análisis → el `tipo_tramite_codigo` del lote. Lote Simple → un modal pide el tipo una sola vez para todas las seleccionadas (catálogo `ListarTiposTramite` ya existe). |
| 4.4 | ¿OT del borrador? | **a) El OT de matrícula según RUNT.** Si la compañía no lo tiene en su lista efectiva de OT, esa placa no se crea y queda en el resumen con el motivo «OT no habilitado para la compañía». |
| 4.5 | ¿Cómo se ejecuta el masivo? | **a) En segundo plano con progreso y resumen** (creadas / omitidas con motivo, reintentar fallidas, se puede salir de la pantalla). **Requisito del usuario: arquitectarlo para que no sature el sistema en picos de carga muy altos.** |
| 4.6 | Detalles por defecto (no objetados) | **Individual:** misma operación con una placa, síncrona; al terminar la fila muestra «Ver trámite #N». **Permiso:** el existente `diagnostico.derivacion.crear` (el backoffice no lo tiene). **Revalidación:** al ejecutar cada placa se vuelve a comprobar que sigue VIABLE y sin trámite. **Autoría:** el borrador queda a nombre del usuario que lanzó la acción, no de una cuenta de servicio. **Trámite cancelado:** para otro trámite del mismo vehículo se hace un diagnóstico nuevo (regla del plan previo). |

**Requisitos no funcionales (carga), para el `architecture-agent`:**
- Cola durable para los trabajos de creación (alinear con ADR-0075 / RabbitMQ, que ya usan las consultas), nunca en memoria.
- **Concurrencia acotada y configurable** hacia core-api, y límite por compañía (*fairness*): un lote enorme de una compañía no puede acaparar a las demás.
- **Contrapresión:** si core-api responde lento o con 429/5xx, bajar el ritmo y reintentar con espera; no amplificar la carga.
- **Idempotencia** por `Origin=diagnostico` + `ExternalRef=vehiculo_id` en Trámites: reintentos y doble clic nunca crean dos borradores.
- Las lecturas del progreso no golpean core-api (contadores propios en Diagnóstico).
- Pruebas de carga con un objetivo explícito (p. ej. N lotes simultáneos × M placas) que fije el arquitecto con el LT.

**Alcance / criterios de aceptación:**
1. En el tablero de un lote, cada fila VIABLE sin trámite tiene casilla y la acción «Crear trámite». Las filas no elegibles muestran la casilla deshabilitada con el motivo (no VIABLE, ya tiene trámite).
2. No hay preselección. Hay «seleccionar todas las elegibles», «quitar selección» y contador.
3. «Crear trámite» individual crea un borrador en Trámites con `Origin=diagnostico`, `ExternalRef=vehiculo_id`, el tipo del lote (o el elegido en Simple), el OT de matrícula del RUNT y los datos del vehículo, del propietario y del RUNT. La fila pasa a «Ver trámite #N», que abre el borrador en Trámites.
4. «Crear trámites (N)» masivo encola la operación y muestra el progreso. Al terminar, muestra un resumen de creadas y omitidas con motivo, y deja reintentar las fallidas.
5. Si el OT de matrícula no está habilitado para la compañía, la placa queda omitida con ese motivo.
6. Un segundo intento sobre un vehículo con trámite no crea otro (409 individual, «ya tenía trámite» en masivo).
7. Si al ejecutar una placa ya no es VIABLE (por ejemplo, el dictamen se recalculó tras un reintento), se omite con motivo.
8. Sin `diagnostico.derivacion.crear` las acciones quedan ocultas y los endpoints responden 403. El backoffice no puede crear trámites.
9. En una prueba de carga con el objetivo acordado, core-api no supera el umbral de latencia y errores definido, y las demás compañías siguen operando.

**Triaje:** Caso **C**, grande y transversal (Diagnóstico + core-api + 2 frontends) → candidato a **`/refine-requirement`** o, como mínimo, diseño previo de `architecture-agent` (ADR nuevo que sustituye al ADR (h)). **Agentes:** `architecture-agent`, `backend-agent` (core-diagnostico: selección, cola, worker, progreso; core-api: endpoint de creación de borrador por origen externo con idempotencia), `database-agent` (tabla de operaciones/derivaciones, índice único por vehículo), `frontend-agent` (selección y progreso en `frontend-diagnostico`; enlace «Ver trámite» hacia `frontend/`), `security-agent` (PII del propietario entre servicios), `qa-agent` (carga) · **Estimación:** ≈ 21 SP (BE diagnóstico 8 + BE core-api 5 + FE 5 + carga/QA 3); se partirá en varias HUs.

**Riesgos / dependencias:**
- core-api es de otro frente (T1 en el plan previo): confirmar quién implementa su lado.
- Depende del punto 2: sin el bloqueo por multas, se crearían borradores de vehículos con multas.
- Revisar la épica #12679 en ADO para reutilizarla en vez de crear otra.
- PII: el documento del propietario viaja entre servicios. Cifrado en tránsito, sin logs con datos (Ley 1581).

## Punto 5 — Nombres técnicos → entendibles

**Descripción del usuario:** «Traducción de nombres técnicos a laborales». Interpretación confirmada (2026-11-11): donde el producto muestra **códigos internos** (enums, códigos de fuente, regla, estado, tipo de trámite, error o motivo NA), mostrar un **nombre de negocio** entendible por un usuario de operación. Solo presentación: los códigos se siguen guardando y viajando igual por la API.
**Enfoque:** inventario por superficie (pantallas, filtros, badges, PDF, Excel, ZIP, errores) → **diccionario único** código → rótulo, usado por todas las superficies → reemplazo → validación de los textos con negocio. Se cruza con el punto 8 (el índice nuevo sale ya con rótulos).
**Situación actual (código) — inventario explore-agent 2026-11-11:**

| Superficie | Dónde | Qué sale crudo |
|---|---|---|
| Pantalla · dictamen | `PanelDictamen.tsx:150,152` | **El título de cada alerta es su código** (`SIMIT_PROPIETARIO`, `SOAT_NO_VIGENTE`…) y «Fuente: {código}». Es el hueco más visible; el front no tiene diccionario de códigos de alerta. |
| Pantalla · backoffice fuentes obligatorias | `FuentesObligatoriasBackoffice.tsx:46,51` | Familia y `Tipo {tipo_tramite_codigo}` (`TRASPASO_STANDARD`…). `SeccionModo.tsx:135` ya lista los tipos y podría dar el nombre. |
| ZIP · `indice.txt` | `EmpaquetadorZip.cs:250,258` | Enum `vehiculo.Estado` crudo y el **GUID** del diagnóstico en el encabezado, en vez de su nombre. |
| PDF por vehículo | `PdfVehiculo.cs:421` | El título de sección cae al código de la fuente si falta en `Titulos`. |
| PDF por vehículo | `PdfVehiculo.cs:221-619` vía `R()` (`:311`) | Estados de solicitudes, garantías, infracciones, licencias, certificados, comparendos, acuerdos y avalúo pasan sin traducir (falta verificar si los valores vienen en mayúsculas). |
| PDF · dictamen | `PdfVehiculo.cs:345` | Texto «redacción IA pendiente»: no es un código, pero nombra la IA ante el cliente. |
| Tooltips de motivo | `FuentesObligatoriasBackoffice.tsx:302`, `SeccionAlcance.tsx:152`, `ResumenValidacion.tsx:69` | Pintan `{motivo}` tal cual llega (origen sin verificar). |

**Ya resuelto (patrón a reutilizar):** en el front, `nombreFuente` (`lib/configuracion.ts:82`), `nombreModo`/`etiquetaEstado` (`lib/diagnosticos/listado.ts:203,208`) y `etiquetasRespuesta.ts` (nombres de campos de las respuestas, con test de contrato; no traduce valores de enum). En el back, `PdfVehiculo.Estado()` (`:293`), `VeredictoEnPalabra`, `NombreFuenteAvaluo`, `ColumnasExcel.Titulo` y `EtiquetaTipoAnalisis`. **El Excel del lote ya sale traducido.**
**No hay diccionario central:** al menos 3 módulos en el front y varios en el back.
**Pendiente de revisar (al implementar):** mensajes de error de la API pintados tal cual (`ListadoDiagnosticos.tsx:95` `error.code`), opciones de select en llenado manual (`FormularioLlenadoManual.tsx:278-279`), `ChipEstadoCelda` y `DescargasLote` (`:127`).

**Decisiones:**

| # | Pregunta | Respuesta |
|---|---|---|
| 5.1 | ¿Dónde vive el diccionario de rótulos? | **a) En el backend, fuente única.** La API devuelve `{codigo, etiqueta}`; pantalla, PDF, Excel y ZIP leen del mismo diccionario. Los rótulos de las reglas del dictamen viven en el catálogo (nombre corto junto a la plantilla de texto). Los diccionarios dispersos del front (`nombreFuente`, `etiquetaEstado`…) pasan a consumir la etiqueta de la API o quedan como respaldo solo si falta. |
| 5.2 | Rótulos de las alertas del dictamen | **a) Aprobada la tabla propuesta** (abajo). El texto largo de cada alerta sigue siendo la plantilla del catálogo. En el PDF, «redacción IA pendiente» → **«Análisis en preparación»**. |
| 5.3 | Resto de rótulos (por defecto) | Tipos de trámite: el nombre que ya entrega `ListarTiposTramite`. Estados de vehículo/lote/celda y estados de las fuentes del PDF: los propone el implementador reutilizando los existentes (`etiquetaEstado`, `PdfVehiculo.Estado()`, `ColumnasExcel.Titulo`) y se listan en el PR para revisión. |

**Rótulos aprobados de las alertas:**

| Código | Rótulo |
|---|---|
| `SOAT_NO_VIGENTE` | SOAT no vigente |
| `RTM_NO_VIGENTE` | Revisión técnico-mecánica no vigente |
| `ESTADO_VEHICULO` | Vehículo no activo en el RUNT |
| Multas del vehículo (v2) | Multas del vehículo |
| Multas del propietario (v2) | Multas del propietario |
| Multas del locatario (v2) | Multas del locatario |
| Multas sin verificar (v2) | No se pudo verificar multas |
| Licencia según SIMIT (v2) | Licencia suspendida o cancelada (SIMIT) |
| `LICENCIA_CONDUCTOR` | Licencia de conducción con novedad |
| Acuerdo de pago (v2) | Acuerdo de pago vigente |
| `LIMITACION` | Limitación a la propiedad |
| `GRAVAMEN` | Gravamen registrado |
| `GARANTIA_MOBILIARIA` | Garantía mobiliaria registrada |
| `PRENDA` | Prenda registrada |
| `INFRACCION_RUNT` | Infracciones registradas en el RUNT |
| `IMPUESTO` | Impuesto vehicular pendiente |
| `FUENTE_PENDIENTE` | Consulta pendiente: {nombre de la fuente} |

**Alcance / criterios de aceptación:**
1. Ninguna pantalla de `frontend-diagnostico` muestra un código interno como texto visible (alertas, fuente, tipo de trámite, familia, estados, motivos, opciones de filtros y selects). Los códigos solo viajan como valores.
2. El panel del dictamen titula cada alerta con su rótulo y muestra «Fuente: {nombre de la fuente}».
3. El PDF, el Excel y el índice del ZIP usan los mismos rótulos que la pantalla, tomados del mismo diccionario del backend. El índice muestra el nombre del diagnóstico, no el GUID.
4. Si llega un código sin rótulo, la UI muestra un texto genérico neutro («Novedad sin clasificar»), nunca el código, y queda registrado en el log técnico.
5. Un test recorre todos los códigos de cada enum o catálogo y falla si alguno no tiene rótulo.
6. Los mensajes de error de la API que se muestran al usuario salen en lenguaje de negocio (`title`/`detail`), no con `error.code`.

**Triaje:** Caso **C** liviano (cambia lo que el usuario lee y amplía el contrato de la API con `etiqueta`; sin cambio de reglas) · **Agentes:** `backend-agent` (diccionario único, `{codigo, etiqueta}` en DTOs/OpenAPI, PDF/ZIP), `frontend-agent` (consumir etiquetas y retirar códigos visibles) · **Estimación:** 5 SP (BE 3 + FE 2)

**Riesgos / dependencias:**
- Implementar después del punto 2 (catálogo v2 con las reglas nuevas) para no rotular dos veces.
- El índice del ZIP lo reescribe el punto 8: coordinar en la misma HU o en orden.

## Punto 6 — Selector de compañía en backoffice

**Descripción del usuario:** El identificador de compañía debe ser un **selector** en el módulo de backoffice.
**Situación actual (código):**
- `SelectorEmpresa` es un `<form>` con un campo de texto para pegar el UUID (validado, distinto de cero). Lo usa `ListaLotesBackoffice.tsx:224` (listado de diagnósticos del backoffice), y la elección viaja como `?empresa=` en cada llamada.
- `GET /backoffice/empresas` (HU #13741) ya devuelve `[{id, nombre, nit}]`: las empresas **activas con Diagnóstico encendido**, ordenadas por nombre (sin depender de ICU), tomadas de Identidad. Responde 503 explícito si Identidad no está configurada o no responde, nunca una lista vacía falsa. Hoy exige `diagnostico.configuracion.contrato.editar` además de la política del backoffice; se pensó para el editor de contrato.
- `frontend-diagnostico/components/backoffice/SelectorEmpresa.tsx:17,52` es un input libre para un UUID que viaja como `?empresa=`. El comentario dice que no hay listado, pero **ya existe** `EmpresasBackofficeEndpoints.Listar` (`:27`, exige el permiso `contrato.editar`).
- **Componente de referencia (lo pidió el usuario):** «Contrato de una empresa (backoffice FLIT)» en `frontend-diagnostico/components/configuracion/PestanaContrato.tsx:237`. Usa `SearchableSelect` de `@flit/ui` (opción = nombre + NIT como `hint`, «Ninguna empresa coincide») y el servicio `listarEmpresasBackoffice` (`lib/configuracion.ts:158`). Ya resuelve los estados cargando (`VehicleLoader`), error (`InlineAlert`, que distingue «no configurado» de «no disponible»), vacío («No hay empresas con Diagnóstico habilitado») y listo.
- El UUID libre solo se pide en `ListaLotesBackoffice.tsx:224`. El detalle del lote y el de la consulta reciben `?empresa=` y, sin una válida, vuelven a esa lista. Fuentes obligatorias sin `?empresa=` es el catálogo de FLIT (no lleva selector).

**Decisiones:**

| # | Pregunta | Respuesta |
|---|---|---|
| 6.1 | ¿Qué compañías aparecen? | **a) Solo las que tienen Diagnóstico encendido** (lo que ya devuelve `GET /backoffice/empresas`). |
| 6.2 | ¿Qué componente se usa? | **El mismo del editor de contrato** (usuario): `SearchableSelect` + `listarEmpresasBackoffice` y sus 4 estados. Se extrae a un componente compartido (`SelectorEmpresaBackoffice`) que usan `PestanaContrato` y `ListaLotesBackoffice`; se retira el `SelectorEmpresa` de UUID libre. |

**Alcance / criterios de aceptación:**
1. En el listado de diagnósticos del backoffice, la compañía se elige en un selector con búsqueda por nombre o NIT, idéntico al del editor de contrato. No hay campo para pegar el UUID.
2. Al elegir una compañía, el listado se carga con `?empresa={id}` (comportamiento actual) y la página vuelve a 1. Al recargar o volver desde el detalle, el selector muestra la compañía de la URL ya elegida.
3. Estados: cargando, error de Identidad (no configurado / no disponible, con reintento), vacío («No hay empresas con Diagnóstico habilitado») y listo, iguales a los del editor de contrato.
4. `GET /backoffice/empresas` deja de exigir `diagnostico.configuracion.contrato.editar` y pasa a la política de configuración del backoffice del punto 1 (SuperAdmin de cualquier compañía o FLIT con `diagnostico.backoffice.ver`).
5. El editor de contrato sigue funcionando igual, ahora con el componente compartido.
6. Accesibilidad: el selector tiene etiqueta, se maneja con teclado y anuncia los resultados (WCAG 2.1 AA, como `SearchableSelect`).

**Triaje:** Caso **C** liviano (cambia la interacción del backoffice y el permiso del endpoint) · **Agentes:** `frontend-agent` (extraer y reutilizar el selector), `backend-agent` (permiso del endpoint, en la misma HU del punto 1) · **Estimación:** 2 SP (FE 2; el cambio de permiso entra en el punto 1)

**Riesgos / dependencias:**
- Depende del punto 1 (nueva política de acceso al backoffice).

## Punto 7 — Placas especiales

**Descripción del usuario:** Hacer pruebas con placas especiales: **maquinaria, motos, carros y motocarros**.
**Situación actual (código):**
- `PLATE_PATTERN` (`frontend-diagnostico/lib/validation/fieldRules.ts:21`): carro `AAA999`, moto `AAA99[A]`, remolque/semirremolque `[RS]9{5,6}`, maquinaria `AA999999`. **No incluye** el motocarro `123ABC`, que `frontend/` de trámites sí acepta.
- El validador .NET replica estas reglas (`Domain/Vehiculos/ValidadorIdentificador.cs:79`). Hay un fixture compartido en `contracts/fixtures/validacion/identificadores-vehiculo.json` y un test que compara el patrón de Diagnóstico con el de trámites (`identificadoresCompartidos.test.ts:83`).
- El fixture (v1, 36 casos inventados) tiene los tipos `PLACA_CARRO`, `PLACA_MOTO`, `PLACA_REMOLQUE`, `PLACA_MAQUINARIA`, `VIN` e `INVALIDO`. **No hay tipo motocarro.** Clasifica en el orden carro → moto → remolque → maquinaria.
- En `frontend/` (Trámites), el motocarro `123ABC` existe como grupo propio (`PlacaOtField.tsx:22`, `MOTOCARRO`), fuera de `PLATE_PATTERN`.
- **Hipótesis a verificar (posible defecto):** `EvaluadorDictamen.EvaluaRtm` exige RTM desde los **5 años** de matrícula para todo vehículo. La norma (Decreto 019/2012 art. 202, por confirmar con `expert-doc-engine`) da plazos distintos: particular a partir del 6.º año; **motos y servicio público desde el 2.º año**. Si se confirma, una moto con 2–5 años sin RTM saldría VIABLE. Además, la maquinaria podría no requerir SOAT/RTM.

**Decisiones:**

| # | Pregunta | Respuesta |
|---|---|---|
| 7.1 | ¿Motocarro? | **a) Agregar soporte en este paquete.** Rama `123ABC` (3 dígitos + 3 letras) en `PLATE_PATTERN` de `frontend-diagnostico`, en `ValidadorIdentificador` (.NET) y en el fixture compartido (tipo nuevo `PLACA_MOTOCARRO`, `version` 2), alineado con Trámites. **Requiere el visto bueno del LT** (pendiente del PR #823) al crear la HU. |
| 7.2 | ¿Cómo se prueba? | **a) Dos capas:** (1) matriz automatizada por clase (carro, moto, motocarro, maquinaria, remolque): validación, fuentes que aplican/no aplican y reglas del dictamen por clase; (2) prueba en QA con **al menos una placa real por clase**. Placas reales: las consigue el usuario o QA. **El usuario pide la data completa visible, sin enmascarar** → ver 7.3 (choca con la regla 5 de CLAUDE.md y la Ley 1581 si la evidencia va a ADO o al PR). |
| 7.3 | ¿Dónde queda la evidencia completa? | **a) Evidencia sin enmascarar en una ubicación de acceso restringido** (carpeta del equipo QA con acceso controlado, o la propia app en QA). **En ADO/PR, solo el resultado enmascarado** (pasa/falla por clase, reglas disparadas, fuentes NA) más la referencia a esa ubicación. En la app la data se sigue viendo completa (decisión 2026-10-27). |
| 7.4 | Si se confirma el defecto de RTM por clase (por defecto) | `expert-doc-engine` dictamina primero plazos de RTM y exigencia de SOAT por clase y servicio. Si el dictamen confirma la discrepancia, se registra como **Bug** (Caso B, `/fix-bug`) dentro de este paquete, con la matriz de 7.2 como test de regresión. |

**Alcance / criterios de aceptación:**
1. **Motocarro:** `123ABC` (y con espacios, guiones o minúsculas) se acepta como `PLACA_MOTOCARRO` en la carga individual y masiva, igual en front y en .NET. El fixture compartido v2 lo incluye y el test de alineación con Trámites sigue verde.
2. **Matriz automatizada** (tests del evaluador y de la carga) por clase: carro particular, carro de servicio público, moto, motocarro, maquinaria y remolque. Cada fila declara qué fuentes aplican o quedan en NA y qué reglas pueden disparar (SOAT, RTM con su plazo, SIMIT, impuesto). Las expectativas salen del dictamen de `expert-doc-engine`.
3. **Prueba en QA:** al menos una placa real por clase recorre carga → consultas → dictamen → PDF/Excel/ZIP sin errores inesperados. Lo que se observe del proveedor real (formatos, NA, errores) se anota.
4. **Evidencia:** completa en la ubicación restringida; en ADO, resultado enmascarado + referencia.
5. Cualquier discrepancia encontrada queda como Bug con su caso en la matriz.

**Triaje:** motocarro → Caso **C** (amplía lo que acepta la carga) · matriz y prueba → `qa-agent` (estrategia + Modo B) · RTM/SOAT por clase → posible Caso **B** según el dictamen · **Agentes:** `expert-doc-engine` (Modo B: SOAT/RTM por clase y servicio), `backend-agent` + `frontend-agent` (motocarro), `qa-agent` (matriz, prueba en QA, `playwright-runner`) · **Estimación:** 8 SP (motocarro FULLSTACK 3 + matriz y prueba en QA 5); el eventual Bug de RTM se estima aparte.

**Riesgos / dependencias:**
- Visto bueno del LT para el motocarro.
- Placas reales por clase (maquinaria y motocarro son las difíciles de conseguir).
- Costo de las consultas reales en QA.
- Los proveedores podrían no soportar maquinaria o motocarro en alguna fuente: entonces esa fuente queda NA con motivo, sin error.

## Punto 8 — Índice del ZIP masivo

**Descripción del usuario:** Cambiar el archivo índice del ZIP de los masivos por un Excel o un CSV.
**Situación actual (código):**
- `EmpaquetadorZip` (servidor, en streaming, por `ColaEvidencias`/`ServicioEvidencias`) escribe `indice.txt` en UTF-8 separado por tabulaciones. Lleva una cabecera (id del lote, fecha y hora de Colombia, N de M con PDF) y las columnas `Orden, Identificador, Estado, En el ZIP, Archivo` (`EmpaquetadorZip.cs:100,246`). `Estado` sale como enum crudo (se cruza con el punto 5).

**Decisiones:**

| # | Pregunta | Respuesta |
|---|---|---|
| 8.1 | ¿Excel o CSV? | **a) Excel `.xlsx`**, reutilizando la infraestructura del Excel del lote (`ExcelLote`, `ColumnasExcel`, `Infraestructura/Excel`): mismo formato y rótulos, placas como texto, encabezado con filtros y vínculo relativo al PDF de cada fila dentro del ZIP. Reemplaza a `indice.txt`. |
| 8.2 | ¿Qué columnas? | **a) Las de hoy con rótulos + 3 operativas** (el usuario cambió de b a a, 2026-11-11): Orden, Placa/VIN, Estado, En el ZIP (Sí/No), Archivo (con vínculo) **+ Dictamen** (Viable / Bloqueado / Pendiente) **+ Motivos de bloqueo** (rótulos del punto 5, separados por «; », solo las reglas que bloquean) **+ Trámite** (número del borrador del punto 4, vacío si no hay). |
| 8.3 | Detalles por defecto | Nombre `indice.xlsx`. La cabecera de hoy va en las filas superiores de la misma hoja, sobre la tabla: **nombre** del diagnóstico (no el GUID), fecha y hora de Colombia, «N de M con PDF». Una sola hoja. |

**Alcance / criterios de aceptación:**
1. El ZIP de la descarga masiva trae `indice.xlsx` y ya no trae `indice.txt`.
2. Encima de la tabla aparecen el nombre del diagnóstico, la fecha y hora de generación (Colombia) y «N de M vehículos con PDF».
3. La tabla tiene las columnas Orden, Placa/VIN, Estado, En el ZIP, Archivo, Dictamen, Motivos de bloqueo y Trámite, con encabezado y autofiltro. Dictamen sin emitir → «Pendiente». Motivos solo con dictamen Bloqueado. Trámite con el número cuando existe borrador del punto 4. Placa/VIN es texto (sin conversión numérica). El Estado usa el rótulo del diccionario del punto 5, nunca el código.
4. En las filas con PDF, «Archivo» abre el PDF correspondiente cuando el ZIP está descomprimido (vínculo relativo). Sin PDF, queda vacío y «En el ZIP» = No.
5. Se mantiene la generación en streaming por `ColaEvidencias`: el índice no carga el lote entero en memoria, ni siquiera en lotes grandes.
6. Tests: el contenido del `.xlsx` (cabecera, columnas y una fila con y otra sin PDF) y el ZIP sin `indice.txt`.

**Triaje:** Caso **C** liviano (cambia un archivo que recibe el usuario) · **Agentes:** `backend-agent` (`EmpaquetadorZip` + escritor Excel existente) · **Estimación:** 3 SP (BACKEND)

**Riesgos / dependencias:**
- Usa el diccionario de rótulos del punto 5: implementar después de él o en la misma HU.
- La columna «Motivos de bloqueo» depende del catálogo v2 (punto 2) y «Trámite», de `tramite_ref` (punto 4). Si el punto 4 sale después, la columna se publica vacía y se llena cuando exista, sin cambiar el formato.
- Verificar que el escritor Excel actual admite escritura incremental (streaming) para lotes de miles de placas.

## Punto 9 — Diseño: iconos, tablas y filtros

**Descripción del usuario:** _pendiente_
**Situación actual (código):**
- `@flit/ui` se usa solo en parte: InlineAlert, Loader, ilustraciones, CabeceraModulo, StatusBadge, Pagination, Modal. Las tablas y los filtros son **propios** (`components/listado`, `components/tablero`, `components/backoffice` con `<input>` nativos).
**Decisiones:** _pendiente_

---

<!-- Plantilla por punto:

## Punto N — <título>

**Descripción del usuario:**
**Situación actual (código):**
**Decisiones:**
| # | Pregunta | Respuesta |
**Alcance / criterios de aceptación:**
**Triaje:** Caso _ · **Agentes:** _ · **Estimación:** _ SP
**Riesgos / dependencias:**
-->

---

# Plan de implementación — ola única (2026-11-11)

> Rama: `feature/ajustes-diagnostico-2026-11-11` (desde `origin/develop` 8ade059e4). **Sin work items en ADO** por pedido del usuario: registro diferido en `.claude/state/pending-work-items/2026-11-11-ajustes-diagnostico.md`; sin work item registrado no hay merge (gate §2 CLAUDE.md).
> Alcance: puntos 1–8. **Punto 9 fuera** (no refinado). Un commit por punto (+1 commit de docs del plan y, si aplica, el Bug de RTM separado dentro del punto 7).
> Método: tests en **rojo antes** de implementar (log rojo guardado por punto en `.claude/state/diagnostico-ola-2026-11-11/`), corridas **filtradas** por símbolo tocado durante el desarrollo, **regresión completa al final** (core-diagnostico + frontend-diagnostico; core-api filtrada a lo tocado por el punto 4).

## Validación de los puntos (2026-11-11)

Reverificado contra la rama (explore-agent) y dictamen normativo (expert-doc-engine). Cambios respecto al refinamiento:

| # | Hallazgo | Efecto en el plan |
|---|---|---|
| V1 | `DisparaSimit` hoy **también** dispara con cualquier acuerdo con saldo ≠ 0 (`EvaluadorDictamen.cs:151-160`). | Se retira esa condición de la regla de multas (decisión 2.5) y el acuerdo pasa a regla propia que advierte. |
| V2 | **Unilateral (Res. 20233040017145, art. 5.3.2.2):** no se valida paz y salvo de multas ni RTM. | **Decisión derivada del dictamen (a confirmar por el usuario):** en `TRASPASO_UNILATERAL` las reglas de multas, «multas sin verificar» y `RTM_NO_VIGENTE` **advierten** (no bloquean). STANDARD y TRANSFERENCIA_DE_DOMINIO bloquean según 2.1/2.4. |
| V3 | Multas «del vehículo»: la norma exige paz y salvo del **propietario** (o del locatario en leasing), no del vehículo. | Se mantiene el bloqueo (decisión 2.1) como **política FLIT**; el texto de la regla no la atribuye a la norma. El comprador no se evalúa (correcto). |
| V4 | **RTM por clase (Ley 769 art. 52, mod. Decreto 019/2012 art. 202):** público, moto y motocarro desde los **2 años**; particular desde los **5**; maquinaria y remolque NA. Hoy `EvaluaRtm` usa 5 años para todos → **defecto confirmado (Caso B)**. | Se corrige dentro del punto 7, en un commit propio de Bug (registro diferido como Bug). Requiere sumar `Clase` y `Servicio` a `HechosRuntVehiculo` y al mapeo del RUNT (hoy no llegan). Clase desconocida → 5 años (comportamiento actual). |
| V5 | SOAT: remolque/semirremolque exento (NA); maquinaria NA con advertencia hasta confirmar. | Entra en la matriz del punto 7. |
| V6 | El consumidor de resultados **ya descarta** los `trabajo_id` viejos (`ConsumidorResultados.cs:21-24`). | Punto 3: basta con asignar `trabajo_id` nuevo al reintentar; se agrega test, no código nuevo de descarte. |
| V7 | No hay timestamp propio de cambio de estado en la celda; `ActualizadoEn` es «último cambio de la celda». | Punto 3 usa `ActualizadoEn` para el criterio de «> 10 min». |
| V8 | Los textos de las reglas están sembrados en `reglas_dictamen` (migración D6) y la versión se guarda en `catalogo_version`. | Punto 2 = catálogo `Version = 2` + migración que siembra las filas v2 (v1 se conserva para los dictámenes emitidos). |
| V9 | `GET /backoffice/empresas` exige `contrato.editar`. | Punto 6 lo pasa a la política de configuración del backoffice (punto 1). |
| V10 | El nombre del diagnóstico ya está cargado en `EmpaquetadorZip` (`:163`); el Excel usa `OpenXmlWriter` (SAX, streaming). | Punto 8 viable en streaming sin cambios de infraestructura. |

## Carriles y orden de commits

Los puntos que tocan los mismos archivos van en serie; el resto en paralelo en worktrees aislados y se integran por cherry-pick en este orden:

| Orden | Commit | Carril | Agentes |
|---|---|---|---|
| 0 | Docs: plan de la ola | — | orquestador |
| 1 | P2 Multas bloqueantes (catálogo v2) | Dictamen | backend |
| 2 | P1 Backoffice para SuperAdmin de cualquier compañía | Backoffice | backend + frontend |
| 3 | P6 Selector de compañía | Backoffice | frontend + backend (permiso) |
| 4 | P3 Reintentos (3 × 2 min, por placa y por lote) | Consultas | backend + frontend |
| 5 | P7 Motocarro + matriz por clase (+ commit Bug RTM/SOAT por clase) | Dictamen | backend + frontend |
| 6 | P5 Rótulos de negocio | Dictamen/Reportes | backend + frontend |
| 7 | P8 Índice del ZIP en Excel | Reportes | backend |
| 8 | P4 Crear trámite individual y masivo | Transversal | backend (diagnóstico + core-api) + database + frontend, según `diseno-crear-tramite-desde-diagnostico.md` y su ADR (Propuesto) |

## Cambios técnicos por punto

- **P2:** `CatalogoReglasDictamen` v2 (efecto por tipo: multas BLOQUEA en STANDARD/TRANSFERENCIA, ADVIERTE en UNILATERAL); regla multas = comparendos > 0 o sin paz y salvo (sin licencia ni acuerdo); reglas nuevas: multas sin verificar, licencia SIMIT y acuerdo de pago; `FUENTE_PENDIENTE` de SIMIT sustituida por «multas sin verificar» donde aplique; migración con semilla v2; `ficha-catalogo-reglas-dictamen.md`. Tests rojos: una fila por regla de la tabla v2 + unilateral.
- **P1:** `PoliticaBackoffice` → dos políticas (configuración: SuperAdmin de cualquier compañía **o** FLIT + `backoffice.ver`; operación FLIT: override y llenado manual, sin cambio). `Capacidades` expone `backoffice` y `backofficeOperacion`; el front oculta override/llenado manual. Tests rojos: matriz de actores (SuperAdmin no FLIT, usuario no FLIT con permiso, FLIT, token de servicio) × secciones.
- **P6:** `SelectorEmpresaBackoffice` compartido (de `PestanaContrato`) en `ListaLotesBackoffice`; se retira `SelectorEmpresa`; `GET /backoffice/empresas` con la política de configuración.
- **P3:** `PoliticaReintentos`: por defecto 3, espera fija configurable (2 min); endpoints `POST /diagnosticos/{id}/vehiculos/{vid}/reintentar` y `POST /diagnosticos/{id}/reintentar-pegadas` (permiso `ConsultasReprocesar`), criterio «pegada» = ERROR o PENDIENTE/EN_CURSO con `ActualizadoEn` > 10 min, `trabajo_id` nuevo e `IntentosAuto = 0`; conteo de pegadas en el tablero; botones en `TableroLote`; `diseno-consultas-por-rabbitmq.md`.
- **P7:** `PLACA_MOTOCARRO` (`123ABC`) en `fieldRules.ts`, `ValidadorIdentificador.cs` y fixture v2; matriz por clase en tests. **Bug RTM/SOAT:** `Clase`/`Servicio` en `HechosRuntVehiculo` + mapeo RUNT; plazo de RTM por clase; SOAT NA en remolque. La prueba en QA con placas reales queda **pendiente** (no se puede correr en local; evidencia restringida según 7.3).
- **P5:** diccionario único en el backend (`RotulosDiagnostico`), `etiqueta` junto a `codigo` en los DTO de alertas/fuentes/tipos + OpenAPI; PDF («Análisis en preparación», fallback «Novedad sin clasificar»); front consume `etiqueta`; test de cobertura de rótulos.
- **P8:** `indice.xlsx` con `TabularWorkbookWriter` (cabecera con nombre, fecha Colombia, N de M), columnas del 8.2; fuera `indice.txt`. «Trámite» vacío hasta que exista P4 (se llena en el commit de P4).
- **P4:** según el diseño y el ADR; partido en incrementos.

## Pendientes que no resuelve esta ola (gates)

- Registrar los work items (`/register-work`) antes del merge; visto bueno del LT (motocarro y acceso entre compañías); revisión de seguridad/Habeas Data de P1 y P4; aceptación del ADR del P4 por el LT; prueba de carga del P4 y QA con placas reales del P7 en ambiente QA; confirmación del usuario de la decisión derivada V2 (unilateral).
