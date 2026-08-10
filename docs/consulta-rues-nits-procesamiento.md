# Consulta RUES por NIT — fuente, respuestas y hallazgos

> Generado: 2026-08-07 · NITs solicitados: **811011779**, **890903938**, **901900037**.
>
> **Las tres consultas en vivo fallaron con HTTP 403**: el token de Verifik —única fuente RUES viva en
> FLIT— venció el **2026-08-01** (§1).
>
> Pero la BD local **sí conserva consultas RUES reales y completas de dos de los tres NITs**, hechas el
> 2026-07-31 cuando la credencial servía (§4). Con ellas se reconstruye el JSON de la respuesta (§5).
>
> **Sobre el JSON crudo:** FLIT **no persiste la respuesta del proveedor en ninguna parte**. La caché
> guarda el resultado *ya mapeado*. Por eso §5 separa dos cosas que no son lo mismo: el **JSON exacto
> de lo que quedó almacenado** (verificable, literal) y una **reconstrucción** del cuerpo del proveedor
> obtenida invirtiendo el mapper (fiel campo a campo, pero con límites explícitos).
>
> Empresas (personas jurídicas): NIT y razón social **no son PII** de persona natural. El campo
> `rues_representacion_legal` sí puede traer nombres y cédulas de representantes legales; donde
> aparecen, se omiten.

---

## 1. Resultado de la consulta solicitada

| NIT | Petición | Resultado |
|---|---|---|
| 811011779 | `GET /v3/co/rues-complete?documentType=NIT&documentNumber=811011779&category=RM` | **HTTP 403**, cuerpo vacío |
| 890903938 | ídem | **HTTP 403**, cuerpo vacío |
| 901900037 | ídem | **HTTP 403**, cuerpo vacío |

El 403 **no es del RUES ni del NIT**: es de la credencial. Sonda de control con el mismo token contra
otro endpoint del mismo proveedor:

```
runt vehicle-by-plate -> HTTP 403 | body: (vacío)
rues-complete         -> HTTP 403 | body: (vacío)
```

El token configurado en `appsettings.Development.json` (`Verifik:BearerToken`) es un JWT HS256 cuyos
claims lo dicen sin ambigüedad — **decodificado sin la firma; el token no se reproduce aquí**:

| Claim | Valor | Lectura |
|---|---|---|
| `iat` | 1782912168 | emitido **2026-07-01 13:22 UTC** |
| `expiresAt` | 1785590568 | vence **2026-08-01 13:22 UTC** |
| `role` | `administrador` | — |

Hoy (2026-08-07) lleva **7 días vencido**.

**Alcance del impacto, no solo RUES.** Ese mismo token lo comparten cinco proveedores
(`InfrastructureExtensions.cs:331`): `verifik` (vehículo), `verifik_simit`, `verifik_rnmc`,
`verifik_conductor` y `verifik_rues`. En local están todos caídos. No se había notado porque:

- en vehículos el primario de la cadena es `kyverum_runt` y Verifik es solo el *fallback*
  (`ConsultationChainOptions.cs:22`) — las consultas de placa siguen funcionando;
- SIMIT está en `mock` en este ambiente (`Consultations:VerifikSimitMode`);
- RUES es el único que va **directo** a Verifik, sin cadena ni fallback, y con `VerifikRuesMode: real`
  no hay mock que lo tape.

---

## 2. La fuente: quién responde el RUES en FLIT

Hay **tres** implementaciones que mencionan RUES; solo una consulta datos:

| Implementación | Endpoint | Estado | Para qué |
|---|---|---|---|
| **`VerifikRuesConsultationProvider`** (key `verifik_rues`) | `GET {Verifik:BaseUrl}/v3/co/rues-complete?documentType=NIT&documentNumber={nit}&category=RM` | **Activo** — `VerifikRuesMode: real` | **La fuente real.** Devuelve el registro mercantil que alimenta el asistente y el certificado |
| Mock del mismo provider | — | Se activa con `VerifikRuesMode: mock` | Datos canónicos de "EMPRESA DEMO S.A.S." |
| `RuesApiClient` (`Flit.Infrastructure/Rues/`) | `POST /api/v1/document-generator/rues-certificate` | **Deshabilitado** (`Rues:Enabled=false`, y la sección `Rues` ni existe en Development) | Autogenerar el PDF del certificado (RF36). Su propio código lo declara provisional y pendiente de confirmación del Líder Técnico |

Así que **RUES = Verifik v3, siempre**. `category=RM` (registro mercantil) es un parámetro estático,
no configurable. Verifik es un revendedor: la fuente última es Confecámaras/RUES, pero FLIT no la
consulta directamente ni tiene credenciales para hacerlo.

**Quién dispara la consulta:** `POST /api/v1/tramites/instances/{id}/rues-lookup`
(`ConsultationEndpoints.cs:90`) → `RuesPersonLookupHandler` → provider `verifik_rues` con el contexto
`RUES_ACTOR_JURIDICAL`. El NIT se resuelve de `nit` → `actor_document_number` → `documentNumber`
(`VerifikRuesConsultationProvider.cs:234`).

A diferencia del vehículo, **la identidad se consulta siempre en vivo**: la HU #10955 quitó el intento
de HIT de caché *antes* de llamar al proveedor. El resultado fresco sí se cachea después — y esa
caché es justamente la que conserva las respuestas de §4.

---

## 3. Qué hace FLIT con la respuesta

### 3.1 El check

Un solo check, `rues`: `registrationStatus == "ACTIVA"` (case-insensitive) → `ok` y overall **green**;
cualquier otro estado → `warn` y overall **yellow**. Sin empresa (404) → `unknown`; error de
transporte o respuesta ilegible → `error` y overall **red** (bloqueo duro).

**El 403 de §1 cae en esa última rama**: el trámite ve "No fue posible verificar la información en
RUES en este momento", sin distinguir credencial vencida de caída del proveedor.

### 3.2 Las 23 llaves `rues_*`

Todas se persisten en `procedure_instance_field_values` con `source = 'consultation'`, igual que las
del vehículo. Origen de cada una:

| Llave FLIT | Campo del contrato Verifik | Nota |
|---|---|---|
| `rues_razon_social` | `businessName` | Con *fallback* `"Sin razón social"` |
| `rues_estado` | `registrationStatus` | Con *fallback* `"DESCONOCIDO"`; es lo que decide el check |
| `rues_matricula_mercantil` | `registrationNumber` | |
| `rues_camara_comercio` | `chamberCommerce` | |
| `rues_camara_ciudad` | `chamberCity` | |
| `rues_camara_departamento` | `chamberDepartment` | |
| `rues_sigla` | `acronym` | |
| `rues_fecha_matricula` | `enrollmentDate` | |
| `rues_ultimo_ano_renovado` | `lastRenewedYear` | |
| `rues_fecha_renovacion` | `renewalDate` | |
| `rues_fecha_actualizacion` | `lastUpdatedDate` | |
| `rues_direccion` | `commercialAddress` | |
| `rues_municipio` | `companyLocation` | **Ver hallazgo §7.4** |
| `rues_email` | `email` | |
| `rues_id_rm` | `idRm` | |
| `rues_tipo_compania` | `companyType` | |
| `rues_tipo_organizacion` | `organizationType` | |
| `rues_razon_cancelacion` | `reasonForCancellation` | **Ver hallazgo §7.3** |
| `rues_representacion_legal` | `legalRepresentatives.faculty` | Texto libre; puede traer nombres y cédulas |
| `rues_nit` | el del contexto, o `NIT` de la respuesta | El del contexto tiene prioridad |
| `rues_categoria` | **derivada** de `data.category` | `RM` → "Registro Mercantil" |
| `rues_actividad_economica` | **derivada** de `economicActivities` | La CIIU principal (`ciiu_act_econ_pri`), "código - descripción" |
| `rues_actividades_json` | `economicActivities` completo | JSON compacto en `value_text` (no en `value_json`) |

Dos matices del procesamiento:

- **Centinelas.** El RUES devuelve literalmente `"Invalid date"` en fechas que no tiene. `Limpio()`
  descarta ese texto y también `"null"`, `"undefined"` y `"n/a"`, para que no lleguen al PDF
  (HU #11132). Una celda vacía es preferible a un dato falso.
- **Derivadas.** `rues_categoria` y `rues_actividad_economica` **no existen** como campo en la
  respuesta: se calculan. Antes de la HU #11132 esas dos celdas del certificado salían siempre en
  blanco.

### 3.3 El snapshot congelado por NIT (ADR-0037)

Además de las llaves sueltas, la consulta se congela en `rues_snapshots_json`, un único `field_value`
indexado por NIT (`RuesSnapshots.cs`):

```json
{ "<nit>": { "queriedAt": "<ISO>", "fields": { "rues_razon_social": "…", "…": "…" } } }
```

Dos razones, ambas de negocio: el certificado debe dar fe de **lo que se consultó al registrar** el
trámite (no del RUES de hoy), y cada consulta se cobra por llamada. Un trámite puede tener dos
personas jurídicas (comprador y vendedor) y las llaves `rues_*` sueltas solo alcanzan para una; el
documento indexado por NIT resuelve eso sin que el número de llaves crezca con los datos. Queda
inmutable solo porque el trigger de BD bloquea `field_values` en cuanto el trámite sale de edición.

### 3.4 Dónde queda cada cosa

| Destino | Qué guarda | Vida |
|---|---|---|
| `procedure_instance_field_values` | Las 23 llaves `rues_*` sueltas, `source='consultation'` | Del trámite; congeladas al salir de edición |
| `procedure_instance_field_values` → `rues_snapshots_json` | El documento por NIT (ADR-0037) | ídem |
| `tramites.external_query_cache` | El **mismo resultado mapeado** (`fieldKey`/`valueText`/`valueJson`), con `queried_at`, `expires_at` y `reuse_count` | TTL de la fuente RUES: **720 h (30 días)** |
| — | **El JSON crudo del proveedor: en ningún sitio** | — |

Esa última fila es la respuesta corta a "¿se puede recuperar la respuesta exacta?". Otras
integraciones sí conservan el cuerpo original (`procedure_instance_biometric_validations.provider_payload`,
`admin_identity_validations.provider_payload`); RUES no.

---

## 4. Respuestas RUES reales disponibles

No se pudieron consultar hoy, pero `external_query_cache` conserva **cuatro consultas reales** hechas
cuando el token servía. No son mocks: el mock devuelve "EMPRESA DEMO S.A.S." / Bogotá.

| NIT | Empresa | Consultado | Campos | Estado |
|---|---|---|---|---|
| **811011779** | RENTING COLOMBIA S.A.S | 2026-07-31 09:24 | 23 | ACTIVA |
| **890903938** | BANCOLOMBIA S.A. | 2026-07-31 14:04 | 23 | ACTIVA |
| 900179202 | INVERSIONES AUTOPREMIER Y CIA SAS | 2026-07-30 13:57 | 23 | ACTIVA |
| 860059294 | LEASING BANCOLOMBIA S.A. | 2026-07-24 23:33 | 5 | **CANCELADA** |

**Dos de los tres NITs pedidos están cubiertos**, con la consulta completa. El tercero, **901900037,
no aparece en ninguna parte**: ni en la caché, ni en `field_values`, ni en actores. Nunca se ha
consultado en este ambiente.

El caso de LEASING BANCOLOMBIA es el único ejemplo real del camino **no-ACTIVA**: `registrationStatus`
= `CANCELADA` ⇒ check `warn` y overall **yellow** (no bloquea el trámite, solo advierte). Es además
anterior a la HU #11132, por eso trae 5 campos y no 23.

### 4.1 NIT 811011779 — RENTING COLOMBIA S.A.S

| Llave | Valor |
|---|---|
| `rues_razon_social` | `RENTING COLOMBIA S.A.S` |
| `rues_estado` | `ACTIVA` |
| `rues_matricula_mercantil` | `0023338512` |
| `rues_id_rm` | `210023338512` |
| `rues_sigla` | `"RENTING COLOMBIA"` ← **con las comillas dobles dentro del valor** |
| `rues_camara_comercio` | `MEDELLIN PARA ANTIOQUIA` |
| `rues_camara_ciudad` / `rues_camara_departamento` | `Medellín` / `Antioquia` |
| `rues_municipio` | `MEDELLIN PARA ANTIOQUIA` ← **no es un municipio** |
| `rues_fecha_matricula` | `1997-10-31` |
| `rues_fecha_renovacion` / `rues_ultimo_ano_renovado` | `2026-03-20` / `2026` |
| `rues_fecha_actualizacion` | `2026-04-27` |
| `rues_tipo_compania` | `SOCIEDADES POR ACCIONES SIMPLIFICADAS SAS` |
| `rues_tipo_organizacion` | `SOCIEDAD ó PERSONA JURIDICA PRINCIPAL ó ESAL` |
| `rues_categoria` | `Registro Mercantil` |
| `rues_actividad_economica` | `7710 - Alquiler y arrendamiento de vehículos automotores` |
| `rues_razon_cancelacion` | `SOCIEDAD COMERCIAL` ← **empresa ACTIVA (§7.3)** |
| `rues_direccion`, `rues_email`, `rues_representacion_legal` | **vacíos** |

### 4.2 NIT 890903938 — BANCOLOMBIA S.A.

| Llave | Valor |
|---|---|
| `rues_razon_social` | `BANCOLOMBIA S.A, ADEMÁS  PODRÁ GIRAR BAJO LA DENOMINACIÓN BANCO DE COLOMBIA S.A.` (doble espacio incluido, tal cual) |
| `rues_estado` | `ACTIVA` |
| `rues_matricula_mercantil` | `0008396404` |
| `rues_id_rm` | `210008396404` |
| `rues_camara_comercio` | `MEDELLIN PARA ANTIOQUIA` |
| `rues_camara_ciudad` / `rues_camara_departamento` | `Medellín` / `Antioquia` |
| `rues_municipio` | `MEDELLIN PARA ANTIOQUIA` |
| `rues_fecha_matricula` | `1984-11-22` |
| `rues_fecha_renovacion` / `rues_ultimo_ano_renovado` | `2026-03-24` / `2026` |
| `rues_fecha_actualizacion` | `2026-06-23` |
| `rues_tipo_compania` | `SOCIEDAD ANONIMA` |
| `rues_tipo_organizacion` | `SOCIEDAD ó PERSONA JURIDICA PRINCIPAL ó ESAL` |
| `rues_categoria` | `Registro Mercantil` |
| `rues_actividad_economica` | `6412 - Bancos comerciales` |
| `rues_razon_cancelacion` | `SOCIEDAD COMERCIAL` |
| `rues_sigla`, `rues_direccion`, `rues_email`, `rues_representacion_legal` | **vacíos** |

Es la misma acreedora que aparece en las prendas de dos de los vehículos de
[`consulta-runt-nzs920-procesamiento.md`](./consulta-runt-nzs920-procesamiento.md).

---

## 5. El JSON: lo exacto y lo reconstruido

### 5.1 JSON exacto de lo persistido (`external_query_cache.payload`)

Esto **sí es literal**: es el contenido de la columna, tal cual, para NIT 811011779. La entrada de
890903938 tiene la misma forma con sus 23 campos.

```json
[
  { "fieldKey": "rues_razon_social",        "valueJson": null, "valueText": "RENTING COLOMBIA S.A.S" },
  { "fieldKey": "rues_estado",              "valueJson": null, "valueText": "ACTIVA" },
  { "fieldKey": "rues_matricula_mercantil", "valueJson": null, "valueText": "0023338512" },
  { "fieldKey": "rues_camara_comercio",     "valueJson": null, "valueText": "MEDELLIN PARA ANTIOQUIA" },
  { "fieldKey": "rues_sigla",               "valueJson": null, "valueText": "\"RENTING COLOMBIA\"" },
  { "fieldKey": "rues_fecha_matricula",     "valueJson": null, "valueText": "1997-10-31" },
  { "fieldKey": "rues_ultimo_ano_renovado", "valueJson": null, "valueText": "2026" },
  { "fieldKey": "rues_fecha_renovacion",    "valueJson": null, "valueText": "2026-03-20" },
  { "fieldKey": "rues_direccion",           "valueJson": null, "valueText": null },
  { "fieldKey": "rues_municipio",           "valueJson": null, "valueText": "MEDELLIN PARA ANTIOQUIA" },
  { "fieldKey": "rues_categoria",           "valueJson": null, "valueText": "Registro Mercantil" },
  { "fieldKey": "rues_actividad_economica", "valueJson": null, "valueText": "7710 - Alquiler y arrendamiento de vehículos automotores" },
  { "fieldKey": "rues_tipo_organizacion",   "valueJson": null, "valueText": "SOCIEDAD ó PERSONA JURIDICA PRINCIPAL ó ESAL" },
  { "fieldKey": "rues_tipo_compania",       "valueJson": null, "valueText": "SOCIEDADES POR ACCIONES SIMPLIFICADAS SAS" },
  { "fieldKey": "rues_email",               "valueJson": null, "valueText": null },
  { "fieldKey": "rues_id_rm",               "valueJson": null, "valueText": "210023338512" },
  { "fieldKey": "rues_fecha_actualizacion", "valueJson": null, "valueText": "2026-04-27" },
  { "fieldKey": "rues_razon_cancelacion",   "valueJson": null, "valueText": "SOCIEDAD COMERCIAL" },
  { "fieldKey": "rues_representacion_legal","valueJson": null, "valueText": null },
  { "fieldKey": "rues_camara_ciudad",       "valueJson": null, "valueText": "Medellín" },
  { "fieldKey": "rues_camara_departamento", "valueJson": null, "valueText": "Antioquia" },
  { "fieldKey": "rues_actividades_json",    "valueJson": null, "valueText": "[{\"codigo\":\"7710\",\"nombre\":\"ciiu_act_econ_pri\",\"descripcion\":\"Alquiler y arrendamiento de vehículos automotores\"},{\"codigo\":\"\",\"nombre\":\"ciiu_act_econ_sec\",\"descripcion\":\"\"},{\"codigo\":\"\",\"nombre\":\"ciiu3\",\"descripcion\":\"\"},{\"codigo\":\"\",\"nombre\":\"ciiu4\",\"descripcion\":\"\"}]" },
  { "fieldKey": "rues_nit",                 "valueJson": null, "valueText": "811011779" }
]
```

Nótese que la lista de actividades viaja **como cadena JSON dentro de `valueText`**, no en `valueJson`
—la columna que existe justo para eso—. Es coherente con lo observado en la BD: `value_json` está en
NULL en el 100 % de las filas del ambiente.

### 5.2 Reconstrucción del cuerpo del proveedor

Lo siguiente **no es una captura**: es el resultado de invertir el mapper
(`VerifikRuesConsultationProvider.Map`) campo a campo sobre los datos de §5.1. Cada valor es real; la
*estructura* es la que el modelo declara.

```jsonc
// RECONSTRUCCIÓN a partir del resultado mapeado — NO es la respuesta capturada del proveedor.
// NIT 811011779 · consulta original: 2026-07-31 09:24 (-05)
{
  "data": {
    "category": "RM",
    "commercialRegistry": {
      "NIT": "811011779",
      "businessName": "RENTING COLOMBIA S.A.S",
      "registrationStatus": "ACTIVA",
      "registrationNumber": "0023338512",
      "idRm": "210023338512",
      "acronym": "\"RENTING COLOMBIA\"",
      "chamberCommerce": "MEDELLIN PARA ANTIOQUIA",
      "chamberCity": "Medellín",
      "chamberDepartment": "Antioquia",
      "companyLocation": "MEDELLIN PARA ANTIOQUIA",
      "commercialAddress": null,
      "email": null,
      "enrollmentDate": "1997-10-31",
      "lastRenewedYear": "2026",
      "renewalDate": "2026-03-20",
      "lastUpdatedDate": "2026-04-27",
      "companyType": "SOCIEDADES POR ACCIONES SIMPLIFICADAS SAS",
      "organizationType": "SOCIEDAD ó PERSONA JURIDICA PRINCIPAL ó ESAL",
      "reasonForCancellation": "SOCIEDAD COMERCIAL",
      "legalRepresentatives": {
        "faculty": null,
        "legalRepresentatives": "<no recuperable: FLIT descarta la lista>"
      }
    },
    "economicActivities": [
      { "code": "7710", "name": "ciiu_act_econ_pri", "description": "Alquiler y arrendamiento de vehículos automotores" },
      { "code": "",     "name": "ciiu_act_econ_sec", "description": "" },
      { "code": "",     "name": "ciiu3",             "description": "" },
      { "code": "",     "name": "ciiu4",             "description": "" }
    ]
  }
}
```

```jsonc
// RECONSTRUCCIÓN a partir del resultado mapeado — NO es la respuesta capturada del proveedor.
// NIT 890903938 · consulta original: 2026-07-31 14:04 (-05)
{
  "data": {
    "category": "RM",
    "commercialRegistry": {
      "NIT": "890903938",
      "businessName": "BANCOLOMBIA S.A, ADEMÁS  PODRÁ GIRAR BAJO LA DENOMINACIÓN BANCO DE COLOMBIA S.A.",
      "registrationStatus": "ACTIVA",
      "registrationNumber": "0008396404",
      "idRm": "210008396404",
      "acronym": null,
      "chamberCommerce": "MEDELLIN PARA ANTIOQUIA",
      "chamberCity": "Medellín",
      "chamberDepartment": "Antioquia",
      "companyLocation": "MEDELLIN PARA ANTIOQUIA",
      "commercialAddress": null,
      "email": null,
      "enrollmentDate": "1984-11-22",
      "lastRenewedYear": "2026",
      "renewalDate": "2026-03-24",
      "lastUpdatedDate": "2026-06-23",
      "companyType": "SOCIEDAD ANONIMA",
      "organizationType": "SOCIEDAD ó PERSONA JURIDICA PRINCIPAL ó ESAL",
      "reasonForCancellation": "SOCIEDAD COMERCIAL",
      "legalRepresentatives": {
        "faculty": null,
        "legalRepresentatives": "<no recuperable: FLIT descarta la lista>"
      }
    },
    "economicActivities": [
      { "code": "6412", "name": "ciiu_act_econ_pri", "description": "Bancos comerciales" },
      { "code": "6422", "name": "ciiu_act_econ_sec", "description": "Actividades de las compañías de financiamiento" },
      { "code": "6493", "name": "ciiu3",             "description": "Actividades de compra de cartera o factoring" },
      { "code": "",     "name": "ciiu4",             "description": "" }
    ]
  }
}
```

### 5.3 Qué NO se puede reconstruir

| Límite | Por qué |
|---|---|
| La **lista estructurada de representantes legales** (`legalRepresentatives.legalRepresentatives[]`: nombre, tipo y número de documento, rol) | El mapper la descarta; solo conserva `faculty`. Nunca llegó a la BD |
| Cualquier **campo que FLIT no modela** (`signature`, `id`, y lo que el servicio mande de más) | Se ignora al deserializar |
| `null` **vs** campo ausente **vs** cadena vacía | Los tres colapsan al mismo `null` tras `Limpio()` |
| Un valor que fuera un **centinela** (`"Invalid date"`, `"null"`, `"n/a"`) | `Limpio()` lo convirtió en `null`; el original es irrecuperable |
| `category` exacto | Se guardó ya traducido (`RM` → "Registro Mercantil"); la reconstrucción deshace la traducción |

Ninguno de esos límites afecta a los valores mostrados: todos los que aparecen en §5.2 se leyeron
literalmente de la caché.

---

## 6. Diferencias observadas

### 6.1 Entre capturas: el efecto de la HU #11132

| Llave | 860059294 (07-24) | 900179202 (07-30) | 811011779 / 890903938 (07-31) |
|---|---|---|---|
| Nº de campos | 5 | 23 | 23 |
| `rues_fecha_matricula` | — | `2007-10-18` | poblada |
| `rues_actividad_economica` | — | poblada | poblada |
| `rues_categoria` | — | **vacía** | **`Registro Mercantil`** |

La HU #11132 corrigió seis nombres de campo que no existían en el contrato (entre ellos
`infoActivitiesEconomic` en vez de `economicActivities`). La captura del 07-30 ya la tiene aplicada
salvo en `rues_categoria`, que solo aparece poblada desde el 07-31.

**Consecuencia práctica:** los trámites con consulta anterior conservan el snapshot con esas celdas
vacías y, por diseño (ADR-0037), **no se reconsultan**. Su certificado seguirá saliendo incompleto
aunque el código ya esté corregido.

### 6.2 Entre el mock y las respuestas reales

| Campo | Mock | Real (3 capturas completas) |
|---|---|---|
| `commercialAddress` → `rues_direccion` | `"CL 1 D No 20 - 45"` | **vacío en las 3** |
| `email` → `rues_email` | `"contacto@empresademo.co"` | **vacío en las 3** |
| `legalRepresentatives.faculty` → `rues_representacion_legal` | texto con nombramientos | **vacío en 2 de 3** |
| `acronym` → `rues_sigla` | `"EMPRESA DEMO"` | vacío en 2 de 3; en la otra, con comillas dentro |
| `reasonForCancellation` | `""` | `"SOCIEDAD COMERCIAL"` **en las 3** |

Tres muestras ya no son anécdota: **dirección y correo no llegan nunca**. O el servicio no los
devuelve, o los nombres `commercialAddress`/`email` no son los del contrato — la misma clase de
defecto que originó la HU #11132, y que en DEV no se ve porque el mock sí los trae.

`faculty` vacío en Bancolombia y Renting, poblado en Autopremier, sugiere que el bloque de
representación legal depende de la cámara o del tipo de sociedad, no de un error de mapeo.

### 6.3 Entre lo que el servicio manda y lo que FLIT modela

`legalRepresentatives` es un **objeto** con dos miembros: `faculty` (texto de facultades) y
`legalRepresentatives` (lista estructurada de representantes). FLIT solo consume `faculty`; la lista
llega y se descarta — decisión deliberada, documentada como pendiente de layout del PO.

En la captura de Autopremier ese texto libre incluye **nombres completos y cédulas** de dos
representantes legales. Es decir: el dato estructurado que haría falta para el directorio de RL por
compañía y para la validación de identidad del RL **ya viaja en cada consulta**, y se tira; mientras
tanto la misma información entra al sistema como prosa dentro de un campo de texto.

---

## 7. Hallazgos

1. **El token de Verifik está vencido desde el 2026-08-01 y tumba las cinco integraciones que lo
   comparten** (RUES, SIMIT, RNMC, conductor y el *fallback* de vehículo). RUES es el único sin red de
   seguridad: no tiene cadena de proveedores ni mock activo, así que **cualquier consulta RUES en
   local hoy devuelve bloqueo duro**. Renovarlo es configuración, no código.
2. **Una credencial vencida es indistinguible de una caída del proveedor.** Cualquier no-2xx se mapea
   al mismo `ProviderUnavailable()`, con el mismo mensaje "vuelve a intentarlo en unos minutos" — un
   consejo inútil cuando lo que hace falta es renovar el token. Un 401/403 merece una entrada de log
   distinguible; hoy el provider RUES no loguea nada.
3. **`rues_razon_cancelacion` lleva un valor que no es una causal de cancelación.** Las **tres**
   empresas ACTIVAS traen `"SOCIEDAD COMERCIAL"` en `reasonForCancellation`, y el generador lo imprime
   bajo la etiqueta **"Razón Cancelación"** (`RuesCertificatePdfGenerator.cs:105`). El certificado de
   una empresa vigente afirma algo que no es cierto. Con 3 de 3 ya no es un caso aislado: o el campo
   está mal interpretado, o esa fila debe ocultarse cuando el estado es `ACTIVA`.
4. **`rues_municipio` no siempre es un municipio.** En Bancolombia y Renting vale
   `MEDELLIN PARA ANTIOQUIA`, que es el nombre de la **cámara de comercio**, idéntico a
   `rues_camara_comercio`. El campo `companyLocation` del proveedor no es homogéneo entre cámaras. Si
   el certificado o el FUR imprimen eso como "Municipio", están imprimiendo otra cosa.
5. **Dirección y correo nunca llegan** (§6.2). Dos celdas del certificado condenadas al blanco
   mientras el mock las muestra llenas.
6. **La respuesta cruda del proveedor no se guarda en ninguna parte.** Otras integraciones sí
   conservan `provider_payload`; RUES no. Por eso no se puede auditar a posteriori qué mandó el
   servicio, ni reprocesar una consulta vieja cuando se corrige el mapeo — solo reconsultar, que
   cuesta. Un `payload` crudo en la caché resolvería las tres cosas.
7. **El snapshot congelado propaga los defectos del día de la consulta.** Es la decisión correcta para
   el negocio (ADR-0037), pero arreglar el mapeo **no repara los trámites ya consultados**. Si eso
   importa, hace falta una reconsulta deliberada, no un despliegue.
8. **La lista estructurada de representantes legales se descarta en cada consulta** (§6.3), justo
   cuando hay trabajo en curso sobre el directorio de RL por compañía.
9. **Sin consulta RUES, la razón social entra a mano y entra sucia.** En los actores locales,
   `RENTING COLOMBIA S.A.S` convive con `renting`, y una de las filas lo marca como persona
   **natural**. Es el argumento práctico para que la consulta sea obligatoria en persona jurídica.
10. **Dato sucio del origen:** `acronym` de Renting llega como `"RENTING COLOMBIA"` **con las comillas
    dobles dentro del valor**. Si se imprime sin normalizar, salen en el PDF.

---

## 8. Cómo completar este documento con las tres consultas en vivo

Falta únicamente la credencial. Con un `Verifik:BearerToken` vigente en
`services/core-api/src/Flit.Api/appsettings.Development.json`:

```powershell
$cfg = Get-Content 'services\core-api\src\Flit.Api\appsettings.Development.json' -Raw | ConvertFrom-Json
$headers = @{ Authorization = "$($cfg.Verifik.AuthScheme) $($cfg.Verifik.BearerToken)"; Accept = 'application/json' }
foreach ($nit in @('811011779','890903938','901900037')) {
  $url = "$($cfg.Verifik.BaseUrl)/v3/co/rues-complete?documentType=NIT&documentNumber=$nit&category=RM"
  (Invoke-WebRequest -Uri $url -Method Get -Headers $headers -TimeoutSec 120).Content | Out-File "rues-$nit.json" -Encoding utf8
}
```

Eso da el **JSON crudo real**, que es justo lo que hoy no existe. Con él se cierran las preguntas
abiertas: si `commercialAddress` y `email` vienen con otro nombre (§6.2), qué trae exactamente
`reasonForCancellation` (§7.3), y qué manda `companyLocation` frente a `chamberCommerce` (§7.4).

Para verificar el **procesamiento** y no solo la respuesta, el provider es `internal` pero
`Flit.Infrastructure` expone sus internos a `Flit.Infrastructure.Tests`
(`Flit.Infrastructure.csproj:55`): se puede ejercitar `VerifikRuesConsultationProvider` completo con un
`HttpMessageHandler` que devuelva el JSON guardado y volcar checks + llaves `rues_*` — el mismo método
usado para el RUNT en el documento hermano.

### Consultas locales usadas aquí

```bash
PSQL="/c/Program Files/PostgreSQL/16/bin/psql.exe"; export PGPASSWORD=postgres

# Consultas RUES reales conservadas en caché (payload = resultado mapeado)
"$PSQL" -h localhost -p 5432 -U postgres -d flit_local -tAc "
select c.document_number, c.queried_at, c.expires_at, jsonb_pretty(c.payload)
from tramites.external_query_cache c
join tramites.external_data_sources s on s.id = c.external_data_source_id
where s.code='RUES' order by c.queried_at;"

# Llaves rues_* por trámite y snapshot congelado
"$PSQL" ... -tAc "select field_key, value_text from tramites.procedure_instance_field_values
where field_key like 'rues%' order by field_key;"

# Razón social digitada a mano vs. consultada
"$PSQL" ... -tAc "select document_number, full_name, person_type from tramites.procedure_instance_actors
where document_number in ('811011779','890903938','901900037');"
```
