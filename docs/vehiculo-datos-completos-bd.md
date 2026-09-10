# Cómo se ve un vehículo completo en la base de datos

> Generado: 2026-08-06 · BD `flit_local` @ `localhost:5432` (la de `appsettings.Development.json`)
> · fuente: `psql` sobre el schema `tramites` · 1.306 instancias, 35 con datos de vehículo.
>
> Datos de personas **enmascarados**. Placas y VIN son datos de prueba locales (`@pii:low` según el
> DDL: identifican al vehículo, no a una persona). No se consultaron `qa-flit-db` ni `pdn-flit-db`.

**No existe una tabla de vehículos.** Un vehículo es una proyección de varias tablas: la bolsa
clave/valor `procedure_instance_field_values` (el 95 % del dato), dos columnas denormalizadas en
`procedure_instances`, y un puñado de `jsonb` alrededor (preflight, checklist, eventos, caché).
Este documento arma esa proyección.

---

## 1. Vista compuesta — el vehículo "completo"

Todas las llaves que el sistema sabe producir para un vehículo, con **valores reales tomados de
distintos trámites** de la BD local (ningún trámite real las tiene todas a la vez). Es la ficha
máxima que el aplicativo puede llegar a tener.

```jsonc
{
  // ── Identificación ────────────────────────────────────────────────────────
  "plate":                       "KOK838",              // source: consultation
  "vin":                         "8A18SRDD4NL929842",   // source: consultation
  "vehicle_chassis":             "8A18SRDD4NL929842",   // suele repetir el VIN
  "vehicle_series":              "YQ12-09678",          // solo 9 de 35 trámites lo traen
  "vehicle_engine_number":       "H4MJ759Q063250",

  // ── Características ───────────────────────────────────────────────────────
  "vehicle_brand":               "RENAULT",
  "vehicle_line":                "KANGOO",
  "vehicle_year":                "2022",                // el RUNT lo llama "modelo"
  "vehicle_class":               "CAMIONETA",           // AUTOMOVIL|CAMION|CAMIONETA|CAMPERO|MOTOCICLETA|EXCAVADORA
  "vehicle_body_type":           "PANEL",               // SEDAN|WAGON|FURGON|SUV|HATCH BACK|SIN CARROCERIA|…
  "vehicle_color":               "BLANCO GLACIAL (V)",
  "vehicle_fuel":                "GASOLINA",            // GASOLINA|DIESEL|ELECTRICO|GAS GASOL|GASO ELEC
  "vehicle_engine_displacement": "1598",                // cilindraje; "0" en eléctricos
  "vehicle_axles":               "2",
  "vehicle_passengers":          "2",
  "vehicle_weight":              "1900",                // peso bruto; hasta 21420 en maquinaria
  "vehicle_service":             "Público",             // Particular | Público
  "vehicle_state":               "ACTIVO",              // ACTIVO | REGISTRADO
  "vehicle_registration_date":   "19/04/2016",          // dd/MM/yyyy, solo 2 de 35 trámites

  // ── Copia "congelada" del RUNT que escribe el PREFLIGHT, no la consulta ───
  "vehicle_color_runt":          "BLANCO GLACIAL (V)",
  "vehicle_fuel_runt":           "GASOLINA",

  // ── Organismo de tránsito de matrícula ────────────────────────────────────
  "transit_office_name":         "STRIA TTOyTTE MCPAL FUNZA",
  "transit_office_code":         "5631000",
  "transit_office_city":         "25286",               // DIVIPOLA
  "transit_office_id":           "eeacc872-a522-56bb-9150-70776b094009",
  "transit_office_origen":       "paso_1",

  // ── SOAT ──────────────────────────────────────────────────────────────────
  "soat_estado":                 "vigente",             // NORMALIZADO a minúscula (es gate)
  "soat_vencimiento":            "2026-08-05T00:00:00.000-05:00",
  "soat_aseguradora":            "AXA COLPATRIA SEGUROS SA",
  "soat_poliza":                 "SOAT-MOCK-001",       // source: OCR del PDF, no del RUNT
  "soat_vigencia":               "2026-07-29",          // source: OCR
  "soat_expedicion":             null,                  // el RUNT local nunca lo devolvió

  // ── RTM (revisión técnico-mecánica) ───────────────────────────────────────
  "rtm_estado":                  "VIGENTE",             // CRUDO, en mayúscula (no es gate)
  "rtm_vencimiento":             "2027-06-23T00:00:00.000-05:00",
  "rtm_entidad":                 null,                  // CDA que expide — sin dato en local
  "rtm_numero":                  null,                  // lectura tolerante, sin coincidencia
  "rtm_vigencia":                null,
  "rtm_expedicion":              null,

  // ── Prendas / gravámenes — NINGUNA fila existe en la BD local ─────────────
  "runt_tiene_gravamenes":       "NO",
  "runt_tiene_prendas":          "NO",
  "runt_nombre_acreedor":        null,
  "runt_gravamenes":             null,                  // iría en value_json (ver §5)
  "runt_consulta_fecha":         null                   // fecha de la consulta al RUNT
}
```

Las últimas cinco llaves **no existen en ninguna fila** de la BD local. El código las produce
(`VerifikResultMapper.cs:191-199`, `KyverumRuntVehicleResultMapper.cs:159-176`) pero el mock nunca
ha devuelto un vehículo con gravamen. Ver §5.

---

## 2. Tres vehículos reales, tal cual están en BD

Ninguno inventado: son el `field_values` completo de tres trámites distintos.

### 2.1 Excavadora (maquinaria — sin VIN ni chasis)

```json
{
  "plate": "MC029554",
  "vin": "NA",
  "vehicle_chassis": "NA",
  "vehicle_series": "YQ12-09678",
  "vehicle_class": "EXCAVADORA",
  "vehicle_body_type": "SIN CARROCERIA",
  "vehicle_year": "2015",
  "vehicle_color": "AZUL VERDE",
  "vehicle_color_runt": "AZUL VERDE",
  "vehicle_fuel": "DIESEL",
  "vehicle_fuel_runt": "DIESEL",
  "vehicle_weight": "21420",
  "vehicle_state": "ACTIVO",
  "vehicle_engine_number": "J05ETA45699",
  "vehicle_registration_date": "19/04/2016",
  "transit_office_name": "STRIA TTOyTTE MCPAL FUNZA"
}
```

La maquinaria llega con `"NA"` literal en `vin` y `vehicle_chassis`, se identifica por
`vehicle_series`, y **no trae SOAT ni RTM**: no aplican. Tampoco marca ni línea.

### 2.2 Motocicleta

```json
{
  "plate": "UTI20C",
  "vin": "9FLDJC5Z1DCG17856",
  "vehicle_chassis": "9FLDJC5Z1DCG17856",
  "vehicle_series": "9FLDJC5Z1DCG17856",
  "vehicle_brand": "BAJAJ",
  "vehicle_line": "PULSAR 180 UG",
  "vehicle_class": "MOTOCICLETA",
  "vehicle_body_type": "SIN CARROCERIA",
  "vehicle_year": "2013",
  "vehicle_color": "BLANCO INFINITO",
  "vehicle_fuel": "GASOLINA",
  "vehicle_engine_displacement": "178",
  "vehicle_engine_number": "DJGBVB01237",
  "vehicle_passengers": "2",
  "vehicle_service": "Particular",
  "vehicle_state": "ACTIVO",
  "soat_aseguradora": "SEGUROS GENERALES SURAMERICANA S.A.",
  "soat_vencimiento": "2025-08-03T00:00:00.000-05:00",
  "transit_office_name": "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA"
}
```

SOAT **vencido** (2025) y sin `soat_estado`: el RUNT no reportó estado, así que la llave del gate no
se escribió. Sin `vehicle_axles` ni `vehicle_weight`.

### 2.3 Camión

```json
{
  "plate": "WNK634",
  "vin": "9GDNPR753GB019628",
  "vehicle_chassis": "9GDNPR753GB019628",
  "vehicle_series": "9GDNPR753GB019628",
  "vehicle_brand": "CHEVROLET",
  "vehicle_line": "NPR",
  "vehicle_class": "CAMION",
  "vehicle_body_type": "FURGON",
  "vehicle_year": "2016",
  "vehicle_color": "ROJO STANDARD",
  "vehicle_fuel": "DIESEL",
  "vehicle_engine_displacement": "5193",
  "vehicle_engine_number": "4HK1-376141",
  "vehicle_axles": "2",
  "vehicle_passengers": "2",
  "vehicle_weight": "7500",
  "vehicle_service": "Público",
  "vehicle_state": "ACTIVO",
  "soat_aseguradora": "AXA COLPATRIA SEGUROS SA",
  "soat_vencimiento": "2026-08-07T00:00:00.000-05:00",
  "transit_office_name": "STRIA TTOyTTE MCPAL FUNZA"
}
```

---

## 3. Cómo se ve realmente en la tabla

`tramites.procedure_instance_field_values` — una fila por llave, único por `(instance, field_key)`:

| field_key | value_text | value_json | source |
|---|---|---|---|
| `plate` | `KOK838` | `NULL` | `consultation` |
| `vehicle_brand` | `RENAULT` | `NULL` | `consultation` |
| `soat_estado` | `vigente` | `NULL` | `consultation` |
| `soat_poliza` | `SOAT-MOCK-001` | `NULL` | `ocr` |
| `vehicle_color` | `BLANCO GLACIAL (V)` | `NULL` | `consultation` |
| `vehicle_color_runt` | `BLANCO GLACIAL (V)` | `NULL` | `consultation` |

`source` ∈ `consultation` (RUNT) · `ocr` (lectura del PDF adjunto) · `user` (digitado). En las 1.035
filas de la BD local **`value_json` es NULL en el 100 %**: todo lo persistido hoy es texto plano.

Y el denormalizado en `tramites.procedure_instances` (solo lectura, lo mantiene un trigger):

```json
{
  "id": "ddd0f48f-cb57-4379-97a2-3435b4c19729",
  "tenant_id": "43a4fe6b-019c-45e2-8699-c04c99b9f4ae",
  "procedure_type_id": "019f8195-fed1-770a-98ae-295ed59b53d4",
  "status": "entregado",
  "plate_flow_status": null,
  "plate": "KOK838",
  "vin": "8A18SRDD4NL929842",
  "transit_office_id": "eeacc872-a522-56bb-9150-70776b094009",
  "submitted_at": "2026-07-29T21:55:23.151237-05:00"
}
```

`transit_office_id` solo se puebla al radicar; durante todo el wizard es `null`.

---

## 4. Los `jsonb` alrededor del vehículo

### 4.1 `procedure_instance_preflight_snapshots.checks` — el semáforo

Los *checks* de la consulta **no se guardan en `field_values`**; quedan aquí. Nótese `Source`:
en local el vehículo lo resuelve **`kyverum_runt`**, no Verifik.

```json
[
  { "Key": "estado_vehiculo", "Label": "Estado del vehículo",          "Status": "ok",      "Source": "kyverum_runt", "Message": null },
  { "Key": "soat",            "Label": "SOAT",                          "Status": "fail",    "Source": "kyverum_runt", "Message": "SOAT vencido o no vigente" },
  { "Key": "tecnomecanica",   "Label": "Revisión técnico-mecánica",     "Status": "unknown", "Source": "kyverum_runt", "Message": "Sin información de tecnomecánica" },
  { "Key": "gravamenes",      "Label": "Gravámenes y limitaciones",     "Status": "ok",      "Source": "kyverum_runt", "Message": null },
  { "Key": "simit_comprador", "Label": "SIMIT comprador",               "Status": "unknown", "Source": "verifik_simit", "Message": "Actor sin documento para consultar comparendos" },
  { "Key": "simit_vendedor",  "Label": "SIMIT vendedor",                "Status": "unknown", "Source": "verifik_simit", "Message": "Actor sin documento para consultar comparendos" }
]
```

`Status` ∈ `ok|warn|fail|unknown`. Un gravamen da `warn` (amarillo), no bloquea.

### 4.2 `procedure_instances.checklist_estado` — checklist documental resuelto

```json
{
  "contrato_leasing": true,
  "doc_locatario": true,
  "paz_salvo_locatario": true,
  "declaracion_arrendadora": true
}
```

### 4.3 `procedure_instance_events.payload` — evento `fur_generado`

Es donde queda la huella de qué documentos se generaron con esos datos del vehículo:

```json
{
  "tipo": "fur_generado",
  "payload": {
    "documentos": [
      { "Tipo": "fur",                      "Filename": "fur_TRM-2026-000022.pdf",                      "Sha256": "1337c79e0689e0bd47ceee181e1e34b2f9bfb3ee05cea964aa9ea46ae58798e9" },
      { "Tipo": "compraventa",              "Filename": "compraventa_TRM-2026-000022.pdf",              "Sha256": "c28c45b21b44c0357e1dda71e1377781f2367110d38e026768d3d66fff11db8a" },
      { "Tipo": "certificado_soat_rtm",     "Filename": "certificado_soat_rtm_TRM-2026-000022.pdf",     "Sha256": "cbf28861129cf6a68ac3bfedd883b76f7c33537818509037dc583e7d14a9a17e" },
      { "Tipo": "certificado_rues",         "Filename": "certificado_rues_TRM-2026-000022.pdf",         "Sha256": "88554a999eaf7990fff6055327e32db2dc719062773edc1c34fef9b1080d5e66" },
      { "Tipo": "mandato",                  "Filename": "mandato_TRM_2026_000022.pdf",                  "Sha256": "ebba4d1c277039a4aa38f9be9ad57e5c8dd66742df7ec5976d5be0df81679cef" }
    ]
  }
}
```

### 4.4 `consultation_templates` — la parametrización que dispara la consulta

```json
{
  "code": "RUNT_VEHICLE",
  "name": "RUNT — Consulta vehículo por placa/VIN",
  "entity_scope": "vehicle",
  "is_active": true,
  "external_refs":       { "provider": "verifik", "endpointKey": "runt_vehicle" },
  "request_schema":      { "plate_or_vin": "string" },
  "required_field_keys": ["plate_or_vin"],
  "external_data_source_id": "019f8195-bbd6-75a6-a39a-5395cf964b42"
}
```

Fuentes catalogadas y su TTL de caché: `RUNT 24h` · `SIMIT 24h` · `RNMC 24h` · `RUES 720h` ·
`FASECOLDA 168h` · `RESOLUCIONES 24h` · `FLIT_INTEGRATIONS` sin TTL.

### 4.5 `external_query_cache.payload` — 8 filas, **todas de persona**

```json
{
  "subject_kind": "person",
  "document_type": "CC",
  "document_number": "***485",
  "vehicle_identifier": null,
  "queried_at": "2026-08-01T23:10:06-05:00",
  "expires_at": "2026-08-02T23:10:06-05:00",
  "reuse_count": 0,
  "payload": [
    { "fieldKey": "person_full_name",          "valueText": "***", "valueJson": null },
    { "fieldKey": "person_license_status",     "valueText": "***", "valueJson": null },
    { "fieldKey": "person_has_pending_fines",  "valueText": "***", "valueJson": null },
    { "fieldKey": "person_has_active_license", "valueText": "***", "valueJson": null }
  ]
}
```

**Cero entradas `subject_kind='vehicle'`.** El cache-aside de vehículo se llavea por `plate_or_vin`
(`RunConsultationCommand.cs:158`) y esa llave no existe en ninguna instancia — el wizard escribe
`plate` y `vin` por separado. La caché de vehículo, en local, nunca ha guardado ni servido nada.

### 4.6 `procedure_instance_commercial` — avalúo (25 filas)

```json
{
  "procedure_instance_id": "912fc4a5-95d5-446f-8b2c-d7b4d71c388a",
  "causal": "COMPRAVENTA",
  "valor_venta": 72900000.00,
  "suggested_value": 72900000.00,
  "suggested_source": "fasecolda",
  "value_origin": "suggestion",
  "tasa_impuesto": null,
  "derechos": null,
  "metodo_pago": null
}
```

### 4.7 `procedure_instance_prenda` — **0 filas** (singular, no `_prendas`)

Estructura real en BD:

```json
{
  "id": "uuid", "tenant_id": "uuid", "procedure_instance_id": "uuid",
  "decision": "varchar",           // PrendaDecision
  "estado": "varchar",             // 'vigente' | 'reemplazada'
  "acreedor_nombre": "varchar",
  "acreedor_documento": "varchar",
  "metadata": "jsonb",             // {} por defecto
  "row_version": "bigint",
  "created_at": "timestamptz", "created_by": "uuid",
  "updated_at": "timestamptz", "updated_by": "uuid"
}
```

Vive aparte de `field_values` a propósito: así la decisión de prenda se puede corregir después de
radicar sin tocar la inmutabilidad de la bolsa de campos.

---

## 5. Lo que el código produce y la BD local no tiene

Búsqueda por `field_key ~* 'prenda|gravamen|acreedor|garantia'` → **ninguna fila**. Así se vería un
vehículo **con prenda**, según los mappers (`KyverumRuntVehicleResultMapper.cs:159-176`), única
llave del sistema que usa la columna `value_json`:

```jsonc
// filas en procedure_instance_field_values
{ "field_key": "runt_tiene_gravamenes", "value_text": "SI",                    "value_json": null },
{ "field_key": "runt_tiene_prendas",    "value_text": "SI",                    "value_json": null },
{ "field_key": "runt_nombre_acreedor",  "value_text": "BANCOLOMBIA S.A.",      "value_json": null },
{ "field_key": "runt_consulta_fecha",   "value_text": "06/08/2026",            "value_json": null },
{
  "field_key": "runt_gravamenes",
  "value_text": null,
  "value_json": [                            // camelCase, nulos omitidos
    {
      "idPrenda": 123456,
      "tipoDocumentoAcreedor": "NIT",
      "numeroDocumentoAcreedor": "890903938",
      "nombreAcreedor": "BANCOLOMBIA S.A.",
      "fechaInscripcion": "2024-03-15",
      "estadoPrenda": "VIGENTE"
    }
  ]
}
```

Para verlo con datos reales hay que correr un trámite contra un vehículo efectivamente prendado, o
extender el mock del proveedor.

---

## 6. Cómo regenerar este documento

```bash
PSQL="/c/Program Files/PostgreSQL/16/bin/psql.exe"
export PGPASSWORD=postgres

# ficha completa de un trámite
"$PSQL" -h localhost -p 5432 -U postgres -d flit_local -tAc "
select jsonb_pretty(jsonb_object_agg(field_key, jsonb_build_object('value_text',value_text,'value_json',value_json,'source',source)))
from tramites.procedure_instance_field_values
where procedure_instance_id='<uuid>'
  and field_key ~ '^(vehicle|soat|rtm|runt|plate|vin|transit_office)';"

# universo de llaves y valores reales
"$PSQL" ... -tAc "
select field_key, count(*), string_agg(distinct source,','), string_agg(distinct value_text,' | ')
from tramites.procedure_instance_field_values
where field_key ~ '^(vehicle|soat|rtm|runt|plate|vin|transit_office)' group by 1 order by 1;"

# todos los jsonb del schema
"$PSQL" ... -tAc "
select table_name||'.'||column_name from information_schema.columns
where table_schema='tramites' and data_type in ('jsonb','json') order by 1;"
```
