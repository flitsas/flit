# ADR-0071: Avisos de sincronización de trámites a FLITO por webhook firmado sobre la outbox del SDK

**Fecha**: 2026-10-16
**Status**: Propuesto
**Deciders**: David Alejandro Chica Hernandez (PO), Claude Code (agente de implementación). Aceptación: Líder Técnico.
**Tags**: arquitectura, backend, integraciones, mensajería, seguridad
**Épica / Features**: #13614 / #13616, #13617, #13618 (HU #13622, #13623, #13624, #13625) · contraparte FLITO #13576
**Contrato**: `docs/integraciones/external-api-tramites-sync.md` v3.3 §8 · `contracts/asyncapi/domain-events.v1.yaml`

## Contexto

FLITO lee los trámites de FLIT 2 con el feed por cursor (ADR-0066 a ADR-0069), consultándolo cada 5 minutos. Se
necesita que un trámite radicado llegue en segundos. Restricciones:

1. El feed sigue siendo la fuente de verdad: el aviso solo indica cuándo leer y no lleva datos del trámite.
2. Ningún cambio del feed puede quedar sin aviso, por ningún camino de escritura (EF, gRPC de ICT, portal, lotes,
   SQL), incluidos los de las tablas hijas.
3. No reabrir el deadlock que corrigió el DDL 128: nada nuevo puede tomar `FOR KEY SHARE` sobre `procedure_instances`
   en cada cambio.
4. Respetar la plataforma actual (ADR-0064, Epic #13316): los webhooks salientes los entrega core-notificaciones a
   partir de un trabajo en la outbox del SDK, y la clave de firma no sale del servicio dueño.
5. El aviso no puede llegar antes de que el cambio aparezca en el feed.
6. La lectura cruza compañías (ADR-0069).

## Decisión

- **Registro**: tabla `integrations.flito_sync_signals` con una fila por trámite (UPSERT) y sin FK al trámite. La
  llena el trigger que ya sella `sync_version` (`tr_procedure_instances_sync_stamp`), en la misma transacción y solo
  cuando nace una versión. Se excluyen los migrados. Sin RLS: es una cola de plataforma de todas las compañías.
- **Despacho**: `FlitoSignalDispatcher` en core-api. Reclama en dos sentencias (bloquea el aviso con
  `FOR NO KEY UPDATE SKIP LOCKED` y luego lee el trámite con una foto nueva). Exige las dos condiciones de
  estabilidad del feed (5 s y `sync_xact < pg_snapshot_xmin`) y la regla «radicado al menos una vez». Firma el cuerpo
  con HMAC-SHA256 y, en la misma transacción, deja `notificaciones.webhook.send` (origen `flito`) en la outbox del
  SDK y sella el aviso.
- **Entrega**: core-notificaciones hace el POST, con su filtro anti-SSRF, reintentos (10 s, 1 min, 10 min) y `.dlq`.
- **Configuración**: `Integrations:FlitoSignals` por ambiente, apagada por defecto; una clave por ambiente,
  entregada fuera de banda.

## Alternativas consideradas

### Opción 1: Colgar a FLITO del outbox de cambios de estado (`procedure_state_change_outbox`)

**Pros:** - Ya existe, con reclamo, reintentos y dead-letter. - Sin tabla nueva.
**Cons:** - Una fila por transición, sin agrupar. - Solo recibe cambios de estado: los de actores, campos,
adjuntos y comercial no llegan, y el feed sí los entrega. - Lo escribe la aplicación, no la base: un camino que no
pase por el servicio de ciclo de vida no avisa.
**Esfuerzo:** S
**Riesgos:** cambios del feed que nunca se avisan.

### Opción 2: Emitir desde la aplicación (evento de dominio en cada guardado)

**Pros:** - Sin SQL en triggers. - Encaja con `IPlatformOutbox` directamente.
**Cons:** - Hay que tocar cada camino de escritura (EF, ICT, portal, lotes, migrador) y no cubre el SQL a mano. -
Sin agrupación, un guardado con varias hijas emitiría varios avisos.
**Esfuerzo:** L
**Riesgos:** avisos perdidos por un camino olvidado.

### Opción 3: POST directo desde core-api

**Pros:** - Sin depender de RabbitMQ ni de core-notificaciones. - Funcionaría hoy en QA y PDN.
**Cons:** - Duplica lo que ya hace core-notificaciones (reintentos, `.dlq`, anti-SSRF, registro) y contradice
ADR-0064 y la Epic #13316. - Habría que rehacerlo después.
**Esfuerzo:** M
**Riesgos:** dos caminos de webhooks con comportamientos distintos.

### Opción 4: Tabla agrupada desde el trigger y entrega por la outbox del SDK (elegida)

**Pros:** - Cubre todos los caminos de escritura y las tablas hijas, porque sale del mismo sello que el feed. -
Agrupa por trámite. - Reutiliza la entrega de la plataforma sin cambiarla. - La clave no sale de core-api.
**Cons:** - Un UPSERT más por transacción que cambia un trámite. - Una tabla que crece una fila por trámite (sin
purga). - QA y PDN dependen de que el corte de la Epic #13316 llegue a esos ambientes.
**Esfuerzo:** M
**Riesgos:** si el despachador no respetara las condiciones del feed, el aviso llegaría antes que el dato.

## Tradeoff aceptado

Se elige la **Opción 4**: un UPSERT más por cambio de trámite y una dependencia de la plataforma de mensajería, a
cambio de no perder ningún cambio del feed y de no duplicar la entrega de webhooks.

## Consecuencias

### Lo que se gana

- FLITO recibe los cambios de 6 a 8 s después de que ocurren, en lugar de hasta 5 minutos.
- La consulta periódica de FLITO pasa a ser un respaldo (cada hora).

### Lo que se pierde

- Independencia de la mensajería: sin RabbitMQ ni core-notificaciones no salen avisos (el feed sigue funcionando).

### Cambios operacionales

- Variables `FLITO_SIGNALS_ENABLED`, `FLITO_SIGNALS_TARGET_URL` y `FLITO_SIGNALS_SIGNING_KEY` por ambiente; PDN se
  enciende solo tras acordarlo con FLITO (runbook de clientes FLITO §6).
- Rotar la clave exige coordinar la hora con FLITO: no hay dos claves válidas a la vez.
- Los avisos fallidos quedan en `notificaciones.webhooks` y en `notificaciones.webhooks.salientes.dlq`.

## ADRs relacionados

- ADR-0064 — datos separados, eventos con outbox y RabbitMQ: la plataforma de entrega.
- ADR-0066 — marca de agua de sincronización: el sello del que sale el aviso.
- ADR-0067 — clientes de integración externos: el feed que el aviso dispara.
- ADR-0069 — lectura entre compañías acotada: por qué la cola no tiene RLS.

## Notas para agentes

- **Backend Agent**: el aviso sale solo del sello de sincronización. Una tabla hija nueva que deba llegar a FLITO
  se agrega a la propagación del DDL 123 y queda avisada sin más. No reclamar con una sola sentencia
  `FOR UPDATE ... JOIN` (lectura vieja del trámite).
- **QA Agent**: probar agrupación, tombstone, migrados, transacción más antigua abierta y escritores concurrentes
  (`FlitoSignalDispatcherPostgresTests`).
- **Security Agent**: la clave nunca va a logs, al repositorio ni a ADO; se firma tal cual (bytes UTF-8).
- **Infra Agent**: QA y PDN requieren RabbitMQ y core-notificaciones; vigilar la `.dlq` de webhooks salientes.
- **Frontend Agent**: sin impacto.
