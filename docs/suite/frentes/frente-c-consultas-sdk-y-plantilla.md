# Frente C — Consultas compartidas, SDK, eventos y plantilla de producto

> **Responsable:** desarrollador de Diagnóstico. **Producto que construye después:** Diagnóstico.
> **Skill:** `flit-suite-c-sdk`. **Prefijo de rama:** `feature/AB-<HU>-suite-c-…`.
>
> Leer antes de empezar: [README de la suite](../README.md), [reglas](../reglas-trabajo-paralelo.md),
> [contrato v1](../contrato-plataforma-v1.md) §3, §6 (consultas), §7 y §11, [plan maestro](../plan-maestro.md)
> §3, §4.7, §4.8 y §4.10, [ADR-0061](../adr-borradores/ADR-0061-suite-productos-plataforma-y-monorepo.md),
> [ADR-0064](../adr-borradores/ADR-0064-datos-separados-eventos-y-reportes.md),
> [ADR-0065](../adr-borradores/ADR-0065-consultas-externas-capacidad-de-plataforma.md) y
> `services/core-ict/` como precedente de servicio separado.

## Objetivo

Que crear un producto nuevo sea ejecutar una plantilla y no reinventar autenticación, aislamiento de datos, eventos ni integraciones. Que los productos consulten RUNT, SIMIT, RNMC, RUES, Fasecolda y demás fuentes a través de la plataforma, con el consumo medido por empresa y producto. Diagnóstico será el mayor consumidor de las dos cosas.

## Qué debe funcionar al terminar la plataforma

- `services/shared` ofrece el SDK .NET: validación del token del hub, `RequireProduct`, contexto de empresa, DbContext con filtro de empresa que falla cerrado, outbox y registro del manifiesto.
- Los eventos de integración se publican por outbox a RabbitMQ y están documentados en AsyncAPI.
- Las consultas externas viven en un módulo de plataforma, se exponen a otros servicios y cada consulta queda medida.
- `templates/flit-product` crea un servicio y su app listos para desplegar.
- El producto de prueba `demo` corre en `dev.demo.flitsas.online` y cumple toda la puerta de salida del plan maestro §5.4.

## Lo que entregas a otros frentes

| Entrega | Para | Cuándo |
|---|---|---|
| Secciones §6 (consultas) y §7 (eventos) del contrato cerradas | A, B | Semana 1 |
| Publicador de eventos con outbox en `core-api` | A, B | Mitad de la Fase 1 |
| `Flit.Platform.Contracts` (sobre de eventos y claves naturales) | Todos | Fin de la Fase 0 |
| Plantilla `flit-product` | Frente B (Comparendos) y tú (Diagnóstico) en la Fase 3 | Fase 2 |
| Producto de prueba `demo` | Validación de la puerta de salida | Fin de la Fase 2 |

## Lo que consumes y el stub mientras tanto

| Necesitas | De | Stub mientras tanto |
|---|---|---|
| Servidor OIDC, JWKS y `/platform/issuers` | A | El SDK valida con una llave RSA local de prueba y una lista fija de emisores en configuración |
| Token de servicio `svc-demo` | A | Token firmado por la llave local de prueba, solo en pruebas |
| `PUT /platform/products/{code}/manifest` y `GET /platform/me/apps` | B | Cliente del SDK contra un servidor falso en las pruebas |
| `@flit/ui`, `@flit/shell`, `@flit/auth` | B, A | La plantilla importa los paquetes; mientras no existan, usa componentes mínimos locales marcados como provisionales |
| RabbitMQ, Redis, namespace `dev` en k3s y Argo CD | Líder | RabbitMQ y Redis en contenedores locales |

## Tus carpetas

Ver [reglas R4](../reglas-trabajo-paralelo.md#r4-propiedad-de-carpetas). Resumen: `services/shared/**`, `Flit.Modules.Consultas` (nuevo), `Flit.Infrastructure/Consultations/**`, `Flit.Tramites.Application/UseCases/Consultations/**`, los archivos nuevos del publicador en `Flit.Infrastructure/Messaging/`, `templates/**`, `services/core-demo/**`, `frontend-demo/**` y `contracts/asyncapi/**`.

## Features sugeridas en ADO

- **C1 — SDK, eventos y plantilla de producto:** C-01, C-02, C-03, C-07, C-08, C-09, C-10.
- **C2 — Consultas externas compartidas:** C-04, C-05, C-06.

---

## Estado

- [ ] C-00 Cerrar §6 (consultas) y §7 (eventos) del contrato con A y B
- [ ] C-01 Publicador de eventos con outbox y RabbitMQ
- [ ] C-02 `Flit.Platform.Contracts`
- [ ] C-03 SDK .NET de producto
- [ ] C-04 Mover las consultas a un módulo de plataforma
- [ ] C-05 API de consultas para servicios
- [ ] C-06 Medición de consumo
- [ ] C-07 Plantilla `flit-product`
- [ ] C-08 Producto de prueba `demo`
- [ ] C-09 OpenAPI generado y cliente TypeScript
- [ ] C-10 Guía para crear un producto

---

## Tareas

### C-00 · Contrato de eventos y consultas · Fase 0 · semana 1 · S

- **Qué:** revisar con A y B las secciones §6 (consultas) y §7 (eventos) de `contrato-plataforma-v1.md` y cerrarlas.
- **Hecho cuando:** PR que cambia solo el contrato, aprobado por los tres frentes.

### C-01 · Publicador de eventos · Fase 0–1 · M

- **Qué:** `IIntegrationEventPublisher` genérico en `core-api`. Escribe en un outbox y un `BackgroundService` publica a RabbitMQ con el sobre del contrato §7, un exchange por productor y reintentos. Sigue el patrón de `ProcedureStateChangeOutboxPublisher` y el estándar de jobs del ADR-0059. Reemplaza la idea del stub `RabbitMqIdentityValidationEventPublisher` sin tocar sus flujos actuales.
- **Dónde:** archivos nuevos en `Flit.Infrastructure/Messaging/`; tabla de outbox con **turno de migración** (R6); cliente de RabbitMQ en PR propio de `Directory.Packages.props`; `contracts/asyncapi/platform-events.v1.yaml`. Pide al líder que `.github/workflows/contracts.yml` valide el archivo nuevo.
- **Hecho cuando:** un evento de prueba sale del outbox y llega a una cola en DEV; si RabbitMQ cae, se reintenta sin perder eventos.

### C-02 · `Flit.Platform.Contracts` · Fase 0 · S–M

- **Qué:** librería en `services/shared/Flit.Platform.Contracts` con el sobre de eventos, los eventos v1 del contrato y los objetos de valor `Placa`, `Vin` y `DocumentoIdentidad` (tipo y número), con normalización y validación.
- **Regla:** sin dependencias de `core-api`. Se referencia por proyecto, como hace `core-api` con `Flit.Ict.Grpc.Contracts`.
- **Hecho cuando:** pruebas unitarias de normalización (mayúsculas, espacios y guiones en placas; dígito de verificación de NIT si aplica) y de serialización del sobre.

### C-03 · SDK .NET de producto · Fase 1 · L

- **Qué:** dos librerías en `services/shared`:
  - `Flit.Platform.Sdk.AspNetCore`: validación del token con el JWKS del hub y la lista de emisores de `/platform/issuers` (caché); `aud` igual al código del producto; policies `RequireProduct` y de permiso; `TenantContext` desde el token; cliente de token de servicio con caché; registro idempotente del manifiesto al arrancar.
  - `Flit.Platform.Sdk.Persistence`: DbContext base con filtro global por `tenant_id` que **falla cerrado** (sin empresa en el contexto no devuelve filas), outbox y conexión con usuario de BD propio.
- **Hecho cuando:** un servicio de prueba con el SDK rechaza tokens de otro producto, otro emisor o sin firma, y no devuelve datos de otra empresa aunque la consulta olvide filtrar.

### C-04 · Consultas en un módulo de plataforma · Fase 1 · L (varios PRs)

- **Qué:** mover sin cambiar comportamiento los contratos y la orquestación de consultas de `Flit.Tramites.*` a `Flit.Modules.Consultas`: `IConsultationProvider` y su registro (hoy en `Flit.Tramites.Application/UseCases/Consultations/`), `ConsultationProviderRegistry`, `ConsultationProviderChainResolver`, `ConsultationChainOptions`, `TenantConsultationOverrideProvider` y `Avaluos/AvaluoProviderRegistry` (en `Flit.Infrastructure/Consultations/`).
- **Regla:** las pruebas actuales de consultas pasan **sin modificarse**. El registro de DI sale de `InfrastructureExtensions.cs` (líneas 479–630) a `ConsultasInfrastructureExtensions.cs`, con una línea en el archivo compartido (R5).
- **Orden sugerido:** primero interfaces, después implementaciones, después DI, en PRs separados de hasta 800 líneas.
- **Hecho cuando:** Trámites consulta igual que antes y ya no hay tipos de consultas bajo `Flit.Tramites.*`.

### C-05 · API de consultas para servicios · Fase 1 · M

- **Qué:** `POST /api/v1/platform/consultas/{fuente}` según el contrato §6: token de servicio con scope `platform.consultas`, `X-Flit-Tenant-Id` validado, cadena de respaldo y overrides de esa empresa, respuesta `ConsultationResult`. Detrás de `Suite:Consultas:ServiceApi`. Mismo patrón que `Flit.Api/Grpc/IctConsultationService.cs`.
- **Hecho cuando:** `svc-demo` consulta una placa en modo mock en DEV y un token de usuario recibe 403.

### C-06 · Medición de consumo · Fase 1 · M

- **Qué:** una fila por consulta con empresa, producto, fuente, proveedor efectivo, resultado, si vino de caché y latencia. `GET /api/v1/platform/admin/consultas/consumo` para el SuperAdmin. Deja preparado un límite por producto, sin activarlo.
- **Dónde:** tabla nueva con **turno de migración**. Incluye las consultas que hace Trámites: el producto se toma del contexto.
- **Hecho cuando:** el SuperAdmin ve el consumo de DEV agrupado por empresa, producto y fuente.

### C-07 · Plantilla `flit-product` · Fase 2 · L

- **Qué:** `templates/flit-product` con:
  - `dotnet new` que genera `services/core-<código>` con capas Domain, Application, Infrastructure y Api, tests, `Dockerfile`, SDK, manifiesto y migración inicial de su schema. Precedente: `services/core-ict`.
  - App `frontend-<código>` con `@flit/ui`, `@flit/shell` y `@flit/auth`, configuración en runtime y catálogo de navegación de ejemplo.
  - Workflow de CI con filtro de ruta y manifiestos de despliegue para Argo CD, que el líder revisa y conecta.
- **Hecho cuando:** un comando genera un producto que compila, pasa sus pruebas y arranca en local en `<código>.localhost`.

### C-08 · Producto de prueba `demo` · Fase 2 · M

- **Qué:** generar `services/core-demo` y `frontend-demo` con la plantilla y desplegarlos en `dev.demo.flitsas.online`. Debe mostrar: login OIDC del hub, barra común y menú de productos, `RequireProduct`, schema `demo` con usuario de BD propio, una consulta compartida medida y un evento publicado y consumido.
- **Hecho cuando:** los puntos de la puerta de salida (plan maestro §5.4) que dependen del producto de prueba se cumplen en QA.

### C-09 · OpenAPI generado y cliente TypeScript · Fase 2 · M

- **Qué:** los servicios nuevos generan su OpenAPI desde el código. CI produce `packages/api-client-<código>` a partir de él. Se revive `pnpm codegen` para esos servicios. `core-api` sigue con su OpenAPI escrito a mano; `contracts/openapi/platform.v1.yaml` es la excepción que sí se mantiene a mano.
- **Hecho cuando:** `frontend-demo` llama a `core-demo` solo con el cliente generado.

### C-10 · Guía para crear un producto · Fase 2 · S

- **Qué:** `docs/suite/guia-crear-producto.md`: generar desde la plantilla, registrar el producto y su cliente OIDC, pedir hosts y certificados, crear el usuario de BD, registrar el manifiesto, encender el producto para una empresa de prueba en DEV y desplegar con Argo CD.
- **Hecho cuando:** el desarrollador de Comparendos crea un producto vacío siguiendo solo la guía, sin ayuda.

---

## Riesgos del frente

| Riesgo | Qué hacer |
|---|---|
| Mover consultas rompe Trámites en silencio | Pruebas actuales sin modificar; PRs pequeños; comparar respuestas en modo mock antes y después |
| La plantilla queda desactualizada frente a los paquetes | El producto `demo` se regenera con la plantilla en cada cambio del SDK |
| RabbitMQ se vuelve crítico sin monitoreo | Pedir al líder alertas de cola y de mensajes muertos (L-05) |
| Costo de proveedores reales en pruebas | Modo mock por defecto; modo real solo con aprobación del líder |

## Bitácora

| Fecha | Tarea | PR | Nota |
|---|---|---|---|
| | | | |
