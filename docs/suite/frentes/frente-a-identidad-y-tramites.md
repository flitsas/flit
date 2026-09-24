# Frente A — Identidad, sesión y Trámites en la suite

> **Responsable:** desarrollador de Trámites. **Producto que retoma después:** Trámites.
> **Skill:** `flit-suite-a-identidad`. **Prefijo de rama:** `feature/AB-<HU>-suite-a-…`.
>
> Leer antes de empezar: [README de la suite](../README.md), [reglas](../reglas-trabajo-paralelo.md),
> [contrato v1](../contrato-plataforma-v1.md) §2, §3, §8 y §9, [plan maestro](../plan-maestro.md) §4.3
> y §4.4, [ADR-0062](../adr-borradores/ADR-0062-identidad-oidc-sobre-dominio-sellado.md) y el ADR de
> Marca Blanca `docs/decisions/ADR-0060-marca-blanca-identidad-dominio-y-tema-de-correo.md`.

## Objetivo

Que un usuario inicie sesión una sola vez en el hub y abra cualquier producto sin volver a escribir la contraseña. Cada producto recibe un token propio con solo sus permisos, validado de verdad en el gateway y en la API. Al final, Trámites funciona como un producto más de la suite en `<ambiente>.tramites.flitsas.online`.

## Qué debe funcionar al terminar la plataforma

- Ningún ambiente desplegado corre con `ASPNETCORE_ENVIRONMENT=Development`. Gateway y API rechazan tokens sin firma válida, de otro emisor o de otro producto.
- El hub sirve el login y los endpoints OIDC en su host, en FLIT y en cada red de Marca Blanca.
- Cada app Next.js usa `@flit/auth`: cookie `HttpOnly; Secure` del propio host, token solo en servidor, refresh rotado en Redis y cierre de sesión global.
- Trámites vive en `<ambiente>.tramites.flitsas.online`, con las rutas viejas repartidas según el plan maestro §4.3.
- La suite de integración de Marca Blanca sigue en verde.

## Lo que entregas a otros frentes

| Entrega | Para | Cuándo |
|---|---|---|
| Secciones §2 y §3 del contrato cerradas | B, C | Semana 1 |
| Gateway y API validando firma, emisor y audiencia en DEV | Todos | Fin de la Fase 0 |
| Servidor OIDC en DEV con `/.well-known/openid-configuration`, JWKS y `GET /api/v1/platform/issuers` | C (SDK), B (hub) | Mitad de la Fase 1 |
| Cliente de servicio `svc-demo` registrado | C | Fin de la Fase 1 |
| `@flit/auth` v1 | B (hub y shell), C (plantilla) | Inicio de la Fase 2 |

## Lo que consumes y el stub mientras tanto

| Necesitas | De | Stub mientras tanto |
|---|---|---|
| `IProductAccessResolver` (contrato §4) | B | `StubProductAccessResolver`: `tramites` activo, roles actuales del usuario |
| `DomainContext.ProductCode` (contrato §5) | B | Deducir el producto del host en tu propio código, detrás de una interfaz |
| Publicador de eventos (contrato §7) | C | Escribir en el outbox existente y no publicar |
| Redis en DEV | Líder | Almacén en memoria solo para pruebas locales |
| Hosts `dev.tramites.flitsas.online` con certificado | Líder | `tramites.localhost` |

## Tus carpetas

Ver [reglas R4](../reglas-trabajo-paralelo.md#r4-propiedad-de-carpetas). Resumen: `Flit.Gateway`, `Flit.Modules.Identity` (nuevo), `Flit.Modules.Security.Application/Auth`, `Flit.Infrastructure/Security`, `Flit.Api/Authorization`, `packages/auth`, `frontend/lib/auth`, `frontend/lib/api/client.ts` y `base-url.ts`, `frontend/middleware.ts`, `frontend-hub/app/(auth)`.

## Features sugeridas en ADO

- **A1 — Autenticación endurecida e identidad OIDC:** A-01 a A-08.
- **A2 — Trámites en la suite:** A-09 a A-14.

---

## Estado

- [ ] A-00 Cerrar §2, §3 y §8 (`@flit/auth`) del contrato con B y C
- [ ] A-01 Inventario de lo que depende de `Development` y nombres de ambiente
- [ ] A-02 Salir de `Development` en DEV
- [ ] A-03 Gateway y API validan firma, emisor y audiencia
- [ ] A-04 Espiga técnica de OpenIddict
- [ ] A-05 Servidor OIDC en `core-api`
- [ ] A-06 Pantallas de login en el hub
- [ ] A-07 Token por producto y refresh en Redis
- [ ] A-08 Marca Blanca sobre OIDC
- [ ] A-09 Paquete `@flit/auth`
- [ ] A-10 Trámites usa `@flit/auth` con convivencia
- [ ] A-11 Trámites en su host y reparto de rutas
- [ ] A-12 Recursos de correo y URLs sin valores fijos
- [ ] A-13 Cierre de sesión global y revocación
- [ ] A-14 Retirar la sesión antigua

---

## Tareas

### A-00 · Contrato de identidad · Fase 0 · semana 1 · S

- **Qué:** revisar con B y C las secciones §2 (token), §3 (token de servicio) y §8 (`@flit/auth`) de `contrato-plataforma-v1.md` y cerrarlas.
- **Hecho cuando:** PR que cambia solo el contrato, aprobado por los tres frentes, con la fila de historial.

### A-01 · Inventario de `Development` · Fase 0 · semana 1 · S

- **Qué:** listar todo lo que cambia según el nombre de ambiente en `core-api`, `Flit.Gateway` y `core-ict`: `IsDevelopment()`, `appsettings.Development.json`, `Jwt__DevGenerate`, `DevelopmentAuthSeeder`, `DevelopmentNoJwtProxyConfigFilter` (`Flit.Gateway/Configuration/`), Swagger y seeds.
- **Dónde:** `docker-compose.prod.yml` (líneas 130, 329 y 467 fijan `Development`), `services/core-api/src/Flit.Gateway/Program.cs:79-130`, `services/core-api/src/Flit.Api/Authorization/ApiSecurityExtensions.cs`.
- **Hecho cuando:** `docs/suite/frentes/a-inventario-ambientes.md` lista cada punto con su decisión y propone los nombres de ambiente (por ejemplo `Development` solo local; `Dev`, `QA` y `Production` desplegados; ya existe `appsettings.QA.json`).

### A-02 · Salir de `Development` en DEV · Fase 0 · semanas 1–2 · M

- **Qué:** aplicar el inventario. DEV corre con su nombre de ambiente propio y las mismas funciones que hoy, sin los atajos de desarrollo.
- **Dónde:** configuración por ambiente en `docker-compose.prod.yml` y `cd.yml`. Pide al líder el cambio en esos archivos (R4) y tú haces el del código.
- **Hecho cuando:** DEV funciona un sprint sin `Development`, incluidos Marca Blanca, ICT y el migrador. QA y PDN siguen en una HU aparte con aprobación del líder (R13).
- **Pruebas:** arranque de cada servicio con el ambiente nuevo; `tests/Flit.Integration.Tests/MarcaBlanca`.

### A-03 · Validación real del token · Fase 0 · semanas 2–3 · M

- **Qué:** quitar `RequireAssertion(_ => true)` y el `SignatureValidator` permisivo del gateway fuera de local. La API falla al arrancar si no tiene llave pública fuera de local. Validar `iss` y `aud`, con `aud=flit-api` aceptado durante la transición.
- **Dónde:** `Flit.Gateway/Program.cs`, `Flit.Api/Authorization/ApiSecurityExtensions.cs`, `Flit.Infrastructure/Security/RsaJwtTokenIssuer.cs`.
- **Hecho cuando:** un token sin firma, con otro emisor o vencido recibe 401 en gateway y API en DEV. ADR-0060 deja de depender de que el gateway "no valide".
- **Pruebas:** unitarias de las policies; integración con token manipulado; suite de Marca Blanca.

### A-04 · Espiga de OpenIddict · Fase 0 · semana 3 · M

- **Qué:** prototipo en una rama de espiga, que no se fusiona: authorization code con PKCE sobre el `LoginHandler` actual, emisor tomado del host sellado, un cliente `tramites` y uno `svc-demo`.
- **Hecho cuando:** `docs/suite/frentes/a-espiga-openiddict.md` responde cuatro preguntas: almacenamiento de OpenIddict (schema `identity`), emisor por host, cómo reutilizar `LoginHandler` y el rendimiento del JWKS. Incluye la decisión.

### A-05 · Servidor OIDC en `core-api` · Fase 1 · M–L

- **Qué:** módulo nuevo `Flit.Modules.Identity` con OpenIddict. Endpoints `/connect/authorize`, `/connect/token`, `/connect/logout`, `/.well-known/openid-configuration` y JWKS. Clientes `plataforma`, `tramites`, `demo`, `svc-demo`. `GET /api/v1/platform/issuers`.
- **Dónde:** `services/core-api/src/Flit.Modules.Identity/**`; registro con **una línea** en `Program.cs` y en `InfrastructureExtensions.cs` (R5); tablas de OpenIddict con **turno de migración** (R6); paquete OpenIddict en PR propio de `Directory.Packages.props`.
- **Hecho cuando:** en DEV, detrás de `Suite:Oidc:Enabled`, un cliente de prueba obtiene un token por authorization code + PKCE y otro por client credentials.
- **Pruebas:** integración del flujo completo contra PostgreSQL real.

### A-06 · Login en el hub · Fase 1 · M

- **Qué:** pantallas de login, recuperación y activación de invitación en `frontend-hub/app/(auth)`, reutilizando lo de `frontend/app/login`, `frontend/app/auth/*` y `frontend/app/invite/activate`, con la marca por host de Marca Blanca.
- **Coordinación:** B crea `frontend-hub`. Tú solo trabajas en `app/(auth)`.
- **Hecho cuando:** el login del hub completa el flujo OIDC de A-05 y muestra la marca de la red en su dominio.

### A-07 · Token por producto y refresh · Fase 1 · M

- **Qué:** emisión según el contrato §2. `aud` y `product` iguales al cliente; roles y permisos del producto desde `IProductAccessResolver` (stub hasta que B entregue); rechazo `PRODUCT_NOT_SUBSCRIBED` o `PRODUCT_ROLE_REQUIRED`. Vida de 15 minutos; refresh rotado y revocable en Redis.
- **Hecho cuando:** un usuario sin rol en un producto no obtiene token para él; un refresh usado dos veces revoca la cadena.

### A-08 · Marca Blanca sobre OIDC · Fase 1 · M

- **Qué:** el login OIDC se sirve también en el dominio de cada red. `NETWORK_DOMAIN_REQUIRED` redirige al hub de la red. `NetworkUrlBaseResolver` apunta al hub de la red. Coordinar con B el campo `purpose` de `tenant_domains`.
- **Dónde:** `Flit.Modules.Security.Application/Auth/Login/LoginHandler.cs`, `Auth/Network/NetworkUrlBaseResolver.cs`, `Flit.Api/Authorization/DomainBindingMiddleware.cs`.
- **Hecho cuando:** la suite de integración de Marca Blanca y la de paridad (#12429) pasan con OIDC encendido.

### A-09 · Paquete `@flit/auth` · Fase 2 · M

- **Qué:** implementar el contrato §8 para Next.js: rutas de login, callback, refresh y logout; proxy `/api/*` con Bearer desde la sesión; sesión en Redis; cookie `HttpOnly; Secure; SameSite=Lax` sin `Domain`.
- **Dónde:** `packages/auth/**`. El líder habilita `packages/*` en el workspace en la semana 1.
- **Hecho cuando:** `frontend-hub` y `frontend-demo` lo usan. Una prueba automática falla si alguna respuesta emite `Set-Cookie` con `Domain`.

### A-10 · Trámites usa `@flit/auth` · Fase 2 · M

- **Qué:** reemplazar `frontend/lib/auth/session.ts` (cookie legible y copia en `localStorage`) y el Bearer desde el navegador en `frontend/lib/api/client.ts`. Convivencia con `Suite:LegacySession:Enabled` durante un sprint.
- **Hecho cuando:** con la bandera nueva, Trámites no guarda tokens en el navegador; con la vieja, todo sigue como hoy.

### A-11 · Trámites en su host · Fase 2 · M

- **Qué:** Trámites pasa a `<ambiente>.tramites.flitsas.online`. El reparto de rutas del plan maestro §4.3 queda en un mapa de redirecciones del hub (`frontend-hub/redirects/legacy-tramites.ts`, archivo tuyo dentro del hub). Redirecciones 308 conservando ruta y parámetros. Detrás de `Suite:TramitesHost:Enabled`.
- **Coordinación:** el líder cambia hosts, certificados y `cd.yml`; B publica el hub en la raíz con `Suite:Hub:Enabled`.
- **Hecho cuando:** los enlaces viejos de invitación, recuperación, portal (`/portal/[token]`) y biometría (`/biometric/[token]`) funcionan en DEV después del cambio.

### A-12 · Recursos de correo y URLs sin valores fijos · Fase 2 · S

- **Qué:** sacar a configuración por ambiente los `https://dev.flitsas.online/...` escritos en código: `NotificationEmailAssetsOptions.cs`, `TramiteCambioEstadoEmailComposer.cs`, `AsignacionPlacaEmailComposer.cs`, `Preview/*Sample.cs`, `EmailThemePublicBrandingOptions.cs`, `Flit.Api/RateLimiting/PublicBrandingOptions.cs`, `FlitBrandedEmailLayout.cs` y `WelcomeRegistrationEmailTemplate.cs`. Recursos nuevos desde `assets.<ambiente>`; los hosts viejos siguen sirviendo `/email-assets`.
- **Hecho cuando:** ninguna URL de ambiente queda fija en código (verificado con `grep`) y los correos de prueba cargan sus imágenes.

### A-13 · Cierre de sesión global y revocación · Fase 2 · S–M

- **Qué:** cerrar sesión en un producto la cierra en toda la suite. Consumir `platform.user.suspended` y `platform.tenant.suspended` para revocar refresh tokens y sesiones.
- **Hecho cuando:** después de cerrar sesión en Trámites, abrir el hub pide login; suspender un usuario corta su sesión en menos de 15 minutos.

### A-14 · Retirar la sesión antigua · Fase 2 · S

- **Qué:** después de un sprint con `Suite:LegacySession:Enabled` apagada en PDN sin incidentes, borrar la cookie `flit_token`, la copia en `localStorage` y la bandera.
- **Hecho cuando:** ya no hay referencias a `flit:jwt` ni a `flit_token` en el código.

---

## Riesgos del frente

| Riesgo | Qué hacer |
|---|---|
| Salir de `Development` rompe algo que hoy funciona por la omisión | A-01 antes que A-02; primero DEV; observar un sprint |
| Validar el token rompe llamadas internas (ICT, migrador, frontend por `CORE_API_ORIGIN`) | Incluirlas en el inventario A-01 con su forma de autenticarse |
| OpenIddict y el emisor por host | A-04 decide antes de A-05 |
| Tiempo repartido con el soporte de Trámites | Solo errores de PDN durante las Fases 1 y 2 (pendiente de confirmar con el PO) |

## Bitácora

| Fecha | Tarea | PR | Nota |
|---|---|---|---|
| | | | |
