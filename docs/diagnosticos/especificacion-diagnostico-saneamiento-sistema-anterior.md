# Especificación del diagnóstico con saneamiento (sistema anterior)

> Generado: 2026-11-13. Ingeniería inversa del código, sin documentación funcional de origen.
>
> - **Frontend:** `D:\FLIT\Front\FrontNextJS`. Módulos `app/vehicleFleet` (Diagnóstico de Vehículos) y `app/vehicleCleanupProcess` (Saneamiento de Vehículos). Next.js con PrimeReact.
> - **Backend:** `D:\FLIT\BackCrudTransfer_master`. Módulo `vehicleFleet`. Node/TypeScript con Express, tsoa, inversify y TypeORM.
>
> Las rutas de archivo de este documento son relativas a la raíz de cada repositorio: **[FE]** para el frontend y **[BE]** para el backend.

---

## 0. Resumen en una página

En el sistema anterior **el saneamiento no es un módulo con reglas propias**: es un **flujo manual de cuatro marcas** sobre cada placa del diagnóstico, operado por dos actores.

```text
 Cliente                          FLIT (operación)                      Cliente / FLIT
 ───────                          ────────────────                      ──────────────
 1. Carga Excel de placas  ──►  2. Consultas automáticas (RUNT, HQ RUNT, SIMIT, impuestos)
                                3. Escribe el «Análisis de diagnóstico»
                                   → la placa queda BLOQUEADA
                                4. Finaliza el diagnóstico (cabecera)
 5. «Solicitar saneamiento» ($)
    escribe un texto libre
    → isToClean = true     ──►  6. Gestiona el saneamiento por fuera del sistema
                                7. Marca «Saneado» → isCleaned = true
                                                                       8. «Ver pagos» de la placa
```

| Marca (`vehicle_fleet_detail`) | Significado | Quién la pone |
|---|---|---|
| `operationsObservations` | Análisis de diagnóstico (texto) | FLIT |
| `isBlocked` | Placa bloqueada. La pone el análisis, el FLIT a mano o el exceso de intentos de consulta | FLIT o el sistema |
| `isToClean` + `managerObservations` | Saneamiento solicitado, con el texto de la solicitud | Cliente |
| `isCleaned` | Saneado | FLIT |

**No hay** regla automática que decida si una placa requiere saneamiento, máquina de estados, cálculo de montos, adjuntos en la solicitud ni notificaciones. **La decisión es humana**: la toma el analista de FLIT con su texto de análisis.

---

## 1. Cómo se carga la placa

### 1.1 Canal

Solo existe la **carga masiva por archivo**. No hay alta unitaria ni selección de placas desde otra pantalla.

| Aspecto | Comportamiento | Evidencia |
|---|---|---|
| Formatos | `.xlsx`, `.xls`, `.csv` | [FE] `vehicleFleet/ui/components/UploadNewVehicleFleet.tsx:56` |
| Lectura | En el navegador, con la librería `XLSX`. Se leen **todas las hojas** del libro | [FE] `UploadNewVehicleFleet.tsx:63-109, 260` |
| Columnas obligatorias | `TipoDocumento`, `Documento`, `PlacaVehiculo` | [FE] `vehicleFleet/domain/IVehicleFleetFileModel.ts:1-6` |
| Tope | **50 filas** en total. Se valida en el front y otra vez en el back | [FE] `UploadNewVehicleFleet.tsx` · [BE] `app/services/vehicleFleet/vehicleFleetService.ts:68-75` |
| Celda vacía | Cuenta como error de la fila | [FE] `UploadNewVehicleFleet.tsx:63-109` |

El documento de la fila es el **propietario declarado por el cliente**. El backend lo guarda también como `previousDocumentType` y `previousDocumentNumber`, para compararlo después con el propietario que reporta el RUNT (campo `ownerChange`).

### 1.2 Validaciones por fila

| Campo | Frontend | Backend |
|---|---|---|
| Tipo de documento | Empieza por `CC`, `NIT`, `CE` o `PA`. La ayuda en pantalla menciona también `RC` | Uno de `CC`, `CE`, `PA`, `RC`, `NIT` |
| Número de documento | Solo dígitos, de 6 a 10 | Solo dígitos |
| Placa | Longitud de 5 a 8 y uno de 4 patrones | Uno de **5** patrones |

Patrones de placa:

| Clase | Regex | Front | Back |
|---|---|---|---|
| Carro | `^[A-Z]{3}\d{3}$` | ✔ | ✔ |
| Moto | `^[A-Z]{3}\d{2}[A-Z]{0,1}$` | ✔ | ✔ |
| Maquinaria | `^[A-Z]{2}\d{6}$` | ✔ | ✔ |
| Remolque | `^[A-Z]{1}\d{5}$` | ✔ | ✔ |
| Motocarro | `^\d{3}[A-Z]{3}$` | ✘ | ✔ |

Evidencia: [FE] `UploadNewVehicleFleet.tsx:172-185` y `common/utilities/StringUtils.ts:14-18, 87-91`; [BE] `vehicleFleetService.ts:129-170`.

> ⚠️ El front rechaza las placas de motocarro (`123ABC`) que el back sí acepta. Si una sola fila falla en el back, **se rechaza el archivo entero**: devuelve `[{rowNum, responseData}]` y no guarda nada.

### 1.3 Envío y respuesta

Endpoint: `POST {API_SERVICE_FLEET_VEHICLE}/api/v1/vehicle-fleet/upload`. Lo llama [FE] `vehicleFleet/infrastructure/vehicleFleet.service.ts:44-62`.

El cuerpo es **JSON, no multipart**: el archivo ya llega parseado.

```json
{
  "fileName": "…", "fileDescription": "…",
  "tenant": "<NIT compañía>",
  "userregistered": "…", "mailUserregistered": "…",
  "companyRegistered": "…", "roleUserregistered": "<primer rol del usuario>",
  "contentFile": [ { "rowNum": 1, "documentType": "CC", "documentNumber": "…", "licensePlate": "ABC123" } ]
}
```

Qué hace el backend ([BE] `vehicleFleetService.ts:97-125`):

- Crea la cabecera `vehicle_fleet_master`. Si no llega nombre, usa `yyyyMMddHHmmss_usuario`.
- Crea un `vehicle_fleet_detail` por fila.
- Responde «Su archivo se cargo correctamente».

En el front ([FE] `UploadNewVehicleFleet.tsx:147-163`), si la respuesta trae `code 200` sale el toast **«El archivo ha sido enviado. Tenga en cuenta que la información será procesada durante el día.»**. Si trae un arreglo, muestra los errores por fila.

### 1.4 Modelo de datos de la carga

**`vehicle_fleet_master`** (cabecera = diagnóstico):

- `id`, `fileName`, `fileDescription`
- `tenant`: FK a `company_parameters.companyNit`
- `registrationDate`
- Usuario que registra: `userregistered`, `mailUserregistered`, `companyRegistered`, `roleUserregistered`
- **`finalized`**, `diagnosticName`, `hasCost`, `observation`, `hasAnalysis`
- Relación 1:N con `vehicle_fleet_detail`

Evidencia: [BE] `infrastructure/persistence/entities/vehicleFleet/vehicleFleetEntity.ts:17-75`.

**`vehicle_fleet_detail`** (una placa):

- Identificación: `vehicleFleetId`, `documentType`, `documentNumber`, `licensePlate`, `previousDocumentType`, `previousDocumentNumber`, `tenant`
- Banderas de consulta: `queryRunt`, `queryHqRunt`, `querySimit`, `queryTaxes`, `queryLessee`, `hqRuntQueryForm`
- Contadores de intentos: `runtQueryAttempt`, `hqRuntQueryAttempt`, `simitQueryAttempt`, `taxQueryAttempt`, `lesseeQueryAttempt`
- Fechas: `registrationDate*` por fuente y `datetimeLastQuery`
- Otros: `ownerChange`, `isBlocked`
- **Saneamiento:** `isToClean`, `isCleaned`, `operationsObservations`, `managerObservations`
- Relación 1:1 con `vehicle_fleet_detail_json`, que guarda las respuestas crudas

Evidencia: [BE] `vehicleFleetDetailEntity.ts:15-124`.

---

## 2. Consultas requeridas

### 2.1 Fuentes

| Fuente | Bandera | Proveedor (proxy) | Servicio [BE] |
|---|---|---|---|
| RUNT | `queryRunt` | `IntempoApiProxy.getContractInfoByPlate` | `vehicleFleetRuntService.ts:103` |
| HQ RUNT (histórico) | `queryHqRunt` | `IntempoApiProxy` | `vehicleFleetHqRuntService.ts:182` |
| SIMIT | `querySimit` | `RegistrationApiProxy.getSimitPlate` | `vehicleFleetSimitService.ts:85-112` |
| Impuestos | `queryTaxes` | Lo resuelve el propio servicio | `vehicleFleetTaxesService` |
| Locatario / infracciones | `queryLessee` | `RegistrationInfractionApiProxy.getInfractionsInfo` | `vehicleFleetLesseeService.ts:219` |

Las respuestas se guardan con `registrationResolutionApiProxy.saveFleetJsonData` en `vehicle_fleet_detail_json`: `responseDataRunt`, `responseDataSimit`, `responseDataHqrunt` y `responseDataTaxes`, cada una con su código de estado. Cada cambio queda registrado en `vehicle_fleet_detail_json_log`.

### 2.2 Cómo se ejecutan

- **Procesamiento por lotes, invocado desde fuera.** Cada fuente tiene un endpoint sin parámetros que recorre las placas pendientes:
  - `GET /vehicle-fleet-runt`
  - `GET /vehicle-fleet-simit`
  - `GET /vehicle-fleet-taxes`
  - `POST /vehicle-fleet-hq-runt`
  - `POST /vehicle-fleet-lessee`

  En el repositorio no hay cron. Se infiere que un programador externo los llama, lo que explica el aviso de «procesada durante el día».
- **Caché diaria del SIMIT.** Si la misma placa ya se consultó hoy, se reutiliza esa respuesta ([BE] `vehicleFleetSimitService.ts:57-60`).
- **Reintentos y bloqueo.** Cada fallo suma 1 al contador `*QueryAttempt`. Al llegar a `limitQueriesToSuspendFleetDiagnosis` (parámetro de configuración), la placa queda **`isBlocked = true`** ([BE] `vehicleFleetSimitService.ts:127, 196-211`).
- **El front no consulta el estado periódicamente** (ni polling ni websocket): el usuario recarga la grilla.

### 2.3 Cuáles son obligatorias

El código no declara un conjunto «obligatorio» explícito. Lo que sí define:

| Criterio | Regla | Evidencia |
|---|---|---|
| Placa *finalizada* en el reporte resumen | `queryHqRunt && querySimit && queryRunt`. **Los impuestos no se exigen** | [BE] `vehicleFleetService.ts:233-242` |
| Botón «Ver pdf» del vehículo | `queryRunt && queryHqRunt` | [FE] `VehicleFleetDetail.tsx:277` |

En la práctica, **RUNT, HQ RUNT y SIMIT son las requeridas**. Impuestos y locatario son complementarias.

### 2.4 Intervención del FLIT sobre las consultas (solo rol FLIT)

| Acción | Efecto | Evidencia [FE] |
|---|---|---|
| Reprocesar SIMIT, Impuestos o RUNT | `{query<X>: false, <x>QueryAttempt: 0, isBlocked: false}`, y el lote la vuelve a tomar | `VehicleFleetDetail.tsx:295-306` |
| Bloquear o desbloquear la placa | Invierte `isBlocked`. El candado cerrado muestra el tooltip «La placa no existe. (Desbloquear)» | `VehicleFleetDetail.tsx:284-285` |
| Editar la información a mano (formulario HQ RUNT) | `POST vehicle-fleet-detail/formHqRunt/{id}` y luego desbloquea | `VehicleFleetHQRuntForm.tsx` |
| Ver la respuesta cruda | `/tableViewer/{id}?detailId=&queryType=`, con `SIMIT=1`, `RUNT=2`, `HQ_RUNT=3` y `OTHER=9` | `VehicleFleetDetail.tsx:208-215` |

Campos del formulario manual HQ RUNT ([FE] `VehicleFleetHQRuntForm.tsx:359-557`):

- **Propietario (obligatorio):** tipo y número de documento, y nombre completo.
- **Locatario (opcional):** tipo y número de documento, nombre completo y tipo de locatario.
- **Accidentalidad.**
- **Impuestos:** «¿Posee impuestos pendientes?» (Sí/No). Con Sí se agregan filas con «Agregar impuesto», cada una con un valor en COP.

### 2.5 Salidas del diagnóstico

- **Consolidado:** Excel `GET vehicle-fleet-detail/generate-file/generate/{id}` y PDF `POST vehicle-fleet-detail-json/generateConsolidated`, con más de 60 campos. Incluye:
  - SOAT y RTM.
  - Garantías mobiliarias, limitaciones y blindaje.
  - Accidentalidad e impuestos.
  - Multas con `detalleMultas` (`valorPagar`, `fechaComparendo`, `numeroComparendo`) y comparendos del locatario.
  - `NormalizacionSaneamiento` y `analisisSaneamiento`.

  Fuente: [FE] `vehicleFleet/domain/IVehicleFleetExcelModel.ts` y `utils/ExcelVehicleFleetDetailMapping.ts:793`.
- `NormalizacionSaneamiento` es un dato **que devuelve el proveedor** (Intempo, Registration o Kyverum). No es una regla de flota ([BE] `infrastructure/proxies/intempoApiProxy.ts:435`).

---

## 3. Condiciones para solicitar el saneamiento

### 3.1 Precondiciones (todas)

La condición está en [FE] `vehicleFleet/ui/components/VehicleFleetDetail.tsx:274-287`:

```ts
const showCleanupProcessButton =
  !isToClean
  && isBlocked
  && (!!operationsObservations && operationsObservations.trim() !== '')
  && isDiagnosticFinalized;

// El botón «Solicitar saneamiento» (ícono $) se pinta si:
(showCleanupProcessButton || isToClean) && !isFlitUser
```

| # | Condición | Quién la produce |
|---|---|---|
| 1 | El usuario **no** es FLIT: lo solicita el cliente | — |
| 2 | La placa tiene un **análisis de diagnóstico** escrito (`operationsObservations` no vacío) | FLIT, con el botón lápiz naranja «Análisis de diagnóstico» |
| 3 | La placa está **bloqueada** (`isBlocked = true`). Guardar el análisis la bloquea automáticamente | FLIT, al guardar el análisis |
| 4 | El diagnóstico (la cabecera) está **finalizado** (`finalized = true`) | FLIT, desde el listado de diagnósticos |
| 5 | Todavía no se ha solicitado (`!isToClean`). Si ya se solicitó, el botón se sigue viendo pero abre el diálogo en solo lectura | — |

Al guardar el análisis ([FE] `VehicleFleetDiagnosticDialog.tsx:25-37`) se envía:

```ts
PUT vehicle-fleet-detail/{id}  { isBlocked: true, operationsObservations: value }
```

Finalizar la cabecera ([FE] `VehicleFleetList.tsx:219-229`) es `PUT vehicle-fleet/{id} { finalized: !finalized }`. Es un interruptor: el FLIT también puede desfinalizar.

### 3.2 Qué **no** se evalúa

En ninguno de los dos repositorios hay una regla que derive el saneamiento de la deuda (multas o impuestos mayores que cero), de las prendas, los embargos, el SOAT, la RTM o el estado en el RUNT.

**El criterio es el juicio del analista de FLIT**, expresado en su texto de análisis. Que la placa «requiera saneamiento» se infiere de que el FLIT la analizó y la bloqueó.

> ⚠️ `isBlocked` tiene dos significados que se mezclan:
> - **Bloqueo por análisis**, puesto por el FLIT.
> - **Bloqueo técnico**, puesto por el sistema al agotar los reintentos. El tooltip «La placa no existe» corresponde a este caso.
>
> El botón de saneamiento solo distingue los dos casos porque además exige un análisis escrito.

### 3.3 Cómo se navega

No hay un salto de una pantalla a la otra. La solicitud se registra **dentro del detalle del diagnóstico**: menú lateral «Diagnóstico de Vehículos», luego un diagnóstico (que abre en otra pestaña como `/vehicleFleet?id=&registrationDate=&finalized=`) y luego la fila de la placa.

Después, la placa aparece en el módulo **«Saneamiento de Vehículos»** del menú lateral. Ese menú se oculta al rol *Contract Manager* ([FE] `core/ui/layout/components/SideBar.tsx:225-234, 288-297`).

---

## 4. Información que se registra para el saneamiento

### 4.1 Solicitud (cliente)

Diálogo `VehicleFleetCleaupProcessDialog` ([FE] `vehicleFleet/ui/components/VehicleFleetCleaupProcessDialog.tsx`):

| Campo | Tipo | Obligatorio | Validación | Se guarda en |
|---|---|---|---|---|
| Id del detalle | Solo lectura | — | — | — |
| Placa | Solo lectura | — | — | — |
| **Información del saneamiento** | Textarea libre | **No** (ni siquiera se valida vacío) | Ninguna | `managerObservations` |

Al guardar se envía `PUT vehicle-fleet-detail/{id} { managerObservations: value, isToClean: true }` y sale el toast «El registro ha sido actualizado!» (líneas 25-43).

Una vez solicitado, el textarea y el botón Guardar quedan **deshabilitados** y el tooltip cambia a **«Saneamiento solicitado»** (líneas 45-52). No se puede editar ni anular la solicitud.

### 4.2 Gestión (FLIT)

| Dato | Cómo | Evidencia [FE] |
|---|---|---|
| Análisis de diagnóstico | Textarea; se registra antes de la solicitud y bloquea la placa | `VehicleFleetDiagnosticDialog.tsx` |
| Saneado | Botón reloj ⏱ / check ✔ en el listado de saneamiento: `PUT vehicle-fleet-detail/{id} { isCleaned: !isCleaned }`. Se puede desmarcar | `vehicleCleanupProcess/ui/components/VehicleCleanupProcessList.tsx:266-275` |

No se registra nada más: ni responsable, ni fecha de cierre propia (solo `updatedAt`), ni costo, ni resultado por concepto.

---

## 5. Qué se necesita para el saneamiento

| Requisito | ¿Lo exige el sistema? |
|---|---|
| Diagnóstico cargado, con las consultas procesadas | Sí, implícitamente: sin datos no hay análisis |
| Análisis escrito por FLIT, placa bloqueada y diagnóstico finalizado | **Sí** (sección 3.1) |
| Usuario de la compañía (no FLIT) que lo solicite | **Sí** |
| Texto de la solicitud | No: el campo admite vacío |
| Pago previo, mandato, poder o documentos del propietario | **No.** No hay ningún requisito en el código |
| Cálculo de montos o tarifas | **No.** Solo existe el booleano `hasCost` en la cabecera, y no se encontró qué lo usa |
| Pagos asociados | Opcionales y por fuera del flujo: se registran en el módulo **registerPayment** y solo se **consultan** desde el saneamiento (sección 7) |

---

## 6. Cómo sabe el usuario que hay un saneamiento

No hay notificaciones (ni correos, ni push, ni campana). Todo se ve **en pantalla**:

**1. En el detalle del diagnóstico** ([FE] `VehicleFleetDetail.tsx`):

- Columna **Análisis SI/NO**, con un tooltip que combina «Análisis de diagnóstico: …» y «Solicitud de saneamiento: …» (línea 285 y siguientes).
- Aparece el ícono **$ «Solicitar saneamiento»** (para el cliente) cuando se cumplen las condiciones.
- Candado en la fila (`isBlocked`).

**2. En el módulo «Saneamiento de Vehículos»** ([FE] `VehicleCleanupProcessList.tsx:293-340`). Es un listado de `vehicle_fleet_detail`:

- El FLIT ve **todas las compañías**: `GET vehicle-fleet-detail/listByCleanup/list`, con hasta 1000 registros por página.
- El cliente ve **solo su compañía**: `GET vehicle-fleet-detail/listByCleanupTenant/list`, con los headers `xflit_tenant` y `xflittenant`.
- Columnas: ID Diagnóstico · ID Vehículo · Tipo Documento · Número Documento · Placa · **Análisis** (filtro SI/NO) · **Solicitud de saneamiento** (`isToClean`, con filtro) · Fecha del diagnóstico · Fecha modificación · **Saneado** (`isCleaned`, filtro SI/NO).
- Exportación a Excel `Saneamiento_{timestamp}.xlsx` ([FE] `vehicleCleanupProcess/utils/ExcelVehicleCleanupMapping.ts`).

**3. Toasts** de confirmación al guardar.

### Estados (inferidos de las banderas, no hay enum)

```text
SIN ANÁLISIS ──(FLIT escribe análisis)──► ANALIZADO + BLOQUEADO
   ──(FLIT finaliza diagnóstico)──► DISPONIBLE PARA SOLICITAR
   ──(cliente solicita)──► SOLICITADO (isToClean)
   ──(FLIT marca)──► SANEADO (isCleaned)   ⇄ (FLIT puede desmarcar)
```

> ⚠️ **Filtro duro en el backend.** Los dos listados de saneamiento tienen fijado `vehicle_fleet_id >= 211` ([BE] `infrastructure/persistence/repositories/vehicleFleet/vehicleFleetDetailRepository.ts:391-444`). Los diagnósticos anteriores al id 211 no aparecen nunca. El propio código recomienda pasarlo a una variable de entorno (`API_SERVICE_FLEET_VEHICLE_DEFAULT_ID_INITIAL`).
>
> **El listado no filtra por `isToClean`.** Muestra todas las placas de diagnósticos a partir del 211, y el usuario filtra con las columnas.

---

## 7. Adjuntos

| Pregunta | Respuesta |
|---|---|
| ¿La solicitud de saneamiento admite adjuntos? | **No.** No hay carga de archivos ni en el diálogo ni en el listado |
| ¿El backend de flota almacena documentos del saneamiento? | **No.** No hay entidad, endpoint ni almacenamiento para eso |
| ¿Hay algún documento visible? | Sí, de forma indirecta. Cuando la placa está **Saneada**, aparece **«Ver pagos»** (`VehicleCleanupProcessList.tsx:270-272`) |

«Ver pagos» abre `VehicleFleetPaymentList` ([FE] `vehicleCleanupProcess/ui/components/VehicleFleetPaymentList.tsx`):

- Muestra los pagos del módulo **registerPayment**: `GET payment?...` en [FE] `registerPayment/infrastructure/payment.service.ts:230`.
- Los filtra por la **placa** del vehículo (`plateComplete CONTAINS licensePlate`, línea 58).
- Columnas: concepto, valor, placa, banco, comprobante, entidad, tipo de trámite y fecha de registro.
- El **comprobante de pago** adjunto se ve en `/pdfViewer/{idAttachedPaymentProof}`.

El único adjunto relacionado es entonces el **comprobante de pago**, que se carga en otro módulo y aquí solo se visualiza.

---

## 8. Inventario de endpoints

Base: `{API_SERVICE_FLEET_VEHICLE}/api/v1`. Rutas en [BE] `src/routes/v1/vehicleFleet*.ts`.

| Método y ruta | Uso |
|---|---|
| `POST /vehicle-fleet/upload` | Carga masiva de placas |
| `GET /vehicle-fleet` · `GET /vehicle-fleet/listByTenant/list` | Listado de diagnósticos: FLIT o compañía |
| `GET /vehicle-fleet/:id` · `PUT /vehicle-fleet/:id` | Cabecera; finalizar o desfinalizar |
| `GET /vehicle-fleet/summaryReport/list` | Reporte resumen: placas finalizadas y consultas Ok/Pendiente |
| `GET /vehicle-fleet-detail` · `…/listByTenant/list` · `…/listByVehicleFleetId/:id` | Placas del diagnóstico |
| `PUT /vehicle-fleet-detail/:id` | **Update parcial genérico.** Es el que usan el análisis, la solicitud, el saneado, el bloqueo y el reproceso |
| `POST /vehicle-fleet-detail/formHqRunt/:id` | Llenado manual HQ RUNT (FLIT) |
| `GET /vehicle-fleet-detail/generate-file/generate/:id` | Consolidado Excel |
| `GET /vehicle-fleet-detail/listByCleanup/list` | Listado de saneamiento (FLIT, global) |
| `GET /vehicle-fleet-detail/listByCleanupTenant/list` | Listado de saneamiento (compañía) |
| `GET/PUT /vehicle-fleet-detail-json…` · `POST /vehicle-fleet-detail-json/generateConsolidated` | Respuestas crudas y PDF consolidado |
| `GET /vehicle-fleet-runt` · `GET /vehicle-fleet-simit` · `GET /vehicle-fleet-taxes` · `POST /vehicle-fleet-hq-runt` · `POST /vehicle-fleet-lessee` | Lotes de consulta por fuente |
| `GET payment?…` (servicio registerPayment) | Pagos de la placa saneada |

---

## 9. Riesgos y deudas detectadas

| # | Hallazgo | Impacto |
|---|---|---|
| R1 | **Todas las transiciones van por el `PUT` genérico de `vehicle-fleet-detail/:id`**, y en los routers de flota no se encontró middleware de autorización por rol. La regla «solo FLIT analiza o marca saneado» vive **solo en el front** | Un cliente podría marcar `isCleaned` o reescribir el análisis llamando a la API directamente |
| R2 | Corte fijo `vehicle_fleet_id >= 211` en los listados de saneamiento | Los diagnósticos antiguos son invisibles |
| R3 | `isBlocked` mezcla el bloqueo por análisis con el bloqueo técnico | El estado es ambiguo |
| R4 | La solicitud acepta texto vacío y no se puede corregir ni anular | Puede haber solicitudes sin contenido |
| R5 | Front y back no validan igual: motocarro y `RC` | El front rechaza filas válidas |
| R6 | No hay notificaciones: el cliente no se entera de que su placa fue analizada o saneada si no entra a mirar | Fricción operativa |
| R7 | Sin trazabilidad de quién y cuándo: solo `updatedAt` | No se puede auditar |

---

## 10. Diferencias con FLIT 2.0 (Diagnóstico de la Suite)

Para contexto, ver `saneamiento-en-diagnostico.md`.

| Aspecto | Sistema anterior | FLIT 2.0 (plan v2) |
|---|---|---|
| Quién decide si hay que sanear | Analista FLIT, con un texto libre | Motor de reglas versionado (Con Análisis IA): veredicto `BLOQUEADO` |
| Disparador | Análisis + bloqueo + diagnóstico finalizado | Dictamen `BLOQUEADO`, o deuda mayor que cero en Simple (R-10, por ratificar) |
| Solicitud | El cliente escribe un texto y se marca `isToClean` | Panel informativo de la deuda por fuente, exportable. No hay solicitud (D-08 = S2) |
| Gestión y cierre | FLIT marca «Saneado» y se consultan los pagos | No contemplado |
| Adjuntos | Solo el comprobante de pago (otro módulo) | No contemplado |

Si se quiere conservar en 2.0 el circuito **solicitud → gestión FLIT → saneado → pagos**, hace falta una decisión de producto nueva: hoy D-08 lo deja fuera.
