# Frente B — Productos, habilitación por empresa, roles por producto y hub

> **Responsable:** Samuel Cardenas, único desarrollador de la suite desde el 2026-09-25 (antes, desarrollador de Comparendos). **Producto que construye después:** Comparendos.
> **Skill:** `flit-suite-b-hub`. **Rama:** una por Feature de ADO, `feature/AB-<Feature>-suite-…`, con commits `HU<id>: …` ([reglas R1](../reglas-trabajo-paralelo.md#r1-ramas-prs-y-merges)).
>
> Leer antes de empezar: [README de la suite](../README.md), [reglas](../reglas-trabajo-paralelo.md),
> [contrato v1](../contrato-plataforma-v1.md) §4, §5, §6, §8 y §9, [plan maestro](../plan-maestro.md)
> §4.5 y §4.6, [ADR-0063](../adr-borradores/ADR-0063-habilitacion-producto-y-rbac-por-producto.md),
> `GUIA-DOCK-INFERIOR-FLOTANTE.md` y `docs/arbol-menu-por-rol.md`.

## Objetivo

Que la plataforma sepa qué productos tiene cada empresa y qué rol tiene cada usuario en cada producto, y lo aplique en la API. Que el hub en `flitsas.online` muestre al usuario sus productos, le permita cambiar de producto con un menú común y cargue el menú propio de cada producto. Comparendos será el primer producto nuevo en usar todo esto.

## Qué debe funcionar al terminar la plataforma

- El SuperAdmin activa, suspende o vence un producto para una empresa, con auditoría. Las hijas de Concesión y Marca Blanca solo pueden tener productos que su cabeza tenga activos.
- Roles y módulos pertenecen a un producto. El AdminCompany asigna roles por producto desde el hub.
- `RequireProduct` rechaza en la API a quien no tiene el producto o el rol, con los códigos del contrato §10.
- `flitsas.online` muestra el inicio con las tarjetas de los productos del usuario. La barra común con el menú de productos y el menú de cuenta es la misma en el hub y en Trámites. Cada producto tiene su propio dock.
- La administración de plataforma vive en el hub y ya no en el menú de Trámites.

## Lo que entregas a otros frentes

| Entrega | Para | Cuándo |
|---|---|---|
| Secciones §4, §5, §6 (tus endpoints) y §8 (`@flit/ui`, `@flit/shell`) del contrato cerradas | A, C | Semana 1 |
| `packages/ui` v0 | C (plantilla) | Fin de la Fase 0 |
| Esqueleto de `frontend-hub` con el grupo `app/(auth)` libre para A | A | Inicio de la Fase 1 |
| `IProductAccessResolver` real | A | Mitad de la Fase 1 |
| `DomainContext.ProductCode` y `tenant_domains.purpose` | A | Mitad de la Fase 1 |
| `PUT /platform/products/{code}/manifest` y `GET /platform/me/apps` | C (plantilla y producto de prueba) | Fin de la Fase 1 |
| `@flit/shell` v1 | A (Trámites), C (plantilla) | Inicio de la Fase 2 |

## Lo que consumes y el stub mientras tanto

| Necesitas | De | Stub mientras tanto |
|---|---|---|
| `@flit/auth` (sesión, logout) | A | Adaptador que lee el token con `frontend/lib/auth/jwt.ts` y se pasa por props a `SuiteShell` |
| Login OIDC en el hub | A | El hub usa el login actual de `frontend/app/login` mientras `Suite:Oidc:Enabled` está apagada |
| Publicador de eventos | C | Escribir en el outbox y no publicar; invalidar la caché localmente |
| Redis en DEV | Líder | Caché en memoria con vida corta |
| `packages/*` y `frontend-*` en el workspace | Líder | Pedirlo en la semana 1 (tarea L-02) |

## Tus carpetas

Ver [reglas R4](../reglas-trabajo-paralelo.md#r4-propiedad-de-carpetas). Resumen: `Flit.Modules.Platform` (nuevo), configuraciones y entidades de RBAC en `Flit.Infrastructure/Persistence/**/Security`, `SecurityModuleRepository.cs`, `Flit.Admin.Domain/Companies/Domains`, `Flit.Infrastructure/Domains`, `packages/ui`, `packages/shell`, `frontend-hub` salvo `app/(auth)`, y en `frontend/`: `Shell.tsx`, `dock/**`, `lib/nav/**` y `RbacAdmin.tsx`.

## Features sugeridas en ADO

- **B1 — Productos, habilitación por empresa y roles por producto:** B-01 a B-08.
- **B2 — Hub y shell común:** B-09 a B-13.

---

## Estado

- [ ] B-00 Cerrar §4, §5, §6 y §8 del contrato con A y C
- [ ] B-01 Inventario plataforma contra Trámites
- [ ] B-02 Paquete `@flit/ui` v0
- [ ] B-03 Módulo de plataforma y schema `platform`
- [ ] B-04 `product_code` en módulos y roles
- [ ] B-05 `IProductAccessResolver` con herencia de jerarquía
- [ ] B-06 Endpoints de plataforma y `RequireProduct`
- [ ] B-07 Migrar los booleans de módulos a la habilitación de productos
- [ ] B-08 `DomainContext` con producto y `tenant_domains.purpose`
- [ ] B-09 Esqueleto de `frontend-hub`
- [ ] B-10 Paquete `@flit/shell`
- [ ] B-11 Inicio del hub y menú de productos
- [ ] B-12 Administración de plataforma en el hub
- [ ] B-13 Trámites adopta `@flit/shell`

---

## Tareas

### B-00 · Contrato de productos y hub · Fase 0 · semana 1 · S

- **Qué:** revisar con A y C las secciones §4, §5, §6 (me/apps, manifiesto, habilitación de productos) y §8 (`@flit/ui`, `@flit/shell`) y cerrarlas.
- **Hecho cuando:** PR que cambia solo el contrato, aprobado por los tres frentes.

### B-01 · Inventario plataforma contra Trámites · Fase 0 · semanas 1–2 · S

- **Qué:** clasificar como **plataforma** o **Trámites** cada pantalla de `frontend/app/admin/**`, cada módulo del SPA `?m=`, cada entrada del dock (`frontend/components/atom/Shell.tsx`, `dock/dockGroups.ts`), cada código de `security.modules` (semilla en `Flit.Infrastructure/Security/DevelopmentAuthSeeder.cs`) y los endpoints de `/api/v1/admin/**` y `/api/v1/security/**`.
- **Hecho cuando:** existe `docs/suite/frentes/b-inventario-plataforma-vs-tramites.md` y A y el líder lo revisaron. Punto de partida del plan maestro §4.6.

### B-02 · `@flit/ui` v0 · Fase 0–1 · M

- **Qué:** crear `packages/ui` con los tokens (`frontend/lib/flit-design-tokens.ts` y los de `app/globals.css`) y los átomos genéricos de `frontend/components/atom`: `DataTable`, `Modal`, `StatusBadge`, `Pagination`, `SearchableSelect`, `InlineAlert`, `Loader` y los que marque el inventario. `Shell.tsx` **no** entra aquí.
- **Regla:** Trámites pasa a importar de `@flit/ui` sin ningún cambio visual. Mueve y reexporta; no rediseñes.
- **Hecho cuando:** `frontend/` compila e importa de `@flit/ui`, y las pruebas de esos átomos no suman fallos a la línea base (R8).

### B-03 · Módulo de plataforma y schema `platform` · Fase 1 · M

- **Qué:** módulo nuevo `Flit.Modules.Platform` (Domain + Application) con las tablas `platform.products` y `platform.tenant_products` (producto encendido o apagado por empresa) del ADR-0063. Semilla: `plataforma`, `tramites`, `comparendos`, `diagnostico`, `demo`. Auditoría en `admin.tenant_config_audit_logs`.
- **Dónde:** configuraciones EF en archivos propios; migración (R6); una línea en `Program.cs` e `InfrastructureExtensions.cs` (R5).
- **Hecho cuando:** la migración corre en DEV y todas las empresas existentes tienen `tramites` activo.

### B-04 · `product_code` en módulos y roles · Fase 1 · M

- **Qué:** columna `product_code` en `security.modules` y `security.roles`. Un rol solo puede incluir permisos de módulos de su producto. Migración que etiqueta los existentes con `tramites` o `plataforma` según B-01. `GET /api/v1/security/modules?product=`.
- **Dónde:** `Flit.Infrastructure/Persistence/Configurations/Security/RbacConfigurations.cs`, `Persistence/Entities/Security/Role.cs` y `SecurityModule.cs`, `Persistence/Repositories/SecurityModuleRepository.cs`. Migración (R6).
- **Hecho cuando:** los roles actuales siguen funcionando igual y cada uno tiene producto. La HU #10664 no se revierte: los módulos siguen sin habilitación por empresa.

### B-05 · `IProductAccessResolver` · Fase 1 · M

- **Qué:** implementar el contrato §4. Producto encendido para la empresa y para su cabeza (reglas fail-closed de ADR-0057 de jerarquía) más los roles y permisos del usuario en ese producto. Caché en Redis, invalidada por `platform.tenant_product.changed` y `platform.roles.changed`, que tú publicas.
- **Entrega a A:** avísale para que borre `StubProductAccessResolver`.
- **Hecho cuando:** pruebas de la matriz del ADR-0063: empresa con y sin producto, usuario con y sin rol, producto encendido o apagado, empresa hija y SuperAdmin (bypass del contrato §2.1).

### B-06 · Endpoints de plataforma y `RequireProduct` · Fase 1 · M

- **Qué:** `GET /api/v1/platform/me/apps`, `PUT /api/v1/platform/products/{code}/manifest` (servicio, idempotente) y `GET/PUT /api/v1/platform/admin/tenants/{tenantId}/products` para que el SuperAdmin encienda o apague productos, según el contrato §6. Policy `RequireProduct` aplicada a todo el grupo de rutas de Trámites, detrás de `Suite:ProductAccess:Enforce`: apagada solo registra, encendida rechaza.
- **Dónde:** `Flit.Modules.Platform` y un archivo de endpoints propio; contrato en `contracts/openapi/platform.v1.yaml`.
- **Hecho cuando:** con la bandera encendida en DEV, una empresa con `tramites` apagado recibe `PRODUCT_NOT_ENABLED` en la API, no solo en el menú.

### B-07 · Booleans de módulos a habilitación de productos · Fase 1 · S

- **Qué:** convertir `tramites_module_enabled` y `comparendos_module_enabled` en filas de `platform.tenant_products`. `resoluciones_module_enabled` no se toca (Resoluciones está fuera de v1). La tarjeta "Próximamente" y el fieldset de la configuración de empresa pasan a leer la habilitación de productos. Retirar los booleans un sprint después.
- **Dónde:** `Flit.Admin.Domain/Companies/Settings/TenantSettings.cs:135-149`, `Configurations/Admin/TenantOperationalPolicyConfiguration.cs`, `Flit.Api/Endpoints/Analytics/DashboardActiveModulesEndpoints.cs`, `frontend/components/admin/companies/tabs/ConfiguracionEmpresaTab.tsx`, `frontend/components/atom/modules/Dashboard.tsx`.
- **Hecho cuando:** ninguna pantalla lee los booleans. Migración (R6) para quitarlos.

### B-08 · `DomainContext` con producto y `tenant_domains.purpose` · Fase 1 · M

- **Qué:** contrato §5. `DomainContext` gana `ProductCode`. `admin.tenant_domains` gana `purpose` (`HUB` o código de producto) y la unicidad pasa de `tenant_id` a `(tenant_id, purpose)`. Los registros actuales quedan con `purpose = HUB`.
- **Dónde:** `Flit.Api/Authorization/DomainContext.cs` y `Middleware/DomainContextMiddleware.cs`, `Flit.Admin.Domain/Companies/Domains/TenantDomain.cs`, `Flit.Infrastructure/Domains/CachedTenantDomainResolver.cs`, vista `admin.v_active_network_domains`. Migración (R6).
- **Hecho cuando:** la suite `tests/Flit.Integration.Tests/MarcaBlanca` pasa sin cambios de comportamiento, y un segundo dominio de la misma red con otro `purpose` resuelve su producto.

### B-09 · Esqueleto de `frontend-hub` · Fase 1 (inicio) · S

- **Qué:** app Next.js 16 en `frontend-hub/`, en el workspace pnpm, con configuración en runtime (sin `NEXT_PUBLIC_*` horneado para hosts), la marca por host de Marca Blanca y el grupo de rutas `app/(auth)` vacío y reservado para A.
- **Hecho cuando:** corre en local en su puerto asignado y el CD la construye como imagen propia (con el líder).

### B-10 · `@flit/shell` · Fase 2 · M–L

- **Qué:** `SuiteShell` según el contrato §8. Barra común con logo de la marca, nombre del producto, menú de productos (rejilla) y menú de cuenta. Dock del producto dibujado desde un catálogo de navegación (Contract A de `GUIA-DOCK-INFERIOR-FLOTANTE.md`). Reutiliza `frontend/components/brand/BrandProvider.tsx`.
- **Regla:** `SuiteShell` no importa nada de un producto concreto. Hoy `Shell.tsx` importa navegación de OT, plataforma y Dr. FLIT; eso pasa al catálogo que entrega Trámites.
- **Hecho cuando:** hub y producto de prueba usan `SuiteShell` con catálogos distintos.

### B-11 · Inicio del hub y menú de productos · Fase 2 · M (+2–3 días por la opción 4)

- **Qué:** el hub de `flitsas.online` según la **opción 4, «Hub con entrada directa»** (plan maestro §4.6; **provisional** mientras el CTO y el líder deciden, confirmar antes de empezar):
  - **Sin sesión:** portada breve con la marca del host (logo, una frase, «Iniciar sesión» y cada producto con «Conocer más ↗» hacia `flitsas.com`). En Marca Blanca, solo la marca de la red.
  - **Con dos o más productos:** inicio con saludo y las tarjetas de los productos de `GET /platform/me/apps`, solo los que el usuario puede abrir, y los accesos de administración.
  - **Con un solo producto:** entrada directa al producto. El hub queda a un clic en ▦ → Inicio.
  - El menú de productos es la única forma de cambiar de producto. El hub tiene su propio menú: Inicio, Empresa, Usuarios y roles, Productos, Marca y dominio, Auditoría. Detrás de `Suite:Hub:Enabled`.
- **Hecho cuando:** un usuario con dos productos ve dos tarjetas y cambia entre ellos sin volver a iniciar sesión (con A-07 listo); un usuario con solo Trámites que abre `flitsas.online` aterriza en Trámites; sin sesión se ve la portada con la marca del host, también en un dominio de Marca Blanca.

### B-12 · Administración de plataforma en el hub · Fase 2 · L

- **Qué:** mover al hub, según el inventario B-01, las pantallas de compañías y sus hijas, usuarios, RBAC por producto (evolución de `RbacAdmin.tsx`), auditoría, marca y dominio, productos habilitados y catálogo de jobs. Las rutas viejas de `frontend/app/admin/**` redirigen al hub. Coordina el mapa de redirecciones con A (A-11).
- **Regla:** mover por secciones, un PR por sección, cada una detrás de `Suite:Hub:Enabled`.
- **Hecho cuando:** el menú de Trámites ya no tiene entradas de plataforma y cada pantalla movida tiene su redirección.

### B-13 · Trámites adopta `@flit/shell` · Fase 2 · M

- **Qué:** `frontend/` pasa a `SuiteShell` con su catálogo de navegación. Se quitan del dock las entradas que se fueron al hub. Se conservan las de Trámites: Trámites, Preasignación, Identidad, Reportes, Historial de placa, Administración OT, Organismos de tránsito, Documental, Improntas, Quipux, Tipos de trámite, Mandatos, FUR, Causales, Generación documental, LOG QX e ICT.
- **Coordinación:** A-10 cambia la sesión de Trámites; B-13 va después de A-10 en el orden de trabajo.
- **Hecho cuando:** las pruebas del dock y del Shell (`frontend/__tests__/Shell.*`, `dock/**`) pasan con el catálogo nuevo.

---

## Riesgos del frente

| Riesgo | Qué hacer |
|---|---|
| Tres migraciones seguidas (B-03, B-04, B-08) chocan con las del resto del equipo en `develop` | Traer `develop` a diario y, si aparece una migración más nueva, regenerar la propia (R6) |
| `Shell.tsx` (872 líneas) está acoplado a productos | Primero catálogo y adaptador, después `SuiteShell`; nunca los dos en el mismo PR |
| Mover pantallas duplica código durante la transición | Una sección por PR, con redirección y bandera |
| `RequireProduct` encendido corta a usuarios reales | Semana en modo "solo registra" y revisión de los registros antes de encender |

## Bitácora

| Fecha | Tarea | PR | Nota |
|---|---|---|---|
| | | | |
