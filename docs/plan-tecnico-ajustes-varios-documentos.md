# Plan técnico — Ajustes varios (documentos, mandatario y aprobación)

**Fuente:** `ajustes-varios-documentos.txt`
**Base de revisión:** `develop` @ `bf4950f9` (revisado sin checkout, vía `git show`/árbol de trabajo)
**Fecha:** 2026-07-28

---

## Estado de ejecución (cierre)

Todo el plan quedó integrado en `feature/AB-11004-ajustes-varios-documentos`, sacada de `develop`
@ `0e705d4c`:

| Ajuste | Commits | Origen |
|---|---|---|
| 7 — error 500 al aprobar | `53d94b3e` | rama `AB-11000` |
| 1 — identidad del mandatario | `b126f2ea`, `e4e0d16b` + **`HU11000`** | ramas `AB-11000` + residuos |
| 2 — regenerar al asignar placa | `960435ec` | rama `AB-11001` |
| 3 — regenerar al aprobar | `494df58c` | rama `AB-11001` |
| 4 — firma real | `46c7c6fc` | rama `AB-11002` |
| 5 — negritas | `8d764090` | rama `AB-11002` |
| 6 — remolques | `1ad29bf6` | rama `AB-11003` |
| — | **`HU11001`** | hallazgo de la auditoría del ajuste 7 (ver abajo) |

**Hallazgo nuevo (`HU11001`):** los ajustes 2 y 3 quedaban anulados en la práctica. La regeneración
corre dentro del scope de tenant del cliente (transacción abierta) y `DbSignatureVaultReader`
—alcanzado por toda regeneración con actor persona jurídica— abría otra transacción sin la guarda de
HU10992. La excepción la absorbía el `try/catch` best-effort de ambos endpoints: los documentos no se
regeneraban y el síntoma reportado seguía vivo. Corregido, junto con `DbMandateSignerReader`.

Verificación: backend `2.966` tests verdes (0 fallos); frontend `1.042` verdes con `7` fallos
**preexistentes en `develop`** en componentes que esta rama no toca (representantes legales, baúl de
firmas, avalúos, usuarios). Pendiente: PR a `develop`.

---

## 0. Estado de partida (importante)

Al revisar el repo aparecieron **dos trabajos ya en curso** que cubren parte de la lista:

| Evidencia | Qué cubre |
|---|---|
| Commit `53d94b3e` (en la rama actual, **no** en develop) — `HU10992: reutilizar la transacción activa al leer identidades vigentes del mandatario` | **Ajuste 7** (error 500 al aprobar) |
| Cambios **sin commitear** en el árbol de trabajo (`DbMandateSignerReader`, `MandateSignerReadModels`, `MandateSignerResponse`, `ListMandateSignersHandler`, `UpdateMandateSignerHandler`, `MandatarioFormPanel.tsx`, `admin-mandate-signers.ts`) marcados HU #10993 / #10994 | **Ajuste 1**, parcialmente (estado `valid/expired/pending/none` + botón "Renovar validación" + envío al **editar** cuando se agrega correo) |

El plan de abajo asume ese punto de partida y solo lista **lo que falta**. Los ajustes 2–6 están **intactos** en develop.

---

## Ajuste 1 — Validación de identidad del mandatario en Admin OT

### Síntoma
- No se apalanca la validación de identidad al crear el mandatario.
- No se envía el correo al crear si no hay validación activa.
- No hay opción de renovar cuando está vencida.

### Diagnóstico
La infraestructura **existe completa** (ADR-0034/0036): `AdminIdentityValidationService` (`Send`/`Resend`/`Approve`), endpoints `POST .../mandate-signers/{id}/identity/send|resend` (`AdminMandateSignerIdentityEndpoints.cs`), cliente tipado en `frontend/lib/api/admin-mandate-signers.ts`. Los huecos reales:

1. **`CreateMandateSignerHandler.cs:101` usa `SendAsync`**, que **siempre arranca una validación nueva** (`StartNewAsync`). La semántica pedida ("enviar solo si no tiene una validación activa") es la de `ResendAsync` (`AdminIdentityValidationService.cs:26-46`): reutiliza si hay aprobada y vigente, y en cualquier otro caso (nunca, en curso, rechazada, expirada) envía. El `UpdateMandateSignerHandler` **ya migró a `ResendAsync`** en el WIP local; el de alta no.
2. **`AdminIdentitySubjectLinker.cs:34-39` no tiene rama para `mandate_signer`**: cae al `_ => Task.FromResult(false)`. Al aprobar la biometría, `admin.mandate_signers.identity_validation_ref` **nunca se setea**. No rompe el gate de aprobación (`MandateSignerDirectory` consulta `admin_identity_validations` por `subject_type` + `subject_ref`, no por el `ref`), pero deja el dato desincronizado y era la razón del badge "sin validar" permanente antes del WIP de #10994.
3. **`MandatariosSection.tsx` (la tabla) no muestra identidad**: el chip y la acción solo existen dentro del panel de edición (`MandatarioFormPanel.tsx:272+`). El OT no ve de un vistazo quién está vencido.
4. El alta traga el fallo del proveedor en silencio (`CreateMandateSignerHandler.cs:103-106`): el usuario no recibe señal de "no se pudo enviar".

### Cambios
| # | Archivo | Cambio |
|---|---|---|
| 1.1 | `Flit.Admin.Application/.../CreateMandateSigner/CreateMandateSignerHandler.cs` | `SendAsync` → `ResendAsync` (reusa vigente, envía si no hay activa). Mantener best-effort. |
| 1.2 | `Flit.Infrastructure/Persistence/Repositories/AdminIdentitySubjectLinker.cs` | Añadir rama `AdminIdentitySubjectTypes.MandateSigner` → set idempotente de `identity_validation_ref` sobre `admin.mandate_signers` (esta tabla **no** tiene RLS: no hace falta `TenantRlsScope`, pero sí filtrar por `ot_tenant_id` para no cruzar OTs). |
| 1.3 | `frontend/components/admin/transit-offices/MandatariosSection.tsx` | Columna "Identidad" con el chip de `identityStatus` (reusando el mapa de `MandatarioFormPanel`) + acción rápida "Enviar/Reenviar/Renovar" en la fila. Extraer el mapa `identityUi` a un módulo compartido para no duplicarlo. |
| 1.4 | `CreateMandateSignerHandler` + `AdminMandateSignersEndpoints` + panel | Devolver en el 201 una señal `identitySent: true/false` y que el toast del front diga "Mandatario registrado. Validación de identidad enviada" o "…, pero no se pudo enviar la validación (reenvíala desde Editar)". |

### Pruebas
- Unit: alta con identidad **vigente** ⇒ `ResendAsync` reusa y **no** llama al proveedor; alta sin identidad ⇒ envía; alta con `AdminIdentityProviderException` ⇒ el mandatario queda creado y `identitySent=false`.
- Unit linker: `LinkAsync("mandate_signer", …)` setea el ref, es idempotente y no toca otro OT.
- RTL: la tabla pinta los 4 estados y la acción cambia de rótulo con `expired`.

### Riesgo
Bajo. `ResendAsync` ya es la semántica usada en edición; el linker es aditivo.

---

## Ajuste 2 — Regenerar FUR y consolidado al asignar placa (preasignación)

### Síntoma
La placa asignada por el OT no se refleja en los formularios.

### Diagnóstico
`OtClientProcedureRepository.AssignPlateAsync` (líneas 467-565) reserva la placa, **escribe `field_values['plate']`** y mueve `plate_flow_status` a `asignado`… y ahí termina. **No regenera nada**: el FUR/compraventa/mandato/solicitud virtual persistidos siguen con la placa vieja (o vacía) y el consolidado sigue vigente en caché.

La pieza que hace falta ya existe: `GenerarFurHandler` implementa `IExpedienteHotDocumentsRegenerator` y al final invalida ambos consolidados (`FurCommand.cs:350` `ConsolidadoMaestroVigente = false` y `:411` `InvalidarConsolidados()`).

### Cambio
En `AdminPlateRangesEndpoints.AssignPlateToProcedureAsync` (`:68-99`), tras un `AssignPlateAsync` exitoso, repetir **el mismo patrón que la aprobación** (`AdminOtEndpoints.cs:931-946`):

```
otRepo.ExecuteInClientTenantScopeAsync(clientTenantId,
    () => furHandler.HandleAsync(instanceId, clientTenantId, ct), ct)
```

envuelto en `try/catch` con log (best-effort: un fallo de regeneración **no** debe deshacer la asignación de placa, que ya consumió el rango). El `clientTenantId` se obtiene del `OtClientProcedure` devuelto por `AssignPlateAsync` (ya viene mapeado) o con un `GetByIdAsync` previo, como hace la aprobación.

> **Decisión de arquitectura:** la orquestación va en el endpoint (capa API), no en el repositorio Admin — el módulo Admin no puede referenciar Trámites. Es el mismo precedente de ADR-0036 §D9 para la aprobación.

### Pruebas
- Integración: asignar placa ⇒ el adjunto `fur` cambia de hash y el `plate` sale en el PDF; `consolidado_maestro_vigente = false`.
- Que un fallo del generador devuelva 200 con la placa asignada (y quede el log).

### Riesgo
Medio-bajo: alarga la latencia del endpoint de asignación (genera N PDFs). Si molesta, medir y considerar hacerlo asíncrono en una HU aparte.

---

## Ajuste 3 — Regenerar todos los documentos al aprobar

### Síntoma
Al aprobar, FUR/consolidado/trámite virtual/mandato no reflejan firmas ni documentación actualizada.

### Diagnóstico
En `AdminOtEndpoints.cs:931-933` la regeneración está condicionada a **dos** cosas:

```csharp
if (result.Status == ApproveOtClientProcedureStatus.Approved
    && decision.Outcome == MandatoApprovalOutcome.Resolved)
```

Es decir: **solo se regenera cuando el trámite exige mandato-persona y se resolvió firmante**. Todo trámite sin mandato (persona natural sin exigencia del OT, OT institucional tipo Sabaneta, trámite sin adjunto `mandato`) se aprueba **sin regenerar nada** ⇒ los documentos quedan como en `preparado`.

`GenerarFurHandler.HandleAsync` sí regenera el paquete completo (FUR, compraventa, `tramite_virtual`, `mandato`, certificados de identidad, RUES, RNMC, escrituras) e invalida los consolidados, así que el arreglo es de **condición**, no de alcance.

### Cambio
1. Quitar `&& decision.Outcome == Resolved`: regenerar **siempre que la aprobación fue exitosa**.
2. Mantener el `try/catch` best-effort y el log `AdminOtMandatoLog`, renombrando el mensaje (ya no es "regeneración del mandato" sino "regeneración del expediente").
3. Verificar que `FirmasVisibles` sea `true` en aprobado — lo es: `FurCommand.cs:500` lo calcula como `status != borrador`.

### Pruebas
- Integración: aprobar un trámite **sin** mandato ⇒ FUR/`tramite_virtual` regenerados con firmas y consolidado invalidado.
- Aprobar con mandato ⇒ sin regresión respecto de hoy.

### Riesgo
Bajo. Amplía un camino ya probado. Ojo con la latencia del `approve` (misma consideración que el ajuste 2).

---

## Ajuste 4 — Firma real en mandato y trámite virtual (baúl o validación de identidad)

### Síntoma
La firma no aparece en el mandato ni en la solicitud de trámite virtual.

### Diagnóstico
Ninguno de los dos generadores pinta firma; solo dibujan una **línea en blanco** y los datos de identificación:
- `SolicitudVirtualPdfGenerator.cs:72-79` — bloque `FirmaBlock` (solo texto).
- `MandatoPdfGenerator.cs:221-256` — `RenderFirmas` con `"____________"` para MANDANTE y MANDATARIO.

Mientras tanto el FUR **sí** resuelve la firma por tipo (HU #10488/#10645) y ese dato **ya viaja** en el mismo `FurDocumentData` que reciben ambos generadores:
- `FurDocumentData.FirmaImagenes` (`IFurDocumentGenerator.cs:83`) — imagen real del **baúl de firmas**, resuelta en `ResolveVaultSignaturesAsync` (`FurCommand.cs:512+`) **solo para actores jurídicos (NIT)**.
- `FurDocumentData.SellosIdentidad` (`:91`) — sello textual de la **validación biométrica** (documento, uuid, serie del certificado, fechas), resuelto en `FurCommand.cs:125-135`.

Es decir: la precedencia pedida ("según el tipo: validación de identidad o baúl de firmas") ya está calculada; los dos PDFs simplemente la ignoran. Falta además la firma del **MANDATARIO**: `MandatarioFirmante` (`IFurDocumentGenerator.cs:150`) solo lleva `Nombre` y `Documento`.

### Cambios
| # | Archivo | Cambio |
|---|---|---|
| 4.1 | `Flit.Tramites.Application/Documents/IFurDocumentGenerator.cs` | Ampliar `MandatarioFirmante` con `byte[]? FirmaImagen` y `string? SelloIdentidad` (ambos opcionales ⇒ sin ruptura de compilación en tests). |
| 4.2 | `Flit.Tramites.Application/.../FurCommand.cs` (`TryGenerateMandatoAsync`) | Al armar `MandatarioFirmante` desde `IMandateSignerDirectory`, resolver su firma con la misma precedencia: `signature_vault_id` (imagen del baúl) → si no, sello de su `admin_identity_validations` aprobada y vigente. Requiere exponer `SignatureVaultId`/`CertificateHash` en `MandateSignerCandidate` (hoy solo trae `IdentityVigente`). |
| 4.3 | `SolicitudVirtualPdfGenerator.cs` | Antes del bloque de identificación, pintar `FirmaImagenes["comprador"]` si existe (`.Image(bytes)` de QuestPDF, alto fijo ~40 pt), y si no, `SellosIdentidad["comprador"]` como sello de texto. Sin ninguno ⇒ la línea en blanco actual. |
| 4.4 | `MandatoPdfGenerator.cs` (`RenderFirmas`) | Igual para el **MANDANTE** (radicador) y, en las variantes Genérica/Bello, para el **MANDATARIO** con lo de 4.1/4.2. En la variante Sabaneta el mandatario es institucional ⇒ solo firma el mandante (comportamiento actual). |
| 4.5 | Ambos | Un único helper compartido (p. ej. `DocumentSignatureRenderer`) para no duplicar la precedencia imagen→sello→línea. |

### Pruebas
- Unit por generador: con imagen de baúl, con sello de identidad, con ninguno (los tres caminos) → aserción sobre el PDF (bytes no vacíos + smoke de render, como en el resto de generadores QuestPDF).
- Integración: traspaso PJ con baúl ⇒ mandato y trámite virtual llevan la imagen; PN validada ⇒ llevan el sello.

### Riesgo
Medio: toca contratos compartidos (`MandatarioFirmante`, `MandateSignerCandidate`). Mitigación: parámetros opcionales con default.

---

## Ajuste 5 — Negrita en palabras clave del mandato y el trámite virtual

### Síntoma
No hay negrita en las palabras clave.

### Diagnóstico
Ambos PDFs escriben párrafos como **texto plano en un solo span**: `SolicitudVirtualPdfGenerator.cs:56` (`col.Item().Text(Parrafo1(...))`) y `MandatoPdfGenerator.cs:54` (`BuildParrafos` devuelve `List<string>`). Solo están en negrita el título y el bloque de firma.

### Cambio
Migrar ambos a **rich text de QuestPDF** (`.Text(t => { t.Span("…"); t.Span("…").Bold(); })`) en vez de `string`:
- Cambiar `BuildParrafos` para devolver una secuencia de *segmentos* (`record Segmento(string Texto, bool Negrita)`) en lugar de `string`.
- Poner en negrita: nombres de MANDANTE/MANDATARIO y empresa, tipo y número de documento, NIT, **placa**, nombre del trámite (TRASPASO DE PROPIEDAD / MATRÍCULA INICIAL), organismo de tránsito, y los rótulos de cláusula (`PRIMERA: OBJETO DEL MANDATO`, `SEGUNDA: …`) que hoy van dentro del texto corrido.
- El texto legal (Resoluciones) se conserva **literal**, solo cambia el formato.

> Pendiente de confirmar con el PO la lista exacta de palabras a resaltar. Propuesta arriba = paridad con las plantillas legacy `virtual-process/*.hbs` (no están en este repo; si se consiguen, calibrar contra ellas).

### Pruebas
Unit de los constructores de segmentos (puros): las claves esperadas salen marcadas `Negrita = true` y la concatenación de segmentos reproduce el texto legal exacto (garantiza que el formateo no alteró el contenido jurídico).

### Riesgo
Bajo, pero **toca texto legal** ⇒ el test de "concatenación == texto original" es obligatorio.

---

## Ajuste 6 — Remolques y semirremolques no consultan en el paso 1

### Síntoma
No consulta; pide aplicar la misma lógica de maquinaria (tipo de documento) en el paso 1.

### Diagnóstico
En develop **ya existe** la mitad del arreglo (commit `4f496266`, HU #10478): `PLATE_PATTERN` acepta `R12345`/`S12345` y maquinaria `AA123456`, y `TramiteWizard.tsx:1360-1374` revela el selector de tipo de documento del propietario cuando la consulta devuelve "vehículo no encontrado". Dos costuras explican que aun así falle con remolques:

1. **La revelación solo dispara con `key === 'vehiculo' && status === 'fail'`.** Kyverum RUNT solo emite ese check cuando la excepción es `IsNotFound` (`KyverumRuntVehicleConsultationProvider.cs:52-54`); **cualquier otra respuesta del proveedor** (no-200, payload no mapeable, timeout) cae en `ProviderUnavailable()` (`:67-72`), que emite `key = "provider"`, `status = "error"` ⇒ bloqueo duro, **sin** revelar el selector. Las placas fuera del RUNT automotor (remolques/semirremolques) muy probablemente están en este segundo camino.
2. **La revelación es reactiva**: obliga a fallar una consulta antes de poder corregir el tipo de documento. Para maquinaria se toleró; para remolques es el mismo círculo si el fallo no es `fail`.

### Cambios
| # | Archivo | Cambio |
|---|---|---|
| 6.1 | `frontend/components/operacion/TramiteWizard.tsx` | Ampliar el disparador de `ownerDocTypeSuggested`: revelar también con `key === 'provider' && status === 'error'` en una consulta **por placa**. |
| 6.2 | `frontend/components/operacion/TramiteWizard.tsx` + `frontend/lib/validation/fieldRules.ts` | Mostrar el selector **proactivamente** (sin esperar el fallo) cuando la placa digitada no tenga forma de automotor: `^[RS][0-9]{4,6}$` (remolque/semirremolque) o `^[A-Z]{2}[0-9]{6}$` (maquinaria). Helper puro exportado (`isNonAutomotorPlate`) para testearlo. |
| 6.3 | `fieldRules.ts` | Relajar la alternativa de remolque a `[RS][0-9]{4,6}` (hoy exige exactamente 5 dígitos) — el RUNT es el validador definitivo, el patrón es anti-tipeo. |
| 6.4 | Copy | Ajustar el aviso para nombrar explícitamente "maquinaria, remolque o semirremolque". |

> **Antes de codificar:** reproducir en DEV con una placa real de remolque y capturar el `checks[]` que devuelve el preflight. Si resulta ser `fail` y no `error`, 6.1 sobra y el problema es solo el patrón (6.3) — el resto del plan no cambia.

### Pruebas
- Unit `fieldRules`: `R1234`, `R12345`, `S123456`, `MC029554` válidas; basura sigue rechazada.
- RTL wizard: con placa `R12345` el selector de tipo de documento aparece **sin** consultar; con check `provider/error` también aparece.

### Riesgo
Bajo, todo frontend.

---

## Ajuste 7 — Error 500 al aprobar (`connection is already in a transaction`)

### Estado: **ya resuelto** en `53d94b3e` (rama actual, pendiente de PR a develop)

`MandateSignerDirectory.LoadVigentIdentitiesAsync` abría una transacción propia con `ExecutionStrategy` para hacer `SET LOCAL row_security = off`, pero al aprobar ya corre dentro de la transacción de `OtClientProcedureRepository.ExecuteInClientTenantScopeAsync` (`:827-840`). El fix (`MandateSignerDirectory.cs:121-127`) detecta `Database.CurrentTransaction is not null` y reutiliza la transacción activa.

### Pendiente
1. **Test de regresión**: hoy el commit no trae uno. Añadir un test de integración que ejercite `GetCandidatesAsync` dentro de un scope de tenant con transacción abierta (PostgreSQL real / Testcontainers — con InMemory no se reproduce, la rama `IsRelational()` lo salta).
2. Auditar si hay **otros** puntos con el mismo patrón `BeginTransaction` + `SET LOCAL` invocables desde dentro del scope del cliente (`grep "SET LOCAL row_security"`).

---

## Orden de ejecución propuesto

| Fase | Ajustes | Motivo |
|---|---|---|
| **1** | 7 (test de regresión) + 3 | Desbloquea la aprobación y hace que aprobar produzca documentos correctos. Ambos tocan el mismo endpoint ⇒ un solo PR. |
| **2** | 2 | Mismo patrón que la fase 1, sobre el endpoint de placa. |
| **3** | 4 + 5 | Ambos tocan los dos generadores PDF ⇒ un solo PR evita conflictos. **Depende de la fase 1**: sin regenerar al aprobar, las firmas nuevas no se verían. |
| **4** | 1 (residuo) | Cerrar sobre el WIP de #10993/#10994 ya presente en el árbol. |
| **5** | 6 | Independiente, frontend, se puede paralelizar en cualquier momento. |

## Propuesta de HUs (Sprint siguiente, no el activo)

| HU | Tipo | SP | Alcance |
|---|---|---|---|
| Regenerar el expediente al aprobar cualquier trámite | BACKEND | 3 | Ajuste 3 + test de regresión del ajuste 7 |
| Regenerar el expediente al asignar placa en preasignación | BACKEND | 3 | Ajuste 2 |
| Firma real (baúl / validación de identidad) en mandato y trámite virtual | BACKEND | 5 | Ajuste 4 |
| Resaltado de palabras clave en mandato y trámite virtual | BACKEND | 2 | Ajuste 5 |
| Identidad del mandatario: alta reusa vigente, anclaje y estado en la lista | FULLSTACK | 3 | Ajuste 1 (residuo) |
| Consulta por placa de remolques y semirremolques en el paso 1 | FRONTEND | 2 | Ajuste 6 |

**Total: 18 SP.** Ninguna HU supera las 800 líneas de PR previstas.

## Convenciones

- Rama por HU: `feature/AB-<id>-<descripcion>`; commits `HU<id>: <descripción>`.
- PRs siempre contra `develop`.
- Activación de cada HU (`Active`) requiere confirmación humana explícita antes de implementar.

## Preguntas abiertas

1. **Ajuste 5** — ¿lista definitiva de palabras clave a resaltar? ¿Existe copia de las plantillas legacy `virtual-process/*.hbs` para calibrar?
2. **Ajuste 6** — se necesita una placa real de remolque en DEV para confirmar si el proveedor responde `fail` o `error`.
3. **Ajustes 2 y 3** — la regeneración es síncrona dentro del request. ¿Se acepta la latencia extra (varios PDFs) o se prefiere encolar? Recomendación: síncrono ahora (es el patrón vigente) y medir antes de complicarlo.
4. **Ajuste 1** — ¿el WIP local de #10993/#10994 va en esta misma rama o en una aparte? Hoy está sin commitear.
