# ADR-0064: Datos separados por producto, integración por eventos y reportes consolidados

**Fecha**: 2026-09-23 (revisado 2026-10-06; enmienda «Excepción: tablas técnicas de core-consultas» 2026-11-25, Propuesto)  
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

## Excepción: tablas técnicas de core-consultas

**Fecha**: 2026-11-25 · **Status**: Propuesto (no cambia el estado del resto del ADR) · **HU**: #14711 (registro de avisos), con precedente en la migración `20261120001000_ImpuestoSeguimientoTrabajos`.

### Contexto

El punto 1 de la decisión ya justifica que estas tablas no tengan FK a `identity.tenants` (A4): la empresa vive en otro servicio. El db-schema-validator dio `MISSING_1` sobre `consultas.vehicle_tax_webhook_deliveries` porque el resto de desvíos del checklist (`.claude/skills/db-schema-validator/checklist-validacion-schema.md`) no tenía respaldo en ningún ADR:

- **A10**: sin RLS ni política `tenant_isolation`.
- **A5, A6, A16**: sin `updated_by`, `deleted_at/by`, `row_version` ni triggers de `row_version` y `audit_log`.
- **A11**: el índice de purga es solo `(created_at)`, sin `tenant_id` al frente.

### Tablas cubiertas

| Tabla | Migración | Qué guarda | Retención y purga | Índice de purga (A11) |
|-------|-----------|------------|-------------------|-----------------------|
| `consultas.vehicle_tax_jobs` | `20261120001000_ImpuestoSeguimientoTrabajos` (más `ix_vehicle_tax_jobs_tenant_id_external_id` en `20261125231810_ImpuestoAvisosWebhook`) | `trabajo_id` del bus → id externo de la consulta de impuesto, estado y contadores de sondeo. Sin placa, documento ni respuesta | 24 h por `created_at`; la borra `SeguimientoImpuestoBd.ReservarAsync` | `ix_vehicle_tax_jobs_created_at` |
| `consultas.vehicle_tax_webhook_deliveries` | `20261125231810_ImpuestoAvisosWebhook` | `(tenant_id, delivery_id)` de cada aviso del proveedor ya verificado, `external_id`, `attempt`. Sin cuerpo, firma ni PII | 7 días por `created_at`; `PurgaAvisosImpuesto` cada hora | `ix_vehicle_tax_webhook_deliveries_created_at` |

`vehicle_tax_jobs` sí cambia de estado (lleva `updated_at`), pero es efímera: su historia no tiene valor de auditoría y desaparece a las 24 h. `vehicle_tax_webhook_deliveries` es inmutable: solo `INSERT … ON CONFLICT DO NOTHING` y purga.

### Condiciones (todas obligatorias para acogerse)

1. **Esquema y usuario de BD propios.** La tabla vive en `consultas` y solo la toca el usuario de BD de core-consultas (punto 1 de la decisión). Nadie más la lee por SQL.
2. **Filtro explícito por `tenant_id` en todo acceso** que no sea la purga. Única variante admitida: el acceso por una clave única global que el mismo servicio ligó a la empresa al crear la fila (`vehicle_tax_jobs.trabajo_id`, uuidv7 del sobre, reservado junto con su `tenant_id`). No se admite una búsqueda por un dato que llega de fuera sin pasar por la empresa, como `external_id` o `delivery_id` sin `tenant_id`.
3. **Filas inmutables o efímeras, sin PII.** Sin placa, VIN, documento, nombres, cuerpos de proveedor ni firmas. Ninguna columna necesita marca `@pii` (A15). Si una columna nueva trae PII, la tabla sale de la excepción.
4. **Purga acotada.** La retención es fija y corta (días, no meses), está en el código y en el `COMMENT` de la tabla. La purga filtra solo por `created_at`, que es el único acceso que recorre todas las empresas y por eso usa el índice sin `tenant_id` al frente. Los demás índices empiezan por `tenant_id` o son la PK o la clave única global de la condición 2.

Lo que la excepción **no** cubre: A1–A3, A7–A9, A12–A15 y A17 siguen aplicando. Una tabla con datos de negocio, con PII o que se conserve más allá de la purga no se acoge, aunque viva en `consultas`.

### Alternativas evaluadas

**Opción 1 — Aplicar el checklist completo (RLS, columnas estándar, triggers, `tenant_id` en el índice de purga).**
- Pros: sin excepciones que mantener; el validador pasa sin nota; uniforme con las tablas de negocio.
- Contras: RLS no protege nada mientras la app sea dueña de las tablas y no haya `FORCE ROW LEVEL SECURITY` (memoria de RLS decorativo; ADR-0072 B3); `row_version`, soft delete y `audit_log` sobre filas que se insertan y se purgan solo generan escrituras y volumen; un índice `(tenant_id, created_at)` no sirve a una purga que recorre todas las empresas.
- Esfuerzo: S. Riesgos: falsa sensación de aislamiento y triggers que fallan con la purga masiva.

**Opción 2 — Excepción acotada con condiciones verificables, en este ADR — elegida.**
- Pros: reutiliza el razonamiento del punto 1 y el precedente de A10/A11 de ADR-0072 para el schema de diagnóstico; las cuatro condiciones se revisan en code review; un solo lugar para todas las tablas técnicas del servicio.
- Contras: el aislamiento depende del filtro a mano en el código; cada tabla nueva tiene que añadirse a la tabla de arriba.
- Esfuerzo: S. Riesgos: un acceso nuevo sin `tenant_id`; se mitiga con la condición 2 en review y en el validador.

**Opción 3 — ADR nuevo solo para las tablas técnicas de core-consultas.**
- Pros: decisión con número propio y fácil de citar.
- Contras: parte en dos la decisión sobre los datos de core-consultas, que ya está aquí y sigue en Propuesto; añade un número más en un directorio que ya tiene duplicados.
- Esfuerzo: S. Riesgos: que los dos ADRs diverjan.

**Tradeoff aceptado:** se acepta que el aislamiento entre empresas dependa del filtro explícito y de la revisión, a cambio de no pagar RLS, triggers ni columnas que hoy no protegen nada en tablas efímeras sin PII.

### Condición de revisión

- **Si core-consultas activa `FORCE ROW LEVEL SECURITY`** o pasa a un rol de app que no es dueño de las tablas, esta excepción **se revisa**: o las dos tablas reciben política `tenant_isolation` (con un rol de purga que la omita), o se justifica de nuevo por qué quedan fuera.
- También se revisa si una tabla cubierta deja de cumplir una de las cuatro condiciones, o si su retención sube de 7 días.

### Notas para agentes

- **Database Agent / db-schema-validator**: una tabla de la lista con las cuatro condiciones cumplidas no es `MISSING` en A5, A6, A10, A11 (solo el índice de purga) ni A16. El `COMMENT` de la tabla debe decir la retención y «sin PII», y el remark de la migración debe citar esta sección.
- **Backend Agent**: toda consulta nueva sobre estas tablas lleva `tenant_id` en el `WHERE`, salvo la purga y el acceso por `trabajo_id`. Nada de PII en columnas nuevas.
- **Security Agent**: revisar la condición 2 en cada PR que toque `Impuesto/SeguimientoImpuestoBd.cs` o `Impuesto/Avisos/RegistroAvisosImpuestoBd.cs`.
- **Infra Agent**: avisar al arquitecto antes de activar `FORCE ROW LEVEL SECURITY` en core-consultas.

## ADRs relacionados

- ADR-0061, ADR-0062.
- ADR-0072 — mismo razonamiento de A10 (sin RLS sin `FORCE`) y A11 (índices de purga y de cola) para el schema de diagnóstico; la sección «Excepción: tablas técnicas de core-consultas» lo aplica a core-consultas.
- ADR-0079 — fachada del impuesto en core-consultas, dueña de `vehicle_tax_jobs` y `vehicle_tax_webhook_deliveries`.
- ADR-0059 (`docs/decisions`) — consumidores como `BackgroundService`.
- ADR-0070 — gRPC entre servicios, REST hacia afuera.

## Notas para agentes

- **Backend Agent**: publicar solo vía outbox; consumidores idempotentes por `eventId` con bandeja de entrada.
- **QA Agent**: pruebas de contrato de eventos; un consolidado nunca mezcla empresas; broker caído no pierde eventos.
- **Security Agent**: eventos sin datos personales más allá de las claves necesarias; un usuario de broker por servicio.
- **Infra Agent**: monitoreo de colas y mensajes muertos.
