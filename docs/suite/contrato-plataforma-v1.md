# Contrato de plataforma v1 — FLIT Suite

> **Estado: CERRADO (v1, 2026-09-24).** Definido sin reunión de arranque. Desde ahora,
> **cualquier cambio** entra por un PR que toca solo este archivo y lleva la aprobación de los tres
> frentes (ver [reglas de trabajo en paralelo](reglas-trabajo-paralelo.md), regla R3).
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
| §6 Endpoints de plataforma | B (me/apps, manifiesto, productos habilitados), C (consultas) | Todos |
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
- **Fuera de v1:** Resoluciones y Flotas no tienen código de producto todavía. El boolean `resoluciones_module_enabled` se queda como está hasta que se defina Resoluciones; B-07 no lo toca.

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
- Se siguen emitiendo `role`, `role_code` y `role_id` por cada rol, además de `roles`, porque las policies actuales leen esos claims.
- Los nombres de claims existentes se conservan para no romper `frontend/lib/auth/jwt.ts` ni las policies actuales.
- `iss` es el host del hub que emitió el token: `https://flitsas.online`, `https://dev.flitsas.online` o el dominio de la red de Marca Blanca. Los servicios aceptan los emisores listados por `GET /api/v1/platform/issuers` (caché 5 minutos).
- Si el producto no está habilitado para la empresa o el usuario no tiene rol en él, el hub **no emite** el token y responde `PRODUCT_NOT_ENABLED` o `PRODUCT_ROLE_REQUIRED` (§10).

### 2.1 SuperAdmin conserva el bypass en todos los productos

Regla de negocio: el SuperAdmin de FLIT entra a cualquier producto, de cualquier empresa, igual que hoy entra a todo core-api.

- **Emisión:** el hub emite token de cualquier producto para un SuperAdmin, aunque el producto no esté habilitado para su empresa y aunque no tenga rol en él. El token lleva `SuperAdmin` en `roles`, `role` y `role_code`.
- **Detección:** un usuario es SuperAdmin si **cualquiera** de sus claims `role` o `role_code` vale `SuperAdmin` (regla multi-rol de la HU #12320, `RequestTenantResolver.IsSuperAdmin`). Nunca se mira solo el primero.
- **Autorización:** `RequireProduct` y las policies de permiso dejan pasar al SuperAdmin sin revisar habilitación ni permisos, igual que `PermissionAuthorizationHandler` hoy. Aplica en core-api y en el SDK (`Flit.Platform.Sdk.AspNetCore`).
- **Alcance por empresa:** el SuperAdmin puede acotar la petición a una empresa con `X-Tenant-Id`, como en `TenantEnforcementMiddleware`. Sin esa cabecera ve todas las empresas. Es la **única** excepción al filtro que falla cerrado del SDK (`Flit.Platform.Sdk.Persistence`): para cualquier otro usuario, sin `tenant_id` no hay filas.
- `X-Tenant-Id` (usuario SuperAdmin) y `X-Flit-Tenant-Id` (token de servicio, §3) son cabeceras distintas y no se mezclan.

## 3. Token de servicio

- Flujo client credentials. `client_id` = `svc-<código>` (p. ej. `svc-diagnostico`). `aud` = servicio destino (`plataforma`).
- Scopes v1: `platform.consultas`, `platform.manifest`, `platform.me.read`.
- Cuando una llamada de servicio actúa por una empresa, envía `X-Flit-Tenant-Id`. El destino lo acepta **solo** con token de servicio válido y lo registra en la traza. Un token de usuario nunca puede usar esa cabecera.

## 4. Acceso a productos

Interfaz en `services/core-api/src/Flit.Modules.Security.Application/ProductAccess/IProductAccessResolver.cs` (carpeta del frente B). La implementación real vive en `Flit.Modules.Platform` (B-03 y B-05), que referencia a `Security.Application`; nunca al revés. La consume A al emitir el token. B crea el archivo de la interfaz, solo con la interfaz y los records, en un PR propio justo después de cerrar este contrato, para que A pueda escribir su stub desde la semana 2:

```csharp
public interface IProductAccessResolver
{
    Task<ProductAccess> ResolveAsync(Guid userId, Guid tenantId, string productCode, CancellationToken ct);
}

public sealed record ProductAccess(
    bool ProductEnabled,              // el producto está encendido para la empresa (y para su cabeza, si tiene)
    IReadOnlyList<RoleRef> Roles,     // roles del usuario en ESTE producto
    IReadOnlyList<string> Permissions);

public sealed record RoleRef(Guid Id, string Code);
```

Mientras B no entregue, A usa `StubProductAccessResolver`: `tramites` activo para todos y roles actuales del usuario. El stub se borra en el PR que conecta la implementación real.

**Habilitación de producto (no es una suscripción comercial).** Un producto está encendido o apagado para una empresa; no hay planes, fechas, vencimientos ni cobro. Encendido: los usuarios de la empresa con rol en el producto pueden entrar. Apagado: nadie de esa empresa entra, salvo el SuperAdmin (§2.1). Una empresa hija de Concesión o Marca Blanca solo puede tener encendido un producto que su cabeza tenga encendido (regla fail-closed de la jerarquía, ADR-0057). Tabla: `platform.tenant_products (tenant_id, product_code, enabled, notes, updated_at, updated_by)`, con auditoría en `admin.tenant_config_audit_logs`. Los booleans actuales pasan a filas de esta tabla en B-07: `tramites_module_enabled` → `tramites` y `comparendos_module_enabled` → `comparendos`.

## 5. `DomainContext` con producto

Se amplía el record existente (`services/core-api/src/Flit.Api/Authorization/DomainContext.cs`) sin romper a sus consumidores:

```csharp
public sealed record DomainContext(DomainKind Kind, string? Host, Guid? HeadTenantId, string ProductCode = ProductCodes.Plataforma);
```

- `ProductCode` va al final y con valor por defecto, para que los constructores actuales (`DomainContextMiddleware`, `HttpDomainContextAccessor` y las fábricas `Flit` y `Network`) sigan compilando sin cambios. `ProductCodes` es una clase estática con los códigos de §1 como `const string` (el valor por defecto de un parámetro tiene que ser constante); la crea B junto con la interfaz de §4.
- Hosts FLIT: raíz o `<ambiente>.<raíz>` ⇒ `plataforma`; `<ambiente>.<producto>.<raíz>` ⇒ `<producto>`; host desconocido ⇒ `plataforma`.
- Redes: `admin.tenant_domains.purpose` (`HUB` ⇒ `plataforma`, o el código del producto).
- Se sigue construyendo **solo** desde el sello `X-Flit-Domain` (ADR-0060).

## 6. Endpoints de plataforma

Todos bajo `/api/v1/platform/**`, documentados en `contracts/openapi/platform.v1.yaml` (archivo nuevo, para no chocar en `core-api.v1.yaml`).

| Método y ruta | Dueño | Auth | Respuesta |
|---|---|---|---|
| `GET /me/apps` | B | Token de usuario, cualquier `aud` | `[{ code, name, icon, url, current }]` con los productos que el usuario puede abrir en su host actual. Al SuperAdmin le devuelve todos |
| `PUT /products/{code}/manifest` | B | Servicio, scope `platform.manifest` | Idempotente. Cuerpo: `{ version, modules:[{code,name,permissions:[{slug,name}]}], defaultRoles:[{code,name,permissions:[…]}] }` |
| `GET /admin/tenants/{tenantId}/products` | B | SuperAdmin | `[{ productCode, enabled, notes, updatedAt, updatedBy }]` |
| `PUT /admin/tenants/{tenantId}/products/{productCode}` | B | SuperAdmin | `{ enabled, notes }`; idempotente y auditado. Apagar la cabeza apaga también a sus hijas |
| `GET /issuers` | A | Anónimo | Lista de emisores válidos (hosts de hub FLIT y de redes activas) |
| `POST /consultas/{fuente}` | C | Servicio, scope `platform.consultas` + `X-Flit-Tenant-Id` | Resultado normalizado actual (`ConsultationResult`) |
| `GET /admin/consultas/consumo?tenantId=&desde=&hasta=` | C | SuperAdmin | Consumo agregado por producto y fuente |

**Permisos del manifiesto.** Formato de `slug`: `<producto>.<módulo>.<acción>` en minúsculas, con `_` si hace falta (por ejemplo `comparendos.bandeja.read`, `diagnostico.informes.export`). Acciones sugeridas: `read`, `create`, `update`, `delete`, `export`, `manage`. `PUT /products/{code}/manifest` rechaza con 400 cualquier `slug` que no empiece por `<code>.`, así un producto no puede registrar permisos de otro. Los slugs actuales de Trámites no se renombran: su producto sale del `product_code` del módulo (B-04).

## 7. Eventos de integración

- Transporte: RabbitMQ, un exchange `topic` por productor (`plataforma`, `tramites`, …). Publicación **solo** vía outbox.
- Sobre común (`Flit.Platform.Contracts`):

```json
{ "eventId": "uuidv7", "type": "platform.tenant_product.changed", "version": 1,
  "occurredAt": "2026-10-01T15:04:05Z", "tenantId": "…", "producer": "plataforma",
  "correlationId": "…", "data": { } }
```

| Evento v1 | Productor | Datos | Uso |
|---|---|---|---|
| `platform.tenant_product.changed` | B | `{ tenantId, productCode, enabled }` | Invalidar caché de acceso |
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

`SessionUser` (lo define `@flit/auth`; el token nunca sale del servidor, así que no va aquí):

```ts
export interface SessionUser {
  id: string;                 // sub
  email: string;
  product: string;            // aud del token de la sesión
  domain: string;             // dom: "flit" o host de la red
  tenant: {
    id: string; name: string; nit: string;
    type: string;             // tenant_type
    entityType: "COMPANY" | "TRANSIT_OFFICE";
    parentId: string | null;  // parent_tenant_id
    isGroupParent: boolean;
  };
  roles: { id: string; code: string }[];
  permissions: string[];
  isSuperAdmin: boolean;      // misma regla multi-rol de §2.1
  expiresAt: number;          // exp, en segundos
}
```

- Los campos salen de los claims que ya lee `frontend/lib/auth/jwt.ts` (`JwtPayload`), con nombres en camelCase.
- Mientras `@flit/auth` no exista, `@flit/shell` recibe la sesión por props desde un adaptador sobre `frontend/lib/auth/jwt.ts`.
- Ningún paquete emite cookies con atributo `Domain`.

## 9. Banderas

Configuración por ambiente (appsettings y configuración runtime del frontend). Todas arrancan en `false` en DEV, QA y PDN y se encienden por ambiente cuando el frente lo pide.

| Bandera | Dueño | Efecto |
|---|---|---|
| `Suite:Oidc:Enabled` | A | Los productos inician sesión por OIDC del hub |
| `Suite:LegacySession:Enabled` | A | Mantiene la cookie `flit_token` actual (convivencia) |
| `Suite:ProductAccess:Enforce` | B | `RequireProduct` rechaza si el producto no está habilitado; apagada = solo registra |
| `Suite:Hub:Enabled` | B | La raíz sirve el hub en vez de Trámites |
| `Suite:TramitesHost:Enabled` | A | Activa el reparto de rutas hacia `<ambiente>.tramites.` |
| `Suite:Consultas:ServiceApi` | C | Expone `/platform/consultas` a otros servicios |

## 10. Errores

RFC 7807 con `code` estable:

| `code` | HTTP | Cuándo |
|---|---|---|
| `PRODUCT_NOT_ENABLED` | 403 | El producto está apagado para la empresa o para su cabeza |
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

Puertos nuevos, siguiendo el esquema actual (DEV y local `40xx`, QA `50xx`, PDN `60xx`). Ya están ocupados `x001` frontend, `x002` gateway, `x003` core-api, `x012` python-ml, `x020` y `x030` migración.

| Servicio | DEV y local | QA | PDN |
|---|---|---|---|
| `frontend-hub` | 4040 | 5040 | 6040 |
| `core-demo` / `frontend-demo` | 4050 / 4051 | — | — |
| `core-comparendos` / `frontend-comparendos` | 4060 / 4061 | 5060 / 5061 | 6060 / 6061 |
| `core-diagnostico` / `frontend-diagnostico` | 4070 / 4071 | 5070 / 5071 | 6070 / 6071 |

El líder los registra en `docs/despliegue-y-puertos.md` en L-03.

## Historial

| Versión | Fecha | Cambio | Aprobado por |
|---|---|---|---|
| v1-borrador | 2026-09-24 | Primera propuesta | — |
| v1 | 2026-09-24 | Resoluciones y Flotas fuera de v1; SuperAdmin con bypass en todos los productos (§2.1); ubicación de `IProductAccessResolver`; habilitación de producto encendido/apagado en lugar de suscripción (§4, §6, §7, §9, §10); `DomainContext` sin romper constructores; formato de slugs del manifiesto; `SessionUser`; puertos | Cerrado por Samuel Cardenas (en rol de líder técnico) |
