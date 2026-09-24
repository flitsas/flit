# ADR-0065: Consultas externas como capacidad compartida de plataforma

**Fecha**: 2026-09-23  
**Status**: BORRADOR (pasa a Propuesto al versionarse en `docs/decisions/`)  
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), arquitectura, PO  
**Tags**: integraciones, consultas, RUNT, SIMIT, Verifik, Kyverum, Fasecolda, medición  
**Base de código**: `origin/develop@e8b7ca65`

## Contexto

El negocio confirmó que algunos productos nuevos usarán las mismas fuentes externas que Trámites. Hoy:

- El registro de consultas vive en el namespace de Trámites: `Flit.Tramites.Application/UseCases/Consultations/IConsultationProvider.cs`, con `ConsultationProviderRegistry`, `ConsultationProviderChainResolver` (respaldo entre proveedores con tiempo límite) y `TenantConsultationOverrideProvider` (cadenas por empresa leídas de `admin.tenant_operational_policies`).
- Proveedores: Verifik (vehículo, SIMIT, RNMC, conductor, RUES), Kyverum RUNT y conductor, Kyverum comparendos para persona jurídica, Intempo, API interna de comparendos de FLIT y un gateway de integraciones. Avalúos tiene un registro aparte (Fasecolda, base gravable, MercadoLibre).
- Modos mock o real por proveedor; secretos por variables de entorno en `core-api`.
- Ya existe una fachada sin trámite de por medio: gRPC `IctConsultationService` para `core-ict`, con token de servicio, y adaptadores de generación documental.
- **No hay medición de consumo** por empresa ni por producto. `tramites.external_query_payloads` exige una instancia de trámite y es traza, no medición.

## Decisión

1. Mover los contratos de consultas y avalúos de `Flit.Tramites.*` a un **módulo de plataforma** (`Flit.Modules.Consultas`), sin cambiar su comportamiento para Trámites.
2. Exponerlo a los productos como **API de servicio** en `/api/v1/platform/consultas/**`, con token de cliente (ADR-0062), `tenant_id` explícito y producto tomado del token. Se usa el mismo patrón que `IctConsultationService`.
3. Mantener cadenas de respaldo, overrides por empresa, caché por empresa y modos mock o real.
4. Añadir **medición de consumo**: una fila por consulta con empresa, producto, fuente, proveedor efectivo, resultado, si vino de caché y latencia. Visible para el SuperAdmin; base para límites por producto.
5. Los secretos de los proveedores quedan solo en `core-api`.

## Alternativas consideradas

### Opción 1: Cada producto integra sus propios proveedores

**Pros:**
- Independencia total del producto.

**Cons:**
- Secretos y contratos duplicados; cada producto rehace respaldo, caché y modos mock.
- Consumo imposible de ver por empresa en un solo lugar.

**Esfuerzo:** M por producto  
**Riesgos:** Costos de proveedores sin control; comportamiento distinto por producto.

### Opción 2: Librería compartida con los clientes de proveedores

**Pros:**
- Reutiliza código sin llamadas de red.

**Cons:**
- Cada servicio sigue teniendo los secretos.
- La caché y los overrides por empresa dependen de tablas de `core-api`.
- Sin medición central.

**Esfuerzo:** M  
**Riesgos:** Secretos repartidos en varios despliegues.

### Opción 3: Capacidad de plataforma expuesta como API de servicio — elegida

**Pros:**
- Un solo lugar para secretos, respaldo, caché, overrides y medición.
- Precedente probado con `core-ict`.
- Los productos solo conocen un contrato normalizado.

**Cons:**
- Latencia de red adicional.
- `core-api` se vuelve dependencia de disponibilidad de los productos para estas consultas.

**Esfuerzo:** M  
**Riesgos:** Cuello de botella; se mitiga con la caché existente, límites por producto y métricas.

## Tradeoff aceptado

Se elige la **Opción 3**: una llamada de red más a cambio de control central de secretos, costos y comportamiento.

## Consecuencias

### Lo que se gana

- Los productos nuevos consultan RUNT, SIMIT y demás fuentes sin integrar nada.
- Consumo visible por empresa y producto.

### Lo que se pierde

- Autonomía de cada producto para elegir proveedor por su cuenta.

### Cambios operacionales

- Contrato OpenAPI del módulo de consultas; cliente generado para los productos.
- Tablero de consumo en la consola SuperAdmin.

## ADRs relacionados

- ADR-0020 (core-api) — capa multi-proveedor de consultas.
- `docs/adr-10878-cache-consultas-ttl.md` — caché de consultas.
- ADR-0061, ADR-0062.

## Notas para agentes

- **Backend Agent**: mover sin cambiar comportamiento; los tests actuales de consultas deben pasar sin modificarse.
- **QA Agent**: una consulta desde un producto queda medida con empresa y producto correctos; empresa sin override usa la cadena global.
- **Security Agent**: ningún producto recibe secretos de proveedores; `tenant_id` validado contra el token de servicio.
