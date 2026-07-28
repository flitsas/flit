# Informe técnico — Por qué el FUR sale con información incompleta de SOAT, RTM, RUES y prendas

**Fecha:** 2026-07-28
**Rama de análisis:** `feature/AB-10909-mandato-solicitud-virtual`
**Alcance:** flujo `registrar trámite → consultar → generar FUR` en `services/core-api` y `frontend`
**Naturaleza:** solo lectura / diagnóstico. No se modificó código.

> **Ampliación (2026-07-28).** Este informe cubre los cuatro bloques reportados, que tocan 3 de los ~10
> documentos del expediente. El **barrido de los documentos restantes** (compraventa, solicitud de
> trámite virtual y mandato) se hizo después y vive en `docs/plan-tecnico-fur-datos-incompletos.md`
> §2.bis. Añadió dos huérfanas más en el propio FUR (`fur_observations`, `fur_processing_date`), un
> criterio de "dato ausente" no unificado entre documentos, y un campo muerto (`Causal`).

---

## 1. Resumen ejecutivo

La información **no se pierde al generar el FUR: nunca llega a existir**. En los cuatro bloques
reportados el generador lee llaves de `field_values` que **ningún proveedor escribe**, o lee datos que
sí existen pero que **el documento no imprime**.

| Bloque | Dónde debería salir | Causa raíz | Severidad |
|---|---|---|---|
| **SOAT** | `certificado_soat_rtm` (anexo del consolidado) | 4 de 6 celdas leen llaves que **no las escribe nadie** en todo el repo | Alta |
| **RTM** | `certificado_soat_rtm` | 5 de 6 celdas leen llaves inexistentes | Alta |
| **RUES** | `certificado_rues` | La precarga por NIT del directorio de RL **corta la consulta RUES**; además solo persiste en `borrador` y hay un solo juego de llaves por trámite | Alta |
| **Prendas** | FUR (formulario oficial) | El acreedor **se captura, viaja hasta el generador y se descarta**: no hay campo en el manifest ni mapeo | Media |

Hay además un agravante transversal: la plantilla de consulta `RUNT_VEHICLE` está cableada al proveedor
**`verifik`**, que es precisamente el mapper que **menos campos hidrata** de los tres disponibles.

---

## 2. Cómo fluye el dato (para ubicar cada hallazgo)

```
Wizard (frontend)
   └─ POST consulta  ──► RunConsultationHandler / RuesPersonLookupHandler
                            └─ provider.ConsultAsync()  ──► mapper → HydratedField[]
                                 └─ UpsertHydratedFields() ──► tramites.procedure_instance_field_values
                                                                    │
GenerarFurHandler (FurCommand.cs) ◄─────────────────────────────────┘
   ├─ FUR (overlay PDF)          ← FurFieldMapper + fur-field-manifest.json
   ├─ certificado_soat_rtm       ← SoatRtmCertificatePdfGenerator
   ├─ certificado_rues           ← RuesCertificatePdfGenerator
   └─ …resto de anexos
```

Dos consecuencias de diseño que conviene fijar antes de leer los hallazgos:

1. **El FUR (el formulario oficial) no tiene campos de SOAT, RTM ni RUES.** El manifest
   `fur-field-manifest.json` define 80 campos y ninguno corresponde a esos bloques. Esa información
   viaja en **anexos** que se fusionan en el consolidado. Lo que el usuario percibe como "el FUR sale
   incompleto" es, técnicamente, "los anexos del expediente salen en blanco".
2. **Regla de negocio vigente (HU #10856):** valor ausente en la consulta ⇒ **celda en blanco**, sin
   marcador ni guion (`SoatRtmCertificatePdfGenerator.Disp`). Por eso el síntoma es "vacío" y no "error".

---

## 3. Hallazgo 1 — SOAT/RTM: 8 llaves leídas, 0 escritas (causa raíz principal)

`FurCommand.cs:276-301` arma el certificado leyendo 13 llaves de `field_values`.
Un `grep` sobre **todo el repositorio** (backend, frontend, seeds SQL, migraciones, tests) confirma que
**8 de esas 13 llaves solo aparecen en ese único punto de lectura y en ningún punto de escritura**:

| Llave leída | ¿Alguien la escribe? | Celda que queda vacía |
|---|---|---|
| `soat_poliza` | ❌ **nadie** | SOAT · N° Póliza |
| `soat_expedicion` | ❌ **nadie** | SOAT · Fecha expedición |
| `soat_vigencia` | ❌ **nadie** | SOAT · Fecha vigencia |
| `rtm_numero` | ❌ **nadie** | RTM · N° RTM |
| `rtm_expedicion` | ❌ **nadie** | RTM · Fecha expedición |
| `rtm_vigencia` | ❌ **nadie** | RTM · Fecha vigencia |
| `rtm_entidad` | ❌ **nadie** | RTM · Entidad expide RTM |
| `runt_consulta_fecha` | ❌ **nadie** | Texto introductorio ("…el día ___") |
| `soat_vencimiento` | ✅ los 2 mappers RUNT | — |
| `soat_aseguradora` | ✅ los 2 mappers RUNT | — |
| `soat_estado` | ⚠️ solo Kyverum + `ValidateSoatViaRunt` | — |
| `rtm_vencimiento` | ✅ los 2 mappers RUNT | — |
| `rtm_estado` | ⚠️ solo Kyverum | — |

**Evidencia:**
- Lectura: `services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/FurCommand.cs:282-298`
- Escritura (inexistente): sin coincidencias fuera de ese archivo en todo el repo.
- No hay tampoco captura manual: el frontend no expone ningún campo con esos nombres.

**Efecto neto:** el certificado SOAT/RTM se emite con **2 de 6 celdas SOAT** y **1 de 6 celdas RTM**
pobladas en el mejor de los casos. El resto sale en blanco por diseño de `Disp()`.

---

## 4. Hallazgo 2 — El proveedor cableado por defecto es el que menos hidrata

`13-HU10201-consultation-providers.sql:13` cablea la plantilla `RUNT_VEHICLE` a:

```sql
SET external_refs = '{"provider":"verifik","endpointKey":"runt_vehicle"}'::jsonb
```

Comparación de los tres mappers de vehículo respecto a lo que el certificado necesita:

| Campo que pide el certificado | Verifik *(activo)* | Kyverum RUNT | INTEMPO |
|---|---|---|---|
| `soat_vencimiento` | ✅ | ✅ | ❌ |
| `soat_aseguradora` | ✅ | ✅ | ❌ |
| `soat_estado` | ❌ **(existe en el DTO, no se mapea)** | ✅ | ❌ |
| `rtm_vencimiento` | ✅ | ✅ | ❌ |
| `rtm_estado` | ❌ **(existe en el DTO, no se mapea)** | ✅ | ❌ |
| `rtm_entidad` | ❌ **(`cdaExpide` existe, no se mapea)** | ❌ (no existe en el DTO) | ❌ |
| `soat_poliza` / `_expedicion` / `_vigencia` | ❌ (no existe en el DTO) | ❌ (no existe en el DTO) | ⚠️ **existe en el DTO, no se mapea** |

Tres observaciones importantes:

1. **Verifik deja datos sobre la mesa.** `VerifikSoat.Estado` y `VerifikTecnomecanica.Estado` /
   `CdaExpide` están deserializados en `VerifikVehicleResponse.cs:115-140` pero
   `VerifikResultMapper.MapHydratedFields` (`:179-193`) no los emite. Son tres celdas recuperables
   **sin tocar el proveedor externo**.
2. **INTEMPO es el único proveedor con el shape completo del SOAT** —
   `IntempoSoat` (`IntempoVehicleResponse.cs:112-134`) trae `noPoliza`, `fechaExpedicion`,
   `fechaVigencia`, `fechaVencimiento`, `entidadExpideSoat` y `estado` — pero
   `IntempoVehicleResultMapper.MapHydratedFields` (`:99-137`) **no hidrata ni un solo campo de SOAT o
   RTM**: solo los 11 campos del vehículo. El dato llega a la aplicación y se descarta en el mapper.
3. **Consecuencia sobre `soat_estado`:** como Verifik no lo hidrata, con el proveedor activo esa llave
   solo se escribe por la ruta separada `ValidateSoatViaRuntHandler`, que corre **después de la
   asignación de placa** (`entregado` + `plate_flow_status = asignado`). En un trámite normal, al
   momento de generar el FUR esa llave todavía no existe ⇒ celda "Estado" en blanco.

---

## 5. Hallazgo 3 — RUES: tres causas independientes de vacío

El certificado RUES (`TryGenerateRuesCertificate`, `FurCommand.cs:675-712`) se emite **siempre que el
trámite tenga un actor con `DocumentType = NIT`**, y lee 21 llaves `rues_*`. Solo dos tienen respaldo
(`RazonSocial` cae a `actor.FullName`, `Nit` cae a `actor.DocumentNumber`); las otras 19 se imprimen
vacías si no hay `field_values`.

### 5.1 La precarga por NIT del directorio de RL corta la consulta RUES *(causa dominante)*

`frontend/components/operacion/ActorsForm.tsx:652-675` — HU #10906 (R3):

```ts
const preload = await tramitesClient.lookupLegalRepresentativeByNit(documentNumber);
if (preload) {
  …
  return;            // ◄── corta aquí: ruesPersonLookup() NUNCA se llama
}
const result = await tramitesClient.ruesPersonLookup(instanceId, { documentNumber });
```

Si el NIT ya está registrado en el directorio de representantes legales del tenant, el wizard precarga
razón social + representante y **nunca dispara la consulta RUES**. El actor jurídico queda bien formado,
el certificado RUES **igual se emite** (basta con que el actor sea NIT), pero como no se escribió ningún
`rues_*`, sale con razón social y NIT y **las otras 19 casillas en blanco**: matrícula mercantil, cámara
de comercio, sigla, fechas, dirección, actividades económicas, representación legal, etc.

Este es el escenario más frecuente en operación real (empresas recurrentes ya cargadas en el directorio)
y explica por qué el síntoma se percibe como intermitente: el mismo trámite con un NIT nuevo sale
completo y con un NIT ya conocido sale vacío.

### 5.2 Solo persiste si el trámite está en `borrador`

`RuesPersonLookupHandler.cs:74-79`:

```csharp
if (string.Equals(instance.Status, TramiteEstado.Borrador, …) && result.HydratedFields.Count > 0)
{
    UpsertHydrated(instance, tenantId, result.HydratedFields);
    await repo.SaveChangesAsync(ct);
}
```

Fuera de borrador el trigger de inmutabilidad de `field_values` bloquea la escritura, así que el handler
la omite deliberadamente. El autopoblado visual del actor **sí** funciona (el gestor ve la razón social
en pantalla), pero **nada queda persistido** ⇒ el certificado sale vacío. Es el caso típico de
"reconsultar en subsanación": la pantalla se ve bien, el PDF no.

### 5.3 Un solo juego de llaves `rues_*` por trámite

Las llaves `rues_*` son **de instancia, no de actor**. En un traspaso PJ → PJ, la segunda consulta
sobrescribe los valores de la primera, y el certificado se emite una sola vez para
`Actors.FirstOrDefault(a => a.DocumentType == "NIT")`. No hay garantía de que la razón social impresa y
la matrícula mercantil impresa pertenezcan a la **misma** compañía.

---

## 6. Hallazgo 4 — Prendas: el acreedor se captura y se descarta

La cadena está completa hasta el último paso, y ahí se rompe:

| Paso | Estado | Evidencia |
|---|---|---|
| El wizard pide acreedor y documento | ✅ | `PrendaForm.tsx:186-215` — comentario literal: *"datos del acreedor que se reflejarán en el FUR"* |
| Se persiste en tabla propia | ✅ | `ProcedureInstancePrenda.AcreedorNombre` / `AcreedorDocumento` |
| El generador lo resuelve | ✅ | `FurCommand.cs:139-141` → `acreedorPrenda` |
| Viaja al modelo del documento | ✅ | `FurCommand.cs:497` → `FurDocumentData.AcreedorPrenda` |
| **Se imprime en el FUR** | ❌ | `FurFieldMapper.cs` **no referencia `AcreedorPrenda` en ninguna línea** |

Lo único que el FUR refleja de la prenda es la casilla `requested_process_11`
(`FurFieldMapper.MarkTramite`, `:191-197`), que marca que *hay* gravamen pero no *de quién*.
No existe además ningún campo candidato en `fur-field-manifest.json` para el beneficiario: el manifest
cubre 80 campos y ninguno corresponde a acreedor, limitación a la propiedad ni entidad del gravamen.

**Corolario (garantías mobiliarias consultadas):** `IntempoGravamen`
(`IntempoVehicleResponse.cs:136-155`) trae `nombreAcreedor`, `numeroDocumentoAcreedor`,
`fechaInscripcion` y `estadoPrenda` de la consulta al RUNT. El mapper solo lo usa para calcular el
semáforo `gravamenes` (ok/warn) y **descarta el detalle**: no se hidrata ningún campo, no se persiste y
no aparece en ningún documento. Lo mismo aplica a Kyverum y Verifik, que solo exponen los flags
`gravamenes` / `prendas` como `"SI"`/`"NO"`.

---

## 7. Qué **no** es la causa (descartado en el análisis)

- **No es un fallo de generación.** El FUR y sus anexos se generan y se persisten correctamente; el
  `GenerarFurHandler` es idempotente y la invalidación de consolidados (`ConsolidadoMaestroVigente`,
  `InvalidarConsolidados`) funciona como está documentada.
- **No es el caché de consultas** (HU #10878/#10885). El caché guarda exactamente el mismo
  `HydratedField[]` que produce el mapper: reutiliza el dato incompleto, no lo empobrece.
- **No es la validación de identidad.** Desde HU #10463 la identidad no bloquea la generación, y afecta
  solo al sello de firma y al certificado de identidad.
- **No es la ausencia de RTM en matrícula inicial.** Ahí el bloque RTM se oculta a propósito
  (`esMatricula` ⇒ `Rtm: null`, `FurCommand.cs:290-291`).
- **No es el modo mock.** En local los providers mock devuelven payloads ricos, pero el estrangulamiento
  está en los **mappers** y en las llaves inexistentes, que son idénticos en mock y en real. Es decir:
  el problema se reproduce igual en ambos modos, y no se resuelve activando el modo real.

---

## 8. Recomendaciones, en orden de impacto por esfuerzo

### Prioridad 1 — sin tocar proveedores externos *(recupera ~5 celdas)*
1. Hidratar en `VerifikResultMapper` los campos que ya vienen deserializados y se ignoran:
   `soat_estado` (`soat.Estado`), `rtm_estado` (`rtm.Estado`), `rtm_entidad` (`rtm.CdaExpide`).
2. Escribir `runt_consulta_fecha` al persistir cualquier consulta de vehículo (el `UpsertHydratedFields`
   ya tiene el `now` a mano) para que el texto introductorio del certificado deje de quedar cojo.

### Prioridad 2 — cerrar el bloque SOAT completo
3. Hidratar los campos SOAT de INTEMPO en `IntempoVehicleResultMapper` (`soat_poliza`,
   `soat_expedicion`, `soat_vigencia`, `soat_vencimiento`, `soat_aseguradora`, `soat_estado`) — es el
   único proveedor con el shape completo y hoy no aporta ninguno.
4. Decidir explícitamente el origen de `soat_poliza` / `soat_expedicion` / `soat_vigencia` /
   `rtm_numero` / `rtm_expedicion` / `rtm_vigencia` con el proveedor activo. Si Verifik/Kyverum no los
   exponen, hay dos salidas legítimas y ninguna es dejarlo como está:
   **(a)** capturarlos manualmente en el wizard (o por OCR del PDF del SOAT, que ya se adjunta como
   `soat_manual`), o **(b)** retirar esas celdas del certificado para no publicar un documento con
   huecos permanentes.

### Prioridad 3 — RUES
5. En la ruta de precarga por directorio (`ActorsForm.tsx:658`), disparar `ruesPersonLookup` **además**
   de la precarga (no en vez de). La precarga resuelve la UX del representante legal; la consulta RUES
   es la que alimenta el certificado. Son objetivos distintos y hoy uno cancela al otro.
6. Permitir persistir `rues_*` fuera de `borrador`, con el mismo patrón acotado que ya usa
   `soat_estado` en el trigger de inmutabilidad (whitelist por llave), o resolver el RUES en el momento
   de generar el FUR en vez de depender de lo que quedó escrito en el wizard.
7. Volver las llaves `rues_*` **por actor** (sufijo de rol o JSON por NIT) para soportar traspasos
   PJ → PJ sin mezclar compañías.

### Prioridad 4 — prendas
8. Añadir al manifest del FUR el campo del acreedor y mapear `FurDocumentData.AcreedorPrenda` (hoy el
   dato ya llega hasta el generador: es exclusivamente un campo de manifest + una línea de mapper).
9. Persistir el detalle de gravámenes que devuelve el RUNT (acreedor, fecha de inscripción, estado) e
   incorporarlo al expediente — hoy se consulta y se tira.

---

## 9. Referencias de código

| Tema | Archivo |
|---|---|
| Ensamblado del FUR y anexos | `services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/FurCommand.cs` |
| Mapeo de campos del FUR | `services/core-api/src/Flit.Infrastructure/Documents/Fur/FurFieldMapper.cs` |
| Manifest del FUR (80 campos) | `services/core-api/src/Flit.Infrastructure/Documents/Fur/fur-field-manifest.json` |
| Certificado SOAT/RTM | `services/core-api/src/Flit.Infrastructure/Documents/SoatRtmCertificatePdfGenerator.cs` |
| Mapper Verifik *(proveedor activo)* | `services/core-api/src/Flit.Tramites.Application/UseCases/Consultations/VerifikResultMapper.cs` |
| Mapper Kyverum RUNT | `services/core-api/src/Flit.Tramites.Application/UseCases/Consultations/KyverumRuntVehicleResultMapper.cs` |
| Mapper INTEMPO | `services/core-api/src/Flit.Tramites.Application/UseCases/Consultations/IntempoVehicleResultMapper.cs` |
| Lookup y persistencia RUES | `services/core-api/src/Flit.Tramites.Application/UseCases/Consultations/RuesPersonLookupHandler.cs` |
| Provider RUES | `services/core-api/src/Flit.Infrastructure/Consultations/VerifikRuesConsultationProvider.cs` |
| Corte de la consulta RUES | `frontend/components/operacion/ActorsForm.tsx:652-675` |
| Captura de prenda | `frontend/components/operacion/PrendaForm.tsx` |
| Cableado del proveedor RUNT | `services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/13-HU10201-consultation-providers.sql:13` |
