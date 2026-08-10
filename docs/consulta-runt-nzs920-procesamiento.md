# Consultas RUNT por placa — respuestas reales y procesamiento en FLIT

> Generado: 2026-08-06 · consultas **reales** al proveedor primario `kyverum_runt`
> (`POST https://runt.kyverum.com/v1/vehiculos:consultar`), no mocks ni fixtures.
> Complementa a [`vehiculo-datos-completos-bd.md`](./vehiculo-datos-completos-bd.md), que describe la
> proyección del vehículo ya persistida en BD; aquí se sigue el dato desde la respuesta cruda.

**Tres consultas, porque una sola no cubre todas las secciones:**

| Placa | Documento propietario | Qué aporta | Sección |
|---|---|---|---|
| **NZS920** | NIT 890903938 | Caso base. **Sin RTM** (el RUNT no devolvió la sección) y con la variante `garantiasPrendas` | §1–§5 |
| **LCL874** | CC 327\*\*\*\*78 | **Con RTM** (3 revisiones) y con la variante `garantias` — prenda que sí se mapea completa | §6 |
| **YNK04A** | CC 426\*\*\*\*25 | Moto: **sin VIN**, prenda `SI` **sin detalle**, ceros reales en peso/ejes | §7 |

> Documentos de las personas naturales **enmascarados** (Ley 1581); las consultas se ejecutaron con
> el número real. Placas y VIN son `@pii:low` según el DDL: identifican al vehículo, no a una persona.
> Ninguna de las tres respuestas del RUNT incluye nombre ni dirección del propietario.
>
> **El esquema de la respuesta es idéntico en las tres** (mismas 22 secciones de `data`, mismos 50
> campos de `vehiculo`): lo que cambia es qué viene poblado. Ver el diff completo en §7.1.

**Petición enviada** (la que arma `KyverumRuntApiClient.ConsultarVehiculoAsync`, `KyverumRuntApiClient.cs:40`):

```json
{ "placa": "NZS920", "documento": "890903938", "tipoDocumento": "N" }
```

`tipoDocumento` = `N` porque `KyverumRuntDocType.Normalize("NIT") → "N"` (`KyverumRuntDocType.cs:16`).
Respuesta: **HTTP 200**, `ok:true`, `fromCache:false` (consulta viva contra el RUNT).

---

## 1. Respuesta obtenida — NZS920 (JSON completo, sin recortar)

```json
{
  "ok": true,
  "data": {
    "vehiculo": {
      "placa": "NZS920",
      "nombrePais": null,
      "numLicencia": "10033649555",
      "estadoAutomotor": "ACTIVO",
      "tipoServicio": "Particular",
      "clase": "CAMIONETA",
      "marca": "HYUNDAI",
      "linea": "TUCSON HIBRIDA",
      "modelo": "2025",
      "color": "GRIS ECO PERLADO",
      "numSerie": null,
      "numMotor": "G4FTRZ074603",
      "numChasis": "TMAJB811BSJ329151",
      "vin": "TMAJB811BSJ329151",
      "cilindraje": "1598",
      "tipoCarroceria": "WAGON",
      "fechaRegistro": "2025-01-07T08:37:47.000-05:00",
      "gravamenes": "NO",
      "organismoTransito": "STRIA TTOyTTE MCPAL FUNZA",
      "capacidadCarga": null,
      "pesoBruto": "2165",
      "numeroEjes": "2",
      "idAutomotor": 608942143,
      "repotenciado": "NO",
      "idTipoServicio": 1,
      "idClaseVehiculo": 5,
      "diasMatriculado": "576",
      "prendas": "NO",
      "clasificacion": "AUTOMOVIL",
      "esRegrabadoMotor": "NO",
      "esRegrabadoChasis": "NO",
      "esRegrabadoSerie": "NO",
      "esRegrabadoVin": "NO",
      "numRegraChasis": null,
      "numRegraMotor": null,
      "numRegraSerie": null,
      "numRegraVin": null,
      "antiguoClasico": "NO",
      "tipoCombustible": "GASO ELEC",
      "pasajerosTotal": null,
      "pasajerosSentados": "5",
      "vehiculoEnsenanza": "NO",
      "puertas": "5",
      "importacion": 0,
      "fechaExpedLTImportacion": null,
      "fechaVenciLTImportacion": null,
      "seguridadEstado": "NO",
      "verValidaDIAN": false,
      "validacionDIAN": null,
      "mostrarSolicitudes": "SI",
      "tipoMaquinaria": null,
      "subpartida": null,
      "fechaMatricula": null,
      "tarjetaRegistro": null,
      "noIdentificacion": null
    },
    "tipoDocPropietario": "N",
    "datosTecnicos": {
      "capacidadCarga": null,
      "pesoBrutoVehicular": "2165",
      "noEjes": "2",
      "noLlantas": null,
      "alto": null,
      "ancho": null,
      "largo": null,
      "pasajerosTotal": null,
      "pasajerosSentados": "5",
      "rodaje": null,
      "peso": null
    },
    "soat": [
      {
        "origen": "NACIONAL",
        "tipoTarifa": "222",
        "numSoat": "3488487200",
        "fechaExpedicion": "2025-12-20T00:00:00.000-05:00",
        "fechaExpediSoat": "2025-12-20T00:00:00.000-05:00",
        "fechaInicioPoliza": "2026-01-03T00:00:00.000-05:00",
        "fechaVencimSoat": "2027-01-02T00:00:00.000-05:00",
        "razonSocialAsegur": "AXA COLPATRIA SEGUROS SA",
        "estado": "VIGENTE",
        "estadoSoat": "EMITIDA",
        "placa": null,
        "nombrePais": null
      },
      {
        "origen": "NACIONAL",
        "tipoTarifa": "221",
        "numSoat": "40925769",
        "fechaExpedicion": "2025-01-02T00:00:00.000-05:00",
        "fechaExpediSoat": "2025-01-02T00:00:00.000-05:00",
        "fechaInicioPoliza": "2025-01-03T00:00:00.000-05:00",
        "fechaVencimSoat": "2026-01-02T00:00:00.000-05:00",
        "razonSocialAsegur": "SEGUROS GENERALES SURAMERICANA S.A.",
        "estado": "NO VIGENTE",
        "estadoSoat": "EMITIDA",
        "placa": null,
        "nombrePais": null
      }
    ],
    "solicitudes": [
      {
        "noSolicitud": "258229923",
        "fechaSolicitud": "2025-01-04T10:32:32.000-05:00",
        "estado": "AUTORIZADA",
        "tramitesRealizados": "TRÁMITE MATRÍCULA INICIAL, ",
        "entidad": "STRIA TTOyTTE MCPAL FUNZA"
      }
    ],
    "garantias": [],
    "garantiasPrendas": [
      {
        "idPrenda": "2619271",
        "idVehiculoPrenda": "2629946",
        "fechaRegistro": "16/07/2026",
        "tipoDocumentoEntidad": "NIT",
        "numeroDocumentoEntidad": "890903938",
        "entidad": "BANCOLOMBIA S.A.",
        "estado": "Registro de la garantía en el RNGM por parte de RUNT"
      }
    ],
    "limitacionesPropiedad": [],
    "polizaCaucion": {
      "noPoliza": null,
      "estadoPoliza": null,
      "fechaExpedicion": null,
      "fechaVigenciaPoliza": null,
      "noCertificacion": null,
      "estadoCertificado": null
    },
    "responsabilidadCivil": [],
    "responsabilidadCivilHabilitar": true,
    "tarjetaOperacion": {
      "empresaAfiliadora": null,
      "modalidadTransporte": null,
      "modalidadServicio": null,
      "radioAccion": null,
      "fechaExpedicion": null,
      "fechaInicio": null,
      "fechaFin": null,
      "estado": null,
      "nroTarjetaOperacion": null
    },
    "permisosPcr": [],
    "datosBlindaje": {
      "blindado": null,
      "nivelBlindaje": null,
      "nivelBlindajeNumero": null,
      "fechaBlindaje": null,
      "fechaDesblindaje": null,
      "numeroResolucion": null,
      "tipoBlindajeNombre": null,
      "fechaExpedicionCertificado": null,
      "fechaExpedicionCertificadoFormatoWS": null,
      "idDocumentoCertificadoBlindaje": null,
      "autorizacion": null
    },
    "informacionGps": {
      "noIdentificacionSerie": null,
      "empresaHabilitacion": null
    },
    "informacionRepotenciado": {
      "repotenciado": null,
      "fechaRepotenciacion": null,
      "modeloRepotenciado": null
    },
    "desintegracion": {
      "placa": null,
      "desintegrar": null
    },
    "certificadoDesintegracion": {
      "noCertificado": null,
      "estadoCertificado": null,
      "fechaExpedicion": null,
      "entidadDesintegradora": null
    },
    "normalizacion": [
      {
        "deficienciaMatriculaInicial": "NO",
        "vehiculoNormalizado": "NO DISPONIBLE",
        "fecha": null,
        "numeroActoAdministrativo": null,
        "descargaCertificado": null,
        "solicitudNormalizacion": null
      }
    ],
    "certificadoDijin": {
      "noCertificado": null,
      "fechaExpedicion": null,
      "entidadCertificado": null,
      "estadoCertificado": null
    },
    "registroInicial": {
      "noCertificado": null,
      "fechaExpedicion": null,
      "estadoCertificado": null,
      "placaReposicion": null
    },
    "registroInicialInvc": {
      "noCertificado": null,
      "fechaExpedicion": null,
      "estadoCertificado": null,
      "placaReposicion": null
    }
  },
  "fromCache": false
}
```

---

## 2. Qué parte de esta respuesta ve FLIT

`KyverumRuntVehicleResponse.cs` modela **solo lo que consume el mapper**; System.Text.Json descarta
en silencio todo lo demás. De las 22 secciones de `data`, FLIT deserializa **6**:

| Sección de `data` | ¿Modelada? | Dónde |
|---|---|---|
| `vehiculo` | Sí, 20 de 50 campos | `KyverumRuntVehicleResponse.cs:90` |
| `tipoDocPropietario` | Sí | `:34` |
| `datosTecnicos` | Sí, 2 de 11 campos (`pesoBrutoVehicular`, `noEjes`) | `:157` |
| `soat` | Sí, 3 de 12 campos por póliza | `:176` |
| `rtm` | Sí (**ausente en esta respuesta**) | `:196` |
| `garantias` / `garantiasPrendas` | Sí, con nombres que **no coinciden** (ver §3.6) | `:66` |
| `solicitudes`, `limitacionesPropiedad`, `polizaCaucion`, `responsabilidadCivil`, `tarjetaOperacion`, `permisosPcr`, `datosBlindaje`, `informacionGps`, `informacionRepotenciado`, `desintegracion`, `certificadoDesintegracion`, `normalizacion`, `certificadoDijin`, `registroInicial`, `registroInicialInvc` | **No** | — |

Campos del bloque `vehiculo` que llegan y se descartan: `clasificacion`, `numLicencia`,
`fechaRegistro`, `fechaMatricula`, `diasMatriculado`, `puertas`, `idAutomotor`, `idClaseVehiculo`,
`idTipoServicio`, `repotenciado`, `esRegrabado*`, `antiguoClasico`, `vehiculoEnsenanza`,
`seguridadEstado`, `importacion`, `capacidadCarga`, `tipoMaquinaria`, `subpartida`, `validacionDIAN`.

---

## 3. Procesamiento por sección

El mapper es `KyverumRuntVehicleResultMapper.MapVehicle` (`:32`). Produce **exactamente dos cosas**:
4 `ConsultationCheck` (semáforo) y una lista de `HydratedField` (datos). Nada más.

### 3.1 `vehiculo` → datos del vehículo

Cada campo se copia a una llave FLIT con `Add()`, que **omite el campo si viene vacío o null**
(`:201`) — por eso las llaves faltantes no se escriben como cadena vacía.

| Campo RUNT | Llave FLIT | Valor para NZS920 |
|---|---|---|
| `placa` | `plate` | `NZS920` |
| `vin` | `vin` | `TMAJB811BSJ329151` |
| `numChasis` | `vehicle_chassis` | `TMAJB811BSJ329151` |
| `numMotor` | `vehicle_engine_number` | `G4FTRZ074603` |
| `numSerie` | `vehicle_series` | *(null → no se escribe)* |
| `marca` | `vehicle_brand` | `HYUNDAI` |
| `linea` | `vehicle_line` | `TUCSON HIBRIDA` |
| `modelo` | `vehicle_year` | `2025` |
| `color` | `vehicle_color` | `GRIS ECO PERLADO` |
| `clase` | `vehicle_class` | `CAMIONETA` |
| `tipoCarroceria` | `vehicle_body_type` | `WAGON` |
| `tipoCombustible` | `vehicle_fuel` | `GASO ELEC` |
| `cilindraje` | `vehicle_engine_displacement` | `1598` |
| `tipoServicio` | `vehicle_service` | `Particular` |
| `estadoAutomotor` | `vehicle_state` | `ACTIVO` |
| `pasajerosSentados` | `vehicle_passengers` | `5` |
| `pesoBruto` ?? `datosTecnicos.pesoBrutoVehicular` | `vehicle_weight` | `2165` |
| `numeroEjes` ?? `datosTecnicos.noEjes` | `vehicle_axles` | `2` |
| `organismoTransito` | `transit_office_name` | `STRIA TTOyTTE MCPAL FUNZA` |

**Check `estado_vehiculo`** (`:50`): `ACTIVO` (case-insensitive) → `ok`; cualquier otro estado →
`fail`; sin dato → `unknown`. Aquí: **`ok`**.

En Matrícula Inicial ese check se **endurece** después (CF-03, `PreflightCommand.cs:216`): `ACTIVO`
significa "ya matriculado" y produce bloqueo duro 422. En Traspaso, `ACTIVO` es lo esperado.

### 3.2 `tipoDocPropietario` → siembra del vendedor

`N` → `owner_document_type = "NIT"` (`:133`). Es el único campo que se lee al nivel de `data` y no
del vehículo: sirve para precargar el tipo de documento del vendedor en traspaso sin preguntárselo
al operador.

### 3.3 `datosTecnicos` → solo fallback

Se usa exclusivamente si `vehiculo.pesoBruto` / `vehiculo.numeroEjes` vienen vacíos (`:156-157`).
Aquí el bloque `vehiculo` sí los trae, así que `datosTecnicos` no aportó nada. Alto, ancho, largo,
llantas y rodaje no se modelan.

### 3.4 `soat` → check + 3 llaves

Es un **array histórico de pólizas**. El procesamiento tiene dos criterios distintos:

- **Check `soat`** (`:65`): `ok` si **alguna** póliza del array está `VIGENTE`; si ninguna, `fail`;
  array vacío o ausente, `unknown`. Aquí hay una `VIGENTE` (AXA) y una `NO VIGENTE` (Sura) → **`ok`**.
- **Datos hidratados** (`:179`): elige **la primera póliza `VIGENTE`**; si no hay ninguna, la primera
  del array. Aquí gana la de AXA.

| Campo RUNT | Llave FLIT | Valor |
|---|---|---|
| `fechaVencimSoat` | `soat_vencimiento` | `2027-01-02T00:00:00.000-05:00` *(ISO crudo, sin formatear)* |
| `razonSocialAsegur` | `soat_aseguradora` | `AXA COLPATRIA SEGUROS SA` |
| `estado` → `SoatGate.Normalize` | `soat_estado` | **`vigente`** (minúscula) |

`soat_estado` no es informativo: es el **gate de aprobación del OT**. El frontend compara estricto
contra `"vigente"` en minúscula (`lib/tramites/estados.ts`), por eso `SoatGate.Normalize`
(`SoatGate.cs:42`) es obligatorio antes de persistir — escribir el `"VIGENTE"` crudo bloquearía la
aprobación de un trámite correcto (HU #10973).

`numSoat`, `fechaExpediSoat` y `fechaInicioPoliza` **llegan en la respuesta pero no se modelan**
(`KyverumRuntVehicleResponse.cs:176` lo documenta como decisión de la HU #11134). Esas tres celdas
del certificado SOAT/RTM se siguen llenando con el **OCR del PDF** adjunto, no con el RUNT — aunque
en esta respuesta el RUNT sí las trae.

### 3.5 `rtm` → ausente en esta respuesta

La respuesta de NZS920 **no incluye la clave `rtm`** (verificado: `data.Rtm` deserializa a `null`).
Para el caso con RTM real, ver **§6**.

- **Check `tecnomecanica`** (`:80`): lista nula o vacía → **`unknown`**, "Sin información de
  tecnomecánica". No es `fail`: un `unknown` no impide el verde.
- **Llaves `rtm_vencimiento` y `rtm_estado`: no se escriben.**

Cuando sí viene, el criterio es sobre `vigente`: alguna `"SI"` → `ok`; todas `"NO APLICA"` →
`unknown`; alguna `"NO"` → `fail`. Kyverum tampoco entrega número de certificado ni fecha de
expedición de la RTM (HU #11135), ni `fechaMatricula` (aquí llega `null`), que es el insumo de la
regla de antigüedad de la RTM (HU #11136).

### 3.6 `garantias` / `garantiasPrendas` → prenda

Dos fuentes independientes que **no se cruzan**:

**a) Los flags del bloque `vehiculo`** → alimentan el check y dos llaves:

| Campo RUNT | Llave FLIT | Valor |
|---|---|---|
| `gravamenes` | `runt_tiene_gravamenes` | `NO` |
| `prendas` | `runt_tiene_prendas` | `NO` |

**Check `gravamenes`** (`:100`): ambos distintos de `"SI"` → `ok`; alguno `"SI"` → `warn` (amarillo,
nunca bloquea); ambos vacíos → `unknown`. Aquí: **`ok`**.

**b) El detalle de acreedores** (`garantias` + `garantiasPrendas`, unidos en `NormalizeGarantias`,
`:211`) → `runt_nombre_acreedor` (texto) y `runt_gravamenes` (**la única llave del sistema que usa la
columna `value_json`**).

Aquí `garantiasPrendas` trae una garantía real de **BANCOLOMBIA S.A., NIT 890903938, registrada el
16/07/2026 en el RNGM**… y el resultado persistido es:

```json
{ "field_key": "runt_gravamenes", "value_json": "[{\"idPrenda\":2619271}]" }
```

Todo el detalle se pierde. La causa es de nombres: Kyverum envía `entidad`,
`tipoDocumentoEntidad`, `numeroDocumentoEntidad`, `fechaRegistro` y `estado`, mientras el DTO
`KyverumRuntGarantia` (`:66`) espera `acreedor`/`nombreAcreedor`, `tipoDocumentoAcreedor`,
`numeroDocumentoAcreedor`, `fechaInscripcion` y `estadoPrenda`. Ninguno coincide salvo `idPrenda`,
que además llega como string y solo se salva porque `JsonSerializerDefaults.Web` permite leer
números desde texto. Como `idPrenda` sobrevive, el ítem no se descarta y queda un objeto vacío.
Consecuencia en el wizard: `runt_nombre_acreedor` no se escribe y `PrendaForm.tsx:143`
(`buildRuntPrendaSummary`) muestra un ítem sin acreedor, sin NIT y sin fecha. Ver §5.

---

## 4. Resultado del mapper para esta respuesta (verificado, no inferido)

Salida real de `KyverumRuntVehicleResultMapper.MapVehicle` sobre el JSON de §1:

```json
{
  "Provider": "kyverum_runt",
  "Overall": "green",
  "Checks": [
    { "Key": "estado_vehiculo", "Status": "ok",      "Source": "kyverum_runt", "Message": null },
    { "Key": "soat",            "Status": "ok",      "Source": "kyverum_runt", "Message": null },
    { "Key": "tecnomecanica",   "Status": "unknown", "Source": "kyverum_runt", "Message": "Sin información de tecnomecánica" },
    { "Key": "gravamenes",      "Status": "ok",      "Source": "kyverum_runt", "Message": null }
  ]
}
```

`Overall` sigue la regla del dominio: algún `fail` → `red`; algún `warn` → `yellow`; el resto →
`green` (un `unknown` **no** impide el verde). Aquí: **verde**.

Y las **25 llaves** hidratadas, en orden de producción:

| # | field_key | value_text | value_json |
|---|---|---|---|
| 1 | `owner_document_type` | `NIT` | |
| 2 | `plate` | `NZS920` | |
| 3 | `vin` | `TMAJB811BSJ329151` | |
| 4 | `vehicle_year` | `2025` | |
| 5 | `vehicle_brand` | `HYUNDAI` | |
| 6 | `vehicle_line` | `TUCSON HIBRIDA` | |
| 7 | `vehicle_color` | `GRIS ECO PERLADO` | |
| 8 | `vehicle_class` | `CAMIONETA` | |
| 9 | `vehicle_fuel` | `GASO ELEC` | |
| 10 | `vehicle_engine_displacement` | `1598` | |
| 11 | `transit_office_name` | `STRIA TTOyTTE MCPAL FUNZA` | |
| 12 | `vehicle_state` | `ACTIVO` | |
| 13 | `vehicle_service` | `Particular` | |
| 14 | `vehicle_body_type` | `WAGON` | |
| 15 | `vehicle_chassis` | `TMAJB811BSJ329151` | |
| 16 | `vehicle_engine_number` | `G4FTRZ074603` | |
| 17 | `vehicle_passengers` | `5` | |
| 18 | `vehicle_weight` | `2165` | |
| 19 | `vehicle_axles` | `2` | |
| 20 | `runt_tiene_gravamenes` | `NO` | |
| 21 | `runt_tiene_prendas` | `NO` | |
| 22 | `runt_gravamenes` | *(null)* | `[{"idPrenda":2619271}]` |
| 23 | `soat_vencimiento` | `2027-01-02T00:00:00.000-05:00` | |
| 24 | `soat_aseguradora` | `AXA COLPATRIA SEGUROS SA` | |
| 25 | `soat_estado` | `vigente` | |

No se escriben: `vehicle_series`, `rtm_vencimiento`, `rtm_estado`, `runt_nombre_acreedor`.

---

## 5. Dónde queda guardado cada cosa

El resultado se parte en **tres destinos distintos**, ninguno es una "tabla de vehículos" (no existe).

### 5.1 Datos → `tramites.procedure_instance_field_values`

`RunPreflightHandler.UpsertHydratedFields` (`PreflightCommand.cs:199`) escribe **una fila por llave**,
con `form_field_id = NULL` (valor "loose", no atado a un campo de formulario),
`source = 'consultation'` y upsert idempotente por `(instancia, field_key)`.

Tres llaves reciben trato especial (**A4/B4, HU #10673, ADR-0029**, `PreflightCommand.cs:855`):
`vehicle_color`, `vehicle_fuel` y `vehicle_body_type` se guardan **dos veces**:

- `vehicle_color_runt` ← siempre se refresca con lo que acaba de decir el RUNT (snapshot);
- `vehicle_color` ← el valor **efectivo**, el que va al FUR, que **no se pisa** si hay una
  transformación activa (flag `cambio_color`/`cambio_combustible`/`cambio_carroceria` en `"true"`, o
  el efectivo ya difiere del snapshot anterior).

Para NZS920 eso significa: `vehicle_color` y `vehicle_color_runt` = `GRIS ECO PERLADO`,
`vehicle_fuel` y `vehicle_fuel_runt` = `GASO ELEC`, `vehicle_body_type` (+ `_runt`) = `WAGON`.

Además, `RunConsultationHandler` añade `runt_consulta_fecha` (`dd/MM/yyyy` en UTC-5) cuando la
consulta entra por la ruta de template con `entity_scope = "vehicle"`
(`RunConsultationCommand.cs:227`); el preflight del wizard **no** escribe esa llave.

La escritura está protegida por un trigger de BD: si la instancia no está en borrador, el
`check_violation` se traduce a `not_draft` (409) y no se persiste nada.

### 5.2 Semáforo → `tramites.procedure_instance_preflight_snapshots`

Los 4 checks **no van a `field_values`**: se serializan a la columna `checks` (jsonb) junto con
`overall` y `provider` (`PreflightCommand.cs:244`). Para este vehículo, en un traspaso, el snapshot
llevaría además `simit_comprador` y `simit_vendedor`. El `provider` sería `kyverum_runt` (más
`verifik_simit` u otro de comparendos, concatenados por coma).

### 5.3 Organismo de tránsito → llaves derivadas

En traspaso, tras hidratar `transit_office_name`, `AutoBindTransitOfficeForTraspasoAsync`
(`PreflightCommand.cs:910`) busca el OT habilitado de la compañía por ese nombre y añade
`transit_office_id`, `transit_office_code` y `transit_office_city`. Aquí buscaría
**"STRIA TTOyTTE MCPAL FUNZA"**; si la compañía no tiene grant vigente para ese organismo, el paso 1
corta con 422 `TRANSIT_OFFICE_UNAVAILABLE` (HU #11200) antes de seguir.

Ojo: `procedure_instances.transit_office_id` (la columna denormalizada) **sigue null hasta radicar**;
lo que se escribe aquí es el `field_value`.

### 5.4 Prenda → `tramites.procedure_instance_prenda`

La consulta **no crea filas** en esa tabla. Solo deja el insumo (`runt_tiene_prendas`,
`runt_gravamenes`, `runt_nombre_acreedor`) que el paso Prenda del wizard lee vía
`buildRuntPrendaSummary` (`PrendaForm.tsx:143`) para sugerir acreedor y NIT. La fila de
`procedure_instance_prenda` la crea la **decisión del operador**, no el RUNT — vive aparte de
`field_values` a propósito, para poder corregirla después de radicar.

Con la respuesta de NZS920, el formulario mostraría "sin prendas" (flags en `NO`) y un ítem de
detalle vacío, pese a la garantía de Bancolombia que trae el RUNT. Con la de LCL874 mostraría
"prendas: SI" y sugeriría BANCOLOMBIA S.A. / NIT 890903938 correctamente (§6.3).

### 5.5 Caché

En el flujo del wizard (`preflight-preview` → `from-consulta`) **no interviene la caché**
`external_query_cache`: solo la usa `RunConsultationHandler`, y se llavea por `plate_or_vin`, llave
que el wizard no escribe (escribe `plate` y `vin` por separado). Lo que evita la segunda llamada al
RUNT es el `previewToken` en memoria (TTL 30 min, un solo uso, `PreflightPreviewCommand.cs:78`).

---

## 6. Segunda consulta — LCL874 (con RTM y con prenda mapeada)

`{ "placa": "LCL874", "documento": "327****78", "tipoDocumento": "C" }` → **HTTP 200**, `ok:true`,
`fromCache:false`. CHEVROLET NHR 2022, CAMIONETA/FURGON, servicio **Público**, DIESEL, VIN
`9GDNLR770NB016633`, matriculado en **STRIA TTEyTTO BELLO**. Esta respuesta sí trae `rtm`, y su
prenda llega por la otra variante (`garantias`, no `garantiasPrendas`).

### 6.1 La sección `rtm` tal como llegó

```json
"rtm": [
  {
    "fechaExpedicionRvt": "2026-03-11T00:00:00.000-05:00",
    "fechaVencimientoRvt": "2027-03-11T00:00:00.000-05:00",
    "nombreCda": "IVESUR COLOMBIA BARRANQUILLA",
    "estadoRvt": "APROBADA",
    "tipoRevision": "REVISION TECNICO-MECANICO",
    "vigente": "SI",
    "numeCerti": "188327294",
    "numeroPlaca": "LCL874",
    "informacionConsistente": "SI",
    "url": "2239ba08-499b-44aa-887e-237a2c54cb53"
  },
  {
    "fechaExpedicionRvt": "2025-03-12T00:00:00.000-05:00",
    "fechaVencimientoRvt": "2026-03-12T00:00:00.000-05:00",
    "nombreCda": "IVESUR COLOMBIA BARRANQUILLA",
    "estadoRvt": "APROBADA",
    "tipoRevision": "REVISION TECNICO-MECANICO",
    "vigente": "NO",
    "numeCerti": "180151310",
    "numeroPlaca": "LCL874",
    "informacionConsistente": "SI",
    "url": null
  },
  {
    "fechaExpedicionRvt": "2024-03-12T00:00:00.000-05:00",
    "fechaVencimientoRvt": "2025-03-12T00:00:00.000-05:00",
    "nombreCda": "IVESUR COLOMBIA BARRANQUILLA",
    "estadoRvt": "APROBADA",
    "tipoRevision": "REVISION TECNICO-MECANICO",
    "vigente": "NO",
    "numeCerti": "172361018",
    "numeroPlaca": "LCL874",
    "informacionConsistente": "SI",
    "url": null
  }
]
```

Es un **histórico de revisiones**, la más reciente primero. Cada ítem trae 10 campos.

### 6.2 Cómo lo procesa FLIT

**De esos 10 campos, el DTO `KyverumRuntRtm` (`KyverumRuntVehicleResponse.cs:196`) modela 3:**
`vigente`, `estadoRvt` y `fechaVencimientoRvt`. Los otros 7 se descartan en la deserialización
(verificado: el objeto materializado solo conserva esos tres).

| Campo RUNT | ¿Modelado? | Uso |
|---|---|---|
| `vigente` | Sí | Único insumo del check y del estado |
| `fechaVencimientoRvt` | Sí | `rtm_vencimiento` |
| `estadoRvt` | Sí | Deserializado pero **nunca leído** por el mapper |
| `numeCerti`, `fechaExpedicionRvt`, `nombreCda`, `tipoRevision`, `numeroPlaca`, `informacionConsistente`, `url` | **No** | Se pierden |

**Check `tecnomecanica`** (`KyverumRuntVehicleResultMapper.cs:80`) — evalúa **todo el array**, en este
orden: alguna `vigente = "SI"` → `ok`; todas `"NO APLICA"` → `unknown`; alguna `"NO"` → `fail`; resto
→ `unknown`. Aquí la primera está vigente, así que el histórico vencido **no ensucia** el resultado:
**`ok`**.

**Datos hidratados** (`:192`) — elige la **primera revisión con `vigente = "SI"`**; si ninguna, la
primera del array (por eso un vehículo con solo revisiones vencidas hidrata la más reciente):

| Campo RUNT | Llave FLIT | Valor |
|---|---|---|
| `fechaVencimientoRvt` | `rtm_vencimiento` | `2027-03-11T00:00:00.000-05:00` *(ISO crudo)* |
| `vigente` → `MapVigencia` | `rtm_estado` | **`VIGENTE`** |

`MapVigencia` (`:267`) traduce `SI`/`NO`/`NO APLICA` → `VIGENTE`/`NO VIGENTE`/`NO APLICA`, en
**mayúscula**, para que el certificado SOAT/RTM (HU #10856) muestre lo mismo con cualquier proveedor.
Ojo con la asimetría frente al SOAT: `soat_estado` se normaliza a **minúscula** porque es un gate
comparado de forma estricta por el frontend; `rtm_estado` es solo informativo y va en mayúscula. No
son intercambiables.

Las llaves `rtm_entidad` (el CDA), `rtm_numero` y `rtm_expedicion` que el certificado sabe pintar
**no se producen aquí**: siguen dependiendo del OCR del PDF, aunque el RUNT acaba de entregar
`nombreCda = "IVESUR COLOMBIA BARRANQUILLA"`, `numeCerti = "188327294"` y
`fechaExpedicionRvt = 2026-03-11`.

### 6.3 La prenda, por la variante que sí funciona

```json
"garantias": [
  {
    "tipoDocumentoAcreedor": "NIT",
    "numeroDocumentoAcreedor": "890903938",
    "acreedor": "BANCOLOMBIA S.A.",
    "fechaInscripcion": "05/08/2026",
    "confecamaras": "SI",
    "patrimonioAutonomo": null
  }
],
"garantiasPrendas": []
```

Aquí el vehículo trae `prendas: "SI"` y la garantía llega en `garantias` con **los nombres que el DTO
espera**. Resultado: check `gravamenes` = `warn` ("gravámenes: NO · prendas: SI"), `overall` =
**yellow**, y el detalle se persiste completo:

```json
{ "field_key": "runt_nombre_acreedor", "value_text": "BANCOLOMBIA S.A." }
{ "field_key": "runt_gravamenes", "value_json":
  "[{\"tipoDocumentoAcreedor\":\"NIT\",\"numeroDocumentoAcreedor\":\"890903938\",\"nombreAcreedor\":\"BANCOLOMBIA S.A.\",\"fechaInscripcion\":\"05/08/2026\"}]" }
```

Compárese con NZS920 (§3.6), donde la misma acreedora llegó por `garantiasPrendas` y se persistió
como `[{"idPrenda":2619271}]`. **Las dos variantes tienen formas distintas y el DTO solo modela una.**
`confecamaras` y `patrimonioAutonomo` tampoco se modelan.

### 6.4 Resultado del mapper (verificado)

```json
{
  "Provider": "kyverum_runt",
  "Overall": "yellow",
  "Checks": [
    { "Key": "estado_vehiculo", "Status": "ok",   "Message": null },
    { "Key": "soat",            "Status": "ok",   "Message": null },
    { "Key": "tecnomecanica",   "Status": "ok",   "Message": null },
    { "Key": "gravamenes",      "Status": "warn", "Message": "El vehículo tiene gravámenes o prendas (gravámenes: NO · prendas: SI)" }
  ]
}
```

**28 llaves hidratadas** (3 más que NZS920: `vehicle_series`, `rtm_vencimiento`, `rtm_estado`, más
`runt_nombre_acreedor`; y sin las que este vehículo sí trae de otra forma). Diferencias notables
frente al caso anterior:

| field_key | Valor | Nota |
|---|---|---|
| `owner_document_type` | `CC` | `tipoDocPropietario: "C"` → `CC` (`:253`) |
| `vehicle_series` | `9GDNLR770NB016633` | NZS920 traía `numSerie: null` |
| `vehicle_service` | `Público` | con tilde, tal cual el RUNT |
| `vehicle_weight` | `4100` | del bloque `vehiculo`; `datosTecnicos` traía `"4100 Kgs"` |
| `soat_vencimiento` | `2027-03-10T00:00:00.000-05:00` | 5 pólizas en el array, gana la única `VIGENTE` |
| `rtm_vencimiento` | `2027-03-11T00:00:00.000-05:00` | |
| `rtm_estado` | `VIGENTE` | |

El detalle del peso importa: `datosTecnicos.pesoBrutoVehicular` llega **con unidades**
(`"4100 Kgs"`, `capacidadCarga: "1650 Kgs"`) mientras el bloque `vehiculo` da el número limpio
(`"4100"`). Como `datosTecnicos` es solo *fallback* (`:156`), un vehículo al que le falte
`vehiculo.pesoBruto` acabaría con `"4100 Kgs"` en `vehicle_weight` y esa cadena iría al FUR.
`capacidadCarga` no se mapea en ninguno de los dos casos.

---

## 7. Tercera consulta — YNK04A (moto): sin VIN y prenda sin detalle

`{ "placa": "YNK04A", "documento": "426****25", "tipoDocumento": "C" }` → **HTTP 200**, `ok:true`,
`fromCache:false`. YAMAHA T-110E 2005, **MOTOCICLETA**, sin carrocería, roja, gasolina 110 cc,
matriculada en **STRIA TTEyTTO ENVIGADO** desde 2004 (7.981 días). 5 SOAT y 5 RTM en el histórico.

### 7.1 ¿Trajo campos nuevos?

**No: el esquema es idéntico en las tres consultas.** Comparando todas las rutas de claves de las
tres respuestas (listas colapsadas), el conjunto de YNK04A **no aporta ni una ruta nueva**; solo le
faltan las de los ítems de prenda, porque aquí ambos arrays vienen vacíos. Es decir: `data` siempre
trae las mismas 22 secciones y el bloque `vehiculo` los mismos 50 campos — lo que cambia es cuáles
vienen poblados.

Lo que sí aporta son **valores y combinaciones nuevas**, y varias tocan el procesamiento:

| # | Novedad | Qué implica en FLIT |
|---|---|---|
| 1 | **`vin: null`** (la moto se identifica por `numChasis`) | `Add()` omite el campo vacío ⇒ **la llave `vin` no se escribe**. Solo queda `vehicle_chassis = 9FK5GE11841743153`. Y como la columna denormalizada `procedure_instances.vin` la puebla un trigger *desde esa llave* (`47-tramites-campos-busqueda.sql:73`), el trámite queda **sin VIN también en la columna de búsqueda** |
| 2 | **`prendas: "SI"` con `garantias: []` y `garantiasPrendas: []`** | Tercer patrón distinto de prenda en tres consultas. El check sale `warn` y `runt_tiene_prendas = SI`, pero **no hay `runt_nombre_acreedor` ni `runt_gravamenes`**: el wizard avisa de la prenda sin poder decir de quién |
| 3 | **`clasificacion: "MOTO"`** (antes `AUTOMOVIL` en ambas) | Se descarta igual (§2). Confirma que `clasificacion` es un vocabulario propio del RUNT, distinto de `clase` (`MOTOCICLETA`), que es el que usa el resolver de plantilla del FUR |
| 4 | **`pesoBruto: "0"`, `numeroEjes: "0"`** | Son ceros **reales**, no ausencias: `Add()` los considera valores válidos ⇒ `vehicle_weight = "0"` y `vehicle_axles = "0"` se persisten y llegan al FUR tal cual |
| 5 | **`capacidadCarga: "0 KILO"`** | Tercera forma del mismo dato en tres consultas: `null` (NZS920), `"1650 Kgs"` (LCL874), `"0 KILO"` (aquí). Refuerza el riesgo de §6.4: `datosTecnicos` mezcla número y unidad en la misma cadena, y es *fallback* de `vehicle_weight` |
| 6 | **`pasajerosTotal: "0"`** | Primer valor no nulo en las tres consultas (en `vehiculo` y en `datosTecnicos`). No está modelado; FLIT usa `pasajerosSentados` (`2`) |
| 7 | **`numSerie: null`** | Sin `vehicle_series`, como NZS920 (LCL874 sí lo traía) |
| 8 | **`numSoat: "3308005953677000"`** (16 dígitos) | Hoy da igual porque no se modela, pero si se modelara (hallazgo §8.3) **debe ser `string`**: no cabe en un `int` y no es un número que se opere |
| 9 | **`nombreCda: " CENTRO DE DIAGNOSTICO AUTOMOTOR EL DIAMANTE"`** | Dato sucio del RUNT: espacio inicial. Relevante solo si se decide mapear el CDA — habría que hacer `Trim()` |
| 10 | Aseguradoras y CDAs no vistos: `LA PREVISORA`, `SEGUROS DEL ESTADO S.A.`, `HDI SEGUROS COLOMBIA S.A.`; `tipoTarifa: "120"` (tarifa de moto) | Ninguno se valida contra catálogo: `soat_aseguradora` se persiste como texto libre |

Y una confirmación útil: `limitacionesPropiedad`, `responsabilidadCivil`, `permisosPcr`,
`polizaCaucion`, `tarjetaOperacion`, `datosBlindaje`, `informacionGps`, `desintegracion`,
`certificadoDijin` y `registroInicial` han venido **vacías o en null en las tres consultas**. No hay
evidencia todavía de cómo se ven pobladas, así que no se puede afirmar que FLIT esté ignorando datos
útiles ahí — solo que no las lee.

### 7.2 La sección `rtm` (5 revisiones)

```json
"rtm": [
  {
    "fechaExpedicionRvt": "2025-08-26T00:00:00.000-05:00",
    "fechaVencimientoRvt": "2026-08-26T00:00:00.000-05:00",
    "nombreCda": "CENTRO DE DIAGNOSTICO AUTOMOTOR BELLO",
    "estadoRvt": "APROBADA",
    "tipoRevision": "REVISION TECNICO-MECANICO",
    "vigente": "SI",
    "numeCerti": "183478098",
    "numeroPlaca": "YNK04A",
    "informacionConsistente": "SI",
    "url": "17617d46-fa85-4cb2-9531-92f435f3a8a2"
  }
  // … 4 revisiones más, todas `vigente: "NO"` (2016, 2016, 2014, 2013)
]
```

Mismo procesamiento que §6.2: gana la primera `vigente = "SI"` ⇒ `rtm_vencimiento = 2026-08-26…` y
`rtm_estado = VIGENTE`; el check `tecnomecanica` sale **`ok`**. Un detalle que aquí se ve mejor: en
la revisión de 2016 `informacionConsistente` llega `null` mientras el resto trae `"SI"` — otro campo
que FLIT no modela y que el RUNT no garantiza.

### 7.3 Resultado del mapper (verificado)

```json
{
  "Provider": "kyverum_runt",
  "Overall": "yellow",
  "Checks": [
    { "Key": "estado_vehiculo", "Status": "ok",   "Message": null },
    { "Key": "soat",            "Status": "ok",   "Message": null },
    { "Key": "tecnomecanica",   "Status": "ok",   "Message": null },
    { "Key": "gravamenes",      "Status": "warn", "Message": "El vehículo tiene gravámenes o prendas (gravámenes: NO · prendas: SI)" }
  ]
}
```

**25 llaves hidratadas.** Las que se comportan distinto respecto a las dos consultas anteriores:

| field_key | YNK04A | NZS920 | LCL874 |
|---|---|---|---|
| `vin` | **no se escribe** | `TMAJB811BSJ329151` | `9GDNLR770NB016633` |
| `vehicle_chassis` | `9FK5GE11841743153` | igual al VIN | igual al VIN |
| `vehicle_series` | no se escribe | no se escribe | `9GDNLR770NB016633` |
| `vehicle_class` | `MOTOCICLETA` | `CAMIONETA` | `CAMIONETA` |
| `vehicle_body_type` | `SIN CARROCERIA` | `WAGON` | `FURGON` |
| `vehicle_weight` | **`0`** | `2165` | `4100` |
| `vehicle_axles` | **`0`** | `2` | `2` |
| `runt_tiene_prendas` | `SI` **sin detalle** | `NO` (con detalle mutilado) | `SI` con detalle completo |
| `runt_nombre_acreedor` | no se escribe | no se escribe | `BANCOLOMBIA S.A.` |
| `rtm_estado` / `rtm_vencimiento` | `VIGENTE` / `2026-08-26…` | no se escriben | `VIGENTE` / `2027-03-11…` |

Resto igual en forma: `owner_document_type = CC`, `plate`, marca/línea/año/color, combustible,
cilindraje, `transit_office_name`, `vehicle_state = ACTIVO`, `vehicle_service = Particular`,
`vehicle_passengers = 2`, `soat_estado = vigente`, `soat_vencimiento`, `soat_aseguradora`.

---

## 8. Hallazgos (de las tres consultas)

1. **`garantias` y `garantiasPrendas` NO tienen la misma forma, y el DTO solo modela la primera.**
   En LCL874 la garantía llega por `garantias` con
   `acreedor`/`numeroDocumentoAcreedor`/`fechaInscripcion` y se persiste completa (§6.3); en NZS920
   llega por `garantiasPrendas` con `entidad`/`numeroDocumentoEntidad`/`fechaRegistro`/`estado` y
   queda reducida a `[{"idPrenda":2619271}]` (§3.6) — misma acreedora, Bancolombia, en ambos casos.
   El comentario del DTO ("misma forma de ítem", `KyverumRuntVehicleResponse.cs:54`) es incorrecto.
   Arreglo directo: aceptar esos alias al deserializar (el frontend ya lee `estado` como alias,
   `PrendaForm.tsx:135`).
2. **La señal de prenda y su detalle son independientes, y las tres consultas dan tres combinaciones
   distintas.** NZS920: flag `NO` + garantía en `garantiasPrendas` (check verde, detalle mutilado).
   LCL874: flag `SI` + garantía en `garantias` (todo correcto). YNK04A: flag `SI` + **ambos arrays
   vacíos** (avisa de la prenda sin poder decir de quién). El check `gravamenes` solo mira los flags,
   así que en el primer caso el trámite avanza sin señal de prenda pese a la garantía registrada en
   el RNGM. Hay que decidir la regla: ¿manda el flag, manda el detalle, o cualquiera de los dos?
3. **Kyverum sí entrega los datos del certificado SOAT y RTM que FLIT da por inexistentes.** Las HUs
   #11134/#11135 concluyeron —sobre fixtures que no los traían— que este proveedor no reporta número
   de póliza/certificado ni fechas de expedición, y por eso esas celdas dependen del OCR del PDF. Las
   tres consultas lo desmienten: SOAT trae `numSoat`, `fechaExpediSoat` y `fechaInicioPoliza` en las
   tres; y la RTM trae `numeCerti`, `fechaExpedicionRvt`, `nombreCda` y `tipoRevision` en las dos que
   la incluyen (§6.2, §7.2). Modelar esos campos permitiría llenar `rtm_entidad`, `rtm_numero`,
   `rtm_expedicion`, `soat_poliza` y `soat_expedicion` desde la fuente oficial en vez del OCR. Al
   hacerlo, `numSoat` debe ser `string`: en YNK04A tiene 16 dígitos.
4. **Un vehículo puede llegar sin VIN y FLIT se queda sin la llave.** YNK04A (moto de 2005) trae
   `vin: null` y solo `numChasis`: no se escribe el field value `vin` y, por el trigger de
   denormalización (`47-tramites-campos-busqueda.sql:73`), `procedure_instances.vin` también queda
   null. La búsqueda y cualquier regla que dependa del VIN se apoyan solo en la placa. Concuerda con
   lo ya observado en maquinaria, que llega con `"NA"` literal
   ([`vehiculo-datos-completos-bd.md`](./vehiculo-datos-completos-bd.md) §2.1).
5. **`clasificacion` se descarta, y es un vocabulario propio.** `AUTOMOVIL` en los dos primeros,
   `MOTO` en la moto — distinto de `clase` (`CAMIONETA`/`MOTOCICLETA`), que es sobre el que trabaja
   el resolver de plantilla del FUR (`VehicleClassificationFurResolver.cs:29`). No es un error —el
   catálogo `vehicle_classification_fur` está construido sobre `vehicle_class`— pero conviene saber
   que el RUNT da una segunda clasificación que no se usa.
6. **`fechaMatricula` llega null en las tres** (la fecha real está en `fechaRegistro`, que no se
   modela). La regla de antigüedad de la RTM (HU #11136) sigue sin insumo con este proveedor, y
   `vehicle_registration_date` no se puebla.
7. **`datosTecnicos` mezcla número y unidad, y es *fallback* de `vehicle_weight`.** Tres consultas,
   tres formatos: `null`, `"4100 Kgs"`, `"0 KILO"`. Mientras el bloque `vehiculo` traiga el peso no
   pasa nada, pero el día que falte se persistiría la cadena con unidad y esta iría al FUR (§6.4).

---

## 9. Cómo reproducir

```powershell
$cfg = Get-Content 'services\core-api\src\Flit.Api\appsettings.Development.json' -Raw | ConvertFrom-Json
# NZS920 (NIT) sin RTM · LCL874 (CC) con RTM · YNK04A (CC) moto sin VIN
$body = @{ placa='NZS920'; documento='890903938'; tipoDocumento='N' } | ConvertTo-Json -Compress
Invoke-WebRequest -Uri "$($cfg.ImprontaRunt.BaseUrl)/v1/vehiculos:consultar" -Method Post `
  -Headers @{ Authorization = "Bearer $($cfg.ImprontaRunt.ApiKey)"; Accept='application/json' } `
  -ContentType 'application/json' -Body $body -TimeoutSec 120
```

Los volcados del mapper (§4, §6.4 y §7.3) se obtuvieron deserializando cada respuesta con
`new JsonSerializerOptions(JsonSerializerDefaults.Web)` y llamando a
`KyverumRuntVehicleResultMapper.MapVehicle`, desde un test temporal en
`Flit.Tramites.Application.Tests` (ya eliminado; el patrón está en
`KyverumRuntVehicleResultMapperTests.cs`).
