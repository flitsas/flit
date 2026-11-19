# Propuesta: integración Diagnóstico Vehicular ↔ FLITO

> Generado: 2026-11-18 · Autor: architecture-agent (oleada `ola-llm-flito`, solo diseño) · Estado: **borrador para revisión del PO y del Líder Técnico**
> ADR asociado: [ADR-0090](../decisions/ADR-0090-diagnostico-integracion-flito-cliente-externo-y-aviso-de-cierre.md) (Propuesto) · Contrato para FLITO: [borrador D-17](borrador-contrato-D17-flito.md)
> Antecedente: [plan-pendientes-dictamen-a-flito.md §6 (D9)](plan-pendientes-dictamen-a-flito.md). Los puntos 1 a 3 de ese documento están desactualizados: el código ya tiene dictamen, override, derivación y saneamientos.

Este documento no cruza gates. No activa HUs, no crea work items y no se publicó en ADO ni en GitHub. La oleada era solo de diseño (decisión del usuario, 2026-11-18).

---

## 1. Contexto

FLITO es el sistema externo de operaciones. Hoy se integra solo con Trámites (core-api). Necesita tres cosas:

1. pedir a FLIT un diagnóstico vehicular de una o varias placas sin usar la pantalla de carga `.xlsx`;
2. consultar el estado y el resultado (veredicto, dictamen, PDF por vehículo, ZIP de evidencias);
3. recibir un aviso firmado cuando el lote termina.

Criterio de salida heredado de D9. FLITO crea un diagnóstico de N placas y recibe el aviso firmado cuando el lote pasa a `COMPLETADO` o `INCOMPLETO`. Si FLITO reintenta la misma solicitud, no se duplica el lote. Si se repite el mismo cierre, no se duplica el aviso. La carga `.xlsx` sigue funcionando igual.

## 2. Estado actual verificado (2026-11-18)

| # | Hecho | Fuente |
|---|---|---|
| H1 | Los clientes de máquina viven en core-api: tabla `integrations.external_clients` (DDL 125), secreto en Argon2id, rotación con ventana, bloqueo, permisos en JSON. | ADR-0067 |
| H2 | La tabla ya admite tipos ligados a una compañía: columnas `tipo` (`externa` \| `persona_natural`) y `company_id`, con `ck_external_clients_company_por_tipo` (`tipo = 'externa' OR company_id IS NOT NULL`). | `Ddl/162-HU14067-integracion-externa-por-compania.sql` |
| H3 | Permisos actuales: `external.tramites.read`, `external.tramites.pii.read`, `external.tramites.attachments.write` y, aparte, `platform.tramites.persona_natural`, que solo vale para su tipo (`EsValidoPara`). | `ExternalScopes.cs` |
| H4 | El pase externo es un JWT RS256 de 30 min. Su emisor es `flit-core-external` y su audiencia `flit-external`. Lo emite y lo valida el mismo proceso (`Flit.Api`): **no se publica ninguna clave pública ni JWKS**. Sin clave configurada fuera de Development, responde 503. | `ExternalJwtTokenIssuer.cs`, `TokenValidationExtensions.cs` |
| H5 | La cuota por cliente es de 120 peticiones por minuto en ventana deslizante. Corre después de la autorización y responde 429 con `Retry-After`. Vive en `Flit.Api/RateLimiting/ExternalClientQuotaMiddleware.cs`. | código |
| H6 | core-diagnostico solo autentica con `AddFlitPlatformAuthentication` (token de producto, audiencia `diagnostico`). `UseGuardSuscripcion` exige la compañía del token y Diagnóstico activo, y es *fail-closed*. No tiene esquema ExternalClient ni rutas `/integracion/*`. | `Program.cs`, ADR-0074 |
| H7 | La cabecera exige autor: `ServicioCabecera` responde 403 `SIN_USUARIO` si el token no trae usuario (`UsuarioCreadorId`). | `ServicioCabecera.cs:119` |
| H8 | La ingesta D1 ya está separada del archivo: `ServicioConfirmacionVehiculos.ConfirmarAsync(diagnosticoId, IReadOnlyList<VehiculoCapturado>, ct)` y `GuardarEnTransaccionAbiertaAsync`. Express ya hace cabecera y vehículo en una sola transacción. | código |
| H9 | La API de usuario trae listado, cabecera, carga y confirmación (multipart), grilla, historial, dictamen, PDF por vehículo, Excel, ZIP asíncrono (pedir, estado y archivo), reproceso y derivación. `diagnostico.v1.yaml` está en v1.6.0. | contrato |
| H10 | Eventos del exchange `flit.diagnostico`: `diagnostico.lote.terminado` (sin consumidor, deduplicación por `(diagnosticoId, version)`, se republica al recerrar), `diagnostico.dictamen.emitido` y `diagnostico.derivacion.solicitada`. Ninguno lleva PII. | `diagnostico-events.v1.yaml` |
| H11 | Los webhooks salientes se entregan con `notificaciones.webhook.send`. El servicio dueño arma y firma el cuerpo (la clave no sale de él); notificaciones aplica el filtro anti-SSRF, reintenta a los 10 s, 1 min y 10 min y deja los fallidos en `.dlq`. El contrato declara `origen` ∈ {`ot`, `ict`, `flito`}; el consumidor lo recorta a 20 caracteres y, por lo leído, no lo valida contra una lista. | `notificaciones-events.v1.yaml`, `TrabajoWebhookConsumer.cs` |
| H12 | ADR-0071: avisos a FLITO sobre trámites con `integrations.flito_sync_signals`. Por regla de D9 esa tabla **no** se reutiliza: es de trámites y su cola no tiene RLS. | ADR-0071 |
| H13 | Hay un precedente de cliente ligado a compañía con una fachada que rechaza cualquier otra empresa: Persona Natural (ADR-0075 de core-api, HU #14067). | ADR-0075 core-api |

**Hechos que faltan** (los debe verificar el explore o el backend al implementar):

- MF1. Si el broker deja publicar a `svc-diagnostico` en `flit.notificaciones` con la routing key `notificaciones.webhook.send` (permisos del usuario del vhost).
- MF2. Si file-manager emite URLs firmadas de vida corta o si las descargas tienen que pasar por el servicio. Esto afecta a la opción A3.
- MF3. En qué archivo está el handler del `POST /api/v1/external/auth/token`, para meter el claim de compañía y la audiencia por tipo.
- MF4. Si `diagnostico.dictamen.emitido` del cierre se escribe en la misma transacción que `diagnostico.lote.terminado`. Si no, hay que decidir el orden del aviso.

## 3. Alternativas

### 3.a Dónde vive el alta y la emisión del cliente de máquina

**A1 — Reutilizar ExternalClient de core-api: tipo nuevo `diagnostico` ligado a la compañía, scopes `external.diagnostico.*` y validación del pase en core-diagnostico.**

- Pros: FLITO usa la misma credencial y el mismo login que ya conoce (`POST /api/v1/external/auth/token`). Hay una sola pantalla de alta para el superadministrador y se reutilizan la rotación, el bloqueo y la bitácora. El modelo `tipo` + `company_id` ya existe (H2), así que solo se agrega un valor al check. El servicio dueño sirve sus propios datos y archivos, sin pasar por un intermediario.
- Contras: hay que publicar la clave pública del pase (H4), que hoy no sale de `Flit.Api`. La audiencia `flit-external` única no basta: hace falta una audiencia por producto. La cuota y la bitácora se tienen que portar o compartir con core-diagnostico. Además se enmienda ADR-0067, que limitaba el pase al prefijo `/api/v1/external` de core-api.
- Esfuerzo: **M**.
- Riesgos: si la audiencia se separa mal, un pase de Trámites entra en Diagnóstico o al revés. Rotar la clave del pase obliga a coordinar dos servicios.

**A2 — Alta propia en core-diagnostico (`diagnostico.clientes_integracion`, su propio login y su propio par de claves).**

- Pros: aislamiento total del producto y ninguna dependencia de core-api para emitir pases. Encaja con «cada producto, su API».
- Contras: sería la **tercera copia** del login, la rotación y el bloqueo (ICT, core-api, Diagnóstico). ADR-0067 ya aceptó dos y avisó del costo. Habría una segunda pantalla de alta y FLITO tendría que manejar dos credenciales y dos logins. También se duplican la bitácora y la cuota.
- Esfuerzo: **L**.
- Riesgos: divergencia de las reglas de seguridad entre copias, que el review no detecta.

**A3 — Fachada en core-api: `/api/v1/external/diagnostico/*` con el pase actual, reenviado por gRPC a core-diagnostico con token de servicio y `x-flit-tenant-id`.**

- Pros: Diagnóstico no cambia su autenticación. Se reutilizan sin cambios la cuota, la bitácora y el login (H5). Es coherente con ADR-0070 (gRPC entre servicios, REST hacia afuera) y con el precedente de Persona Natural (H13).
- Contras: core-api gana superficie de otro producto (REST en core-api, proto y servidor gRPC en Diagnóstico) y crece el acoplamiento de despliegue. El PDF y el ZIP tendrían que atravesar core-api o depender de URLs firmadas que no sabemos si existen (MF2). Cada ruta nueva toca dos servicios.
- Esfuerzo: **M-L**.
- Riesgos: archivos grandes en streaming por gRPC, y un cuello de botella en core-api para un producto que escala aparte.

### 3.b API de entrada

**B1 — JSON por placas en una ruta dedicada `/api/v1/diagnostico/integracion/diagnosticos`, reutilizando `ServicioCabecera` y `ConfirmarAsync` (H8) en una sola transacción, como Express.**
- Pros: una sola llamada y un lote completo; no queda ninguna cabecera colgada en `CREADO`. Aplica las mismas reglas de validación de D1. Contrato propio, versionado aparte de la API de usuario.
- Contras: hay que agregar un adaptador JSON → `VehiculoCapturado`, y la ruta exige el ajuste de autor de H7.
- Esfuerzo: **M**. Riesgos: que la validación JSON se separe de la del archivo (se mitiga pasando por la misma regla).

**B2 — Reutilizar las rutas de usuario (`POST /diagnosticos` + `/carga` + `/vehiculos` multipart) aceptando también el pase externo.**
- Pros: no hay ingesta nueva.
- Contras: FLITO tendría que fabricar un `.xlsx`. Son tres llamadas sin atomicidad (una cabecera en `CREADO` puede quedar huérfana). Los permisos de usuario se mezclan con los de máquina, y cualquier cambio de la pantalla rompe a FLITO.
- Esfuerzo: **S-M**. Riesgos: acoplamiento con la UI.

**B3 — Una llamada Express por placa.**
- Pros: ya existe.
- Contras: N lotes para N placas, sin aviso consolidado, y la cuota se agota en lotes grandes.
- Esfuerzo: **S**. Riesgos: ruido en el listado y en la facturación.

Lecturas (comunes a B1): estado y resumen, dictamen por vehículo, PDF por vehículo y ZIP asíncrono, todas bajo `/integracion/*` como adaptadores delgados sobre los servicios actuales.

### 3.c Aviso saliente

Transporte:

- **C1 — Consumidor propio en core-diagnostico de `diagnostico.lote.terminado`** (hoy sin consumidor, H10). Filtra los lotes de origen `INTEGRACION`, arma el cuerpo, lo firma con HMAC-SHA256 y deja `notificaciones.webhook.send` en la outbox. La bandeja del SDK deduplica por `eventId` y la entrega deduplica por `(diagnosticoId, version)`.
  - Pros: no toca el cierre, reutiliza el evento que ya existe y el aviso nunca llega antes que el dato (se lee después del commit). El patrón de entrega es el de ADR-0071 y no usa `flito_sync_signals`. Esfuerzo **S-M**.
  - Contras: el consumidor necesita acceso de lectura a los veredictos (en el mismo servicio no es un problema). Depende de RabbitMQ.
- **C2 — Tabla `diagnostico.avisos_integracion` escrita en la transacción del cierre más un despachador periódico** (calco de ADR-0071).
  - Pros: no depende del consumidor y permite reclamar y reintentar desde la base.
  - Contras: es una tabla y un despachador nuevos para un único camino de escritura, el cierre. ADR-0071 necesitaba el trigger porque había muchos caminos; aquí no. Esfuerzo **M**.
- **C3 — POST directo desde core-diagnostico.** Se descarta: contradice ADR-0064 y duplica los reintentos, la `.dlq` y el filtro anti-SSRF.

Contenido:

- **K1 — Solo el cierre**: ids, estado, versión y totales. FLITO pide el detalle después. Es lo más pequeño y puede salir sin dictamen.
- **K2 — Cierre más veredicto por vehículo y enlaces a la API** (`referenciaExterna`, `vehiculoId`, `veredicto`, `dictamenVersion`, rutas relativas). No lleva PII ni placa. A FLITO le ahorra N lecturas.
- **K3 — Resultado completo con PII.** Se descarta (Ley 1581): manda datos personales a un destino que reintenta y deja copias en `.dlq` y en `notificaciones.webhooks`.

### 3.d Compañía, cuotas, idempotencia y PII

Compañía (tenant):

- **T1 — Un cliente por compañía** (`tipo = 'diagnostico'`, `company_id` obligatorio, claim de compañía fijo en el pase). `UseGuardSuscripcion` funciona sin cambios y el aislamiento por compañía (ADR-0072) se cumple por construcción.
- **T2 — Un cliente `externa` para todas las compañías, que manda la empresa en una cabecera con lista blanca.** Requiere lectura entre compañías (ADR-0069) y una tabla de lista blanca, y un error de configuración expone a otras compañías.
- **T3 — Un cliente con varias compañías asignadas** (relación cliente↔compañía y la compañía elegida en el login, que entra en el pase). Es una evolución de T1 si FLITO opera para muchas compañías.

Recomendación: **T1** para la primera entrega y T3 como evolución si D-17 dice que FLITO opera para muchas compañías.

Autor (H7). Agregar a la cabecera `origen` (`USUARIO` \| `INTEGRACION`) y `cliente_integracion_id`, y dejar `UsuarioCreadorId` nulo solo cuando `origen = INTEGRACION`. Un check lo impone (`origen = 'USUARIO' OR cliente_integracion_id IS NOT NULL`). La otra opción es un usuario técnico por compañía, que se descarta: mezcla identidades de máquina y de personas, lo mismo que ADR-0067 opción A ya descartó.

Cuotas, en tres capas:

1. Cuota técnica por cliente: la ventana deslizante de H5, llevada a una librería compartida (`Flit.Suite.AspNetCore`) o portada tal cual. Clave `Diagnostico:Integracion:PeticionesPorMinuto`, con 60 propuestas.
2. Límites de negocio que ya existen: `limite_vehiculos_lote` de la configuración de la compañía (422 `LIMITE_SUPERADO`), `AddLimitesSolicitudes` (429 `DEMASIADAS_SOLICITUDES`) y la suscripción o crédito de la compañía.
3. Un tope de lotes en proceso por cliente (`Diagnostico:Integracion:LotesEnProcesoMax`, 5 propuestos): responde 429 `LOTES_EN_PROCESO`.

Los SLA de FLITO no se deciden aquí: se **escalan al Líder Técnico** (regla 6 del agente).

Idempotencia:

- La cabecera `Idempotency-Key` (UUID) es obligatoria en el `POST`. Se guarda en `diagnostico.solicitudes_integracion` con único `(tenant_id, cliente_integracion_id, clave)`, más el hash SHA-256 del cuerpo normalizado y el `diagnostico_id`.
- Misma clave con el mismo cuerpo: responde `200` con el mismo diagnóstico. Misma clave con otro cuerpo: `409 IDEMPOTENCIA_CONFLICTO`. La clave vale 72 h.
- La `referenciaExterna` del lote es única por `(tenant_id, cliente_integracion_id)`, como segunda red y para buscar por ella.
- El aviso se deduplica por `eventoId` y por `(diagnosticoId, version)`.

PII (Ley 1581):

| Dato | Sale a FLITO | Scope |
|---|---|---|
| ids, estados, totales, `referenciaExterna`, veredicto, códigos de motivo y alertamiento | Sí, en lecturas y en el webhook | `external.diagnostico.read` |
| Placa y VIN (los mandó FLITO) | Sí, en lecturas. **Nunca en el webhook** | `external.diagnostico.read` |
| Documento del locatario declarado | Nunca se devuelve completo; enmascarado `****1234` | — |
| Nombres y documentos de propietarios, texto redactado del dictamen, detalle por fuente, PDF, ZIP | Sí, en lecturas | `external.diagnostico.pii.read` |
| Cualquier PII en el webhook | **No** | — |

Cada lectura con `pii.read` deja una fila en una bitácora de accesos, solo de inserción (calco de `integrations.external_access_log`, con precedente local en `diagnostico.accesos_reporte` y la IP `@pii:medium`), en el schema `diagnostico`.

## 4. Recomendación

**A1 + B1 + C1/K2 + T1.**

- **A1** porque FLITO conserva una sola credencial y un solo login, el superadministrador una sola pantalla, y el servicio dueño sirve sus propios PDF y ZIP. El costo real (publicar la clave pública y separar la audiencia) es acotado y una sola vez. A2 sería la tercera copia del login. A3 mete a core-api en el camino de los archivos de otro producto.
- **B1** porque es atómico, reutiliza la ingesta D1 sin pasar por la pantalla y no ata a FLITO a la UI.
- **C1** porque el cierre ya publica `diagnostico.lote.terminado` en su transacción y nadie lo consume. Un consumidor propio basta y no hace falta una tabla nueva. **K2** si FLITO lo confirma en D-17; si no, sale como K1, que es un subconjunto compatible (los campos de veredicto son opcionales en el esquema).
- **T1** porque mantiene el aislamiento por compañía sin código nuevo de lectura entre compañías.

## 5. Diagrama de secuencia

```mermaid
sequenceDiagram
    autonumber
    participant F as FLITO
    participant A as core-api (/api/v1/external/auth/token)
    participant D as core-diagnostico (/api/v1/diagnostico/integracion)
    participant I as core-identity (suscripción)
    participant DB as BD diagnostico
    participant MQ as RabbitMQ (flit.diagnostico)
    participant C as core-consultas (motor)
    participant N as core-notificaciones

    F->>A: POST auth/token {clientId, clientSecret}
    A-->>F: JWT RS256 aud=flit-external-diagnostico, tenant=company_id, scope=external.diagnostico.*
    F->>D: POST /integracion/diagnosticos (Bearer, Idempotency-Key)
    D->>D: valida pase (clave pública del emisor externo, aud, scope), cuota por cliente
    D->>I: ¿Diagnóstico activo para la compañía? (guard, caché 1 min)
    I-->>D: activo
    D->>DB: BEGIN; busca Idempotency-Key
    alt clave ya vista con el mismo cuerpo
        DB-->>D: diagnostico_id
        D-->>F: 200 Diagnóstico existente
    else nueva
        D->>DB: cabecera (origen=INTEGRACION) + vehículos válidos (ConfirmarAsync) + celdas PENDIENTE + solicitud; COMMIT
        D-->>F: 201 {id, estado=EN_PROCESO, enlaces, rechazados[]}
    end
    D->>C: consultas por la cola durable (motor ADR-0075)
    C-->>D: resultados
    D->>DB: cierre del lote COMPLETADO/INCOMPLETO + dictamen + outbox diagnostico.lote.terminado
    DB-->>MQ: diagnostico.lote.terminado {diagnosticoId, version}
    MQ->>D: consumidor AvisoCierreIntegracion (bandeja, deduplica por eventId)
    D->>DB: ¿origen=INTEGRACION? lee veredictos; ¿aviso (id,version) ya enviado?
    D->>D: arma cuerpo K2 sin PII y firma HMAC-SHA256 (clave del ambiente)
    D->>DB: outbox notificaciones.webhook.send (origen=flito-diagnostico) + sello del aviso
    DB-->>N: notificaciones.webhook.send
    N->>F: POST webhook (X-Webhook-Signature, X-Flit-Evento, X-Correlation-Id)
    alt 2xx
        F-->>N: 200
    else fallo
        N->>N: reintentos 10 s / 1 min / 10 min, luego .dlq
    end
    F->>D: GET /integracion/diagnosticos/{id} (respaldo o detalle)
    F->>D: GET .../vehiculos/{vid}/pdf · POST/GET .../evidencias (scope pii.read)
```

## 6. Esbozo OpenAPI (archivo nuevo `contracts/openapi/diagnostico-integracion.v1.yaml`)

Se propone un archivo aparte de `diagnostico.v1.yaml`, porque el consumidor es externo y se versiona por su cuenta, igual que `external-api.v1.json` en core-api. Es una decisión abierta (DA-6).

```yaml
openapi: 3.1.0
info: { title: FLIT — API de integración de Diagnóstico Vehicular, version: 0.1.0-borrador }
servers: [{ url: https://{host}/api/v1/diagnostico/integracion }]
security: [{ clienteExterno: [] }]
paths:
  /diagnosticos:
    post:
      operationId: crearDiagnosticoIntegracion
      summary: Crea un lote con la lista de vehículos y lo encola (scope external.diagnostico.write)
      parameters:
        - { name: Idempotency-Key, in: header, required: true, schema: { type: string, format: uuid } }
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/SolicitudDiagnostico' } } }
      responses:
        '201': { description: Lote creado y EN_PROCESO, content: { application/json: { schema: { $ref: '#/components/schemas/DiagnosticoCreado' } } } }
        '200': { description: Repetición de la misma Idempotency-Key con el mismo cuerpo; mismo lote }
        '400': { description: Cuerpo mal formado o falta Idempotency-Key (IDEMPOTENCY_KEY_REQUERIDA) }
        '401': { description: Pase ausente, vencido o de otra audiencia }
        '403': { description: Sin scope; PRODUCTO_NO_HABILITADO }
        '409': { description: IDEMPOTENCIA_CONFLICTO | REFERENCIA_EXTERNA_DUPLICADA }
        '422': { description: SIN_VALIDOS | LIMITE_SUPERADO | FUENTE_NO_HABILITADA | TIPO_TRAMITE_REQUERIDO | … (mismas reglas que D1) }
        '429': { description: rate_limited | DEMASIADAS_SOLICITUDES | LOTES_EN_PROCESO, headers: { Retry-After: { schema: { type: integer } } } }
        '503': { description: CATALOGO_NO_DISPONIBLE, headers: { Retry-After: { schema: { type: integer } } } }
    get:
      operationId: buscarDiagnosticoIntegracion
      summary: Busca por referenciaExterna (scope external.diagnostico.read)
      parameters: [{ name: referenciaExterna, in: query, required: true, schema: { type: string, maxLength: 64 } }]
      responses: { '200': { description: Resumen, content: { application/json: { schema: { $ref: '#/components/schemas/ResumenDiagnostico' } } } }, '404': { description: NO_ENCONTRADO } }
  /diagnosticos/{id}:
    get:
      operationId: obtenerDiagnosticoIntegracion
      summary: Estado, totales y veredicto por vehículo (scope external.diagnostico.read)
      responses: { '200': { content: { application/json: { schema: { $ref: '#/components/schemas/ResumenDiagnostico' } } } }, '404': { description: NO_ENCONTRADO (no existe, es de otra compañía o no lo creó la integración) } }
  /diagnosticos/{id}/vehiculos/{vid}/dictamen:
    get: { operationId: obtenerDictamenIntegracion, summary: Dictamen vigente con texto redactado (scope external.diagnostico.pii.read) }
  /diagnosticos/{id}/vehiculos/{vid}/pdf:
    get: { operationId: descargarPdfIntegracion, summary: PDF del vehículo (scope external.diagnostico.pii.read), responses: { '200': { content: { application/pdf: {} } }, '409': { description: NO_DISPONIBLE (lote abierto) } } }
  /diagnosticos/{id}/evidencias:
    post: { operationId: pedirZipIntegracion, summary: Pide el ZIP de evidencias (asíncrono, 202 con eid; scope pii.read) }
  /diagnosticos/{id}/evidencias/{eid}:
    get: { operationId: estadoZipIntegracion, summary: Estado del ZIP }
  /diagnosticos/{id}/evidencias/{eid}/archivo:
    get: { operationId: descargarZipIntegracion, summary: Descarga del ZIP, responses: { '200': { content: { application/zip: {} } } } }
components:
  securitySchemes:
    clienteExterno: { type: http, scheme: bearer, bearerFormat: JWT, description: Pase de POST /api/v1/external/auth/token (core-api) con aud flit-external-diagnostico }
  schemas:
    SolicitudDiagnostico:
      type: object
      additionalProperties: false
      required: [referenciaExterna, nombre, modo, vehiculos]
      properties:
        referenciaExterna: { type: string, maxLength: 64, pattern: '^[A-Za-z0-9._-]+$', description: Id del pedido en FLITO. Sin datos personales. }
        nombre: { type: string, maxLength: 150 }
        modo: { type: string, enum: [SIMPLE, CON_ANALISIS] }
        tipoTramiteCodigo: { type: string, description: Obligatorio con CON_ANALISIS }
        fuentes: { type: array, items: { type: string }, description: Opcional; por defecto las habilitadas de la compañía }
        vehiculos:
          type: array
          minItems: 1
          description: Tope = limite_vehiculos_lote de la compañía
          items:
            type: object
            additionalProperties: false
            required: [referenciaExterna, identificador]
            properties:
              referenciaExterna: { type: string, maxLength: 64, description: Id del ítem en FLITO; vuelve en el webhook en lugar de la placa }
              identificador: { type: string, maxLength: 17, description: Placa (≤ 8) o VIN (17) }
              documentoLocatario:
                type: object
                description: Opcional; se cifra y nunca se devuelve completo
                properties: { tipo: { type: string }, numero: { type: string, maxLength: 20 } }
    DiagnosticoCreado:
      type: object
      required: [id, referenciaExterna, estado, aceptados, rechazados, enlaces]
      properties:
        id: { type: string, format: uuid }
        referenciaExterna: { type: string }
        estado: { type: string, enum: [EN_PROCESO] }
        aceptados: { type: integer }
        rechazados: { type: array, items: { type: object, properties: { referenciaExterna: { type: string }, code: { type: string, description: 'IDENTIFICADOR_INVALIDO | DUPLICADO' } } } }
        estimadoConsultas: { type: integer }
        enlaces:
          type: object
          properties:
            self: { type: string, description: Ruta relativa a la API de integración }
            app: { type: string, format: uri, description: Pantalla del lote en la suite; exige sesión de suite (X2) }
    ResumenDiagnostico:
      type: object
      properties:
        id: { type: string, format: uuid }
        referenciaExterna: { type: string }
        estado: { type: string, enum: [EN_PROCESO, COMPLETADO, INCOMPLETO] }
        version: { type: integer }
        terminadoEn: { type: [string, 'null'], format: date-time }
        vehiculos:
          type: array
          items:
            type: object
            properties:
              vehiculoId: { type: string, format: uuid }
              referenciaExterna: { type: string }
              identificador: { type: string, description: Placa o VIN tal como llegó }
              estado: { type: string }
              veredicto: { type: [string, 'null'], enum: [VIABLE, BLOQUEADO, null] }
              dictamenVersion: { type: [integer, 'null'] }
              motivos: { type: array, items: { type: string }, description: Códigos, sin texto libre }
    Error:
      type: object
      required: [code]
      properties: { code: { type: string }, campo: { type: string }, mensaje: { type: string } }
```

### Payload del webhook (K2)

`POST {url de FLITO}` · `Content-Type: application/json`

Cabeceras:

- `X-Webhook-Signature: sha256=<hex del HMAC-SHA256 de los bytes UTF-8 del cuerpo>`
- `X-Flit-Evento: diagnostico.lote.cerrado`
- `X-Flit-Evento-Id: <uuid>`
- `X-Correlation-Id`

```json
{
  "evento": "diagnostico.lote.cerrado",
  "eventoId": "0b6f…-uuid",
  "emitidoEn": "2026-11-18T15:04:05Z",
  "ambiente": "QA",
  "diagnostico": {
    "id": "uuid",
    "referenciaExterna": "FLITO-PED-000123",
    "estado": "COMPLETADO",
    "version": 3,
    "terminadoEn": "2026-11-18T15:04:00Z",
    "totales": { "vehiculos": 12, "viables": 9, "bloqueados": 2, "sinDictamen": 1 },
    "vehiculos": [
      { "vehiculoId": "uuid", "referenciaExterna": "ITEM-1", "veredicto": "VIABLE", "dictamenVersion": 1 }
    ],
    "enlaces": { "self": "/api/v1/diagnostico/integracion/diagnosticos/uuid" }
  }
}
```

Reglas del webhook:

- No lleva placa, VIN, documentos, nombres ni el texto del dictamen.
- `vehiculos` se omite si D-17 elige K1.
- Si el lote supera `Diagnostico:Integracion:Aviso:MaxVehiculos` (500 propuestos), `vehiculos` se omite y `totales` se mantiene; FLITO pagina por `GET`.
- Un recierre (reproceso o llenado manual) emite otro aviso con una `version` mayor. FLITO se queda con la de mayor `version`.

## 7. Modelo de datos conceptual

Contexto: **Diagnóstico** (schema `diagnostico`) e **Integraciones** (schema `integrations` de core-api, solo el tipo nuevo).

- `integrations.external_clients` (core-api): se agrega `'diagnostico'` a `ck_external_clients_tipo`; `company_id` obligatorio por el check actual.
- `diagnostico.diagnosticos`: columnas `origen`, `cliente_integracion_id` y `referencia_externa`; `usuario_creador_id` nulo solo en integración.
- `diagnostico.vehiculos`: `referencia_externa` (opcional).
- `diagnostico.solicitudes_integracion` (nueva): idempotencia.
- `diagnostico.avisos_integracion` (nueva, mínima): sello `(diagnostico_id, version)` del aviso enviado, para no repetirlo aunque la bandeja reciba el evento dos veces por caminos distintos (republicación).
- `diagnostico.accesos_integracion` (nueva): bitácora solo de inserción de las lecturas con PII.

DDL de referencia (borrador; lo materializa `database-agent` con el checklist §A):

```sql
-- core-api
ALTER TABLE integrations.external_clients DROP CONSTRAINT ck_external_clients_tipo;
ALTER TABLE integrations.external_clients
    ADD CONSTRAINT ck_external_clients_tipo CHECK (tipo IN ('externa', 'persona_natural', 'diagnostico'));

-- core-diagnostico (migración EF)
ALTER TABLE diagnostico.diagnosticos
    ADD COLUMN origen varchar(16) NOT NULL DEFAULT 'USUARIO',
    ADD COLUMN cliente_integracion_id uuid NULL,          -- id de integrations.external_clients (sin FK: otro servicio)
    ADD COLUMN referencia_externa varchar(64) NULL,
    ALTER COLUMN usuario_creador_id DROP NOT NULL,
    ADD CONSTRAINT ck_diagnosticos_origen CHECK (origen IN ('USUARIO', 'INTEGRACION')),
    ADD CONSTRAINT ck_diagnosticos_autor_por_origen CHECK (
        (origen = 'USUARIO' AND usuario_creador_id IS NOT NULL)
        OR (origen = 'INTEGRACION' AND cliente_integracion_id IS NOT NULL));
CREATE UNIQUE INDEX uq_diagnosticos_referencia_externa
    ON diagnostico.diagnosticos (tenant_id, cliente_integracion_id, referencia_externa)
    WHERE referencia_externa IS NOT NULL;

ALTER TABLE diagnostico.vehiculos ADD COLUMN referencia_externa varchar(64) NULL;

CREATE TABLE diagnostico.solicitudes_integracion (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    cliente_integracion_id uuid NOT NULL,
    clave uuid NOT NULL,
    hash_cuerpo char(64) NOT NULL,
    diagnostico_id uuid NOT NULL REFERENCES diagnostico.diagnosticos (id),
    created_at timestamptz NOT NULL DEFAULT now(),
    expira_en timestamptz NOT NULL,
    CONSTRAINT uq_solicitudes_integracion_clave UNIQUE (tenant_id, cliente_integracion_id, clave)
);
-- + RLS por tenant_id como el resto del schema (ADR-0072); columnas estándar del checklist §A las agrega database-agent

CREATE TABLE diagnostico.avisos_integracion (
    diagnostico_id uuid NOT NULL REFERENCES diagnostico.diagnosticos (id),
    version int NOT NULL,
    tenant_id uuid NOT NULL,
    evento_id uuid NOT NULL,
    enviado_en timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_avisos_integracion PRIMARY KEY (diagnostico_id, version)
);

CREATE TABLE diagnostico.accesos_integracion (      -- solo inserción, sin row_version ni auditoría
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id uuid NOT NULL,
    cliente_integracion_id uuid NOT NULL,
    ruta varchar(200) NOT NULL,
    diagnostico_id uuid NULL,
    ip inet NULL,                                     -- @pii:medium
    status smallint NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
```

## 8. Archivos que se tocarían en la implementación

**core-api**

- `services/core-api/src/Flit.Admin.Domain/Integrations/ExternalScopes.cs`: `DiagnosticoRead`, `DiagnosticoWrite`, `DiagnosticoPiiRead`, tipo `Diagnostico` en `ExternalIntegrationTypes` y su catálogo en `EsValidoPara` / `AlcancesDe`.
- `services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/<siguiente>-HUxxxx-integracion-diagnostico.sql`: el check del tipo.
- `services/core-api/src/Flit.Infrastructure/Security/ExternalJwtTokenIssuer.cs`: audiencia por tipo (`flit-external-diagnostico`), claim de compañía y exposición de la clave pública.
- El handler del login externo (MF3) y un endpoint de clave pública (`GET /api/v1/external/.well-known/jwks.json`) o la distribución de la clave pública PEM por configuración (DA-3).
- `services/core-api/src/Flit.Api/Authorization/ExternalClientAuthorization.cs`: las policies de `/api/v1/external/*` siguen rechazando el pase `diagnostico`.
- Frontend superadministrador: el alta de la integración de tipo `diagnostico` en la ficha de la compañía, como `persona_natural` (ruta por ubicar).
- `docs/integraciones/runbook-alta-clientes-flito.md`: sección Diagnóstico.

**core-diagnostico**

- `services/core-diagnostico/src/Flit.Diagnostico.Api/Program.cs`: registro del esquema del pase externo, la cuota y el consumidor del aviso.
- `.../Plataforma/ClienteIntegracion.cs` (nuevo): esquema JwtBearer del pase externo, policies `external.diagnostico.*` y lectura de la compañía del claim.
- `.../Plataforma/GuardSuscripcion.cs`: acepta la compañía del pase externo, con el mismo comportamiento *fail-closed*.
- `.../Plataforma/LimitesSolicitudes.cs`: la cuota por cliente y `LOTES_EN_PROCESO`.
- `.../Modulos/Integracion/IntegracionEndpoints.cs`, `ServicioSolicitudIntegracion.cs`, `LecturasIntegracion.cs`, `AvisoCierreIntegracion.cs` (consumidor), `FirmaWebhook.cs`, `BitacoraAccesosIntegracion.cs` (nuevos).
- `.../Modulos/Modulos.cs`: mapear `/integracion`.
- `.../Modulos/Diagnosticos/ServicioCabecera.cs`: autor por origen.
- `.../Modulos/Vehiculos/ServicioConfirmacionVehiculos.cs`: `referenciaExterna` en `VehiculoCapturado`.
- `.../Modulos/Diagnosticos/DiagnosticosEndpoints.cs`: el listado rotula el origen «Integración FLITO».
- `.../Persistence/DiagnosticoDb.cs`, `.../Persistence/Configuraciones/*` y `.../Persistence/Migrations/<nueva>`.
- Tests: `Flit.Diagnostico.Tests`, con fuga entre compañías por el pase externo, idempotencia, firma, deduplicación del aviso y scopes de PII.

**Contratos y otros**

- `contracts/openapi/diagnostico-integracion.v1.yaml` (nuevo).
- `contracts/asyncapi/notificaciones-events.v1.yaml`: `origen` suma `flito-diagnostico`.
- `contracts/asyncapi/diagnostico-events.v1.yaml`: sin cambio de forma; la descripción de `lote.terminado` pasa a decir que tiene consumidor.
- `frontend/`: rótulo de origen en el listado y el detalle del lote (rutas de Diagnóstico por ubicar).
- Infra: variables por ambiente `DIAGNOSTICO_INTEGRACION_AVISO_ENABLED`, `..._TARGET_URL`, `..._SIGNING_KEY` y `EXTERNALJWT_PUBLIC_KEY` en core-diagnostico; permiso de broker (MF1).

## 9. Estimado grueso (Fibonacci)

| # | HU | Tipo | SP |
|---|---|---|---|
| 1 | Tipo de integración `diagnostico` en core-api: scopes, check, audiencia y claim de compañía en el pase, clave pública | BACKEND | 5 |
| 2 | Alta de la integración Diagnóstico desde la ficha de la compañía (superadministrador) | FRONTEND | 3 |
| 3 | core-diagnostico acepta el pase externo: esquema, policies, guard, cuota y bitácora de accesos con PII | BACKEND | 5 |
| 4 | `POST /integracion/diagnosticos`: lote JSON por placas sobre la ingesta D1, con origen y autor de máquina (migración) | BACKEND | 5 |
| 5 | Idempotencia (`Idempotency-Key`, `referenciaExterna`) y `LOTES_EN_PROCESO` | BACKEND | 3 |
| 6 | Lecturas de integración: resumen, búsqueda, dictamen, PDF y ZIP con separación de scope PII | BACKEND | 5 |
| 7 | Aviso firmado al cerrar: consumidor de `lote.terminado`, cuerpo K2, firma, `webhook.send`, deduplicación | BACKEND | 5 |
| 8 | Rótulo «Integración FLITO» en el listado y el detalle | FRONTEND | 2 |
| 9 | Pruebas de contrato con FLITO y E2E del circuito en QA | QA | 3 |
| | **Total** | | **36 SP** (9 HUs, una Feature; épica por definir) |

Orden: 1 → 3 → 4 → 5 → 6 → 7, con 2 y 8 en paralelo y 9 al final. La HU 7 puede salir como K1 antes del dictamen si D-17 lo pide.

## 10. Notas operativas por agente

- **database-agent**: materializar §7 con el checklist §A. RLS en `solicitudes_integracion` y `avisos_integracion`. `accesos_integracion` va solo de inserción, como `external_access_log` (excepción documentada). El índice parcial único de `referencia_externa` lleva `tenant_id` primero.
- **backend-agent**: no reutilizar `integrations.flito_sync_signals`. El adaptador JSON pasa por la misma regla de validación que el archivo (no la reescribe). La audiencia del pase Diagnóstico debe ser distinta y las policies de core-api tienen que rechazarla, y al revés. La clave de firma no va a logs.
- **frontend-agent**: solo el rótulo de origen y el alta en la ficha de la compañía. El enlace `app` exige sesión de suite (X2).
- **qa-agent**: fuga (un pase de la compañía A no lee la B, 404), pase de Trámites rechazado en Diagnóstico, misma clave con otro cuerpo, reintento del webhook sin duplicados, recierre con versión mayor, `.xlsx` intacto.
- **security-agent**: revisar la tabla de PII de §3.d, que no haya PII en el webhook, la rotación de las dos claves (pase y firma) y que la bitácora de accesos tenga la IP marcada.
- **infra-agent**: distribuir la clave pública del pase y la clave de firma por ambiente, fuera de banda. Permiso de broker para `svc-diagnostico` en `flit.notificaciones` (MF1). Encender PDN solo tras el acuerdo con FLITO.

## 11. Decisiones abiertas

Ver §«Decisiones abiertas» del informe y el borrador D-17. Resumen: DA-1 (A1 vs A3), DA-2 (K1 vs K2), DA-3 (JWKS vs PEM por configuración), DA-4 (T1 vs T3), DA-5 (aviso por override del dictamen sin recierre), DA-6 (archivo OpenAPI aparte), DA-7 (SLA y cuotas, escalado al LT), DA-8 (épica o Feature de D9 y si la HU 7 sale antes del dictamen).
