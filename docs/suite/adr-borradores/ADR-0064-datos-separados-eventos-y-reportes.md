# ADR-0064: Datos separados por producto, integración por eventos y reportes consolidados

**Fecha**: 2026-09-23  
**Status**: BORRADOR (pasa a Propuesto al versionarse en `docs/decisions/`)  
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), arquitectura, PO  
**Tags**: datos, integración, eventos, RabbitMQ, outbox, reportes, asyncapi  
**Base de código**: `origin/develop@e8b7ca65`

## Contexto

Decisiones de negocio para la suite (ADR-0061):

- Vehículo y persona no son entidades maestras compartidas: cada producto tiene las suyas, pero deben integrarse con facilidad.
- El aislamiento por schema es suficiente.
- Reportes por producto; las empresas con varios productos necesitan algunos consolidados fuera de cada producto.
- Hay presupuesto para un broker.

Estado actual:

- `core-api` usa outbox con `BackgroundService` (`IdentityValidationOutbox`, `ProcedureStateChangeOutbox`). El publicador RabbitMQ existe solo como stub. No hay RabbitMQ desplegado.
- La analítica consulta con SQL crudo cruzando schemas, donde ningún filtro de EF protege el tenant.
- AsyncAPI tiene un solo canal (`contracts/asyncapi/domain-events.v1.yaml`).

## Decisión

1. **Cada producto es dueño de sus datos**, en su schema y con usuario de BD propio. Ningún servicio lee por SQL el schema de otro.
2. **Claves naturales comunes** en `services/shared/Flit.Platform.Contracts`: `Placa`, `Vin`, `DocumentoIdentidad` (tipo + número), con la misma normalización y validación en todos los productos.
3. **Eventos de dominio** publicados con outbox a RabbitMQ (exchange por producto, nombres versionados), documentados en AsyncAPI y validados en CI. Todo evento lleva `event_id`, `tenant_id`, `occurred_at` y las claves naturales pertinentes.
4. **APIs de consulta** entre servicios con token de cliente (ADR-0062) para lecturas puntuales.
5. **Reportes consolidados**: cada producto publica hechos reportables; la plataforma los consume en un schema `reporting` de solo lectura, filtrado por `tenant_id`, y el hub arma ahí los reportes especiales.

## Alternativas consideradas

### Opción 1: Maestro compartido de vehículo y persona en la plataforma

**Pros:**
- Un registro por placa o documento en toda la suite; cruces inmediatos.

**Cons:**
- Contradice la decisión de negocio.
- Acopla los modelos: un cambio pedido por Flotas afecta a Comparendos.
- La plataforma se vuelve dueña de datos de dominio.

**Esfuerzo:** M  
**Riesgos:** Cuello de botella de cambios en la plataforma.

### Opción 2: Lectura SQL cruzada entre schemas

**Pros:**
- Rápida; es lo que hace la analítica actual.

**Cons:**
- Rompe la independencia de despliegue.
- Repite el modo de falla silenciosa de tenant documentado en la analítica.
- Impide mover un producto a otro clúster.

**Esfuerzo:** S  
**Riesgos:** Fugas de datos entre empresas sin error visible.

### Opción 3: Datos propios, claves naturales, eventos con outbox, APIs y read-model de reportes — elegida

**Pros:**
- Productos independientes e integrables por placa, VIN o documento.
- Reutiliza el patrón outbox que el equipo ya opera.
- Consolidados sin permisos cruzados de BD.

**Cons:**
- Consistencia eventual en los consolidados.
- RabbitMQ pasa a ser infraestructura crítica.

**Esfuerzo:** M  
**Riesgos:** Eventos mal versionados rompen consumidores; se mitiga con validación AsyncAPI en CI.

## Tradeoff aceptado

Se elige la **Opción 3**: consistencia eventual en los consolidados a cambio de independencia real entre productos y de no repetir el SQL cruzado.

## Consecuencias

### Lo que se gana

- Productos desacoplados en datos y despliegue, integrables por identificadores estables.

### Lo que se pierde

- Cruces instantáneos por SQL entre productos.

### Cambios operacionales

- RabbitMQ en k3s con usuario por servicio, colas de reintento y de mensajes muertos.
- Un archivo AsyncAPI por producto en `contracts/asyncapi/`.
- El publicador RabbitMQ de `core-api` pasa de stub a implementación real.

## ADRs relacionados

- ADR-0061, ADR-0062.
- ADR-0059 — consumidores como `BackgroundService`.

## Notas para agentes

- **Backend Agent**: publicar solo vía outbox; consumidores idempotentes por `event_id`.
- **QA Agent**: pruebas de contrato de eventos; un consolidado nunca mezcla empresas.
- **Security Agent**: eventos sin datos personales más allá de las claves necesarias.
- **Infra Agent**: monitoreo de colas y mensajes muertos.
