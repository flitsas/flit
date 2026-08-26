# Plan técnico — Ajustes de validación de identidad

> **Versión:** v2 — reescrita tras crítica adversarial (Fase 2 de `/refine-requirement`)
> **Fecha:** 2026-08-05 · rama `develop` @ `4f5b2eef`
> **Estado:** Propuesta. **Sin Feature ni HUs en ADO.** Bloqueada por D14 y por la conciliación del Feature #11004.
> **Origen:** `context/ajuste-validacion-identidad.txt`.
> **Antecedente:** [`criterios-mejoras-prevalidacion-validacion-identidad.md`](criterios-mejoras-prevalidacion-validacion-identidad.md) (2026-07-28) → **Feature #11004** en ADO.
> **Mapa del módulo:** `context/modulos/09-identidad-y-prevalidaciones.md`.

---

## 0. Qué cambió de v1 a v2 — léelo antes que nada

La v1 se apoyaba en tres premisas. **Dos eran falsas** y se verificaron leyendo el código:

| Premisa v1 | Realidad verificada | Efecto |
|---|---|---|
| "Hay dos botones que un humano puede pulsar" | Hay **al menos cinco disparadores** del correo, y **dos son automáticos y sin UI**: el wizard al guardar un actor (`TramiteWizard.tsx:915-921`) y el backend para persona jurídica sin RL utilizable (`ActorsCommand.cs:369`) | CF-04 era inviable tal como estaba escrito |
| "El SuperAdmin ve todos los tenants, agrupar por documento fusionaría empresas" | **Falso.** Los 18 endpoints exigen `X-Tenant-Id` y responden **400** sin él, también al SuperAdmin (`BiometricaEndpoints.cs`, 15 ocurrencias). Toda petición es mono-tenant | El riesgo "Alto" de v1 era un **fantasma**; y la agrupación por empresa de D1 **no se puede construir** |
| "Cambiar el 409 por un 200 informativo" | El 409 es **carga estructural**: `ActorsCommand.cs:367-369` dispara el envío automático y **ignora el error a propósito** | El 200 rompía un flujo automático además del contrato publicado |

La tercera premisa —que los guards actuales no cubren a una persona ya aprobada y vigente— **sí se sostiene**, y sigue siendo el núcleo del pedido A.

**Consecuencia:** el bloque A no es "unificar tres guards". Es **decidir en un solo lugar si corresponde enviar el correo**, con una precedencia explícita, aplicada también a los caminos automáticos —donde el efecto no es "confirmar" sino simplemente "no enviar".

---

## 1. Qué se pide

| # | Pedido literal | Interpretación v2 |
|---|---|---|
| **A** | Si ya tiene una validación activa, informar y **no enviar** correo; si no, informar y enviar | Precedencia única de decisión de envío, aplicada a **los cinco disparadores** |
| **B1** | Agrupar atascadas en acordeón **por empresa o por persona** | **Solo por persona.** Por empresa es imposible hoy (§2.4) |
| **B2** | Agrupar el log por número de documento, un solo registro | Vista agrupada **por persona**, en una superficie que no rompa a los otros dos consumidores del endpoint |
| **B3** | Ver todas las validaciones al entrar al proceso, cada una segmentada | Detalle multi-validación por persona |

---

## 2. Verificación contra el código

### 2.1 Los cinco disparadores del correo

| # | Disparador | Guard actual | ¿Tiene UI? |
|---|---|---|---|
| 1 | `POST /biometric-validations` (prevalidación) | `FindActiveStandaloneValidationAsync` — **solo en vuelo, solo standalone**, y **no lee `ExpiresAt`** | sí |
| 2 | `POST /instances/{id}/biometric` — mock | guard por parte dentro de la instancia | sí |
| 3 | `POST /instances/{id}/biometric` — Kyverum | guard por parte **distinto del mock** (terminaliza el enlace vencido para permitir reenvío) | sí |
| 4 | Wizard al guardar actor → `ensureIdentity` = `requiere_validacion` → `iniciarBiometric` | hereda el de (2)/(3) | **no** |
| 5 | Backend, PJ sin RL utilizable y sin cobertura de baúl (`ActorsCommand.cs:369`) | hereda el de (2)/(3), **y descarta el error a propósito** | **no** |

Fuera de alcance, por decisión: la **ruta admin** (RL y mandatarios) va deliberadamente **sin** guard —`ADR-0034-validacion-identidad-admin-desacoplada.md` (Aceptado)— porque ahí una validación en curso **sí** se puede reenviar.

**El hueco real:** ninguno de los cinco pregunta *"¿esta persona ya tiene identidad aprobada y vigente?"*. Solo lo hace `ensureIdentity`, que decide **reutilizar** pero no impide que el disparador siguiente mande el correo igual.

### 2.2 Ya existen cuatro normalizaciones distintas del documento

| Dónde | Regla | De quién depende |
|---|---|---|
| `ProcedureInstanceRepository:343` | igualdad **exacta** en SQL | gate por instancia, `EnsureIdentity`, `FurCommand` |
| `BiometricRules.IdentidadKey:308` | `Trim` + `ToUpperInvariant` | gate en **lote**, listado, trámites vinculados |
| `EnsureIdentityCommand.DocCoincide:159` | `Trim` + `OrdinalIgnoreCase` | fila propia del trámite |
| `ListLinkedProceduresByIdentityDocumentsAsync:409` | `ToUpper` **solo del número** | trámites asociados |

> **Este es el riesgo mayor del plan.** Hoy, para un documento que difiera en mayúsculas o espacios, la ruta por instancia dice `requiere_validacion` mientras el chip del listado dice aprobado. Unificar en `Trim+Upper` **abre el gate**: esos trámites pasarían a radicables sin biométrica nueva. **Es un cambio de veredicto de radicación, no un refactor.**

### 2.3 El 409 no se puede cambiar por un 200

Los consumidores tratan todo 2xx como éxito con payload completo (`BiometricStep.tsx:826`, `TramiteWizard.tsx:918`); el contrato publicado declara `201` (`contracts/openapi/core-api.v1.yaml`); hay 4 tests clavados a `biometria_activa`/`prevalidacion_activa`; y el disparador (5) **depende de que el error sea ignorable**.
**Solución: `409` con cuerpo informativo.** Cumple el pedido A igual y no rompe a nadie.

### 2.4 La agrupación por empresa es imposible hoy

`ListStuckAsync` filtra por un único `tenantId` y el endpoint responde **400 sin `X-Tenant-Id`**. En el resultado **nunca** hay más de un tenant. Habilitarlo exigiría un endpoint multi-tenant nuevo, autorización del requeue cross-tenant y tocar el middleware — no es "un campo más".

### 2.5 El endpoint de listado lo comparten tres superficies

`GET /biometric-validations` lo consumen `Validaciones.tsx`, `PrevalidacionesModule.tsx` y **`Dashboard.tsx` (dos llamadas para tarjetas)**. Agrupar en ese endpoint cambia las tres. Además `Total` **es** `stats.Total` (un solo campo para dos semánticas), y la rama del filtro `motivoRechazo` **pagina en memoria sobre un cap de 2000 filas**: agrupada, devolvería conteos falsos.

### 2.6 Solapamiento con el Feature #11004 (verificado en ADO)

| Id | Título | Estado | SP |
|---|---|---|---|
| **#11004** | [Trámites] Mejoras prevalidación y tracking de validación de identidad | **New** | — |
| #11005 | [BACKEND] Contrato de listado, rechazo de jurídica y detalle/auditoría por validationId | **Active** | 5 |
| #11006 | [FRONTEND] Prevalidación — alta solo natural y listado standalone | Resolved | 3 |
| #11007 | [FRONTEND] Panel de tracking compartido en Validaciones y Prevalidaciones | **Active** | 5 |
| #11008 | [FRONTEND] Detalle drawer con poll y tracking embebido | **Active** | 5 |
| #11009 | [FRONTEND] Trámite — historial completo de validaciones por parte | **Active** | 3 |

**Ninguna de las cuatro `Active` tiene commits propios** (`git log --grep` = 0), pero el código que describen **sí existe**, entregado bajo commits de la #11006 (PR #198) — el guard `prevalidacion_solo_natural` incluso cita "Feature #11004" en su comentario. Es el patrón **A15** de `context/04-estado-ado.md`: HUs entregadas que siguen `Active`.

**Consecuencia:** CF-06 de este plan se superpone con #11008/#11009. No se pueden crear HUs nuevas al lado sin resolver esto primero (**D14**).

---

## 3. Criterios funcionales v2

### CF-01 · Precedencia única de decisión de envío

**Enunciado:** existe un único componente que responde *"¿corresponde enviar correo de validación a esta persona?"*, con esta precedencia, y lo consultan los cinco disparadores:

1. **Cobertura por baúl** (`FirmaBaulCobertura.Aplica` + firma activa y vigente) → no enviar. *Precedencia D8 de `ADR-0025-baul-firmas-custodia-y-consumo.md` (Aceptado).*
2. **Identidad aprobada y vigente** por documento en el tenant → no enviar, reutilizar.
3. **Validación en vuelo con enlace no vencido** → no enviar.
4. **Validación en vuelo con enlace vencido** → **no crear fila nueva: encauzar al reenvío** (que tiene tope de 3 y cooldown de 5 min). *Cierra el bypass que la v1 abría.*
5. En cualquier otro caso → enviar.

**No sustituye** la idempotencia por parte dentro de la instancia: es una capa **previa**. La paridad que se exige es sobre **la decisión de envío**, no sobre todos los guards.

- **Medible:** para un mismo documento, los cinco disparadores toman la misma decisión de envío. Test de paridad por disparador.

### CF-02 · No se envía correo cuando la precedencia dice que no

**Excepción explícita:** el reenvío por **cambio de correo** (`ActorsCommand.ResendIdentityOnEmailChangeAsync`) **conserva** su comportamiento actual —expira la previa y crea una nueva—; si no, corregir un correo mal escrito se volvería un no-op silencioso.

- **Medible:** `IKyverumVerifyClient.StartVerificationAsync` no se invoca en los casos 1–4; sí se invoca tras un cambio de correo.

### CF-03 · El bloqueo informa, sin romper el contrato

**Enunciado:** los disparadores con UI responden **`409` con cuerpo**: `motivo`, `status`, `validatedAt`, `validUntil`, `validationId`, `origen` (trámite / standalone / baúl). El código de estado **no cambia**.

- **Medible:** los 4 tests existentes siguen verdes; la UI pinta "Ya validada · vigente hasta el 04/09/2026" sin una segunda llamada.

### CF-04 · Confirmación donde hay UI; silencio donde no la hay

**Enunciado:** en los disparadores 1–3 la UI muestra el destinatario y confirma antes de enviar. En los automáticos (4 y 5) **no hay confirmación** —no hay diálogo posible— pero tampoco se envía si la precedencia lo impide, y el estado resultante queda visible en la tarjeta de la parte.

- **Medible:** RTL sobre el formulario y sobre el paso del wizard; para 4 y 5, test de que no se llama al proveedor.

### CF-05 · Vista agrupada por persona, sin romper a los otros consumidores

**Enunciado:** la vista agrupada vive en un **modo o endpoint propio**, de forma que `Dashboard` y `Prevalidaciones` no cambian. Cada fila es una persona `(tenant, documento normalizado)` con: estado de su validación **más reciente** (D5), contador, y **peor alerta** según este orden de severidad —**`atascada` > `rechazada` > `expirada` > `por_vencer`**— restringida a validaciones **no terminales o de los últimos 30 días**.

Filtros en modo agrupado: solo los que tienen semántica de persona (`documento`, `estado`, `vigencia`, `fechas`). Los de validación (`referenceNumber`, `modalidad`, `partyRole`, `provider`, `score`, `motivoRechazo`) **se deshabilitan** en modo agrupado y se indica por qué.

- **Medible:** 7 validaciones del mismo documento → 1 fila con `count=7`; el Dashboard devuelve exactamente lo mismo que antes.

### CF-06 · Detalle multi-validación

**Enunciado:** una sola petición devuelve las N validaciones de la persona con sus trámites asociados y su tracking, ordenadas de más reciente a más antigua, **con N acotado (tope 50 + paginación)** y el polling detenido cuando **todas** son terminales.

- **Requiere ADR** que supersede la decisión 2 de `ADR-0036-prevalidacion-natural-tracking-desacoplado-instancia.md` (tracking por `validationId`).

### CF-07 · Atascadas agrupadas por persona

**Enunciado:** acordeón por persona, con contador por grupo, conservando *Reintentar* por fila y *Reintentar todos*. **Sin nivel de empresa** (§2.4).

---

## 4. Decisiones

### Cerradas

| # | Decisión | Cerrada como |
|---|---|---|
| **D2** | Qué cuenta como "activa" | Aprobada vigente **+** en vuelo — ampliado por CF-01 con baúl y con el caso del enlace vencido |
| **D3** | ¿Forzar una nueva teniendo vigente? | **No.** Se ofrece *Ver la existente* |
| **D4** | ¿Cruza tenants? | **No.** Alcance tenant. La ruta admin queda fuera por `ADR-0034` |
| **D5** | Estado de la fila agrupada | El de la **más reciente**, más contador y chip |
| **D6** | Filtros en fila agrupada | Aplican a la más reciente; los de validación se deshabilitan (CF-05) |
| **D7** | KPI | Siguen contando **validaciones**, etiquetado |
| **D1** | Agrupación de atascadas | **Solo por persona.** El nivel de empresa se descarta: cada petición es mono-tenant, así que el usuario vería siempre un único grupo (§2.4) |
| **D14** | Feature #11004 (4 HUs `Active` con código aparentemente entregado) | **Se ignora y se crean los 2 Features nuevos** — decisión del usuario, 2026-08-05. ⚠ **Riesgo aceptado:** las HUs 2.5 y 2.6 muy probablemente reimplementan lo ya entregado bajo #11008/#11009, y el board queda con dos Features solapados |

### Nuevas — abiertas, bloquean el diseño

| # | Decisión | Por qué importa | Recomendación |
|---|---|---|---|
| **D8** | Función canónica de normalización | `Trim+Upper` ya cambia veredictos del gate; algo más agresivo (quitar puntos, guiones, ceros a la izquierda) **fusiona documentos distintos** y el error siempre cae **a favor de radicar** | `Trim+Upper` y nada más |
| **D9** | Medición previa obligatoria | Consulta de solo lectura que cuente cuántos pares (actor, validación) empatan con la regla nueva y no con la actual = trámites que cambian de veredicto | **Bloqueante:** sin ese número, la HU de normalización no se activa |
| **D10** | ¿Normalizar en lectura o al escribir? | En lectura → índices por expresión. Al escribir → migración sobre **cinco** tablas | En lectura, con índice por expresión |
| **D11** | Alias `NIT`/`N`, `CC`/`C` | No es normalizar: es una tabla de equivalencias que cambia la clave de identidad y toca `EsActorJuridico` y `FirmaBaulCobertura` | **Fuera de alcance.** ADR propio |
| **D12** | Cómo cerrar la carrera de dos POST simultáneos | Un `if` en el handler no la cierra; hacen falta un **índice único parcial** sobre `(tenant, doc_norm)` filtrado por estados en vuelo — y eso **sí es schema** | Incluir el índice único parcial |
| **D13** | Estrategia de consulta agrupada | `GROUP BY` + `COUNT` = dos pasadas sobre la tabla más escrita. `DISTINCT ON` da "la más reciente" en una, pero **EF Core no lo expresa** → SQL crudo en el repositorio | `DISTINCT ON` con SQL crudo, como ya hace analítica |
| **D8–D13** | Ver arriba | — | **Adoptadas las recomendaciones** el 2026-08-05. D9 deja de ser decisión y pasa a ser **tarea bloqueante** dentro de la HU 1.1 |

---

## 5. Descomposición v2 — dos Features

La v1 (7 HUs / 27 SP — la suma estaba mal, decía 26) subestimaba HU1 y HU3. Corregidas, cruza el techo de 8 HUs, así que se parte.

**Creados en ADO el 2026-08-05** — todos en `New`, Sprint 3 (el activo es el 2), tag `DOR`, `AssignedTo` Juan Felipe Montoya Garcia. **Sin activar.**

### Feature [#11260](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/11260) — Decisión única de envío de validación de identidad

| ID | HU | Tipo | SP |
|---|---|---|---:|
| **#11262** | Normalización canónica del documento + **medición previa** (D9) | BACKEND | 3 |
| **#11263** | Componente de precedencia de envío (baúl → vigente → en vuelo → vencido→reenvío) | BACKEND | 5 |
| **#11264** | Aplicarlo a prevalidación + `409` con cuerpo | BACKEND | 3 |
| **#11265** | Aplicarlo a los disparadores de trámite (2, 3, 4, 5) con testeo del gate | BACKEND | 5 |
| **#11266** | Índice único parcial anti-carrera (D12) | BACKEND | 2 |
| **#11267** | Confirmación e información en las dos superficies con UI | FRONTEND | 3 |

**6 HUs · 21 SP.**

### Feature [#11261](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/11261) — Vista de identidad por persona

| ID | HU | Tipo | SP |
|---|---|---|---:|
| **#11268** | Acordeón por persona en atascadas | FRONTEND | 2 |
| **#11269** | Índice por expresión para la vista agrupada | BACKEND | 2 |
| **#11270** | Consulta agrupada `DISTINCT ON` en modo/endpoint propio | BACKEND | 5 |
| **#11271** | Grilla por persona con contador y expansión | FRONTEND | 3 |
| **#11272** | Endpoint de validaciones por persona (+ ADR que supersede) | BACKEND | 3 |
| **#11273** | Detalle multi-validación segmentado | FRONTEND | 5 |

**6 HUs · 20 SP.** ⚠ Por D14, **#11272 y #11273 se crearon nuevas** aun sabiendo que probablemente reimplementan lo entregado bajo #11008/#11009. Riesgo aceptado explícitamente y anotado en la Discussion de ambas.

**Orden sugerido:** #11268 (quick win, sin dependencias) → #11262 + D9 → #11263 → #11264 → #11267 → #11265 → #11266 → #11269 → #11270 → #11271 → #11272 → #11273. Deja para el final lo de mayor blast radius.

---

## 6. Riesgos

| Riesgo | Impacto | Mitigación |
|---|---|---|
| **La normalización cambia veredictos del gate de radicación** | **Crítico** | D9 bloqueante: medir antes de activar. HU 1.1 aislada y mergeable sola |
| La HU 1.4 toca el endpoint que alimenta `borrador → preparado` | Alto | Conservar la idempotencia por parte; suites `wizard-avance-bloqueado` y `biometric-step` corridas **localmente** (el CI de PR no corre `vitest`) y pegadas como evidencia |
| Regresión del baúl (Bug #11141 ya costó una tercera copia del predicado) | Alto | CF-01 pone el baúl **primero** en la precedencia; test explícito del escenario PJ + baúl vigente + mecanismo "sello de identidad" |
| Duplicar trabajo del Feature #11004 | Alto | D14 antes de crear nada |
| `DISTINCT ON` obliga a SQL crudo en el repositorio | Medio | Decidido en D13, no durante la implementación; pasa por `db-schema-validator` |
| Dashboard y Prevalidaciones comparten el endpoint | Medio | CF-05 exige modo/endpoint propio; test de no-regresión del Dashboard |

---

## 7. Lo que este plan NO arregla

- **Bug #11141 vivo en `IdentityApprovalResolver`** (copia local del predicado del baúl). La HU 1.2 toca ese archivo: decidir si se arregla ahí o se radica aparte.
- **Sin política de retención de la PII biométrica.**
- **Los endpoints de identidad no tienen RBAC** (sí autenticación y aislamiento por tenant).
- **El Feature #11004 con 4 HUs `Active`** — higiene de ADO, gate del PO humano.

---

## 8. Gates

1. ~~**D14** — conciliar el Feature #11004.~~ **Decidido:** se ignora y se crean los 2 Features nuevos (§4).
2. ~~**D8–D13** — cerrar con el usuario.~~ **Adoptadas las recomendaciones.** D9 sigue siendo tarea bloqueante dentro de la HU 1.1.
3. ~~Crear los 2 Features + 12 HUs en ADO.~~ **Hecho el 2026-08-05:** #11260 y #11261 con sus 12 hijas (#11262–#11273), `New` · Sprint 3 · `DOR` · asignadas a un humano, vinculadas al padre y con trazabilidad en Discussion.
4. **Pendiente:** ADR en `Propuesto` para la precedencia de envío (CF-01) y para el tracking por persona (CF-06, supersede `ADR-0036-prevalidacion-natural-tracking-desacoplado-instancia.md`).
5. Activar HU por HU con confirmación explícita. **#11263 no se activa** hasta que la medición de #11262 esté publicada en su Discussion.
6. Merge solo con work item registrado, reviewer humano real y confirmación explícita.

---

*v1 elaborada por el hilo orquestador; v2 reescrita tras crítica adversarial de `architecture-agent` y `tech-lead-agent` (Fase 2 de `/refine-requirement`). Las tres premisas falsas de la v1 se verificaron una a una contra el código antes de reescribir.*
