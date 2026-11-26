# Guía de integración: API de liquidación de impuesto vehicular

Esta guía es para el equipo del sistema que consume la API de FLIT. Explica qué configurar de su lado, qué contratos usa cada llamada y el paso a paso para:
1. dar de alta una empresa;
2. pedir liquidaciones;
3. recibir el resultado por webhook;
4. descargar los recibos.

El contrato normativo completo está en [04-api.md](04-api.md).

## 1. Resumen

```text
Sistema integrador                         API FLIT
──────────────────                         ────────
1. Alta de la empresa ───────────────────▶ POST /api/v1/integracion/empresas   (Bearer INTEGRATION_TOKEN)
   ◀─── api_key + webhook_secret (las mismas en cada llamada con ese NIT)

2. Consulta de liquidación ──────────────▶ POST /api/v1/consultas               (X-API-Key)
   ◀─── 202 { id, status: "queued" }

3. Aviso del resultado ◀────────────────── POST {callbackUrl}                   (firmado con webhook_secret)
   ───▶ 2xx en < 10 s

4. Recibos PDF ──────────────────────────▶ GET /api/v1/files/{fileId}/download  (X-API-Key)
```

Hay tres credenciales, cada una con un uso distinto:

| Credencial | Quién la emite | Para qué | Cómo viaja |
| --- | --- | --- | --- |
| `INTEGRATION_TOKEN` | FLIT, una para el sistema integrador | solo para crear empresas | `Authorization: Bearer <token>` |
| `api_key` | la API, una por empresa; sale del alta | consultas, resultados y PDF de esa empresa | `X-API-Key: <api_key>` |
| `webhook_secret` | la API, uno por empresa; sale del alta | verificar que un aviso viene de FLIT | no se envía: con él se recalcula `X-Flit-Signature` |

## 2. Configuración del lado del integrador

### 2.1 Variables de entorno

| Variable (sugerida) | Ejemplo | Obligatoria | Notas |
| --- | --- | --- | --- |
| `FLIT_API_BASE_URL` | `https://api-flit.ejemplo.com` | sí | Base de la API, sin `/` final. La entrega FLIT por ambiente (DEV, QA, producción) |
| `FLIT_INTEGRATION_TOKEN` | `kQ2v…` (≥ 32 caracteres) | solo si el sistema crea empresas | Secreto **crítico**: con él y un NIT se obtienen las credenciales de cualquier empresa. Lo entrega FLIT por un canal seguro |
| `FLIT_API_KEY` | `AbCdEfGh…` (43 caracteres) | sí, una por empresa | Secreto. Sale de la respuesta del alta (paso 1) |
| `FLIT_WEBHOOK_SECRET` | `whsec_…` | sí, si usa webhook | Secreto, uno por empresa. Sale de la respuesta del alta |
| `FLIT_CALLBACK_URL` | `https://integrador.ejemplo.com/webhooks/flit` | sí, si usa webhook | Endpoint propio que recibe los avisos |
| `FLIT_WEBHOOK_TOLERANCE_SECONDS` | `300` | no | Antigüedad máxima aceptada de `X-Flit-Timestamp` |
| `FLIT_HTTP_TIMEOUT_SECONDS` | `30` | no | Timeout de las llamadas a la API |

Si el sistema maneja varias empresas, la `api_key` y el `webhook_secret` no van en variables de entorno. Se guardan **por empresa** en un almacén de secretos (Vault, AWS Secrets Manager, Azure Key Vault o una tabla cifrada), con la empresa como llave.

### 2.2 Requisitos del endpoint de webhook (`FLIT_CALLBACK_URL`)

- **Esquema y host:** `https` con un certificado válido, publicado en internet. No sirven `localhost`, `*.local`, `*.internal` ni IPs privadas.
- **Puerto:** 443, 8443 o uno entre 1024 y 65535.
- **Forma de la URL:** como máximo 2048 caracteres, sin usuario ni contraseña (`https://u:p@…`) y sin fragmento (`#…`). Puede llevar query (`?ref=…`): llega tal cual en el aviso.
- **Método y respuesta:** acepta `POST` con `Content-Type: application/json` y responde cualquier `2xx` en **menos de 10 segundos**. Conviene responder primero y procesar después.
- **Redirecciones:** no redirige (`3xx`). FLIT no sigue redirecciones y las cuenta como falla.
- **Firewall:** si tiene allowlist de IPs, pida a FLIT la IP de salida del worker del ambiente.

### 2.3 Buenas prácticas

- Guarde las credenciales en el almacén de secretos **en el mismo momento** en que recibe la respuesta del alta. Si las pierde, repita el alta con el mismo NIT: devuelve las mismas.
- Si una respuesta trae `api_key_rotated: true`, reemplace la clave guardada de esa empresa en todos los sistemas: la anterior ya no funciona.
- Guarde `FLIT_INTEGRATION_TOKEN` solo en el servicio que hace el alta, nunca en clientes, navegadores ni repositorios. Si sospecha que se filtró, pida a FLIT que lo rote.
- No registre en logs la `api_key`, el `webhook_secret`, el `INTEGRATION_TOKEN` ni el header `X-Flit-Signature`.
- Use siempre HTTPS hacia la API.

## 3. Paso 1: dar de alta la empresa (u obtener sus credenciales)

Se llama una vez por empresa cliente, y se puede repetir sin problema. Si el NIT ya existe, la API **no da error**: devuelve la misma empresa con sus mismas credenciales. Así, la misma llamada sirve para:
- dar de alta una empresa nueva;
- recuperar la `api_key` y el `webhook_secret` de una empresa que ya existe, por ejemplo una que FLIT creó desde su consola o una cuyas credenciales se perdieron;
- reintentar con tranquilidad si la primera llamada se cortó por timeout.

Si el sistema no crea empresas, FLIT le entrega la `api_key` y el `webhook_secret` de cada una y este paso se omite.

### Contrato

`POST {FLIT_API_BASE_URL}/api/v1/integracion/empresas`

| Header | Valor |
| --- | --- |
| `Authorization` | `Bearer {FLIT_INTEGRATION_TOKEN}` |
| `Content-Type` | `application/json` |

Cuerpo. Los tres campos son obligatorios y no se aceptan campos adicionales:

| Campo | Regla |
| --- | --- |
| `legal_name` | razón social, de 1 a 200 caracteres |
| `tax_id` | NIT de 3 a 15 dígitos, **con o sin** dígito de verificación (`900123456` o `900123456-8`). Ver abajo |
| `contact_email` | correo válido |

### El NIT y su dígito de verificación

- Envíe el NIT solo con dígitos. Si quiere mandar el dígito de verificación (DV), póngalo al final, después de un guion: `900123456-8`.
- Los puntos y los espacios se ignoran: `900.123.456-8` es lo mismo que `900123456-8`.
- Si no envía el DV, FLIT lo calcula con el algoritmo de la DIAN. Si lo envía y **no coincide** con el calculado, la llamada responde `400` con el mensaje "el dígito de verificación no corresponde al NIT". Casi siempre es un NIT mal digitado.
- FLIT guarda el NIT sin el DV en `company.tax_id` y el DV aparte en `company.tax_id_dv`. `900123456`, `900.123.456` y `900123456-8` son la misma empresa.
- No se aceptan letras ni otros identificadores (cédulas de extranjería, pasaportes): la empresa se identifica por su NIT.

Para validar el DV de su lado antes de llamar (opcional), use el algoritmo de la DIAN:

```js
// Módulo 11 de la DIAN. nit: solo dígitos, sin DV, de 1 a 15.
function digitoVerificacion(nit) {
  const pesos = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];
  let suma = 0;
  for (let i = 0; i < nit.length; i++) suma += Number(nit[nit.length - 1 - i]) * pesos[i];
  const r = suma % 11;
  return r > 1 ? 11 - r : r;
}
digitoVerificacion("900123456"); // 8
digitoVerificacion("901234567"); // 7
```

### Ejemplo

```bash
curl -sS -X POST "$FLIT_API_BASE_URL/api/v1/integracion/empresas" \
  -H "Authorization: Bearer $FLIT_INTEGRATION_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "legal_name": "Flota Andina SAS",
        "tax_id": "900123456-8",
        "contact_email": "ops@flota.example"
      }'
```

Respuesta `201 Created` (empresa nueva):

```json
{
  "company": {
    "id": "3f6c2a7e-6a0b-4c8e-9a51-2f3b5d7c9e10",
    "legal_name": "Flota Andina SAS",
    "tax_id": "900123456",
    "tax_id_dv": 8,
    "contact_email": "ops@flota.example",
    "active": true,
    "api_key_prefix": "AbCdEfGh",
    "api_key_created_at": "2026-11-25T15:00:00Z",
    "has_webhook_secret": true,
    "webhook_secret_created_at": "2026-11-25T15:00:00Z",
    "created_at": "2026-11-25T15:00:00Z",
    "updated_at": "2026-11-25T15:00:00Z"
  },
  "created": true,
  "api_key": "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde",
  "api_key_rotated": false,
  "webhook_secret": "whsec_Zx9…"
}
```

Si repite la llamada con el mismo NIT, responde `200 OK` con el mismo cuerpo, pero con `"created": false`: la misma `company.id`, la misma `api_key` y el mismo `webhook_secret`.

### Cómo leer la respuesta

| Campo | Significado |
| --- | --- |
| `created` | `true` si la empresa nació en esta llamada (`201`). `false` si ya existía (`200`) |
| `api_key` | la clave de la empresa. Viene **siempre** |
| `webhook_secret` | el secreto de webhook de la empresa. Viene **siempre** |
| `api_key_rotated` | `true` solo si FLIT tuvo que **reemplazar** una clave anterior que no se podía recuperar. La clave vieja ya no sirve: use la nueva en todos lados. Pasa una sola vez por empresa, con claves creadas antes de esta versión |
| `company.active` | `false` si FLIT desactivó la empresa. Las credenciales vienen igual, pero la `api_key` responde `401` hasta que FLIT la reactive |

Si la empresa ya existía:
- **Datos:** se ignoran `legal_name` y `contact_email` del cuerpo, y se devuelven los que FLIT tiene guardados. Para cambiarlos, pídalo a FLIT.
- **Credenciales:** se devuelven las que tiene. Si le falta alguna (por ejemplo, FLIT la creó desde su consola sin generar el secreto de webhook), se genera en ese momento y se devuelve.
- **Rotación:** la llamada nunca rota una credencial que ya existe, salvo el caso de `api_key_rotated`.

Guarde `company.id`, `api_key` y `webhook_secret` asociados a la empresa. Si los pierde, vuelva a hacer esta misma llamada.

### Errores

| Status | `error.code` | Qué hacer |
| --- | --- | --- |
| 400 | `bad_request` | Corregir el cuerpo; `error.message` dice qué falla (campo, formato del NIT o DV que no corresponde) |
| 401 | `unauthorized` | Revisar `FLIT_INTEGRATION_TOKEN` y el formato `Bearer <token>` |
| 404 | `not_found` | La integración no está habilitada en ese ambiente: avisar a FLIT |
| 415 | `unsupported_media_type` | Falta `Content-Type: application/json` |

Todos los errores tienen esta forma: `{"error": {"code": "...", "message": "..."}}`.

**Reintentos del alta.** Si la llamada se corta por timeout o falla con un `5xx`, reintente con el mismo cuerpo. No crea una empresa duplicada: el reintento devuelve la empresa y las credenciales del primer intento, con `created: false`.

## 4. Paso 2: consultar los departamentos disponibles

`GET /api/v1/fuentes` con `X-API-Key` devuelve los departamentos, si cada uno está activo y el esquema de datos que exige.

```bash
curl -sS "$FLIT_API_BASE_URL/api/v1/fuentes" -H "X-API-Key: $FLIT_API_KEY"
```

```json
{
  "items": [
    { "id": "uuid", "name": "Antioquia", "department_code": "05", "department_name": "ANTIOQUIA", "active": true,
      "params_schema": { "type": "object", "required": ["documentType", "documentNumber", "plateNumber", "detail"],
                         "properties": { "detail": { "required": ["secretaryDepartmentCode", "vehicleModel", "secretaryCity", "OwnerName"] } } } }
  ]
}
```

Departamentos implementados (código DANE en `detail.secretaryDepartmentCode`):

| Código | Departamento | Datos adicionales que exige |
| --- | --- | --- |
| `05` | Antioquia | `vehicleModel`, `secretaryCity` y `OwnerName` (responden el cuestionario del portal) |
| `76` | Valle del Cauca | `vehicleModel`, `secretaryCity` y `OwnerName` |
| `68`, `81`, `41`, `18`, `44` | Santander, Arauca, Huila, Caquetá, La Guajira | placa de 6 caracteres |
| `17` | Caldas | placa de 6 caracteres; documento de 6 a 10 dígitos |
| `63` | Quindío | placa de 5 o 6 caracteres |
| `13` | Bolívar | documento que no empiece por 0 |
| `08` | Atlántico | opcional `vehicleModel` y `vehicleBrand` (inscribe el vehículo en la cuenta del portal) |
| `25` | Cundinamarca | `OwnerEmail` y `OwnerCellPhone` u `OwnerPhone`. Sin ellos, el portal no genera la factura y la consulta termina en `invalid_params` |

| Campo | Tipo | Notas |
| --- | --- | --- |
| `department_code` | string | Código DANE de 2 dígitos. Es la llave de la fuente |
| `name` / `department_name` | string | Nombre para mostrar; se usa `name` y, si viene vacío, `department_name` |
| `active` | boolean | **Obligatorio.** `true` si el departamento acepta consultas ahora; `false` si está deshabilitado temporalmente. La API siempre lo envía; si un cliente no lo recibe, debe tratar la fuente como **inactiva** y no enviarle consultas |
| `params_schema` | object | JSON Schema de los datos que exige la consulta de ese departamento |

La lista y los esquemas vigentes son siempre los de `GET /api/v1/fuentes`. Una fuente puede estar inactiva temporalmente (`active: false`); una consulta a ella responde `409 source_inactive`.

## 5. Paso 3: solicitar una liquidación

### Contrato

`POST {FLIT_API_BASE_URL}/api/v1/consultas`

| Header | Valor |
| --- | --- |
| `X-API-Key` | `{FLIT_API_KEY}` de la empresa |
| `Content-Type` | `application/json` |

Cuerpo (payload estándar):

| Campo | Obligatorio | Regla |
| --- | --- | --- |
| `documentType` | sí | `CC`, `NIT`, `CE`, `PA`, `TI` o `CD` |
| `documentNumber` | sí | de 3 a 20 letras o dígitos; con NIT, **sin** dígito de verificación |
| `plateNumber` | sí | de 4 a 10 letras o dígitos; se pasa a mayúsculas y se quitan espacios y guiones |
| `callbackUrl` | no | URL del webhook (sección 2.2). Si no viene, no hay aviso y se consulta el estado con `GET` |
| `detail.secretaryDepartmentCode` | sí | dos dígitos; elige el departamento |
| `detail.*` | según el departamento | datos del vehículo y del propietario (tabla de abajo) |

Campos de `detail`. Todos son texto salvo los `Bis`, que son booleanos. Un campo desconocido es 400.

| Campo | Uso |
| --- | --- |
| `secretaryDepartmentCode`, `secretaryDepartmentDescription` | departamento de la secretaría |
| `secretaryName`, `secretaryCity` | organismo y municipio de tránsito |
| `vehicleModel`, `vehicleBrand`, `vehicleType`, `vehicleServiceType`, `vehicleFuelType` | datos del vehículo; se comparan con el portal en `result.validations` |
| `OwnerName`, `OwnerEmail`, `OwnerCellPhone`, `OwnerPhone` | propietario |
| `OwnerAddressDepartment`, `OwnerAddressCity` | dirección del propietario (no la de la secretaría) |
| `OwnerAddressTypeRoad`, `OwnerAddressNumber1`, `OwnerAddressLetter1`, `OwnerAddressBis1`, `OwnerAddressSuffixletter1`, `OwnerAddressCardinality1`, `OwnerAddressNumber2`, `OwnerAddressLetter2`, `OwnerAddressBis2`, `OwnerAddressSuffixletter2`, `OwnerAddressPlate`, `OwnerAddressCardinality2`, `OwnerAddressComplete` | dirección por partes |

No se aceptan los campos del contrato anterior (`processStep`, `lastTaxHandler`, `credentials`): responden 400.

### Ejemplo

```bash
curl -sS -X POST "$FLIT_API_BASE_URL/api/v1/consultas" \
  -H "X-API-Key: $FLIT_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
        "documentType": "CC",
        "documentNumber": "1000000001",
        "plateNumber": "AAA123",
        "callbackUrl": "https://integrador.ejemplo.com/webhooks/flit?empresa=3f6c2a7e",
        "detail": {
          "secretaryDepartmentCode": "25",
          "OwnerName": "PEDRO PEREZ",
          "OwnerEmail": "pedro@correo.example",
          "OwnerCellPhone": "3001234567",
          "OwnerAddressDepartment": "CUNDINAMARCA",
          "OwnerAddressCity": "CHIA"
        }
      }'
```

Respuesta `202 Accepted`. La consulta queda en cola:

```json
{ "id": "8d1e0c55-…", "status": "queued", "source_id": "uuid", "created_at": "2026-11-25T15:01:00Z" }
```

Guarde el `id`: es la llave para relacionar el aviso y para consultar el estado.

### Errores

| Status | `error.code` | Causa |
| --- | --- | --- |
| 400 | `bad_request` | forma inválida, campo desconocido o `callbackUrl` inválida |
| 401 | `unauthorized` | `X-API-Key` ausente o inválida, o empresa inactiva |
| 409 | `source_inactive` | el departamento está deshabilitado temporalmente |
| 422 | `department_unsupported` | no hay fuente para ese `secretaryDepartmentCode`, o falta |
| 422 | `invalid_params` | el departamento exige un dato que no vino o no tiene el formato |
| 422 | `adapter_unavailable` | el departamento no está disponible en este despliegue |
| 422 | `webhook_not_configured` | se mandó `callbackUrl` y la empresa no tiene secreto de webhook |

Cada `POST` crea una consulta nueva. No hay idempotencia: si reintenta por un timeout, puede quedar duplicada.

## 6. Paso 4: recibir el resultado por webhook

Cuando termina la consulta, FLIT envía un `POST` a la `callbackUrl`. También envía otro aviso si FLIT reintenta la consulta más tarde.

### Headers del aviso

| Header | Ejemplo | Uso |
| --- | --- | --- |
| `Content-Type` | `application/json` | |
| `User-Agent` | `flit-webhook/1` | |
| `X-Flit-Event` | `consulta.finalizada` | tipo de evento |
| `X-Flit-Delivery` | `b7c0…` | id único del aviso: úselo para deduplicar |
| `X-Flit-Timestamp` | `1764082869` | segundos Unix del envío |
| `X-Flit-Signature` | `v1=5f2b…` (64 hex) | HMAC-SHA256 del timestamp y el cuerpo |

### Cuerpo del aviso

```json
{
  "delivery_id": "b7c0c3f2-…",
  "event": "consulta.finalizada",
  "created_at": "2026-11-25T15:01:35.000000Z",
  "consulta": {
    "id": "8d1e0c55-…",
    "source_id": "uuid",
    "source_name": "Cundinamarca",
    "run_id": "uuid",
    "status": "succeeded",
    "attempt": 1,
    "error_code": null,
    "error_message": null,
    "file_errors": [],
    "result": {
      "status": "con_deuda",
      "plateNumber": "AAA123",
      "department": { "code": "25", "name": "CUNDINAMARCA" },
      "consultedAt": "2026-11-25T15:01:35Z",
      "vehicle": { "brand": null, "line": null, "model": null, "class": null, "serviceType": null, "fuelType": null, "appraisal": null },
      "pendingPeriods": [
        { "period": 2025, "appraisal": 41738000, "tax": 710000, "penalty": 0, "interest": 249000, "discounts": 224000,
          "previousPayments": null, "totalToPay": 771700,
          "otherCharges": [ { "description": "Costos administrativos", "amount": 36700 } ],
          "dueDate": "2026-11-30", "paymentReference": null, "fileId": "f1a2…" }
      ],
      "totals": { "tax": 710000, "penalty": 0, "interest": 249000, "discounts": 224000, "totalToPay": 771700 },
      "lastPayment": null,
      "clearance": null,
      "consolidatedFileId": null,
      "validations": [ { "field": "OwnerName", "sent": "PEDRO PEREZ", "portal": null, "result": "match" } ],
      "warnings": [],
      "error": null
    },
    "created_at": "2026-11-25T15:01:00.123456Z",
    "started_at": "2026-11-25T15:01:01.000000Z",
    "finished_at": "2026-11-25T15:01:35.000000Z"
  }
}
```

Cómo leerlo:

- **`consulta.status`** es el estado de la consulta: `succeeded`, `partial` (faltó algún PDF; ver `file_errors` y `result.warnings`) o `failed`.
- **`result.status`** es el estado de negocio:
  - `con_deuda`: hay vigencias en `pendingPeriods`, cada una con sus montos y su `fileId`;
  - `paz_y_salvo`: sin deuda; `pendingPeriods` va vacío y `totals.totalToPay` en 0;
  - `error`: la consulta falló; `result.error.code` es el código y `result.error.message`, un texto para mostrar al usuario final.
- **Montos y fechas:** los montos van en pesos, como números con hasta dos decimales. Las fechas van como `AAAA-MM-DD`. Un dato que el portal no da va en `null`, nunca en 0.
- **`totals`** suma por concepto, y `totals.totalToPay` es el total a pagar.
- **`otherCharges`** ya está incluido en `totalToPay`: no lo sume otra vez.
- Una vigencia con `warning` `period_not_liquidable` está adeudada, pero el portal no deja liquidarla. Va sin montos ni `fileId`.

La forma completa de `result` está en [04-api.md](04-api.md), "Respuesta estándar". Es la misma que devuelve `GET /api/v1/consultas/{id}`.

### Cómo procesar un aviso

1. Leer el **cuerpo crudo**, tal como llega, sin parsearlo ni reformatearlo.
2. Verificar la firma:
   1. calcular `HMAC-SHA256(webhook_secret, "{X-Flit-Timestamp}.{cuerpo crudo}")` en hex;
   2. anteponer `v1=`;
   3. comparar con `X-Flit-Signature` en tiempo constante.

   La clave es el `webhook_secret` completo, con su prefijo `whsec_`.
3. Rechazar el aviso (responder `401`) si la firma no coincide o si el timestamp tiene más de 5 minutos de diferencia con su reloj.
4. Deduplicar por `X-Flit-Delivery`. Si ya lo procesó, responder `200` sin repetir.
5. Si hay varios avisos de la misma `consulta.id`, quedarse con el de **mayor `consulta.attempt`**. El orden de llegada no está garantizado.
6. Responder `200` de inmediato y procesar en segundo plano.

### Reintentos de FLIT

| Respuesta del integrador | Qué hace FLIT |
| --- | --- |
| `2xx` en menos de 10 s | entregado |
| otro status, `3xx`, timeout o error de red | reintenta a 1 min, 5 min, 30 min, 2 h, 6 h y 12 h (7 envíos en unas 21 h). Cada reintento lleva el mismo `delivery_id` y el mismo cuerpo, con un `X-Flit-Timestamp` y una firma nuevos |
| `410 Gone` | deja de reintentar ese aviso |

Si se agotan los reintentos, FLIT puede reenviar el aviso a pedido. Mientras tanto, el resultado siempre está disponible con `GET /api/v1/consultas/{id}` (paso 5).

### Ejemplos de receptor

**Node.js (Express)**

```js
import express from "express";
import crypto from "node:crypto";

const app = express();
const SECRET = process.env.FLIT_WEBHOOK_SECRET;
const TOLERANCE = Number(process.env.FLIT_WEBHOOK_TOLERANCE_SECONDS ?? 300);

// express.raw: hace falta el cuerpo crudo para la firma.
app.post("/webhooks/flit", express.raw({ type: "application/json" }), async (req, res) => {
  const ts = Number(req.get("X-Flit-Timestamp"));
  const sig = req.get("X-Flit-Signature") ?? "";
  if (!Number.isFinite(ts) || Math.abs(Date.now() / 1000 - ts) > TOLERANCE) return res.sendStatus(401);

  const expected = "v1=" + crypto.createHmac("sha256", SECRET).update(`${ts}.`).update(req.body).digest("hex");
  const a = Buffer.from(expected), b = Buffer.from(sig);
  if (a.length !== b.length || !crypto.timingSafeEqual(a, b)) return res.sendStatus(401);

  const deliveryId = req.get("X-Flit-Delivery");
  if (await yaProcesado(deliveryId)) return res.sendStatus(200);

  res.sendStatus(200); // responder antes de procesar
  const aviso = JSON.parse(req.body.toString("utf8"));
  await encolar(aviso); // guardar por consulta.id, quedándose con el mayor consulta.attempt
});
```

**Python (Flask)**

```python
import hmac, hashlib, os, time
from flask import Flask, request, abort

app = Flask(__name__)
SECRET = os.environ["FLIT_WEBHOOK_SECRET"].encode()
TOLERANCE = int(os.environ.get("FLIT_WEBHOOK_TOLERANCE_SECONDS", "300"))

@app.post("/webhooks/flit")
def flit_webhook():
    raw = request.get_data()  # cuerpo crudo
    try:
        ts = int(request.headers["X-Flit-Timestamp"])
    except (KeyError, ValueError):
        abort(401)
    if abs(time.time() - ts) > TOLERANCE:
        abort(401)
    expected = "v1=" + hmac.new(SECRET, f"{ts}.".encode() + raw, hashlib.sha256).hexdigest()
    if not hmac.compare_digest(expected, request.headers.get("X-Flit-Signature", "")):
        abort(401)
    delivery_id = request.headers["X-Flit-Delivery"]
    if not ya_procesado(delivery_id):
        encolar(request.get_json())
    return "", 200
```

**C# (ASP.NET Core minimal API)**

```csharp
app.MapPost("/webhooks/flit", async (HttpRequest req) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var raw = ms.ToArray(); // cuerpo crudo

    if (!long.TryParse(req.Headers["X-Flit-Timestamp"], out var ts) ||
        Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts) > 300)
        return Results.Unauthorized();

    var secret = Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("FLIT_WEBHOOK_SECRET")!);
    var payload = Encoding.UTF8.GetBytes($"{ts}.").Concat(raw).ToArray();
    var expected = "v1=" + Convert.ToHexString(HMACSHA256.HashData(secret, payload)).ToLowerInvariant();
    var received = req.Headers["X-Flit-Signature"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(received)))
        return Results.Unauthorized();

    var deliveryId = req.Headers["X-Flit-Delivery"].ToString();
    if (!await YaProcesado(deliveryId)) await Encolar(raw);
    return Results.Ok();
});
```

**Go**

```go
func flitWebhook(w http.ResponseWriter, r *http.Request) {
	raw, err := io.ReadAll(io.LimitReader(r.Body, 5<<20))
	if err != nil {
		http.Error(w, "", http.StatusBadRequest)
		return
	}
	ts, err := strconv.ParseInt(r.Header.Get("X-Flit-Timestamp"), 10, 64)
	if err != nil || math.Abs(float64(time.Now().Unix()-ts)) > 300 {
		http.Error(w, "", http.StatusUnauthorized)
		return
	}
	m := hmac.New(sha256.New, []byte(os.Getenv("FLIT_WEBHOOK_SECRET")))
	fmt.Fprintf(m, "%d.", ts)
	m.Write(raw)
	expected := "v1=" + hex.EncodeToString(m.Sum(nil))
	if !hmac.Equal([]byte(expected), []byte(r.Header.Get("X-Flit-Signature"))) {
		http.Error(w, "", http.StatusUnauthorized)
		return
	}
	w.WriteHeader(http.StatusOK)
	go procesar(r.Header.Get("X-Flit-Delivery"), raw) // deduplicar y guardar
}
```

### Probar la verificación de la firma

Con estos valores fijos, su implementación debe dar exactamente esta firma:

| Dato | Valor |
| --- | --- |
| secreto | `whsec_prueba` |
| `X-Flit-Timestamp` | `1700000000` |
| cuerpo | `{"a":1}` |
| firma esperada | `v1=0ca0bb9c916989fb6c1d1eb729a180da30cb078b7cbe551c8f8da0ec452ac9db` |

```bash
printf '1700000000.{"a":1}' | openssl dgst -sha256 -hmac whsec_prueba
```

### Implementar el receptor paso a paso

#### 1. Ciclo de vida del `webhook_secret`

- **Origen:** nace con la empresa y sale en la respuesta del alta (paso 1). Cada empresa tiene uno solo.
- **Recuperarlo:** si lo pierde, repita el alta con el mismo NIT. Devuelve el mismo secreto.
- **Rotación:** solo FLIT lo rota, desde su consola, si se lo pide (por ejemplo, porque se filtró). Desde ese momento:
  - todos los avisos salen firmados con el secreto nuevo, también los que estaban en reintento;
  - el alta con ese NIT devuelve el secreto nuevo.
- **Rotación sin perder avisos:** durante la ventana del cambio, verifique con el secreto nuevo y, si falla, con el anterior. Cuando termine la ventana, borre el anterior:

  ```js
  const secretos = [secretoActual, secretoAnterior].filter(Boolean);
  const valida = secretos.some((s) => firmaValida(s, ts, raw, sig));
  ```

- **Activación:** el `callbackUrl` solo funciona si la empresa tiene secreto. Si no lo tiene, `POST /consultas` responde `422 webhook_not_configured`. Con empresas creadas por el alta de integración esto no pasa, porque el secreto siempre viene.

#### 2. Varias empresas en un mismo receptor

Cada empresa firma con **su** secreto. El receptor tiene que saber cuál usar **antes** de verificar la firma, y no debe confiar en el cuerpo para decidirlo, porque todavía no está verificado. La forma recomendada:

1. **Al pedir la consulta,** ponga el id de la empresa en la query de la `callbackUrl`. Llega tal cual en el aviso:

   ```text
   https://integrador.ejemplo.com/webhooks/flit?empresa=3f6c2a7e-6a0b-4c8e-9a51-2f3b5d7c9e10
   ```

2. **Al recibir el aviso:**
   1. con `?empresa=`, busque el secreto de esa empresa; si no existe, responda `401`;
   2. verifique la firma con ese secreto;
   3. ya verificada, compruebe que `consulta.id` es una consulta que usted pidió **para esa misma empresa**.

Un tercero puede cambiar la query, pero sin el secreto de la empresa no puede producir una firma válida.

#### 3. Modelo de datos sugerido

Dos tablas: una para no procesar dos veces el mismo aviso y otra con el último resultado de cada consulta.

```sql
-- Una fila por aviso recibido y verificado. Deduplica por delivery_id.
CREATE TABLE flit_avisos (
    delivery_id   uuid PRIMARY KEY,           -- X-Flit-Delivery
    empresa_id    uuid NOT NULL,              -- de ?empresa=
    consulta_id   uuid NOT NULL,              -- consulta.id
    attempt       int  NOT NULL,              -- consulta.attempt
    cuerpo        jsonb NOT NULL,             -- el aviso completo
    recibido_en   timestamptz NOT NULL DEFAULT now(),
    procesado_en  timestamptz                 -- null hasta procesarlo
);

-- El último resultado de cada consulta.
CREATE TABLE flit_consultas (
    consulta_id   uuid PRIMARY KEY,           -- id del 202 de POST /consultas
    empresa_id    uuid NOT NULL,
    placa         text NOT NULL,
    attempt       int,
    estado        text,                       -- consulta.status
    resultado     jsonb,                      -- consulta.result
    actualizado_en timestamptz
);
```

Al procesar un aviso, guarde el resultado solo si es de un intento más nuevo. Así un aviso viejo que llega tarde no pisa uno nuevo:

```sql
UPDATE flit_consultas
SET attempt = $2, estado = $3, resultado = $4, actualizado_en = now()
WHERE consulta_id = $1 AND (attempt IS NULL OR attempt < $2);
```

El receptor hace `INSERT INTO flit_avisos … ON CONFLICT (delivery_id) DO NOTHING` y responde `200`. Si el `INSERT` no insertó nada, el aviso ya estaba. Un proceso aparte toma las filas con `procesado_en` en null, actualiza `flit_consultas` y descarga los PDF.

#### 4. Prueba punta a punta en QA

1. **Publicar el receptor** en una URL `https` pública del ambiente de QA. Para probar desde un equipo local, use un túnel HTTPS que dé una URL pública. FLIT no envía a `localhost` ni a IPs privadas.
2. **Verificar la firma** con el vector de la sección anterior.
3. **Crear una consulta** en QA con `callbackUrl` apuntando al receptor, con una placa de prueba que le indique FLIT.
4. **Esperar el aviso.** Debe llegar en segundos o minutos, según el portal. Revise que:
   - la firma se verificó;
   - quedó una fila en `flit_avisos`;
   - `flit_consultas` tiene `estado` y `resultado`.
5. **Confirmar del lado de FLIT** con `GET /api/v1/consultas/{id}`: `webhook.status` debe ser `delivered` y `webhook.last_status_code`, `200`.
6. **Probar las fallas:**
   - que el receptor responda `500` una vez: FLIT reintenta al minuto con el mismo `delivery_id`, y el receptor debe procesarlo una sola vez;
   - cambiar un byte del secreto: el receptor debe responder `401`, y `webhook.status` queda en `pending` con `last_status_code` `401`;
   - pedir a FLIT que reenvíe un aviso ya entregado: el receptor responde `200` sin duplicar.

#### 5. Problemas frecuentes

| Síntoma | Causa probable | Solución |
| --- | --- | --- |
| La firma nunca coincide | El framework parsea el JSON antes y usted firma el objeto re-serializado | Firme los **bytes crudos** del cuerpo (`express.raw`, `request.get_data()`, `io.ReadAll`) |
| La firma nunca coincide | Usó el secreto sin el prefijo `whsec_` o lo decodificó de base64 | Use el secreto completo como texto, tal cual llegó en el alta |
| La firma nunca coincide | Firmó solo el cuerpo | El mensaje es `"{X-Flit-Timestamp}.{cuerpo}"`, con el punto |
| Falla solo con algunas empresas | Verifica con el secreto de otra empresa | Elija el secreto por `?empresa=` (punto 2) |
| Rechaza avisos válidos por antigüedad | Reloj del servidor desfasado | Sincronícelo con NTP y use una tolerancia de 300 s |
| `webhook.status` queda en `pending` y `last_status_code` es `301`/`302` | El endpoint redirige (por ejemplo, de `http` a `https`, o agrega `/` al final) | Registre en `callbackUrl` la URL final exacta. FLIT no sigue redirecciones |
| `last_status_code` vacío y reintentos | Timeout (más de 10 s) o error de TLS o DNS | Responda `200` antes de procesar; revise el certificado y el firewall (IP de salida de FLIT) |
| Llegan dos avisos de la misma consulta | Un reintento de FLIT (mismo `delivery_id`) o un reintento de la consulta (otro `attempt`) | Deduplique por `delivery_id` y quédese con el mayor `attempt` |
| `422 webhook_not_configured` al crear la consulta | La empresa no tiene secreto | Repita el alta con su NIT: genera el secreto y lo devuelve |

## 7. Paso 5: consultar el estado sin esperar el aviso

Sirve si no usa webhook, si quiere confirmar un aviso o si se agotaron los reintentos.

```bash
curl -sS "$FLIT_API_BASE_URL/api/v1/consultas/8d1e0c55-…" -H "X-API-Key: $FLIT_API_KEY"
```

- La respuesta trae `status`, `attempt`, `error_code`, `file_errors` y `result`, con la misma forma que el aviso. Además trae:
  - `callback_url`: la URL guardada;
  - `webhook`: el estado del último aviso, con `status` `pending`, `delivered` o `failed`, `attempts`, `last_status_code` y `next_attempt_at`.
- Mientras `status` sea `queued` o `running`, `result` es `null`.
- Si hace polling, use un intervalo de 5 a 10 s y un límite. Una consulta tarda de segundos a unos minutos según el portal.

Listado: `GET /api/v1/consultas?status=succeeded&limit=50&cursor=…`. Pagina con `next_cursor`.

## 8. Paso 6: descargar los recibos PDF

Cada `fileId` de `result` se descarga con la `api_key` de la misma empresa. Están en `pendingPeriods[].fileId`, `consolidatedFileId` y `clearance.fileId`.

```bash
curl -sS -o recibo-AAA123-2025.pdf \
  "$FLIT_API_BASE_URL/api/v1/files/f1a2…/download" -H "X-API-Key: $FLIT_API_KEY"
```

- **Respuesta:** `200` con `Content-Type: application/pdf` y `Content-Disposition` con el nombre del archivo.
- **`404 not_found`:** el archivo no existe o es de otra empresa.
- **`404 file_missing`:** el registro existe, pero el archivo ya no está en el almacenamiento.
- **`503 storage_unavailable`:** falló el almacenamiento. Reintente más tarde.
- **Mismo `fileId` en varias vigencias:** en algunos portales (Caldas, Edesk) un solo recibo cubre todas las vigencias, así que todas llevan el mismo `fileId`.
- **Vigencia sin recibo:** un `fileId` en `null` significa que no hay PDF para esa vigencia. La razón va en `warnings`.

## 9. Códigos de error de la consulta

Cuando `consulta.status` es `failed`, el código aparece en dos lugares: en `error_code` y en `result.error.code`.

| Código | Significado | ¿Reintentar? |
| --- | --- | --- |
| `invalid_params` | los datos no sirven para el portal (municipio inexistente, falta email o teléfono en Cundinamarca) | no; corregir los datos y crear otra consulta |
| `owner_validation_failed` | el documento o el nombre no corresponden al vehículo según el portal | no; revisar los datos |
| `vehicle_data_required` | el portal pide datos del vehículo que no vinieron | no; completar los datos |
| `portal_rejected` | el portal respondió un error de negocio; el mensaje trae el texto del portal | depende del mensaje |
| `captcha_blocked`, `auth_failed`, `rate_limited`, `timeout`, `http_status`, `proxy_failed` | problema temporal del portal o de la conexión | sí, más tarde (otra consulta) |
| `source_inactive`, `adapter_unavailable` | el departamento está deshabilitado | sí, más tarde |
| `parse_error`, `unexpected`, `worker_lost` | error interno o cambio del portal | sí; si se repite, avisar a FLIT |
| `pdf_download_failed`, `s3_error` | los montos están, pero faltó un PDF (`partial`) | FLIT puede reintentar la consulta |

## 10. Lista de verificación para salir a producción

- [ ] `FLIT_API_BASE_URL` del ambiente correcto y llamadas solo por HTTPS.
- [ ] `FLIT_INTEGRATION_TOKEN` en el almacén de secretos, si el sistema crea empresas.
- [ ] `api_key` y `webhook_secret` de cada empresa guardados al recibir la respuesta del alta, con `company.id` como llave.
- [ ] NIT enviado solo con dígitos (con o sin `-DV`), y el `400` por DV incorrecto mostrado al usuario.
- [ ] Departamentos tomados de `GET /api/v1/fuentes` y consultados solo si `active: true` (un ítem sin `active` cuenta como inactivo).
- [ ] El alta se reintenta sin miedo ante un timeout, y `api_key_rotated: true` reemplaza la clave guardada.
- [ ] Endpoint de webhook en `https` público, con respuesta `2xx` en menos de 10 s y sin redirecciones.
- [ ] Verificación de la firma probada con el vector de la sección 6.
- [ ] Secreto elegido por `?empresa=` en la `callbackUrl` y no por el cuerpo; `consulta.id` comprobada contra la empresa después de verificar.
- [ ] Prueba punta a punta en QA completa, incluidas las fallas (sección 6, "Prueba punta a punta en QA").
- [ ] Validación de la antigüedad de `X-Flit-Timestamp`, con el reloj del servidor sincronizado por NTP.
- [ ] Deduplicación por `X-Flit-Delivery` y orden por `consulta.attempt`.
- [ ] Respaldo con `GET /api/v1/consultas/{id}` cuando no llega el aviso.
- [ ] Descarga de los PDF por `fileId` con la `api_key` de la empresa dueña.
- [ ] Ningún secreto ni la firma en los logs.
