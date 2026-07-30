# Plan técnico — Tablas certificadoras SOAT, RTM y RUES

> **Origen:** `ajustes-tablas-certificadoras.txt` (PO).
> **Base de código:** `develop` @ `7ec15462` (ya incluye Feature #11045 y #10972).
> **Estado:** solo plan. No implementado.
> **Objetivo del PO:** que los tres datos se **almacenen en base de datos** y el consolidado **pinte
> desde ahí**, sin re-consultar al proveedor cada vez que se regenera un documento.

---

## 0. Cómo se generan hoy los documentos (mapa verificado)

Un único punto ensambla TODOS los documentos del expediente: `GenerarFurHandler`
(`services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/FurCommand.cs`).
Lee un diccionario `fv` = `field_values` de la instancia y llama a un generador por documento.

```
Consulta externa (RUNT / RUES)          OCR de documentos cargados
        │                                        │
        ▼                                        ▼
RunConsultationHandler                   PersistOcrFieldsHandler
  · cache-aside external_query_cache       · prompt por tipo de documento
  · UpsertHydratedFields → field_values    · mapea a llaves field_values
        └──────────────┬─────────────────────────┘
                       ▼
              tramites.procedure_instance_field_values     ← trigger DB: solo escribible en borrador
                       │
                       ▼
              GenerarFurHandler (FurCommand.cs)
                       ├─ FUR (overlay PdfSharpCore)
                       ├─ Compraventa / Mandato / Solicitud virtual (QuestPDF)
                       ├─ certificado_soat_rtm  ← SoatRtmCertificatePdfGenerator   (FurCommand.cs:290-322)
                       ├─ certificado_rues      ← RuesCertificatePdfGenerator      (FurCommand.cs:245, 780-831)
                       ├─ certificado_rnmc / certificado_identidad / escrituras
                       ▼
              Expediente consolidado (merge por orden de prelación)
```

Dos matices que condicionan todo el plan:

1. **`field_values` solo se puede escribir en borrador.** Un trigger de BD lo impide fuera de
   borrador; `RunConsultationHandler` lo traduce a `not_draft`
   (`RunConsultationCommand.cs:122-131, 315-331`). Todo lo que no se capture durante el borrador,
   ya no entra — y el consolidado se regenera muchas veces después de esa ventana.
2. **Las llaves son de INSTANCIA, no de actor.** `rues_nit`, `rues_razon_social`… son un único
   juego por trámite. Con dos personas jurídicas (comprador y vendedor) solo una puede estar
   representada; la otra cae siempre al camino de consulta en vivo.

---

## 1. Hallazgos

### H1 — El contrato del RUES real NO coincide con el modelo. La consulta real revienta entera. 🔴

`VerifikRuesConsultationProvider.cs:265-331` declara los nombres JSON como *"la mejor aproximación
al contrato Verifik RUES v3"* (comentario textual en `:283-284`). Contrastado contra la respuesta
real que compartiste:

| Campo del certificado | Modelo espera | Respuesta real | Efecto hoy |
|---|---|---|---|
| Acrónimo / Sigla | `tradeName` | `acronym` | en blanco |
| Dirección Comercial | `address` | `commercialAddress` | en blanco |
| Ubicación | `city` | `companyLocation` | en blanco |
| Fecha Inscripción | `registrationDate` | `enrollmentDate` | en blanco |
| Categoría Inscripción | `category` | *(no existe)* | en blanco siempre |
| Actividad económica | `economicActivity` | *(no existe)* | en blanco siempre |
| Tabla de actividades | `data.infoActivitiesEconomic` | `data.economicActivities` | tabla vacía |
| **Representación legal** | `legalRepresentatives` : **string** | **objeto** `{faculty, legalRepresentatives[]}` | **rompe el parseo** |

El último es el grave. `ReadFromJsonAsync<VerifikRuesResponse>` intenta meter un objeto en un
`string?` → `JsonException` → lo captura `:85-88` → `ProviderUnavailable()` → check `error`, overall
`red`, **cero campos hidratados**. Es decir: en modo real, para cualquier empresa cuya respuesta
traiga ese objeto (la forma canónica de v3), la consulta RUES **falla completa**, no "sale
incompleta".

**Por qué nadie lo notó:** el mock (`:93-138`) construye la forma que el modelo espera, no la que
devuelve el servicio. En DEV todo se ve bien. La divergencia solo aparece con `VERIFIK_RUES_MODE=real`.

Campos correctos que sí funcionan: `NIT`, `businessName`, `chamberCommerce`, `companyType`, `email`,
`idRm`, `lastRenewedYear`, `lastUpdatedDate`, `organizationType`, `reasonForCancellation`,
`registrationNumber`, `registrationStatus`, `renewalDate`.

Datos reales que hoy se descartan y podrían aprovecharse: `chamberCity`, `chamberDepartment`,
`establishmentOwner[]` y —sobre todo— la **lista estructurada de representantes legales**
(`nombre` / `documentType` / `documentNumber` / `role`), mucho más útil que el párrafo libre
`faculty` que es lo único que el certificado pinta hoy.

### H2 — `renewalDate: "Invalid date"` se imprimiría literal

La muestra real trae ese string. `FlitDocumentDate.Normalize` deja el texto tal cual cuando no puede
interpretarlo (decisión deliberada: nunca inventar ni vaciar un dato). Resultado: una celda del
certificado que dice **"Invalid date"**. Hay que sanear los centinelas del proveedor.

### H3 — El RUES se consulta EN VIVO en cada generación (lo que el PO quiere eliminar)

`RuesActorDataResolver` lleva escrito, textual: *"**Deliberadamente NO lee la caché de reúso**. HU
#10955 estableció que el estado en RUES de una persona jurídica se consulta siempre en vivo, y un
certificado no debe dar fe de un payload cacheado"* (`RuesActorDataResolver.cs:34-42`).

Y por H4 (llaves de instancia) ese resolutor entra casi siempre. **Lo que pide el PO revierte una
decisión de arquitectura vigente y documentada** → requiere ADR, no solo código.

### H4 — El certificado SOAT/RTM lee 6 campos por ramo; el RUNT solo alimenta 3

`FurCommand.cs:303-318` arma dos bloques de 6 campos. Quién los escribe hoy:

| Llave | Productor real |
|---|---|
| `soat_vencimiento`, `soat_aseguradora` | RUNT (Verifik/Kyverum) |
| `soat_estado` | RUNT, normalizado por `SoatGate` |
| `soat_poliza`, `soat_vigencia`, `soat_expedicion` | **OCR del PDF del SOAT cargado** |
| `rtm_vencimiento`, `rtm_estado`, `rtm_entidad` | RUNT |
| `rtm_numero`, `rtm_vigencia`, `rtm_expedicion` | **OCR del PDF de la RTM cargada** |

(Registro declarativo en `FieldValueContractGuardTests.cs:94-106`.)

O sea: la mitad de cada tabla depende de que el operador **cargue el documento y el OCR acierte**.
El PO dice que ese dato viene del RUNT — y tiene razón: `IntempoSoat`
(`IntempoVehicleResponse.cs:112-134`) **ya modela** `noPoliza`, `fechaExpedicion`, `fechaVigencia`,
`fechaVencimiento`, `entidadExpideSoat`, `estado`. Es decir, el contrato del RUNT sí trae los seis;
el modelo de **Verifik** (`VerifikVehicleResponse.cs:115-125`) solo declara tres y descarta el resto
en la deserialización.

### H5 — Intempo no hidrata NADA

`IntempoVehicleResultMapper` produce un *check* de SOAT y ningún `field_value` (`:38, 62`). El
proveedor que "viene pronto" generaría el certificado en blanco. Además Intempo **no tiene bloque
RTM** en el modelo.

### H6 — Kyverum: sin póliza/vigencia/expedición y sin fecha de matrícula

`KyverumRuntSoat` y `KyverumRuntRtm` (`:124-151`) traen 3 y 3 campos. Tampoco hidrata
`vehicle_registration_date`, que es justo el insumo de la regla de los 5 años.

### H7 — La regla "RTM solo si el vehículo tiene más de 5 años" no existe

Hoy la tabla RTM se pinta en **todo** traspaso: `esMatricula ? null : new SoatRtmBlock(...)`
(`FurCommand.cs:310-318`). El insumo existe —`vehicle_registration_date`, hidratado en
`VerifikResultMapper.cs:178-179`— pero **ningún documento lo consume**.

### H8 — Seguridad: token Verifik vivo en el repo 🔴

`ajustes-tablas-certificadoras.txt` (raíz del repo) contiene un Bearer de Verifik **válido hasta el
~2026-08-02**. El archivo está sin trackear, pero un `git add -A` lo commitea. **Recomendación:
rotar el token y mover el archivo fuera del árbol o añadirlo a `.gitignore` antes de tocar nada.**

---

## 2. Decisiones abiertas

| # | Decisión | Recomendación |
|---|---|---|
| **D1** | Dónde persistir el RUES para reúso | **CERRADA (PO).** Snapshot **congelado por actor**, tomado **en el momento de registrar el trámite**. El certificado da fe de lo que se consultó al registrar, y nunca se re-consulta después. Ver §3 Eje C. |
| **D2** | Papel de la caché de 24 h | **CERRADA (PO).** `external_query_cache` conserva su propósito original: reusar durante 24 h la información ya registrada, en los **pasos de vendedor y comprador** del wizard. **No** es la fuente del certificado. ⚠️ Ver D2-bis: ese reúso hoy está apagado. |
| **D2-bis** | El reúso de 24 h que describe el PO **hoy no ocurre** | HU #10955 quitó la lectura de caché de `RuesPersonLookupHandler` (`:54-57`) y de `RuntPersonLookupHandler`: cada paso consulta en vivo y la caché **solo se escribe**. Si se quiere el comportamiento descrito, hay que **re-habilitar la lectura** — es una segunda reversión de #10955 y va en el mismo ADR. **Decisión del PO.** |
| **D3** | Precedencia RUNT vs OCR en SOAT/RTM | **RUNT gana**; el OCR queda como respaldo cuando el RUNT no trajo el campo. Es lo que pide el PO sin perder lo que ya funciona. |
| **D4** | Regla de 5 años: ¿desde qué fecha? | `vehicle_registration_date` (fecha de matrícula del RUNT). **Si el dato falta → mostrar la tabla** (fail-open: es peor omitir una RTM debida que incluir una de más). |
| **D5** | ¿5 años contra qué fecha? | Contra la **fecha de generación del documento**, no la de radicación. |
| **D6** | ¿Pintar los representantes legales estructurados del RUES? | Sí — sección nueva en el certificado y, opcionalmente, precarga del directorio de RL (`plan-rl-escrituras-por-compania`). **Requiere visto bueno del PO** porque cambia el layout del PDF. |
| **D7** | ¿Qué hacer con el `soat_estado` que ya alimenta el gate del OT? | **No tocarlo.** Es a la vez dato del certificado y gate de aprobación (HU #10804/#10973); cualquier cambio de normalización rompe la aprobación en el front. |

---

## 3. Propuesta

### Eje A — SOAT (H4, H5, H6)

1. Ampliar `VerifikSoat` con `noPoliza`, `fechaExpedicion`, `fechaVigencia` (y `nitEntidad` si viene),
   espejo de `IntempoSoat`. **Confirmar los nombres exactos con la sonda del §5.**
2. `VerifikResultMapper`: hidratar `soat_poliza`, `soat_vigencia`, `soat_expedicion` desde el RUNT.
3. Igual en `KyverumRuntSoat` / su mapper, con los nombres propios de Kyverum.
4. `IntempoVehicleResultMapper`: hidratar las seis llaves (hoy no hidrata ninguna).
5. Precedencia D3: el OCR no debe pisar un valor que ya trajo el RUNT.

### Eje B — RTM (H4, H6, H7)

6. Mismo tratamiento para `rtm_numero`, `rtm_vigencia`, `rtm_expedicion` en los tres proveedores.
   Intempo necesita además el bloque RTM completo, que hoy no existe.
7. Hidratar `vehicle_registration_date` también en Kyverum e Intempo.
8. **Regla de los 5 años** en el dominio, no en el generador — una función pura testeable, p. ej.
   `RtmCertificado.Aplica(procedureType, fechaMatricula, hoy)`, consumida en `FurCommand.cs:310`.
   Traspaso **y** antigüedad > 5 años → tabla; en cualquier otro caso, `null` (que ya oculta la tabla).

### Eje C — RUES (H1, H2, H3)

9. **Corregir el contrato** (H1): renombrar las 6 claves erradas, modelar
   `legalRepresentatives` como objeto `{faculty, legalRepresentatives[]}`, y `economicActivities`
   como lista en el nivel `data`. Añadir `chamberCity` / `chamberDepartment`.
10. **Alinear el mock con la forma real** — si el mock no reproduce el contrato del proveedor, la
    siguiente divergencia vuelve a pasar desapercibida. Sembrarlo con la muestra que compartió el PO.
11. **Sanear centinelas** (H2): `"Invalid date"`, `""` y similares → celda en blanco.
12. **Snapshot congelado por actor, tomado al registrar** (D1). Diseño:

    - **Quién lo toma:** `RuesPersonLookupHandler` — ya es la costura del registro (el wizard lo
      invoca al resolver el actor jurídico por NIT) y ya persiste las `rues_*` en borrador
      (`:71-78`). Se le añade guardar el payload completo **contra el actor**, no contra la instancia.
    - **Dónde:** `procedure_instance_actors.metadata` (jsonb ya existente,
      `ProcedureInstanceActor.cs:30`) bajo la llave `rues`, con `queried_at` y `provider`.
      **Cero migración.** Verificado: esa tabla solo tiene trigger de auditoría y RLS
      (`Ddl/06-HU10150-procedure-instances.sql:84-102`) — **no** le aplica el trigger de
      inmutabilidad que sí bloquea `field_values` fuera de borrador.
    - **Quién lo lee:** `TryGenerateRuesCertificatesAsync` (`FurCommand.cs:780-831`) pasa a leer el
      snapshot del actor. Regenerar el expediente cien veces = **cero consultas al RUES**.
    - **Congelado:** una vez escrito no se refresca solo. Se re-escribe únicamente si el operador
      vuelve a consultar ese NIT estando el trámite en edición (borrador o subsanación).

13. **Retirar `RuesActorDataResolver` del camino normal.** Con el snapshot por actor deja de tener
    sentido como fuente: queda solo como respaldo para **trámites legacy** sin snapshot (junto a la
    lectura actual de `field_values`, que sigue sirviendo al primer actor jurídico). Propuesta: que
    ese respaldo sea explícito y logueado, para poder medir cuántas consultas en vivo quedan y
    apagarlo después.
14. **ADR** que revierta lo que corresponda de HU #10955 con la justificación de costo del PO:
    el certificado deja de consultar en vivo (cerrado) y, si el PO acepta D2-bis, se re-habilita
    además la lectura de caché en los pasos de vendedor/comprador. `Propuesto` — el paso a
    `Aceptado` es del Líder Técnico.
15. **Efecto colateral que se resuelve solo:** al ser por actor, desaparece el defecto de H4 —
    hoy, con comprador y vendedor jurídicos, las `rues_*` de instancia solo pueden representar a uno.
16. **Dependencia dura:** con el snapshot congelado, si la consulta falla al registrar **no hay
    segunda oportunidad** fuera de la ventana de edición. Eso convierte a **H1 en bloqueante**: hay
    que corregir el contrato del RUES (HU 1) **antes** de activar el congelado, o los trámites
    nuevos quedarían con snapshot vacío de forma permanente.

### Eje D — Guardia

15. Extender `FieldValueContractGuardTests` con las llaves nuevas y sus productores.
16. Test de contrato que deserialice **la muestra real** del RUES y exija que ningún campo del
    certificado quede nulo por nombre de clave equivocado. Es la prueba que habría cazado H1.

---

## 4. Fases sugeridas (una HU por línea, todas bajo un Feature nuevo)

| # | HU | Alcance | SP |
|---|----|---------|----|
| 1 | Contrato RUES real + mock alineado + saneo de centinelas | H1, H2 | 5 |
| 2 | Snapshot RUES congelado por actor (al registrar) + consumo desde BD + ADR | H3, D1 | 8 |
| 3 | SOAT completo desde el RUNT (Verifik + Kyverum) | H4, H6 | 5 |
| 4 | RTM completa desde el RUNT | H4, H6 | 3 |
| 5 | Regla RTM > 5 años en traspaso | H7, D4, D5 | 3 |
| 6 | Intempo hidrata SOAT/RTM | H5 | 5 |
| 7 | Guardias de contrato + regresión | Eje D | 3 |
| 8 | *(condicional a D2-bis)* Re-habilitar el reúso de 24 h en pasos vendedor/comprador | D2-bis | 3 |

Orden: **1 → 2 es dependencia dura** (§3.16: congelar sin arreglar el contrato deja snapshots vacíos
para siempre). Ese bloque es independiente de **3 → 4 → 5**; la 6 puede ir en paralelo o diferirse
hasta que Intempo esté disponible de verdad. La 7 cierra. La 8 solo si el PO confirma D2-bis.

---

## 5. Validación pendiente (te toca a ti lanzarla)

Dejé lista una sonda contra los dos endpoints reales, pero **el clasificador de permisos bloqueó la
llamada saliente**, así que no pude ejecutarla. Lee el token del `.txt` y nunca lo imprime; guarda
las respuestas crudas en el scratchpad:

```
! & 'C:\Users\USUARIO\AppData\Local\Temp\claude\D--Cursor-FLIT-2-0\8eea1644-6648-4436-a15c-0cdd1dcb1b63\scratchpad\probe-verifik.ps1'
```

Consulta `rues-complete` con el NIT 890903938 y el RUNT `vehicle-by-plate` con KPP192. Lo que
resuelve:

- **Los nombres exactos** de los campos de SOAT y RTM en la respuesta del RUNT por Verifik — es el
  único supuesto del Eje A/B que hoy está inferido (de `IntempoSoat`) y no verificado.
- Si la RTM viaja con número de certificado y fechas de expedición/vigencia, o si esos tres campos
  solo existen en el documento y el OCR sigue siendo necesario.
- Confirmación de H1 en vivo: si la consulta RUES real devuelve el check `error` que predice el
  análisis del payload.

Con esas respuestas cierro el §3 sin supuestos.

---

## 6. Riesgos

| Riesgo | Mitigación |
|---|---|
| `soat_estado` alimenta el gate de aprobación del OT | D7: no se toca su normalización; los campos nuevos son aditivos |
| Trámites ya cursados con celdas vacías | El snapshot no repuebla el pasado. Si el PO lo quiere, es una migración de datos aparte — decisión suya |
| El trigger de borrador impide capturar tarde | Por eso el snapshot RUES vive **fuera** de `field_values`, en `actors.metadata`, que no tiene esa restricción |
| Congelar antes de arreglar el contrato → snapshots vacíos permanentes | HU 1 antes que HU 2, sin excepción (§3.16) |
| Trámites legacy sin snapshot | Respaldo explícito y logueado (§3.13); se apaga cuando el contador llegue a cero |
| Cambiar el layout del certificado RUES (D6) | Validación visual del PO antes de mergear |
| El token de la sonda expira ~2026-08-02 | Rotarlo igualmente por H8 |
