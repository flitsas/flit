# Inventario: plataforma frente a Trámites (B-01)

> **HU #12899** · Frente B (Samuel Cardenas) · Base: `develop@da3074ac` (2026-09-25).
> Revisan: Juan Felipe Montoya (frente A) y Jorman Copete (líder).
>
> Para qué sirve: decidir qué pantallas, entradas de menú, módulos RBAC y endpoints de hoy son de la
> **plataforma** (se mueven al hub en `flitsas.online`) y cuáles son del **producto Trámites** (se
> quedan en `tramites.flitsas.online`). Es la entrada de B-04 (`product_code` en módulos y roles),
> B-07 (habilitación de productos), B-10 (`@flit/shell`), B-12 (administración en el hub) y B-13
> (Trámites adopta el shell).

## Cómo leer este documento

- **P** = plataforma · **T** = Trámites · **P/T** = hay que partirlo.
- Cada fila tiene una **decisión**. Donde el código no alcanza para decidir, la decisión queda como
  **propuesta** y se lista en [Decisiones abiertas](#decisiones-abiertas).
- Criterio: es **plataforma** lo que cualquier producto necesita igual (identidad, empresas y su
  jerarquía, usuarios, roles y permisos, marca y dominio, auditoría, habilitación de productos,
  banners, catálogo de jobs, proveedores de consultas externas). Es **Trámites** lo que solo existe
  por el negocio de trámites vehiculares y organismos de tránsito (OT).
- Las rutas de archivo son relativas a `frontend/` o a `services/core-api/src/Flit.Api/Endpoints/`
  según la sección.

## Resumen

| Superficie | Total | P | T | P/T |
|---|---|---|---|---|
| Pantallas `frontend/app/admin/**` | 44 páginas | 5 | 36 | 3 (ficha de compañía, catálogo de OT, notificaciones) |
| Módulos del SPA `?m=` | 14 claves | 3 (`usuarios`, `rbac`, `auditoria`) | 10 | 1 (`dashboard`, que en el hub se reemplaza por su propio inicio) |
| Rutas de primer nivel fuera de admin | 18 | 7 (login, auth, invitación, perfil, 403, 404) | 10 | 1 (`/`, la SPA actual) |
| Entradas del dock | ≈39 | ≈10 | ≈27 | 2 |
| Módulos RBAC (`security.modules`) | 17 | 4 | 13 | 0 |
| Roles sembrados | 4 | 2 | 2 | 0 |
| Endpoints `/api/v1/{auth,security,superadmin,admin}` | ≈340 | por grupo, ver sección 6 | por grupo, ver sección 6 | — |

Lo más importante no son los números sino los [hallazgos](#hallazgos-que-afectan-a-otros-frentes):
dos de ellos cambian tareas del frente A y uno obliga a una decisión de contrato.

---

## 1. Pantallas `frontend/app/admin/**`

Acceso por URL: `frontend/middleware.ts` → `lib/auth/guard.ts` (`evaluateAdminAccess`). SuperAdmin
entra a todo `/admin/*`; AdminCompany a `/admin/companies/*`; usuarios OT a `/admin/transit-offices/*`;
tres rutas se abren por permiso (`generacion-documental.read`, `runt_confirmation.*`, `banners.manage`).

| Ruta | Decisión | Nota |
|---|---|---|
| `/admin/companies` | **P** | Lista, alta, activación de empresas |
| `/admin/companies/[tenantId]` | **P/T** | Ficha de empresa: se parte por pestaña, ver 1.1 |
| `/admin/companies/[tenantId]/children` | **P** | Panel de red: hijas, marca y dominio de Marca Blanca |
| `/admin/transit-offices` | **T** | Catálogo de organismos. El OT es un tenant, pero solo existe para Trámites |
| `/admin/transit-offices/[id]/*` (client-procedures, rules, requirements, documents, reportes, mandatos, imprint-validation, revocation-requests, configuracion, webhooks) | **T** | Hub OT completo. `webhooks` solo es accesible por URL y parece legado |
| `/admin/transit-offices/[id]/usuarios` | **T (propuesta)** | Son usuarios, pero acotados al organismo y con UI propia. Se unifica con Usuarios del hub después de B-12 (ver hallazgo 5) |
| `/admin/causales-rechazo`, `/admin/documents/**`, `/admin/improntas/**`, `/admin/quipux` | **T** | Catálogos del negocio de trámites |
| `/admin/jobs` | **P** | Catálogo de procesos periódicos. Hoy solo lista jobs de ICT y Quipux: cada producto registrará los suyos |
| `/admin/jobs/ict` | **T** | Cadencia del job ICT |
| `/admin/rbac` | **P** | Redirige a `/?m=rbac` |
| `/admin/banners` | **P** | Banners globales |
| `/admin/plataforma/tipos-tramite`, `/mandatos`, `/fur`, `/confirmacion-runt/**` | **T** | Viven bajo `/admin/plataforma/` pero son de Trámites (ver hallazgo 6) |
| `/admin/plataforma/notificaciones` | **P/T** | Canales y buzón de pruebas son P; las plantillas son de eventos de trámite |
| `/admin/generacion-documental/**` | **T** | Emite documentos RUES y de transferencia; dominio documental de Trámites |
| `/admin/migracion` | **T (temporal)** | Consola V1 → V2 oculta; se borra al apagar V1. No se mueve |

### 1.1 Ficha de empresa `/admin/companies/[tenantId]` (`components/admin/companies/CompanyConfigTabs.tsx`)

| Pestaña o sección | Decisión | Nota |
|---|---|---|
| Trámites (`tabs/TramitesTab.tsx`) | **T** | Bloqueos por familia y política de trámites de la empresa |
| Configuración Empresa (`tabs/ConfiguracionEmpresaTab.tsx`) | **P/T** | Se parte en B-12: |
| ↳ Módulo de Trámites / Comparendos / Resoluciones | **P** | Pasa a la pantalla «Productos» del hub (habilitación, contrato §4). Resoluciones queda fuera de v1 |
| ↳ SMTP | **P** | Remitente de correo de la empresa |
| ↳ Destinatarios de notificación | **T** | Avisos de aprobación y rechazo de trámites |
| ↳ Firma precargada, validar SOAT | **T** | Parámetros operativos de trámites |
| ↳ Fuente de comparendos | **T hoy** | Cuando exista el producto Comparendos, esta configuración se revisa con él |
| ↳ Proveedores de consulta RUNT (`ConsultaProvidersSection.tsx`) y de avalúo (`AvaluoProvidersSection.tsx`) | **P** | Capacidad compartida del frente C (ADR-0065) |
| Whitelist (`panels/WhitelistPanel.tsx`) | **P** | Whitelist de correos (`POST/GET /admin/companies/{t}/whitelist`) |
| OT (`panels/OTConfigTablePanel.tsx`, `TransitBlocksPanel.tsx`) | **T** | Organismos concedidos y bloqueados |
| Documentos (`CompanyDocumentParamsPanel`) | **T** | Parámetros documentales |
| Representantes legales, escrituras, baúl de firmas | **T (propuesta)** | Datos maestros de la empresa que hoy solo usan Trámites y Mandatos. Ver decisión abierta D3 |
| Mandatarios | **T** | Mandatos |
| Usuarios (`panels/CompanyUsersPanel.tsx`) | **P** | |
| Historial de cambios (`panels/AuditLogPanel.tsx`) | **P** | Auditoría de configuración |
| Hijas, Marca, Dominio | **P** | Jerarquía y Marca Blanca |

## 2. Módulos del SPA `?m=`

Resolución en `frontend/app/page.tsx`; registro en `lib/nav/modules.ts`; el tipo `ModuleId` está en
`components/atom/Shell.tsx:76-90` y mezcla ids de los dos productos.

| Clave | Decisión | Nota |
|---|---|---|
| `dashboard` | **T** | Mide trámites (o la cola del OT). El hub tendrá su propio inicio con las tarjetas de productos (B-11) |
| `tramites`, `historial-placa`, `reportes`, `reportes-detallados`, `log-qx`, `ict-logs`, `ict-reportes`, `ict-trazabilidad` | **T** | |
| `validaciones` | **T (propuesta)** | Validación biométrica. Hoy nace del trámite (`/api/v1/tramites/biometric-validations`). Candidata a capacidad compartida más adelante |
| `usuarios`, `rbac`, `auditoria` | **P** | La pestaña «Clientes ICT» dentro de Usuarios es **T** y se separa en B-12 |
| `ayuda` | **T hoy** | El manual es de operación de trámites. El hub tendrá su propia ayuda |

## 3. Rutas de primer nivel fuera de `/admin`

| Ruta | Decisión | Nota |
|---|---|---|
| `/login`, `/auth/forgot-password`, `/auth/reset-password`, `/invite/activate`, `/profile/change-password`, `/403`, not-found | **P** | Pasan a `frontend-hub/app/(auth)` (frente A: A-06) o al hub |
| `/` | **P/T** | Hoy es la SPA de Trámites con el dock común. Con `Suite:Hub:Enabled` la raíz sirve el hub (B-11) |
| `/tramites/**`, `/log-qx/[submissionId]`, `/portal/[token]`, `/biometric/[token]` | **T** | `/portal` y `/biometric` son públicas por token; sus enlaces se conservan con redirección (A-11) |
| `/manual/**` | **T hoy** | Igual que `ayuda` |
| `/api/migracion/*` | **T (temporal)** | |
| `/empresa/*` | **Código muerto** | El middleware y el guard lo protegen pero no existe `app/empresa/`. Se borra (hallazgo 7) |

## 4. Dock y navegación

El dock se arma en código imperativo en `components/atom/Shell.tsx` (líneas 92-572), no desde un
catálogo. `components/atom/dock/dockGroups.ts` solo asigna cada `key` a un grupo. Por eso B-10 tiene
que empezar por **un catálogo declarativo** (Contrato A de `GUIA-DOCK-INFERIOR-FLOTANTE.md`) con
`product: "plataforma" | "tramites"` en cada ítem, antes de tocar `SuiteShell`.

| Grupo | Entrada (`key`) | Decisión |
|---|---|---|
| (botón central) | Inicio → `/?m=dashboard` | **T** (el hub tiene su propio Inicio) |
| tramites | `tramites`, `historial-placa`, `ot-adm-tramites` | **T** |
| identidad | `validaciones` | **T** |
| reportes | `reportes`, `reportes-detallados`, `ot-adm-reportes` | **T** |
| usuarios | `usuarios` | **P** |
| usuarios | `ot-adm-usuarios` | **T (propuesta)**, igual que la pantalla |
| administradores | `admin-companies` (Compañías / Administración), `admin-network` (Red de clientes), `admin-jobs`, `rbac`, `auditoria`, `admin-banners` | **P** |
| administradores | `admin-transit` (Organismos, Causales), `admin-documents`, `admin-improntas`, `admin-quipux`, `admin-generacion-documental` | **T** |
| administradores | `admin-plataforma` ▾: `admin-tipos-tramite`, `admin-confirmacion-runt`, `admin-mandatos`, `admin-fur` | **T** |
| administradores | `admin-plataforma` ▾: `admin-notificaciones` | **P/T** (ver pantalla) |
| administracion | `ot-adm-documents`, `ot-adm-mandatos`, `ot-adm-imprint-validation` | **T** |
| integraciones | `log-qx`, `ict` ▾ (`ict-logs`, `ict-trazabilidad`, `ict-reportes`) | **T** |
| menú de cuenta ⋮ | Cambio de contraseña, Salir | **P** (menú de cuenta de `@flit/shell`) |
| menú de cuenta ⋮ | Ayuda → `/manual` | **T hoy** |

**Qué sale de `Shell.tsx` hacia el catálogo de Trámites** (B-10 y B-13): `ot-nav` (`OT_ADM_DOCK`,
`resolveOtHubHref`, `isOtHubSegmentActive`, `otHubListPath`), la llamada `fetchOtProfile`,
`OT_ADMIN_SPA_OMIT`, `generacion-documental-nav`, `CONFIRMACION_RUNT_BASE_PATH`, los predicados
`canReadLogQx`, `canReadIctLogs`, `canAccessRuntConfirmation`, `isOtUser`, `isOtAdmin`, y los flags que
recibe `DrFlitAssistant` (`historialPlacaEnabled`, `canSearchValidaciones`).
**Qué se queda en el shell común**: `isSuperAdmin`, `isAdminCompany`, `isGroupParent`, `canManageBanners`.
`dockGroups.ts` importa `OT_ADM_DOCK` (línea 11): es su único acoplamiento a Trámites.

## 5. Módulos RBAC y roles: `product_code` para B-04

Fuente: `Flit.Infrastructure/Security/DevelopmentAuthSeeder.cs` (único lugar que siembra el catálogo).

| `security.modules.code` | Permisos | `product_code` |
|---|---|---|
| `auth` | auth.me.read | `plataforma` |
| `usuarios` | usuarios.manage, security.users.reset_password | `plataforma` |
| `rbac` | rbac.manage | `plataforma` |
| `banners` | banners.manage | `plataforma` |
| `dashboard` | dashboard.read | `tramites` |
| `tramites` | tramites.read, tramites.create | `tramites` |
| `reportes` | reportes.read, reportes.{resumen,operacion,ot,uso,productividad,consultas}.read, reportes.programacion.manage | `tramites` |
| `reportes-detallados` | reportes.detallados.{read,export} | `tramites` |
| `validaciones` | validaciones.{read,manage} | `tramites` |
| `improntas` | improntas.{read,generate} | `tramites` |
| `historial-placa` | historial-placa.read | `tramites` |
| `logqx` | logqx.read | `tramites` |
| `ict-logs` | ict.logs.read, ict.pii.reveal | `tramites` |
| `ict-clients` | ict.clients.manage | `tramites` |
| `generacion-documental` | generacion-documental.{read,generate} | `tramites` |
| `confirmacion-runt` | runt_confirmation.{settings.manage,history.read} | `tramites` |
| `admin-tramites-avanzado` | AdminTramite{CambiarEstado,Anular,LimpiarConsolidado,CargarConsolidado,ReenviarValidacion,ReasignarGestor} | `tramites` |

| Rol | `target_entity_type` | `product_code` |
|---|---|---|
| `SuperAdmin` | COMPANY | `plataforma` (ADR-0063; bypass en todos los productos, contrato §2.1) |
| `AdminCompany` | COMPANY | `plataforma` (ADR-0063), **con el problema de la decisión D1** |
| `ot_admin` | TRANSIT_OFFICE | `tramites` |
| `Radicador` | COMPANY | `tramites` |

Notas para la migración de B-04:

- **Rellenar `product_code` por `code`**, con los valores de estas tablas. No se puede depender del
  seeder: solo corre en `Development` (hallazgo 1) y en QA/PDN el catálogo no lo crea ningún código
  versionado.
- Un módulo o rol que no esté en la tabla (creado a mano desde RBAC Admin) queda en `tramites` por
  defecto y se registra en el log de la migración para revisarlo.
- Slug usado en código y nunca sembrado: `security.users.reset_password.all`
  (`Flit.Modules.Security.Application/Auth/AdminResetPassword/AdminResetPasswordHandler.cs:40,162`).
  Se siembra en `usuarios` (plataforma) dentro de B-04.
- `SeedBaseModulesAsync` sale temprano si ya existe `dashboard`: agregar `ProductCode` a su arreglo no
  actualiza bases ya sembradas.

## 6. Endpoints

Inventario completo agrupado por archivo en el anexo A. A nivel de grupo:

| Grupo | Decisión |
|---|---|
| `/api/v1/auth/*` (`AuthEndpoints.cs`, 7 endpoints) | **P** (frente A) |
| `/api/v1/security/*` (`SecurityEndpoints.cs`, 13) | **P** |
| `/api/v1/superadmin/{modules,permissions,roles,users,audit}` | **P** |
| `/api/v1/superadmin/{external-data-sources,consultation-templates}` | **P** (catálogo de consultas, frente C) |
| `/api/v1/superadmin/{procedure-types,procedure-entities}` | **T** |
| `/api/v1/admin/companies` (CRUD, estado, parent, whitelist, audit-log), branding, domain, children, children/invitations, `/api/v1/company/{branding,domain}`, `/api/v1/internal/domains` | **P** |
| `/api/v1/admin/companies/{t}/settings` y `children/{c}/settings` | **P/T**: el mismo JSON mezcla habilitación de productos y flags de trámites. Se parte en B-07 |
| `/api/v1/admin/companies/{t}/{transit-grants,transit-blocks,transit-agreements,ot-consultation-restrictions,ot-blocking-policies,ot-prenda-document-policies}` | **T** |
| `/api/v1/admin/companies/{t}/{mandate-signers,personalized-documents,document-params}` | **T** |
| `/api/v1/admin/companies/{t}/{legal-representatives,deeds,signature-vault,identity-validations}` | **T (propuesta, D3)** |
| `/api/v1/admin/companies/{t}/notification-delivery-logs`, `/admin/plataforma/notificaciones` (canales, buzón) | **P** |
| `/api/v1/admin/plataforma/notificaciones/plantillas` | **T** |
| `/api/v1/admin/platform/{hierarchy-switches,network-access-audit}`, `/api/v1/admin/banners`, `/api/v1/public/banners`, `/api/v1/{public,me}/branding`, `/api/v1/me/ui-preferences` | **P** |
| `/api/v1/admin/ict/{jobs,job-settings}` | **T** (son jobs de ICT; el catálogo común de jobs es P) |
| `/api/v1/admin/transit-office-tenants` | **T (propuesta)**: alta de tenants de tipo OT |
| `/api/v1/admin/ot/**` salvo usuarios e invitaciones, `/admin/plate-ranges`, `/admin/transit-offices/**`, `/admin/quipux`, `/admin/log-qx`, `/admin/runt-confirmation`, `/admin/plataforma/{mandatos,fur}`, documental, improntas, generación documental, `/admin/tramites/*` | **T** |
| `/api/v1/admin/ot/{users,invitations}/*` | **P**, pero duplicado de `/security/*` (hallazgo 5) |
| `/api/v1/analytics/*`, `/detailed-report/*`, report-schedules, alert-rules, queries | **T** |
| `UsageEvents`, `DashboardActiveModules` | **P** (`DashboardActiveModules` desaparece con B-07) |
| `/api/v1/tramites/**`, `/api/v1/public/{procedures,procedure-types,biometrica,kyverum,portal}`, `/api/v1/integrations/ot`, gRPC ICT | **T** |

---

## Hallazgos que afectan a otros frentes

1. **El catálogo RBAC solo se siembra en `Development`** (`DevelopmentAuthSeeder.cs:86`,
   `if (!environment.IsDevelopment()) return;`). Cuando A-02 saque DEV de `Development`, **DEV deja de
   recibir los módulos y permisos nuevos** que se agreguen al seeder. Para el frente A (A-01): incluirlo
   en el inventario de ambientes y decidir si el catálogo pasa a sembrarse por migración o por un
   seeder que corra en todos los ambientes. Para el frente B: B-04 rellena `product_code` por
   migración, no por seeder.
2. **Roles de plataforma dentro de Trámites (decisión D1).** El contrato §2 dice que `AdminCompany`
   solo viaja en tokens con `aud=plataforma`. Pero Trámites decide cosas con ese rol hoy:
   `frontend/app/tramites/revocatorias/page.tsx:39` (`isAdminCompany`) y los endpoints
   `Endpoints/Tramites/Network{Reports,Children,Attachment,Procedure}Endpoints.cs`. Con el contrato
   como está, esas funciones dejan de funcionar el día que se encienda `Suite:Oidc:Enabled`. Además,
   `AdminCompany` tiene hoy permisos de módulos de Trámites (reportes, historial-placa,
   generación documental), y el contrato §4 dice que un rol solo puede tener permisos de su producto.
3. **Los permisos guardan la ruta HTTP.** El seeder guarda `RoutePattern` con las rutas actuales
   (por ejemplo `DevelopmentAuthSeeder.cs:912-915, 1165, 1549`). También dependen del prefijo
   `Middleware/TenantEnforcementMiddleware.cs:219` y `TenantWriteGuardMiddleware.cs:26`
   (`/api/v1/admin/tramites`) y `UsageTelemetryMiddleware.cs:100-102` (`/api/v1/security`,
   `/api/v1/admin`). **Recomendación: no mover rutas de plataforma a `/api/v1/platform/**`** en esta
   fase. Solo los endpoints nuevos del contrato §6 van ahí; los existentes se etiquetan con su producto
   y se quedan en su ruta.
4. **«Red de clientes» no aparece en el dock hoy.** `Shell.tsx:505-513` emite la key `admin-network`,
   pero no está en `DOCK_ITEM_GROUP` y `buildDockGroups` descarta en silencio lo que no tiene grupo
   (`dockGroups.ts:167`). La clave `mi-empresa` (`dockGroups.ts:107`) no la emite nadie. Es un bug
   actual de Trámites; se corrige al crear el catálogo de B-10 (y se puede corregir antes con una
   línea, si Trámites lo pide).
5. **La gestión de usuarios está triplicada**: `/api/v1/security/*`, `/api/v1/admin/ot/{users,invitations}/*`
   y `/api/v1/admin/companies/{h}/children/{c}/{invitations,users}`. Las tres son plataforma. En B-12 el
   hub usa `/security/*`; unificar las otras dos queda como deuda con su propia HU.
6. **«Plataforma» no significa plataforma.** El contenedor del dock `admin-plataforma` y las rutas
   `/admin/plataforma/{tipos-tramite,mandatos,fur,confirmacion-runt}` son de Trámites. En B-13 el
   contenedor se renombra (por ejemplo «Configuración de trámites»). Las URLs no se cambian para no
   romper enlaces.
7. **Código muerto**: `/empresa/*` está protegido en `middleware.ts` y `lib/auth/guard.ts`
   (`evaluateEmpresaAccess`) pero no existe `app/empresa/`. `RbacAdmin.tsx` menciona `/empresa/roles`.
8. **Documentación desactualizada**: `docs/arbol-menu-por-rol.md` todavía lista el grupo
   `preasignacion` y, en el dock del Admin OT, Reglas, Requisitos y Configuración, que el código ya
   retiró (HU #12850 y #12856).

## Decisiones abiertas

| # | Decisión | Opciones | Propuesta | Decide |
|---|---|---|---|---|
| D1 | `AdminCompany` en Trámites (hallazgo 2) | (a) `AdminCompany` viaja también en los tokens de Trámites; (b) un rol por usuario **en cada producto**, con un rol `admin_tramites` propio de Trámites | **DECIDIDO 2026-09-25: opción (b).** Ver [Decisión D1](#decisión-d1-un-rol-por-producto) | Samuel Cardenas (B) |
| D2 | Inicio del hub y `dashboard` | El FAB abre hoy el dashboard de trámites | El hub tiene su propio inicio (B-11) y `dashboard` se queda en Trámites | Samuel |
| D3 | Representantes legales, escrituras, baúl de firmas, vigencia de identidad | Plataforma (datos maestros de la empresa) o Trámites | **Trámites** en v1: hoy solo los usan Trámites y Mandatos. Se reabre si otro producto los necesita (Comparendos podría necesitar el representante legal de la empresa) | Samuel |
| D4 | Usuarios del organismo (`/admin/transit-offices/[id]/usuarios`, `/admin/ot/users`) | Hub o Trámites | **Trámites** en v1; se unifica con Usuarios del hub en una HU aparte | Samuel |
| D5 | Catálogo RBAC fuera de `Development` (hallazgo 1) | Migración, seeder para todos los ambientes, o nada | Lo decide el frente A en A-01 | Juan Felipe (A) |

### Decisión D1: un rol por producto

**Decidido el 2026-09-25 por Samuel Cardenas (frente B), opción (b).**

- **La regla de rol único evoluciona a «un rol por usuario en cada producto».** Se conserva el
  principio funcional del 2026-08-04 («un usuario tiene un rol; lo que varía son los permisos»), pero
  aplicado dentro de cada producto. El índice único de `security.user_role_assignments`, hoy por
  (usuario, empresa), pasa a (usuario, empresa, producto del rol).
- **`AdminCompany` queda en `plataforma`** y solo conserva permisos de plataforma: administrar la
  empresa en el hub (usuarios, roles, marca, configuración).
- **Trámites tiene su propio rol de administración, `admin_tramites`** (producto `tramites`,
  `target_entity_type` COMPANY), con los permisos de Trámites que hoy tiene `AdminCompany`: reportes,
  reportes detallados, historial de placa y generación documental, más los permisos nuevos que
  reemplacen las comprobaciones por rol (revocatorias del gestor y red de clientes).
- **Nadie nota el cambio:** la migración de B-04 asigna `admin_tramites` a cada usuario que hoy tiene
  `AdminCompany`.
- **Qué cambia en Trámites** (lo hace el frente A en A-07/A-10, o una HU propia del producto): las
  comprobaciones por rol pasan a preguntar por `admin_tramites` o por su permiso:
  `app/tramites/revocatorias/page.tsx`, `components/operacion/RevocationRequestButton.tsx`,
  `components/operacion/TramitesFiltrosBar.tsx` y `Endpoints/Tramites/Network{Procedure,Reports,Children,Attachment}Endpoints.cs`
  (hoy responden 403 `network_role_required` sin `AdminCompany`).
- **El contrato no cambia:** `AdminCompany` sigue viajando solo en tokens con `aud=plataforma` (§2) y
  cada rol tiene permisos de un solo producto (§4).
- **Para los productos nuevos:** cada uno define su propio rol de administración (Comparendos lo
  decide en su diseño).
- **Pendiente:** informar a Andrés (PO) del cambio de la regla de rol único, porque fue una decisión
  funcional.

## Qué hace cada tarea con este inventario

| Tarea | Usa |
|---|---|
| B-04 | Sección 5: valores de `product_code`, backfill por `code`, slug faltante |
| B-07 | 1.1 (Configuración Empresa) y sección 6 (`settings`): qué campos pasan a `platform.tenant_products` |
| B-10 | Sección 4: catálogo declarativo con `product`, imports que salen de `Shell.tsx` |
| B-12 | Secciones 1, 1.1 y 2: pantallas y pestañas **P** que se mueven al hub, con redirección |
| B-13 | Sección 4: entradas **T** que se quedan en el dock de Trámites; renombrar `admin-plataforma` |
| A-01, A-06, A-07, A-11 | Hallazgos 1 y 2; sección 3 (rutas de auth y públicas) |

---

## Anexo A. Endpoints por archivo

Rutas relativas a `services/core-api/src/Flit.Api/Endpoints/`. Policies en
`Authorization/AdminAuthorization.cs` y `ApiSecurityExtensions.cs:104-138`. No hay fallback policy:
un endpoint sin `RequireAuthorization` es anónimo.

| Archivo | Prefijo | Endpoints | Autorización | Decisión |
|---|---|---|---|---|
| AuthEndpoints.cs | /api/v1/auth | login, forgot-password, reset-password, admin/reset-password, change-password, activate, me | anónimo / autenticado | P |
| SecurityEndpoints.cs | /api/v1/security | invitations (4), modules, roles, users (listar, editar, rol, quitar rol, suspender, levantar, borrar) | AdminCompany / UserAdmin / SuperAdmin | P |
| SuperAdmin/Security{Modules,Permissions,Roles,Users,Audit}Endpoints.cs | /api/v1/superadmin | CRUD de módulos, permisos, roles; restaurar usuario; auditoría | SuperAdmin | P |
| SuperAdmin/CatalogEndpoints.cs | /api/v1/superadmin | procedure-entities (T); external-data-sources, consultation-templates (P) | SuperAdmin | P/T |
| SuperAdmin/ProcedureTypeEndpoints.cs | /api/v1/superadmin/procedure-types | CRUD, publish, archive, wizard, reglas, perfil, pasos, validate | SuperAdmin | T |
| AdminCompaniesEndpoints.cs | /api/v1/admin/companies | index, detalle, crear, editar, status, parent, settings, whitelist, audit-log (P); transit-grants/blocks/agreements, ot-* policies (T) | AdminCompany / SuperAdmin | P/T |
| AdminCompaniesBrandingEndpoints.cs, AdminCompaniesDomainEndpoints.cs | /api/v1/admin/companies/{t}/{branding,domain} | marca y dominio | SuperAdmin | P |
| CompanyBrandingEndpoints.cs, CompanyDomainEndpoints.cs | /api/v1/company/{branding,domain} | autogestión | MarcaBlancaHeadCompany | P |
| Internal/InternalDomainsEndpoints.cs | /api/v1/internal/domains | active, pending-certificate, certificate | anónimo (interno) | P |
| AdminCompanyChildren{,Invitations,Config}Endpoints.cs | /api/v1/admin/companies/{h}/children | hijas, invitaciones, usuarios, settings, whitelist, audit-log | GroupHeadCompany | P |
| AdminCompanyChildrenSubmoduleEndpoints.cs | …/children/{c}/{mandate-signers,legal-representatives,signature-vault,personalized-documents,deeds,document-params} | submódulos de la hija | GroupHeadCompany / SuperAdmin | T |
| AdminHierarchySwitchesEndpoints.cs | /api/v1/admin/platform/hierarchy-switches | GET, PUT | SuperAdmin | P |
| Tramites/NetworkAccessAuditEndpoints.cs | /api/v1/admin/platform/network-access-audit; /api/v1/tramites/network-access-audit/mine | auditoría | SuperAdmin / autenticado | P |
| AdminBannersEndpoints.cs | /api/v1/admin/banners | CRUD | banners.manage | P |
| AdminPlataformaNotificacionesEndpoints.cs | /api/v1/admin/plataforma/notificaciones | buzón de pruebas, canales, envíos | SuperAdmin | P |
| AdminPlataformaNotificacionesPlantillasEndpoints.cs | …/notificaciones/plantillas | listado, muestra | SuperAdmin | T |
| AdminCompanyNotificationDeliveryLogsEndpoints.cs | /api/v1/admin/companies/{t}/notification-delivery-logs | GET | AdminCompany | P |
| AdminIctJob{Catalog,Settings}Endpoints.cs | /api/v1/admin/ict | jobs, runs, job-settings | SuperAdmin | T |
| AdminIdentityVigenciaEndpoints.cs | /api/v1/admin/companies/{t}/identity-validations | by-document | AdminCompany | T (D3) |
| AdminTransitOfficeTenantsEndpoints.cs | /api/v1/admin/transit-office-tenants | index, crear, status | SuperAdmin | T |
| AdminOtEndpoints.cs | /api/v1/admin/ot | perfil, flags, requisitos, webhooks, bandeja, revocatorias, consolidado, documentos, reglas, prelación, etiquetas, improntas (T); users, invitations (P, duplicado) | OtModule / SuperAdmin | T (+P) |
| AdminOt{Metrics,Queries,Mandatos}Endpoints.cs, Analytics/AdminOt{ReportSchedules,AlertRules}Endpoints.cs | /api/v1/admin/ot/* | métricas, consultas, mandatos, informes, alertas | OtModule | T |
| AdminPlataforma{Mandatos,Fur}Endpoints.cs | /api/v1/admin/plataforma/{mandatos,fur} | plantillas de mandato, simulador FUR | SuperAdmin | T |
| AdminPlateRangesEndpoints.cs | /api/v1/admin/plate-ranges | rangos y asignación de placas | OtModule | T |
| AdminTransitOfficesEndpoints.cs, AdminQuipuxEndpoints.cs, AdminLogQxEndpoints.cs | /api/v1/admin/{transit-offices,quipux,log-qx} | OT, Quipux | OtModule / SuperAdmin / logqx.read | T |
| AdminRuntConfirmationEndpoints.cs | /api/v1/admin/runt-confirmation | settings, attempts, runs, consult-now | runt_confirmation.* | T |
| AdminMandateSigner{s,Identity}Endpoints.cs, AdminCompanyMandateSignersEndpoints.cs | …/mandate-signers | mandatarios (identidad: 410 Gone) | OtModule / AdminCompany | T |
| AdminSignatureVaultEndpoints.cs, AdminLegalRepresentative{s,Identity}Endpoints.cs, AdminDeedsEndpoints.cs | /api/v1/admin/companies/{t}/{signature-vault,legal-representatives,deeds} | datos maestros | AdminCompany | T (D3) |
| AdminPersonalizedDocumentsEndpoints.cs, AdminCompanyDocumentParamsEndpoints.cs, AdminDocumentTypesEndpoints.cs, AdminProcedureDocumentRequirementsEndpoints.cs, AdminDocument{Order,Requirement}OverridesEndpoints.cs, AdminOtPrendaDocumentPolicyEndpoints.cs, AdminResolvedDocumentMatrixEndpoints.cs | documental | CRUD documental | SuperAdmin / AdminCompany / OtModule | T |
| AdminRejectionReasonsEndpoints.cs, AdminImprontasEndpoints.cs, AdminGeneracionDocumentalEndpoints.cs | /api/v1/admin/{rejection-reasons,improntas,generacion-documental} | catálogos y generación | SuperAdmin / permisos | T |
| Tramites/Admin{Anular,Consolidado,Estado,ReasignarGestor,ReenviarValidacion}Endpoints.cs | /api/v1/admin/tramites | acciones avanzadas | AdminTramite* | T |
| UserUiPreferencesEndpoints, Public{Banners,Branding}, MeBranding | /api/v1/{me,public} | preferencias, banners y marca | autenticado / anónimo | P |
| Analytics, DetailedReport, ReportSchedules, AlertRules, Queries, IctQueries, IctReports | /api/v1/{analytics,detailed-report,…} | reportes | varios | T |
| UsageEvents, DashboardActiveModules | /api/v1/… | telemetría de uso, tarjetas de módulos | autenticado | P |

## Historial

| Fecha | Cambio |
|---|---|
| 2026-09-25 | Primera versión (B-01) sobre `develop@da3074ac` |
| 2026-09-25 | D1 decidida: un rol por producto y `admin_tramites` (opción b) |
