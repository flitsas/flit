# Contrato de plataforma v1 — FLIT Suite

> **Estado: BORRADOR.** Se cierra en la reunión de arranque (semana 1) con la firma de los tres
> frentes y del líder técnico. Después de cerrado, **cualquier cambio** entra por un PR que toca
> este archivo y lleva la aprobación de los tres frentes (ver
> [reglas de trabajo en paralelo](reglas-trabajo-paralelo.md), regla R3).
>
> Para qué existe: los tres frentes construyen piezas que se consumen entre sí (token, acceso a
> productos, eventos, paquetes de UI). Este archivo fija las formas **antes** de implementarlas,
> para que cada frente programe contra el contrato y use un stub mientras el dueño entrega.

| Sección | Dueño | Consumidores |
|---|---|---|
| §1 Códigos de producto y hosts | Líder | Todos |
| §2 Token de acceso por producto | Frente A | B, C |
| §3 Token de servicio | Frente A | C |
| §4 Acceso a productos (`IProductAccessResolver`) | Frente B | A |
| §5 `DomainContext` con producto | Frente B | A |
| §6 Endpoints de plataforma | B (me/apps, manifiesto, suscripciones), C (consultas) | Todos |
| §7 Eventos de integración | Frente C (sobre y publicador), dueño de cada evento | Todos |
| §8 Paquetes frontend | A (`@flit/auth`), B (`@flit/ui`, `@flit/shell`) | Todos |
| §9 Banderas | Líder | Todos |
| §10 Errores | Todos | Todos |
| §11 Hosts locales | Líder | Todos |

---

## 1. Códigos de producto y hosts

| Código | Nombre | PDN | QA | DEV |
|---|---|---|---|---|
| `plataforma` | Hub y login | `flitsas.online` | `qa.flitsas.online` | `dev.flitsas.online` |
| `tramites` | Trámites | `tramites.flitsas.online` | `qa.tramites.flitsas.online` | `dev.tramites.flitsas.online` |
| `comparendos` | Comparendos | `comparendos.flitsas.online` | `qa.comparendos.flitsas.online` | `dev.comparendos.flitsas.online` |
| `diagnostico` | Diagnóstico | `diagnostico.flitsas.online` | `qa.diagnostico.flitsas.online` | `dev.diagnostico.flitsas.online` |
| `demo` | Producto de prueba de la plantilla | — | — | `dev.demo.flitsas.online` |

- El código es el mismo en carpeta (`core-<código>`, `frontend-<código>`), schema, `aud` del token, roles y aplicación de Argo CD. Excepción histórica: Trámites vive en `services/core-api` y `frontend/`.
- La API para integradores sigue en `api.<ambiente>.flitsas.online` (`api.flitsas.online` en PDN).

## 2. Token de acceso por producto

Emitido por el servidor OIDC del hub (ADR-0062). Vida 15 minutos. Firma RS256 con llaves publicadas en `/.well-known/jwks.json` del hub.

```json
{
  "iss": "https://flitsas.online",
  "aud": "tramites",
  "sub": "0192f4c1-…",
  "email": "usuario@empresa.co",
  "tenant_id": "0192f4c1-…",
  "tenant_name": "…", "company_name": "…", "company_nit": "…",
  "tenant_type": "RENTING | CONCESIONARIO | FLIT | CONCESION | MARCA_BLANCA",
  "entity_type": "COMPANY | TRANSIT_OFFICE",
  "parent_tenant_id": "0192f4c1-… | null",
  "is_group_parent": false,
  "dom": "flit | <host de la red>",
  "product": "tramites",
  "roles": [{ "id": "…", "code": "Radicador" }],
  "permissions": ["tramites.instances.create", "…"],
  "exp": 0, "iat": 0, "jti": "…"
}
```

Reglas:

- `aud` y `product` son siempre el código del producto que pidió el token.
- `roles` y `permissions` contienen **solo** los del producto `aud`. Los roles de `plataforma` (`SuperAdmin`, `AdminCompany`) viajan solo en tokens con `aud=plataforma`, salvo `SuperAdmin`, que viaja en todos para conservar el bypass actual.
- Los nombres de claims existentes se conservan para no romper `frontend/lib/auth/jwt.ts` ni las policies actuales.
- `iss` es el host del hub que emitió el token: `https://flitsas.online`, `https://dev.flitsas.online` o el dominio de la red de Marca Blanca. Los servicios aceptan los emisores listados por `GET /api/v1/platform/issuers` (caché 5 minutos).
- Sin suscripción activa o sin rol en el producto, el hub **no emite** el token y responde `PRODUCT_NOT_SUBSCRIBED` o `PRODUCT_ROLE_REQUIRED` (§10).

## 3. Token de servicio

- Flujo client credentials. `client_id` = `svc-<código>` (p. ej. `svc-diagnostico`). `aud` = servicio destino (`plataforma`).
- Scopes v1: `platform.consultas`, `platform.manifest`, `platform.me.read`.
- Cuando una llamada de servicio actúa por una empresa, envía `X-Flit-Tenant-Id`. El destino lo acepta **solo** con token de servicio válido y lo registra en la traza. Un token de usuario nunca puede usar esa cabecera.

## 4. Acceso a productos

Interfaz en `Flit.Modules.Security.Application` (o el módulo de plataforma que cree el frente B), implementada por B y consumida por A al emitir el token:

```csharp
public interface IProductAccessResolver
{
    Task<ProductAccess> ResolveAsync(Guid userId, Guid tenantId, string productCode, CancellationToken ct);
}

public sealed record ProductAccess(
    bool SubscriptionActive,          // suscripción de la empresa (o heredada de su cabeza) vigente
    IReadOnlyList<RoleRef> Roles,     // roles del usuario en ESTE producto
    IReadOnlyList<string> Permissions);

public sealed record RoleRef(Guid Id, string Code);
```

Mientras B no entregue, A usa `StubProductAccessResolver`: `tramites` activo para todos y roles actuales del usuario. El stub se borra en el PR que conecta la implementación real.

## 5. `DomainContext` con producto

Se amplía el record existente (`services/core-api/src/Flit.Api/Authorization/DomainContext.cs`) sin romper a sus consumidores:

```csharp
public sealed record DomainContext(DomainKind Kind, string? Host, Guid? HeadTenantId, string ProductCode);
```

- Hosts FLIT: raíz o `<ambiente>.<raíz>` ⇒ `plataforma`; `<ambiente>.<producto>.<raíz>` ⇒ `<producto>`; host desconocido ⇒ `plataforma`.
- Redes: `admin.tenant_domains.purpose` (`HUB` ⇒ `plataforma`, o el código del producto).
- Se sigue construyendo **solo** desde el sello `X-Flit-Domain` (ADR-0060).

## 6. Endpoints de plataforma

Todos bajo `/api/v1/platform/**`, documentados en `contracts/openapi/platform.v1.yaml` (archivo nuevo, para no chocar en `core-api.v1.yaml`).

| Método y ruta | Dueño | Auth | Respuesta |
|---|---|---|---|
| `GET /me/apps` | B | Token de usuario, cualquier `aud` | `[{ code, name, icon, url, current }]` con los productos que el usuario puede abrir en su host actual |
| `PUT /products/{code}/manifest` | B | Servicio, scope `platform.manifest` | Idempotente. Cuerpo: `{ version, modules:[{code,name,permissions:[{slug,name}]}], defaultRoles:[{code,name,permissions:[…]}] }` |
| `GET /admin/subscriptions?tenantId=` | B | SuperAdmin | Suscripciones de la empresa |
| `PUT /admin/subscriptions/{tenantId}/{productCode}` | B | SuperAdmin | `{ status, startsAt, endsAt, notes }`; auditado |
| `GET /issuers` | A | Anónimo | Lista de emisores válidos (hosts de hub FLIT y de redes activas) |
| `POST /consultas/{fuente}` | C | Servicio, scope `platform.consultas` + `X-Flit-Tenant-Id` | Resultado normalizado actual (`ConsultationResult`) |
| `GET /admin/consultas/consumo?tenantId=&desde=&hasta=` | C | SuperAdmin | Consumo agregado por producto y fuente |

## 7. Eventos de integración

- Transporte: RabbitMQ, un exchange `topic` por productor (`plataforma`, `tramites`, …). Publicación **solo** vía outbox.
- Sobre común (`Flit.Platform.Contracts`):

```json
{ "eventId": "uuidv7", "type": "platform.subscription.changed", "version": 1,
  "occurredAt": "2026-10-01T15:04:05Z", "tenantId": "…", "producer": "plataforma",
  "correlationId": "…", "data": { } }
```

| Evento v1 | Productor | Datos | Uso |
|---|---|---|---|
| `platform.subscription.changed` | B | `{ tenantId, productCode, status }` | Invalidar caché de acceso |
| `platform.user.suspended` | A | `{ userId, tenantId }` | Revocar refresh tokens y sesiones |
| `platform.tenant.suspended` | B | `{ tenantId }` | Revocar sesiones de la empresa |
| `platform.roles.changed` | B | `{ userId, tenantId, productCode }` | Forzar renovación del token |

- Todos los eventos se documentan en `contracts/asyncapi/platform-events.v1.yaml` (dueño C) antes de publicarse.
- Los consumidores son idempotentes por `eventId`.

## 8. Paquetes frontend

```ts
// @flit/auth (frente A) — usado por cada app Next.js
export function createAuthRoutes(opts: { productCode: string }): {
  login: RouteHandler; callback: RouteHandler; logout: RouteHandler; refresh: RouteHandler;
};
export function createApiProxy(opts: { upstream: string }): RouteHandler; // /api/* con Bearer desde la sesión
export async function getSession(): Promise<SessionUser | null>;         // solo servidor
export function useSession(): { user: SessionUser | null; status: "loading" | "ready" }; // cliente

// @flit/shell (frente B)
export function SuiteShell(props: {
  productCode: string;
  nav: NavCatalog;            // Contract A de GUIA-DOCK-INFERIOR-FLOTANTE.md
  children: React.ReactNode;
}): JSX.Element;              // barra común + menú de productos + menú de cuenta + dock del producto

// @flit/ui (frente B) — tokens y átomos extraídos de frontend/components/atom
```

- Mientras `@flit/auth` no exista, `@flit/shell` recibe la sesión por props desde un adaptador sobre `frontend/lib/auth/jwt.ts`.
- Ningún paquete emite cookies con atributo `Domain`.

## 9. Banderas

Configuración por ambiente (appsettings y configuración runtime del frontend). Todas arrancan en `false` en DEV, QA y PDN y se encienden por ambiente cuando el frente lo pide.

| Bandera | Dueño | Efecto |
|---|---|---|
| `Suite:Oidc:Enabled` | A | Los productos inician sesión por OIDC del hub |
| `Suite:LegacySession:Enabled` | A | Mantiene la cookie `flit_token` actual (convivencia) |
| `Suite:Subscriptions:Enforce` | B | `RequireProduct` rechaza sin suscripción; apagada = solo registra |
| `Suite:Hub:Enabled` | B | La raíz sirve el hub en vez de Trámites |
| `Suite:TramitesHost:Enabled` | A | Activa el reparto de rutas hacia `<ambiente>.tramites.` |
| `Suite:Consultas:ServiceApi` | C | Expone `/platform/consultas` a otros servicios |

## 10. Errores

RFC 7807 con `code` estable:

| `code` | HTTP | Cuándo |
|---|---|---|
| `PRODUCT_NOT_SUBSCRIBED` | 403 | La empresa no tiene el producto activo |
| `PRODUCT_ROLE_REQUIRED` | 403 | El usuario no tiene rol en el producto |
| `TOKEN_AUDIENCE_MISMATCH` | 401 | Token de otro producto |
| `SESSION_DOMAIN_MISMATCH` | 401 | Ya existe (ADR-0060); se conserva |
| `NETWORK_DOMAIN_REQUIRED` | 403 | Ya existe (ADR-0060); ahora redirige al hub de la red |

## 11. Hosts locales

Los navegadores resuelven `*.localhost` a la máquina local, y `ict.localhost` ya se usa en el gateway.

| Superficie | Host local |
|---|---|
| Hub y login | `localhost:<puerto del hub>` |
| Trámites | `localhost:3000` hoy; `tramites.localhost:3000` cuando se active el reparto de rutas |
| Producto nuevo | `<código>.localhost:<puerto de su app>` |
| API | `localhost:4002` (gateway), `localhost:4003` (core-api) |

Los puertos nuevos los asigna el líder en `docs/despliegue-y-puertos.md`, siguiendo la tabla de puertos existente, antes de que cada frente cree su app.

## Historial

| Versión | Fecha | Cambio | Aprobado por |
|---|---|---|---|
| v1-borrador | 2026-09-24 | Primera propuesta | — |
