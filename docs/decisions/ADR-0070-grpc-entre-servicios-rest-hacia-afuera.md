# ADR-0070: gRPC entre servicios, REST hacia afuera

**Fecha**: 2026-10-06  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), arquitectura  
**Tags**: arquitectura, backend, integración, gRPC, REST, OpenAPI, protobuf, buf  
**Base de código**: `origin/develop@ed34d85b`  
**Epic**: #13316 (Feature #13317, HU #13325)

## Contexto

La plataforma se separa en servicios: Identidad ya salió de `core-api`, y Consultas y Notificaciones siguen (Epic #13316). Hace falta una sola forma de que un servicio pida algo a otro y espere la respuesta. Hoy conviven dos:

- **gRPC**: `core-ict` llama a `core-api` (`IctOrchestration`, `IctConsultation`) por el puerto interno `8082`, sin publicar, con un secreto HMAC compartido (esquema `IctService`). Los contratos viven en `Flit.Ict.Grpc.Contracts`.
- **REST**: el contrato de plataforma v1 (§6) define las llamadas de servicio como REST: `PUT /platform/products/{code}/manifest` (implementado en `core-identity`, scope `platform.manifest`) y `POST /platform/consultas/{fuente}` (sin implementar).

Hacia afuera todo es REST: los frontends llaman a su propio servidor de Next.js, los integradores usan `/api/v1/external/*` (ADR-0067) y los proveedores avisan por webhook.

Lo asíncrono (eventos y trabajos en cola) se decide aparte, en ADR-0064.

## Decisión

1. **Las llamadas síncronas entre servicios usan gRPC** sobre HTTP/2 en la red interna, en un puerto propio de cada servicio, sin publicar en la VPS y sin pasar por nginx ni por el gateway.
2. **Todo lo que entra o sale de FLIT es REST con OpenAPI**: navegador (a través del servidor de Next.js), integradores, avisos de proveedores, webhooks salientes y consultas a proveedores.
3. **Contratos** en `contracts/proto/flit/<servicio>/v<n>/`, paquete `flit.<servicio>.v<n>`; tipos comunes y motivos de error en `flit.platform.v1`. `buf lint` y `buf breaking` corren en CI. El código se genera con `Grpc.Tools` en el build, en un proyecto `Flit.<Servicio>.Grpc.Contracts` por servicio.
4. **Autenticación**: token de servicio emitido por Identidad (client credentials, ADR-0062, contrato §3) en la metadata `authorization`. La empresa viaja en `x-flit-tenant-id`; la correlación en `x-correlation-id` y `traceparent`. El servidor rechaza tokens de usuario.
5. **Errores**: estado gRPC más `google.rpc.ErrorInfo` con `reason` igual al `code` del contrato §10. Si el error sale hacia afuera, el borde lo traduce a RFC 7807.
6. **Resiliencia por defecto en el SDK**: deadline de 3 s que se propaga, reintentos solo ante `UNAVAILABLE` en métodos idempotentes, corte de circuito y `grpc.health.v1`.
7. **Un caso de uso, varias puertas**: los métodos gRPC y los endpoints REST son delgados y llaman al mismo handler de la capa Application. La lógica de negocio no conoce el protocolo.
8. **Lo que ya existe no se rompe**: `PUT /platform/products/{code}/manifest` sigue siendo REST mientras sus consumidores lo usen. No se crean endpoints REST nuevos para llamadas entre servicios.

## Alternativas consideradas

### Opción 1: REST con OpenAPI para todo, también entre servicios

**Pros:**
- Un solo tipo de contrato y de herramienta en toda la plataforma.
- Se depura con curl o Postman y pasa por cualquier proxy sin configuración.
- El equipo ya lo domina.

**Cons:**
- Contratos más débiles: los clientes generados desde OpenAPI se desalinean si no hay pruebas de contrato estrictas.
- Sin deadlines propagados ni streaming.
- ICT tendría que migrar a REST o quedar como excepción.

**Esfuerzo:** S  
**Riesgos:** cambios que rompen consumidores sin que CI lo detecte.

### Opción 2: gRPC entre servicios, REST hacia afuera — elegida

**Pros:**
- Contratos tipados y verificados en CI con `buf breaking`: un cambio que rompe no se fusiona.
- Deadlines propagados entre saltos, mejor manejo de servicios lentos.
- Continúa el precedente que ya opera ICT.
- Los integradores y frontends no se enteran: siguen en REST.

**Cons:**
- Dos tipos de contrato (`.proto` interno, OpenAPI externo).
- HTTP/2 en la red interna y balanceo en el cliente cuando haya réplicas.
- Depuración con `grpcurl` en lugar de curl.

**Esfuerzo:** M  
**Riesgos:** con réplicas, todas las llamadas pueden ir a una sola por la conexión persistente; se mitiga con balanceo `round_robin` sobre DNS al pasar a k3s.

### Opción 3: gRPC para todo, también hacia afuera (gRPC-Web o transcodificación en el gateway)

**Pros:**
- Un solo tipo de contrato.

**Cons:**
- Los integradores y los navegadores necesitan clientes especiales o transcodificación.
- Rompe la API externa existente (ADR-0067) y los webhooks.
- Más infraestructura en el borde.

**Esfuerzo:** L  
**Riesgos:** fricción con integradores y regresión en lo que ya funciona.

## Tradeoff aceptado

Se elige la **Opción 2**: mantener dos tipos de contrato a cambio de contratos internos estrictos, verificados en CI, y de no tocar nada de lo que ven los clientes. El costo de la Opción 1 aparece justo donde más crece la plataforma: muchos servicios internos cambiando a la vez.

## Consecuencias

### Lo que se gana

- Un cambio en un contrato interno que rompe a un consumidor se detecta antes del merge.
- Un servicio lento no retiene a quien lo llama más allá del deadline.
- Los servicios nuevos (Consultas, Notificaciones, los productos) nacen con la misma forma de llamar y ser llamados.

### Lo que se pierde

- La sencillez de depurar todo con las mismas herramientas.
- Un mismo endpoint ya no sirve a la vez a integradores y a servicios: hace falta exponer las dos puertas cuando se necesiten.

### Cambios operacionales

- Cada servicio expone un puerto gRPC interno sin publicar, como el `8082` que ya usa `core-api` para ICT. nginx no cambia y no hay dominios ni certificados nuevos.
- CI gana `buf lint` y `buf breaking` sobre `contracts/proto`.
- Reflection de gRPC solo en DEV.

## ADRs relacionados

- ADR-0062 — tokens de servicio emitidos por Identidad.
- ADR-0064 — eventos y trabajos en cola (lo asíncrono).
- ADR-0065 — Consultas como servicio, primer servicio con API gRPC nueva.
- ADR-0067 (`services/core-api/docs/adr`) — API para integradores externos, que sigue en REST.

## Notas para agentes

- **Backend Agent**: métodos gRPC delgados sobre handlers de Application; nunca lógica en el servicio gRPC. Clientes solo a través del SDK, nunca `GrpcChannel` suelto.
- **QA Agent**: probar el rechazo de tokens de usuario, la falta de `x-flit-tenant-id` y el vencimiento del deadline.
- **Security Agent**: los puertos gRPC nunca se publican; un token de servicio con scope mínimo por servicio.
- **Infra Agent**: HTTP/2 sin TLS en la red interna de Docker; en k3s, servicio headless y balanceo en el cliente.

## Referencias externas

- gRPC status codes y `google.rpc.ErrorInfo` (Google API design guide).
- `buf` — lint y detección de cambios que rompen en Protobuf.
