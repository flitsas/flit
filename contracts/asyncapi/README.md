# AsyncAPI — mensajes del bus de FLIT

Contratos de lo que viaja por RabbitMQ entre servicios ([ADR-0064](../../docs/decisions/ADR-0064-datos-separados-eventos-y-reportes.md),
contrato de plataforma §7). Las llamadas que esperan respuesta van por gRPC (`contracts/proto`) y lo que entra o
sale de FLIT por REST (`contracts/openapi`).

| Archivo | Exchange | Qué contiene |
|---|---|---|
| [`plataforma-events.v1.yaml`](plataforma-events.v1.yaml) | `flit.plataforma` | Productos encendidos, suspensiones y cambios de roles |
| [`tramites-events.v1.yaml`](tramites-events.v1.yaml) | `flit.tramites` | Cambio de estado del trámite y validaciones de identidad |
| [`consultas-events.v1.yaml`](consultas-events.v1.yaml) | `flit.consultas` | Medición de cada consulta a proveedores |
| [`notificaciones-events.v1.yaml`](notificaciones-events.v1.yaml) | `flit.notificaciones` | Trabajos en cola que recibe Notificaciones (correo) |
| [`domain-events.v1.yaml`](domain-events.v1.yaml) | — | Webhook HTTP saliente al Organismo de Tránsito (no es del bus) |

Todos los mensajes del bus usan el sobre común [`comun/sobre.v1.yaml`](comun/sobre.v1.yaml): `eventId` (UUIDv7,
llave de idempotencia), `type`, `version`, `occurredAt`, `tenantId`, `producer`, `correlationId` y `data`.

## Reglas

- Un mensaje se documenta aquí **antes** de publicarse.
- Nombre `<productor>.<entidad>.<hecho>`; exchange `topic` por productor, `flit.<productor>`.
- Publicación solo vía outbox; consumo idempotente por `eventId` con bandeja de entrada.
- Sin datos personales que el consumidor no necesite: ids, claves naturales y el cambio.
- Un cambio incompatible en `data` sube `version` y convive con la anterior mientras haya consumidores.

## Validación

CI (`contracts.yml`, job `validate-asyncapi`) valida todos los `.yaml` de esta carpeta con `@asyncapi/parser`
en versión fija. En local:

```bash
npm install --no-save --prefix /tmp/asyncapi @asyncapi/parser@3.4.0
ASYNCAPI_PARSER_DIR=/tmp/asyncapi node contracts/asyncapi/validate.cjs
```

> Historia: hasta 2026-10 el job llamaba a `@asyncapi/cli validate events.v1.yaml`, pero el archivo se había
> renombrado para esquivar un CLI roto y el paso nunca validaba nada
> ([detalle](../../docs/ci/asyncapi-cli-broken-generator-hooks.md)).
