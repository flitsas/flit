# ADR-0090: Diagnóstico — integración con FLITO con el cliente externo de core-api, una API de integración propia y un aviso firmado al cerrar el lote

**Fecha**: 2026-11-18
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT. La aceptación es exclusiva del Líder Técnico humano, en su propio PR (regla FLIT 15). El contenido del aviso y el modelo de compañía dependen del contrato D-17 con el equipo FLITO y del PO.
**Épica / Feature / HU**: ninguna todavía. D9 no tiene épica ni Feature (plan-pendientes-dictamen-a-flito.md §6).
**Tags**: arquitectura, backend, seguridad, integraciones, mensajería, diagnostico, ley-1581
**Supersedes**: ninguno.
**Enmienda**: ADR-0067 (core-api, Propuesto). El pase externo deja de valer solo en `/api/v1/external/*` de core-api: un tipo nuevo de integración, `diagnostico`, emite un pase con otra audiencia que valida core-diagnostico.
**Documentos**: `docs/diagnosticos/propuesta-integracion-flito-diagnostico.md` (diseño completo) · `docs/diagnosticos/borrador-contrato-D17-flito.md` (borrador para FLITO)

## Contexto

FLITO, el sistema externo de operaciones, necesita pedir diagnósticos vehiculares por placas, leer su resultado y saber cuándo termina el lote. Hoy solo se integra con Trámites:

- Los clientes de máquina viven en core-api (`integrations.external_clients`, ADR-0067). La tabla ya admite tipos ligados a una compañía (`tipo` y `company_id`, DDL 162).
- El pase RS256 lo emite y lo valida el mismo proceso. No se publica ninguna clave pública.
- core-diagnostico solo acepta el token de producto, y su guard de suscripción exige la compañía del token, con comportamiento *fail-closed*.
- La cabecera exige un usuario autor (`SIN_USUARIO`).
- La ingesta D1 ya recibe una lista de vehículos sin archivo (`ConfirmarAsync`).
- El cierre publica `diagnostico.lote.terminado` por la outbox y hoy no tiene consumidor.
- Los webhooks salen por `notificaciones.webhook.send`, firmados por el servicio dueño (ADR-0064, ADR-0071).
- `integrations.flito_sync_signals` es de Trámites y no se reutiliza.

Restricciones:

- Aislamiento por compañía (ADR-0072).
- Ley 1581: mínima PII fuera de FLIT y nunca en el aviso.
- No triplicar el login de máquina.
- La carga `.xlsx` no cambia.
- No decidir SLA externos sin el Líder Técnico.

## Decisión

1. **Alta y emisión del cliente en core-api** con un tipo nuevo, `diagnostico`, ligado a una compañía: un cliente por compañía (T1). Scopes nuevos: `external.diagnostico.read`, `external.diagnostico.write` y `external.diagnostico.pii.read`. El pase lleva la audiencia `flit-external-diagnostico` y el claim de compañía. core-diagnostico lo valida con la clave pública del emisor externo. Las policies de `/api/v1/external/*` rechazan ese pase, y el token de producto de Diagnóstico rechaza el pase de Trámites.
2. **API de integración propia** bajo `/api/v1/diagnostico/integracion/*`, con su contrato aparte (`contracts/openapi/diagnostico-integracion.v1.yaml`):
   - `POST /diagnosticos` recibe un JSON con las placas y crea el lote completo en una sola transacción, con la misma ingesta y las mismas reglas de D1.
   - Lecturas de resumen, dictamen, PDF y ZIP como adaptadores delgados de los servicios actuales.
   - `Idempotency-Key` obligatoria y `referenciaExterna` única por compañía y cliente.
   - La cabecera registra `origen = INTEGRACION` y el cliente; el usuario autor queda nulo solo en ese origen.
3. **Aviso al cerrar por un consumidor propio de `diagnostico.lote.terminado`**:
   - Filtra los lotes de integración y arma un cuerpo sin PII: ids, estado, versión, totales y, si D-17 lo confirma, veredicto por vehículo con la `referenciaExterna` del ítem en lugar de la placa.
   - Lo firma con HMAC-SHA256 con una clave por ambiente, distinta de la de Trámites, y deja `notificaciones.webhook.send` (origen `flito-diagnostico`) en la outbox.
   - Deduplica por `(diagnosticoId, version)`.
4. **PII por scope**:
   - `read`: ids, estados, veredicto, códigos, y la placa o el VIN que FLITO mandó.
   - `pii.read`: personas, texto del dictamen, PDF y ZIP, con una bitácora de accesos solo de inserción.
   - El documento del locatario nunca se devuelve completo.
5. **Cuotas en tres capas**: técnica por cliente (la ventana deslizante de core-api), los límites de negocio que ya existen y un tope de lotes en proceso por cliente. Los valores definitivos y el SLA los decide el Líder Técnico con FLITO.

## Alternativas consideradas

### Opción 1: Cliente externo de core-api validado en core-diagnostico (elegida)

**Pros:** - Una sola credencial y un solo login para FLITO, una sola pantalla de alta. - Reutiliza la rotación, el bloqueo y la bitácora de ADR-0067 y el modelo `tipo` + `company_id` del DDL 162. - Diagnóstico sirve sus propios PDF y ZIP, sin intermediario. - El guard de suscripción y el aislamiento por compañía funcionan sin lectura entre compañías.
**Cons:** - Hay que publicar la clave pública del pase (JWKS o PEM por configuración). - Una audiencia por producto. - La cuota y la bitácora se portan o se comparten. - Enmienda ADR-0067.
**Esfuerzo:** M
**Riesgos:** si la audiencia se separa mal, el pase de un producto entra en el otro. Rotar la clave obliga a coordinar dos servicios.

### Opción 2: Alta propia en core-diagnostico

**Pros:** - Aislamiento total del producto. - Sin dependencia de core-api para emitir pases.
**Cons:** - Tercera copia del login, la rotación y el bloqueo (ICT, core-api, Diagnóstico). - Segunda pantalla de alta. - FLITO maneja dos credenciales. - Bitácora y cuota duplicadas.
**Esfuerzo:** L
**Riesgos:** las reglas de seguridad divergen entre copias.

### Opción 3: Fachada en core-api reenviada por gRPC

**Pros:** - Diagnóstico no cambia su autenticación. - Se reutilizan la cuota, la bitácora y el login tal cual. - Coherente con ADR-0070 y con el precedente de Persona Natural.
**Cons:** - core-api gana superficie de otro producto y cada ruta nueva toca dos servicios. - El PDF y el ZIP cruzan core-api o dependen de URLs firmadas que no están verificadas. - Mayor acoplamiento de despliegue.
**Esfuerzo:** M-L
**Riesgos:** archivos grandes por gRPC y core-api como cuello de botella.

Subdecisiones evaluadas, con el detalle en la propuesta §3:

- **Entrada.** JSON por placas (elegida) frente a reutilizar las rutas multipart de usuario (tres llamadas no atómicas, FLITO fabrica un `.xlsx`) o Express por placa (N lotes).
- **Transporte del aviso.** Consumidor de `lote.terminado` (elegido) frente a una tabla de avisos y un despachador calcados de ADR-0071 (más código para un único camino de escritura) o un POST directo (descartado por ADR-0064).
- **Contenido del aviso.** Cierre más veredicto (K2, recomendado), solo cierre (K1, subconjunto compatible) o resultado con PII (descartado, Ley 1581).
- **Compañía.** Un cliente por compañía (T1, elegido), un cliente global con lista blanca (T2, pide lectura entre compañías) o varias compañías por cliente (T3, evolución).

## Tradeoff aceptado

Se acepta enmendar ADR-0067 y abrir la distribución de la clave pública del pase externo a un segundo servicio. A cambio, no se crea la tercera copia del login de máquina y no se mete core-api en el camino de los datos y archivos de Diagnóstico. Frente a la opción 3, el costo de A1 es fijo y se paga una vez (clave pública y audiencia). El de A3 crece con cada ruta y con el tamaño de cada archivo.

## Consecuencias

### Lo que se gana

- FLITO crea lotes de N placas sin la pantalla de carga y recibe un aviso firmado al cerrarse, sin PII.
- Los reintentos de FLITO no duplican lotes y los recierres no duplican avisos.
- `diagnostico.lote.terminado` gana su primer consumidor.

### Lo que se pierde

- El pase externo deja de ser asunto exclusivo de `Flit.Api`: la rotación de su par de claves se coordina con core-diagnostico.
- La cabecera admite un autor nulo (solo para `origen = INTEGRACION`, impuesto por un check).

### Cambios operacionales

- Variables nuevas por ambiente en core-diagnostico: la clave pública del pase externo y `DIAGNOSTICO_INTEGRACION_AVISO_ENABLED`, `_TARGET_URL` y `_SIGNING_KEY`. Apagadas por defecto. La clave de firma se entrega fuera de banda, una por ambiente.
- El usuario de broker de `svc-diagnostico` tiene que poder publicar `notificaciones.webhook.send` en `flit.notificaciones` (sin verificar).
- Runbook de alta de clientes FLITO: sección Diagnóstico, con identificadores únicos por ambiente y por compañía que no se reutilizan.
- PDN se enciende solo tras el acuerdo D-17 y el visto bueno del Líder Técnico sobre el SLA.

## ADRs relacionados

- ADR-0067 (core-api): clientes de integración externos. Se enmienda la audiencia y el alcance del pase.
- ADR-0071 (core-api): avisos de sincronización a FLITO. Se copia el patrón de entrega y no se reutiliza su tabla.
- ADR-0064: datos separados, outbox y RabbitMQ; la entrega de webhooks.
- ADR-0069 (core-api): lectura entre compañías. Explica por qué se descarta T2.
- ADR-0070: gRPC entre servicios y REST hacia afuera (alternativa 3).
- ADR-0072: aislamiento por tenant de Diagnóstico.
- ADR-0074: puerta mínima y guard de suscripción.
- ADR-0075 (decisions): consultas por RabbitMQ, el motor que corre el lote.
- ADR-0076 (decisions): respuesta completa con PII cifrada. Explica por qué el PDF y el ZIP piden `pii.read`.
- ADR-0077 (decisions): dictamen por reglas, la fuente del veredicto.
- ADR-0075 (core-api): Persona Natural, precedente de integración ligada a compañía.

## Notas para agentes

- **Backend Agent**: no reutilizar `integrations.flito_sync_signals`. El JSON entra por la misma regla de validación que el archivo. Las policies de core-api rechazan el pase `diagnostico` y Diagnóstico rechaza el pase `externa`. La clave de firma se firma tal cual (bytes UTF-8) y nunca va a logs.
- **Database Agent**: columnas `origen`, `cliente_integracion_id` y `referencia_externa` con check de autor por origen; tablas `solicitudes_integracion` y `avisos_integracion` con RLS; bitácora de accesos solo de inserción (excepción documentada, como `external_access_log`); check del tipo en `integrations.external_clients`.
- **Frontend Agent**: alta de la integración en la ficha de la compañía y rótulo «Integración FLITO» en el listado y el detalle del lote.
- **QA Agent**: fuga entre compañías por el pase, pase de otro producto rechazado, misma clave con otro cuerpo (409), webhook reintentado sin duplicados, recierre con versión mayor y `.xlsx` sin regresión.
- **Security Agent**: revisar la tabla de PII de la propuesta §3.d, que el webhook no lleve PII, la rotación de las dos claves y la IP marcada en la bitácora.
- **Infra Agent**: distribuir la clave pública y la de firma por ambiente, permiso de broker y vigilar `notificaciones.webhooks.salientes.dlq`.

## Referencias externas

- RFC 6750 (Bearer), RFC 7517 (JWKS), borrador IETF `httpapi-idempotency-key-header`.
