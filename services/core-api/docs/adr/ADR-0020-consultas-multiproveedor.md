# ADR-0020: Capa multi-proveedor de consultas externas (trámites runtime)

**Fecha**: 2026-06-18
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, Samuel Cardenas (HU #10201, Feature #10128)
**Tags**: arquitectura, backend, integraciones, tramites, runtime, consultas

## Contexto

La operación runtime de trámites (HU #10201, Feature #10128) necesita consultar fuentes externas durante la ejecución de un trámite para dos fines:
1. **Panel semáforo (preflight)**: validar el estado del vehículo/actores antes de avanzar.
2. **Hidratar campos bloqueados**: poblar `field_values` con datos provenientes de la fuente externa.

Hoy el único proveedor real es **Verifik** (`https://api.verifik.co`, endpoints `/v2/co/runt/vehicle-by-vin` y `/vehicle-by-plate`).

A futuro (post-#10128) la organización migrará a un **gateway propio de integraciones (Flit Integrations / prototipo Johan.Jimenez)** que centralizará los proveedores externos. Por ello no queremos acoplar el motor de trámites a Verifik: el handler de consultas no debe conocer detalles del proveedor concreto.

## Decisión

Introducir una abstracción de proveedor de consultas y un registro que la resuelve dinámicamente:

- **`IConsultationProvider`**: expone `Key` y `ConsultAsync(ConsultationContext) -> ConsultationResult`, con un resultado **normalizado**:
  `{ provider, overall(green|yellow|red), checks[](status ok|warn|fail|unknown), hydratedFields[] }`.
- **`IConsultationProviderRegistry`**: resuelve el proveedor a partir de `consultation_templates.external_refs.provider`.

Implementaciones:
- **`VerifikConsultationProvider`** — `HttpClient` tipado, MVP real contra `api.verifik.co`.
- **`FlitIntegrationsGatewayProvider`** — stub `NotConfigured`, placeholder de la migración futura al gateway Johan.

El endpoint `POST /api/v1/tramites/instances/{id}/consultations/{templateCode}` (tenanted, header `X-Tenant-Id`) orquesta vía `RunConsultationHandler`, que persiste los `hydratedFields` como `field_values` con `source="consultation"`.

## Alternativas consideradas

### Opción A: Llamar Verifik directo desde el handler

**Pros:** menos piezas, implementación inmediata.
**Cons:** acopla el motor de trámites a Verifik, no testeable de forma aislada, no future-proof frente al gateway.
**Esfuerzo:** S
**Riesgos:** reescritura completa al migrar al gateway Flit Integrations.

### Opción B: Integrar ya el gateway externo (Flit Integrations / Johan)

**Pros:** evita una migración futura.
**Cons:** el gateway no está listo para #10128.
**Esfuerzo:** L (bloqueante por dependencia externa)
**Riesgos:** bloquea la entrega de #10128/#10201.

### Opción C: Procesamiento en cola / asíncrono

**Pros:** desacopla latencia de la consulta externa.
**Cons:** complejidad innecesaria para el MVP; el preflight es síncrono por naturaleza.
**Esfuerzo:** M
**Riesgos:** sobre-ingeniería sin necesidad funcional.

### Opción D: Abstracción `IConsultationProvider` + registry (elegida)

**Pros:** desacopla el handler del proveedor, testeable con providers fake, migración a gateway = agregar un provider sin tocar handler/endpoint/FE.
**Cons:** introduce el primer `HttpClient` + primer `IOptions` en core-api.
**Esfuerzo:** M
**Riesgos:** complejidad AOT (ver consecuencias).

## Tradeoff aceptado

Se acepta introducir la capa de abstracción y sus costos asociados:
1. **Primer `HttpClient` tipado + primer `IOptions`** en core-api (antes solo presentes en `Flit.Gateway`).
2. **AOT está hoy DESACTIVADO** en `Flit.Api` (`<PublishAot>false</PublishAot>`, y Infra/Application/Domain también lo sobreescriben a `false` pese al `Directory.Build.props` global) → la (de)serialización del payload Verifik funciona por **reflexión** (`ReadFromJsonAsync` / `JsonSerializerDefaults.Web`) sin problema. El **JSON source-gen** (`JsonSerializerContext` para `VerifikVehicleResponse`) queda como **prerequisito futuro** SI algún proyecto se activa a AOT real.
3. La **normalización** en `ConsultationResult` aísla el frontend: `PreflightSnapshot` no cambia al cambiar de proveedor.

## Consecuencias

### Lo que se gana
- El motor de trámites no conoce Verifik; el handler trabaja solo con la abstracción normalizada.
- Migrar al gateway Johan = añadir `FlitIntegrationsGatewayProvider` real sin tocar `RunConsultationHandler`, el endpoint ni el frontend.
- Resultados testeables con providers en memoria.

### Lo que se pierde
- Se añade indirección (registry + contexto + resultado normalizado) sobre una llamada que hoy solo tiene un proveedor real.
- Si en el futuro algún proyecto se activa a AOT real, habrá que introducir JSON source-gen (`JsonSerializerContext`) en la capa de (de)serialización; hoy con AOT desactivado la reflexión basta.

### Seguimiento
- Configuración Verifik por entorno (`VERIFIK_*`); `.env.verifik` está gitignored.
- Revisar este ADR al integrar el **gateway Flit Integrations** (post-#10128): promover el stub `FlitIntegrationsGatewayProvider` a implementación real.
- Ligado a Feature #10128 / HU #10201.

## Referencias
- ADR-0019: Motor de parametrización — Catálogos globales sin tenant_id (SuperAdmin)
- ADR-0018: Modelo de datos fase-1 FLIT Evolution
- Feature #10128, HU #10201
