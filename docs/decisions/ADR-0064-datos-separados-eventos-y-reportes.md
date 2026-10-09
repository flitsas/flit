# ADR-0064: Datos separados por producto, integración por eventos y reportes consolidados

**Fecha**: 2026-09-23 (revisado 2026-10-06)  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), arquitectura, PO  
**Tags**: datos, integración, eventos, RabbitMQ, outbox, inbox, reportes, asyncapi  
**Base de código**: `origin/develop@ed34d85b`

## Contexto

Decisiones de negocio para la suite (ADR-0061):

- Vehículo y persona no son entidades maestras compartidas: cada producto tiene las suyas, pero deben integrarse con facilidad.
- El aislamiento por schema es suficiente.
- Reportes por producto; las empresas con varios productos necesitan algunos consolidados fuera de cada producto.
- Hay presupuesto para un broker.

Estado actual:

- `core-api` usa outbox con `BackgroundService` (`IdentityValidationOutbox`, `ProcedureStateChangeOutbox`). El publicador RabbitMQ existe solo como stub (`RabbitMqIdentityValidationEventPublisher`). No hay RabbitMQ desplegado.
- La analítica consulta con SQL crudo cruzando schemas, donde ningún filtro de EF protege el tenant.
- AsyncAPI tiene un solo canal (`contracts/asyncapi/domain-events.v1.yaml`, webhook del OT).

Revisión del 2026-10-06: se fijan el despliegue del broker mientras llega k3s, la librería, el formato de los mensajes y las convenciones de nombres, acordados para la Epic #13316. Las llamadas síncronas entre servicios pasan a ADR-0070.

## Decisión

1. **Cada producto es dueño de sus datos**, en su schema y con usuario de BD propio. Ningún servicio lee por SQL el schema de otro.
2. **Claves naturales comunes** en `Flit.Platform.Contracts`: `Placa`, `Vin`, `DocumentoIdentidad` (tipo + número), con la misma normalización y validación en todos los productos.
3. **Eventos de dominio y trabajos en cola** publicados con outbox a RabbitMQ, documentados en AsyncAPI y validados en CI:
   - Un exchange `topic` por productor, `flit.<productor>`; tipo de evento `<productor>.<entidad>.<hecho>`; cola `<consumidor>.<propósito>`; mensajes muertos en `<cola>.dlq`.
   - Mensajes en **JSON** con el sobre del contrato de plataforma §7 (`eventId` uuidv7, `type`, `version`, `occurredAt`, `tenantId`, `producer`, `correlationId`, `data`). `eventId`, `type`, `correlationId` y `traceparent` viajan también como propiedades del mensaje.
   - Publicación **solo** vía outbox en la misma transacción del cambio, con confirmación del broker. Entrega al menos una vez.
   - Consumo con **bandeja de entrada** `(event_id, consumidor)` en el schema del consumidor: idempotente por `eventId`. Reintentos con espera creciente (10 s, 1 min, 10 min) y luego `.dlq` con alerta.
   - El orden entre mensajes no está garantizado; quien lo necesite compara `occurredAt` o una versión de la entidad.
   - Los eventos llevan ids, claves naturales y el cambio; nada de datos personales que el consumidor no necesite.
4. **Llamadas síncronas** entre servicios por gRPC con token de servicio (ADR-0070, ADR-0062).
5. **Reportes consolidados**: cada producto publica hechos reportables; la plataforma los consume en un schema `reporting` de solo lectura, filtrado por `tenant_id`, y el hub arma ahí los reportes especiales.
6. **Broker y librería**: RabbitMQ como un contenedor por ambiente en la misma VPS, con volumen persistente, un virtual host por ambiente y un usuario por servicio (escribe solo en su exchange, lee solo sus colas), hasta la migración a k3s. Se usa el cliente oficial `RabbitMQ.Client` envuelto en el SDK de plataforma, sobre el patrón outbox que ya se opera.

## Alternativas consideradas

### Opción 1: Maestro compartido de vehículo y persona en la plataforma

**Pros:**
- Un registro por placa o documento en toda la suite; cruces inmediatos.

**Cons:**
- Contradice la decisión de negocio.
- Acopla los modelos: un cambio pedido por Flotas afecta a Comparendos.
- La plataforma se vuelve dueña de datos de dominio.

**Esfuerzo:** M  
**Riesgos:** cuello de botella de cambios en la plataforma.

### Opción 2: Lectura SQL cruzada entre schemas

**Pros:**
- Rápida; es lo que hace la analítica actual.

**Cons:**
- Rompe la independencia de despliegue.
- Repite el modo de falla silenciosa de tenant documentado en la analítica.
- Impide mover un producto a otro clúster.

**Esfuerzo:** S  
**Riesgos:** fugas de datos entre empresas sin error visible.

### Opción 3: Datos propios, claves naturales, eventos con outbox e inbox, llamadas gRPC y read-model de reportes — elegida

**Pros:**
- Productos independientes e integrables por placa, VIN o documento.
- Reutiliza el patrón outbox que el equipo ya opera.
- Consolidados sin permisos cruzados de BD.

**Cons:**
- Consistencia eventual en los consolidados.
- RabbitMQ pasa a ser infraestructura crítica.

**Esfuerzo:** M  
**Riesgos:** eventos mal versionados rompen consumidores; se mitiga con validación AsyncAPI en CI.

Sobre la librería del broker se evaluaron también MassTransit (de pago desde la v9) y Wolverine (MIT). Se descartan por ahora: el outbox propio ya existe y el cliente oficial deja la plataforma sin atarse a una licencia. Si más adelante hacen falta sagas, se evalúa Wolverine.

## Tradeoff aceptado

Se elige la **Opción 3**: consistencia eventual en los consolidados a cambio de independencia real entre productos y de no repetir el SQL cruzado.

## Consecuencias

### Lo que se gana

- Productos desacoplados en datos y despliegue, integrables por identificadores estables.
- Un efecto que falla (un correo, un webhook) no se pierde: queda en su `.dlq`.

### Lo que se pierde

- Cruces instantáneos por SQL entre productos.

### Cambios operacionales

- RabbitMQ como contenedor por ambiente en la VPS (150 a 300 MB), con volumen persistente, vhost por ambiente y usuario por servicio; pasa a k3s con lo demás.
- Un archivo AsyncAPI por productor en `contracts/asyncapi/`.
- El publicador RabbitMQ de `core-api` pasa de stub a implementación real.
- Limpieza diaria: 7 días de filas publicadas en la outbox y 30 en la bandeja de entrada. Los mensajes muertos no se borran solos.

## ADRs relacionados

- ADR-0061, ADR-0062.
- ADR-0059 (`docs/decisions`) — consumidores como `BackgroundService`.
- ADR-0070 — gRPC entre servicios, REST hacia afuera.

## Notas para agentes

- **Backend Agent**: publicar solo vía outbox; consumidores idempotentes por `eventId` con bandeja de entrada.
- **QA Agent**: pruebas de contrato de eventos; un consolidado nunca mezcla empresas; broker caído no pierde eventos.
- **Security Agent**: eventos sin datos personales más allá de las claves necesarias; un usuario de broker por servicio.
- **Infra Agent**: monitoreo de colas y mensajes muertos.
