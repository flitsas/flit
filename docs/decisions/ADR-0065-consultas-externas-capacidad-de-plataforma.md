# ADR-0065: Consultas externas como servicio de plataforma

**Fecha**: 2026-09-23 (revisado 2026-10-06)  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), arquitectura, PO  
**Tags**: integraciones, consultas, RUNT, SIMIT, Verifik, Kyverum, Fasecolda, medición, gRPC  
**Base de código**: `origin/develop@ed34d85b`

## Contexto

El negocio confirmó que los productos nuevos usarán las mismas fuentes externas que Trámites. Hoy:

- El registro de consultas vive en el namespace de Trámites: `Flit.Tramites.Application/UseCases/Consultations/IConsultationProvider.cs`, con `ConsultationProviderRegistry`, `ConsultationProviderChainResolver` (respaldo entre proveedores con tiempo límite) y `TenantConsultationOverrideProvider` (cadenas por empresa leídas de `admin.tenant_operational_policies`).
- Proveedores: Verifik (vehículo, SIMIT, RNMC, conductor, RUES), Kyverum RUNT y conductor, Kyverum comparendos para persona jurídica, Intempo, API interna de comparendos de FLIT y un gateway de integraciones. Avalúos tiene un registro aparte (Fasecolda, base gravable, MercadoLibre).
- Modos mock o real por proveedor; secretos por variables de entorno en `core-api`.
- Ya existe una fachada sin trámite de por medio: gRPC `IctConsultationService` para `core-ict`, con token de servicio.
- Kyverum avisa resultados de validación de identidad por webhook a Trámites (`KyverumWebhookVerifier`, reconciliación de validaciones).
- **No hay medición de consumo** por empresa ni por producto. `tramites.external_query_payloads` exige una instancia de trámite y es traza, no medición.

Revisión del 2026-10-06: la Epic #13316 fija como resultado que Consultas sea un **servicio propio**, no un módulo dentro de `core-api`, para que una caída o un despliegue de Trámites no deje sin consultas a los demás productos. Las llamadas entre servicios usan gRPC (ADR-0070).

## Decisión

1. Mover los contratos y adaptadores de consultas y avalúos de `Flit.Tramites.*` a un **módulo de plataforma** (`Flit.Modules.Consultas`), sin cambiar su comportamiento para Trámites.
2. Alojarlo en un **servicio propio, `core-consultas`**, creado desde la plantilla de servicio de plataforma, con su schema `consultas` y su usuario de base.
3. Exponerlo a los demás servicios por **gRPC** (`flit.consultas.v1`), con token de servicio (scope `platform.consultas`), empresa en `x-flit-tenant-id` y producto tomado del token. `IctConsultationService` pasa a atenderlo este servicio.
4. Mantener cadenas de respaldo, modos mock o real y caché. Los **overrides por empresa y la caché pasan al schema `consultas`**: el servicio no lee `admin.tenant_operational_policies`.
5. Añadir **medición de consumo**: una fila por consulta con empresa, producto, fuente, proveedor efectivo, resultado, si vino de caché y latencia. Visible para el SuperAdmin; base para límites por producto. Se publica además como evento `consultas.consulta.realizada` (ADR-0064).
6. **Los avisos de proveedores llegan a Consultas**: un receptor por proveedor verifica la firma, guarda el cuerpo tal como llegó, responde de inmediato y publica el resultado por el bus para el servicio dueño (hoy Trámites).
7. **Los secretos de los proveedores quedan solo en `core-consultas`.**
8. **Transición sin riesgo**: Trámites e ICT pasan a llamar a Consultas detrás de una bandera, con respaldo en la cadena en proceso de `core-api`. El respaldo se retira en un corte, igual que el de identidad, cuando Consultas está verificado en PDN.

## Alternativas consideradas

### Opción 1: Cada producto integra sus propios proveedores

**Pros:**
- Independencia total del producto.

**Cons:**
- Secretos y contratos duplicados; cada producto rehace respaldo, caché y modos mock.
- Consumo imposible de ver por empresa en un solo lugar.

**Esfuerzo:** M por producto  
**Riesgos:** costos de proveedores sin control; comportamiento distinto por producto.

### Opción 2: Módulo de plataforma dentro de `core-api`, expuesto como API de servicio

Era la decisión de la primera versión de este borrador.

**Pros:**
- Un solo lugar para secretos, respaldo, caché, overrides y medición.
- Menos piezas que operar.

**Cons:**
- `core-api` sigue siendo dependencia de disponibilidad de todos los productos: si Trámites cae o se despliega mal, nadie consulta.
- Los secretos de proveedores siguen en el proceso más grande y más cambiado.

**Esfuerzo:** M  
**Riesgos:** el monolito sigue siendo el punto único de falla que la separación quiere quitar.

### Opción 3: Servicio propio `core-consultas`, expuesto por gRPC — elegida

**Pros:**
- Un solo lugar para secretos, respaldo, caché, overrides y medición, aislado de Trámites.
- Los productos nuevos consultan RUNT, SIMIT y demás fuentes sin integrar nada ni depender de `core-api`.
- Precedente probado con `IctConsultationService`.

**Cons:**
- Un servicio más que desplegar y vigilar.
- Una llamada de red adicional para Trámites.

**Esfuerzo:** M–L  
**Riesgos:** cuello de botella si se cae; se mitiga con la caché, el respaldo en proceso durante la transición, límites por producto y métricas.

## Tradeoff aceptado

Se elige la **Opción 3**: un servicio más que operar a cambio de que las consultas sobrevivan a una caída de Trámites y de que los secretos de proveedores salgan del monolito.

## Consecuencias

### Lo que se gana

- Los productos nuevos consultan proveedores sin integrar nada.
- Consumo visible por empresa y producto.
- Una caída o un mal despliegue de `core-api` no deja sin consultas a los demás servicios.

### Lo que se pierde

- Autonomía de cada producto para elegir proveedor por su cuenta.
- La llamada en proceso de Trámites, que pasa a ser una llamada de red.

### Cambios operacionales

- Servicio `core-consultas` en los tres ambientes, con su base, su usuario y los secretos de proveedores (los crea el líder técnico).
- La URL del webhook de Kyverum apunta al receptor de Consultas.
- Contrato `contracts/proto/flit/consultas/v1`; cliente generado para los demás servicios.
- Tablero de consumo en la consola SuperAdmin.

## ADRs relacionados

- ADR-0020 (core-api) — capa multi-proveedor de consultas.
- `docs/adr-10878-cache-consultas-ttl.md` — caché de consultas.
- ADR-0061, ADR-0062, ADR-0064, ADR-0070.

## Notas para agentes

- **Backend Agent**: mover sin cambiar comportamiento; las pruebas actuales de consultas deben pasar sin modificarse.
- **QA Agent**: una consulta desde un producto queda medida con empresa y producto correctos; empresa sin override usa la cadena global; con Consultas caído y el respaldo apagado, el resto del trámite sigue funcionando.
- **Security Agent**: ningún otro servicio recibe secretos de proveedores; `tenant_id` validado contra el token de servicio; avisos de proveedores con firma verificada antes de procesarse.
- **Infra Agent**: el puerto gRPC de Consultas no se publica; alertas sobre latencia y errores por proveedor.
