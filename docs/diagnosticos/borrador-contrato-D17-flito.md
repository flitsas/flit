# Borrador del contrato D-17 — Diagnóstico Vehicular para FLITO

> Generado: 2026-11-18 · Estado: **BORRADOR para revisión del equipo FLITO, el PO y el Líder Técnico**. No es un contrato firmado y nada de esto está implementado.
> Diseño de base: `propuesta-integracion-flito-diagnostico.md` y ADR-0090 (Propuesto).
> Este documento no contiene secretos, claves ni datos personales. Las credenciales se entregan fuera de banda, nunca por correo ni por herramientas de trabajo.

## 1. Qué permite la integración

1. FLITO pide un diagnóstico vehicular para una lista de placas o VIN.
2. FLIT consulta las fuentes (RUNT y las que tenga habilitadas la compañía) y, si el modo es con análisis, emite un dictamen por vehículo: `VIABLE` o `BLOQUEADO`.
3. Cuando el lote termina, FLIT envía a FLITO un aviso firmado.
4. FLITO consulta el resultado y, con el permiso de datos personales, descarga el PDF por vehículo y el ZIP de evidencias.

La carga por archivo `.xlsx` desde la pantalla sigue funcionando y no cambia.

## 2. Cliente y credenciales

- FLITO usa el **mismo login de máquina** que ya tiene para Trámites: `POST /api/v1/external/auth/token` con `clientId` y `clientSecret`.
- Para Diagnóstico se da de alta **un cliente distinto por compañía** (tipo `diagnostico`). Ejemplo de identificador: `flito-diag-<compañía>-qa`. Los identificadores son únicos por ambiente y no se reutilizan.
- El pase dura 30 minutos y sirve **solo** para la API de Diagnóstico (audiencia `flit-external-diagnostico`). El pase de Trámites no sirve en Diagnóstico y el de Diagnóstico no sirve en Trámites.
- El secreto se muestra una sola vez al darlo de alta. Rota con una ventana de gracia, se bloquea tras varios intentos fallidos y se puede desactivar.

## 3. Permisos (scopes)

| Scope | Qué permite |
|---|---|
| `external.diagnostico.write` | Crear diagnósticos |
| `external.diagnostico.read` | Ver estado, totales, veredicto por vehículo, códigos de motivo, placa o VIN |
| `external.diagnostico.pii.read` | Ver datos de personas, el texto del dictamen, el PDF por vehículo y el ZIP de evidencias. Cada uso queda registrado |

Cada cliente lleva solo los scopes que FLITO necesite (pregunta P3).

## 4. Endpoints

Base: `https://<host del ambiente>/api/v1/diagnostico/integracion`. Pase en `Authorization: Bearer <pase>`.

| Método y ruta | Scope | Qué hace |
|---|---|---|
| `POST /diagnosticos` | write | Crea el lote. Exige la cabecera `Idempotency-Key` (UUID). Responde 201 con el id y el estado `EN_PROCESO` |
| `GET /diagnosticos?referenciaExterna=…` | read | Busca un lote por la referencia de FLITO |
| `GET /diagnosticos/{id}` | read | Estado, totales y veredicto por vehículo |
| `GET /diagnosticos/{id}/vehiculos/{vid}/dictamen` | pii.read | Dictamen vigente con su texto |
| `GET /diagnosticos/{id}/vehiculos/{vid}/pdf` | pii.read | PDF del vehículo, cuando el lote ya cerró |
| `POST /diagnosticos/{id}/evidencias` | pii.read | Pide el ZIP de evidencias (asíncrono) |
| `GET /diagnosticos/{id}/evidencias/{eid}` | pii.read | Estado del ZIP |
| `GET /diagnosticos/{id}/evidencias/{eid}/archivo` | pii.read | Descarga del ZIP |

### Ejemplo de solicitud (datos ficticios)

```http
POST /api/v1/diagnostico/integracion/diagnosticos
Authorization: Bearer <pase>
Idempotency-Key: 3f1c2a9e-7b4d-4c1e-9a55-0d2b8e6f1a77
Content-Type: application/json
```

```json
{
  "referenciaExterna": "PED-000123",
  "nombre": "Pedido flota norte",
  "modo": "CON_ANALISIS",
  "tipoTramiteCodigo": "TRASPASO",
  "vehiculos": [
    { "referenciaExterna": "ITEM-1", "identificador": "AAA000" },
    { "referenciaExterna": "ITEM-2", "identificador": "BBB111" }
  ]
}
```

- `tipoTramiteCodigo` del ejemplo es ilustrativo: los códigos válidos son los del catálogo de la compañía.
- `modo` puede ser `SIMPLE` (solo consultas) o `CON_ANALISIS` (consultas y dictamen). `CON_ANALISIS` exige `tipoTramiteCodigo`.
- `fuentes` es opcional. Si no se manda, se usan las fuentes habilitadas de la compañía.
- El máximo de vehículos por lote es el límite configurado para la compañía.
- `documentoLocatario` es opcional en cada vehículo. Se guarda cifrado y nunca se devuelve completo.
- `referenciaExterna` acepta solo letras, números, `.`, `_` y `-`, hasta 64 caracteres. **No debe contener datos personales.**

### Respuesta 201

```json
{
  "id": "<uuid>",
  "referenciaExterna": "PED-000123",
  "estado": "EN_PROCESO",
  "aceptados": 2,
  "rechazados": [],
  "estimadoConsultas": 4,
  "enlaces": {
    "self": "/api/v1/diagnostico/integracion/diagnosticos/<uuid>",
    "app": "https://<host>/diagnostico/diagnosticos/<uuid>"
  }
}
```

- `rechazados` lista los ítems con un identificador inválido o duplicado (`IDENTIFICADOR_INVALIDO`, `DUPLICADO`). Si no hay ningún vehículo válido, la respuesta es 422 y no se crea nada.
- El enlace `app` abre la pantalla del lote en la FLIT Suite. Exige que la persona tenga sesión en la suite.

## 5. Idempotencia

- Repetir la misma `Idempotency-Key` con el mismo cuerpo devuelve el mismo lote (200), sin crear otro. La clave vale 72 horas.
- La misma clave con otro cuerpo responde 409 `IDEMPOTENCIA_CONFLICTO`.
- `referenciaExterna` es única por cliente: repetirla con otra clave responde 409 `REFERENCIA_EXTERNA_DUPLICADA`.

## 6. Aviso al cerrar (webhook)

Cuándo se envía: cuando el lote pasa a `COMPLETADO` o `INCOMPLETO`. Si un lote se reabre (por ejemplo, por un reproceso) y vuelve a cerrar, llega otro aviso con una `version` mayor.

A dónde se envía: a una URL HTTPS pública de FLITO, una por ambiente (pregunta P1).

Formato:

- Cabeceras:
  - `X-Webhook-Signature: sha256=<hex>`
  - `X-Flit-Evento: diagnostico.lote.cerrado`
  - `X-Flit-Evento-Id: <uuid>`
  - `X-Correlation-Id`
- Cuerpo:

```json
{
  "evento": "diagnostico.lote.cerrado",
  "eventoId": "<uuid>",
  "emitidoEn": "2026-11-18T15:04:05Z",
  "ambiente": "QA",
  "diagnostico": {
    "id": "<uuid>",
    "referenciaExterna": "PED-000123",
    "estado": "COMPLETADO",
    "version": 1,
    "terminadoEn": "2026-11-18T15:04:00Z",
    "totales": { "vehiculos": 2, "viables": 1, "bloqueados": 1, "sinDictamen": 0 },
    "vehiculos": [
      { "vehiculoId": "<uuid>", "referenciaExterna": "ITEM-1", "veredicto": "VIABLE", "dictamenVersion": 1 },
      { "vehiculoId": "<uuid>", "referenciaExterna": "ITEM-2", "veredicto": "BLOQUEADO", "dictamenVersion": 1 }
    ],
    "enlaces": { "self": "/api/v1/diagnostico/integracion/diagnosticos/<uuid>" }
  }
}
```

- El aviso **no lleva** placa, VIN, documentos, nombres ni el texto del dictamen. Cada vehículo se identifica por la `referenciaExterna` que mandó FLITO.
- `vehiculos` va solo si FLITO elige el aviso con veredicto (pregunta P2). En lotes muy grandes se omite y FLITO lee el detalle por `GET`.

### Firma

- `X-Webhook-Signature` es el HMAC-SHA256, en hexadecimal, de los bytes UTF-8 exactos del cuerpo, con una clave por ambiente. Esa clave es distinta de la de los avisos de Trámites.
- FLITO debe:
  - verificar la firma sobre el cuerpo sin re-serializarlo, con comparación en tiempo constante;
  - rechazar avisos con `emitidoEn` de más de 5 minutos (pregunta P6);
  - deduplicar por `eventoId` y quedarse con la `version` más alta de cada `diagnostico.id`.

### Respuesta y reintentos

- FLITO debe responder **2xx en menos de 10 segundos** y procesar después.
- Cualquier otra respuesta o un timeout se reintenta a los 10 s, 1 min y 10 min. Después el aviso queda en la cola de fallidos de FLIT para revisión manual.
- El aviso es una señal y la fuente de verdad es `GET /diagnosticos/{id}`. FLITO debe tener una consulta de respaldo (pregunta P5).

## 7. Cuotas y SLA (propuestos, sin acordar)

| Límite | Valor propuesto | Error |
|---|---|---|
| Peticiones por cliente | 60 por minuto | 429 `rate_limited` con `Retry-After` |
| Lotes en proceso por cliente | 5 | 429 `LOTES_EN_PROCESO` |
| Vehículos por lote | límite de la compañía | 422 `LIMITE_SUPERADO` |
| Consultas o crédito | según el contrato de la compañía | 403 o 422, según la regla |

El tiempo de proceso de un lote depende de las fuentes externas (RUNT y las demás). **FLIT no compromete aquí ningún SLA de tiempo**: lo define el Líder Técnico con FLITO.

## 8. Errores

Todos los errores responden JSON `{ "code": "...", "campo": "...", "mensaje": "..." }`.

| HTTP | Códigos | Qué hacer |
|---|---|---|
| 400 | `IDEMPOTENCY_KEY_REQUERIDA`, cuerpo mal formado | Corregir la petición |
| 401 | pase ausente, vencido o de otra audiencia | Pedir otro pase |
| 403 | sin scope, `PRODUCTO_NO_HABILITADO` | Revisar el alta o la suscripción de la compañía |
| 404 | `NO_ENCONTRADO` | El lote no existe o no es de esta compañía o cliente |
| 409 | `IDEMPOTENCIA_CONFLICTO`, `REFERENCIA_EXTERNA_DUPLICADA`, `NO_DISPONIBLE` | No reintentar igual |
| 422 | `SIN_VALIDOS`, `LIMITE_SUPERADO`, `FUENTE_NO_HABILITADA`, `TIPO_TRAMITE_REQUERIDO`, `TIPO_TRAMITE_NO_DISPONIBLE`, `VALOR_FUERA_DE_CONJUNTO` | Corregir los datos |
| 429 | `rate_limited`, `DEMASIADAS_SOLICITUDES`, `LOTES_EN_PROCESO` | Esperar lo que indique `Retry-After` |
| 503 | `CATALOGO_NO_DISPONIBLE` | Reintentar después de `Retry-After` |

## 9. Datos personales (Ley 1581)

- FLITO solo recibe datos personales con el scope `external.diagnostico.pii.read`, y cada lectura queda registrada.
- El aviso nunca lleva datos personales.
- FLITO se compromete a tratar los datos recibidos según la autorización del titular y la finalidad del servicio (pregunta P7).

## 10. Ambientes y pruebas

- Primero DEV y después QA, cada uno con su cliente, su clave de firma y su URL de aviso. PDN se enciende solo tras firmar este contrato.
- Pruebas de contrato conjuntas: crear, reintentar con la misma clave, aviso firmado, aviso repetido, recierre con una versión mayor y descarga de PDF y ZIP.

## 11. Preguntas abiertas para FLITO

- **P1.** ¿Cuál es la URL HTTPS del aviso en cada ambiente? ¿Hay IPs de origen que FLITO deba permitir?
- **P2.** ¿El aviso debe llevar solo el cierre o también el veredicto por vehículo?
- **P3.** ¿Qué scopes necesita FLITO? ¿Hace falta `pii.read` o le basta con el veredicto y los códigos?
- **P4.** ¿Para cuántas compañías opera FLITO? ¿Le sirve un cliente por compañía o necesita un cliente con varias compañías?
- **P5.** ¿Cada cuánto consultaría FLITO el estado como respaldo?
- **P6.** ¿Acepta FLITO la ventana de 5 minutos sobre `emitidoEn` y el reintento 10 s / 1 min / 10 min?
- **P7.** ¿Qué hace FLITO con el PDF y el ZIP? ¿Dónde los guarda y cuánto tiempo? Es necesario para la base legal del tratamiento.
- **P8.** ¿Necesita FLITO un aviso cuando soporte FLIT corrige el dictamen de un vehículo sin reabrir el lote?
- **P9.** ¿Qué volumen espera FLITO: lotes por día, vehículos por lote y horas pico?
- **P10.** ¿FLITO necesita crear el trámite desde el diagnóstico (derivación) o eso sigue en la pantalla?
- **P11.** ¿Cómo rota FLITO la clave de firma? Hoy no hay dos claves válidas a la vez y la rotación se coordina por hora.
