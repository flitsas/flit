# FLIT — Contexto funcional y técnico para desarrolladores nuevos

> Documento de onboarding. Describe **cómo funciona el sistema hoy** (branch `develop`):
> qué módulos existen, qué tablas pertenece a cada uno, cómo se relacionan entre sí,
> y el flujo completo de negocio de inicio a fin.
>
> Última actualización: 2026-06-30 · Fuente: lectura directa del código en `develop`.
>
> ⚠️ Nota: el documento `context/project-overview.md` describe un estado **anterior**
> (decía "sin controladores, sin capa Application, auth deshabilitada, frontend mock").
> Eso ya **no es cierto**: hoy el backend expone decenas de endpoints reales con CQRS,
> el frontend consume la API real y RBAC/JWT están operativos. Este documento es la
> referencia vigente para el funcionamiento.

---

## Tabla de contenidos

1. [Qué es FLIT](#1-qué-es-flit)
2. [Arquitectura de alto nivel](#2-arquitectura-de-alto-nivel)
3. [Módulos del sistema](#3-módulos-del-sistema)
4. [Modelo de datos por schema/módulo](#4-modelo-de-datos-por-schemamódulo)
5. [Relaciones cross-módulo (FKs que cruzan schemas)](#5-relaciones-cross-módulo-fks-que-cruzan-schemas)
6. [Autenticación y RBAC](#6-autenticación-y-rbac)
7. [Inventario de endpoints por módulo](#7-inventario-de-endpoints-por-módulo)
8. [Integraciones externas](#8-integraciones-externas)
9. [Flujo de negocio de inicio a fin](#9-flujo-de-negocio-de-inicio-a-fin)
10. [Frontend: rutas y wizard](#10-frontend-rutas-y-wizard)
11. [Decisiones de arquitectura (ADRs) y deuda](#11-decisiones-de-arquitectura-adrs-y-deuda)
12. [Glosario](#12-glosario)

---

## 1. Qué es FLIT

**FLIT** es una plataforma **SaaS multi-tenant** para digitalizar **trámites vehiculares** en
Colombia (matrícula inicial, traspaso, etc.). Cada empresa cliente (concesionario, renting,
gestor) es un **tenant**. El sistema:

- Parametriza tipos de trámite y sus reglas (motor de procedimientos dinámico).
- Ejecuta instancias de trámite con un **wizard server-driven** (el backend decide los pasos).
- Valida contra fuentes oficiales (**RUNT, SIMIT, RNMC** vía Verifik/Intempo).
- Valida identidad biométrica (**Kyverum**).
- Almacena documentos en **S3** (vía file-manager con presigned URLs).
- Genera **FUR** (Formato Único de Registro) y firma electrónica.
- Tiene **RBAC** multi-tenant (roles, permisos, módulos por tenant) y **analytics**.

### Roles principales

| Rol | Alcance | Qué hace |
|-----|---------|----------|
| **SuperAdmin** | Global (FLIT) | Crea compañías, configura tenants, parametriza catálogos globales de trámites, administra RBAC global. Bypass total de permisos. |
| **AdminCompany** | Un tenant | Administra usuarios, roles y permisos de **su** empresa; invita operadores. Se siembra automáticamente al crear la compañía. |
| **ot_admin** | Organismo de Tránsito | Administra la configuración de una OT (perfil, reglas, webhooks, documentos). |
| **Operador** (rol custom del tenant) | Un tenant | Ejecuta trámites (crea instancias, llena wizard, radica). |

---

## 2. Arquitectura de alto nivel

```
┌──────────────────────────────────────────────────────────────┐
│  Frontend — Next.js 16 (App Router, React 19)                │
│  :3000 dev · cookie JWT flit_token                            │
└───────────────────────────────┬──────────────────────────────┘
                                │ HTTPS  (Authorization: Bearer)
                                ▼
┌──────────────────────────────────────────────────────────────┐
│  Flit.Gateway — YARP reverse proxy  :4002                    │
│  Aquí se VALIDA el JWT real · CORS · rate limit · correlation │
│  /api/* → core   ·  /api/v1/auth/* y /webhooks/* públicos     │
│  /ml/*  → python-ml                                           │
└───────────────────────────┬──────────────────────────────────┘
                            │
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  Flit.Api — ASP.NET Core (.NET 10), Minimal API  :4003       │
│  Modular monolith: módulos Security, Admin, Tramites,        │
│  Analytics. CQRS manual (handlers POCO, sin MediatR).         │
│  Middlewares: TenantEnforcement, autorización por policy.     │
└───────────────────────────┬──────────────────────────────────┘
                            │ EF Core (un solo FlitDbContext)
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  PostgreSQL 16 — schemas: identity, security, tramites,      │
│  admin, analytics, catalogs, audit (multi-tenant, snake_case)│
└──────────────────────────────────────────────────────────────┘

         python-ml (FastAPI) :4012 — OCR/ML (stub), detrás del gateway
```

### Puntos clave de la arquitectura

- **Modular monolith**: un solo proceso (`Flit.Api`) con módulos verticales por bounded
  context. Cada módulo tiene su capa `*.Application` (casos de uso / handlers) y, donde
  aplica, `*.Domain`. La infraestructura (EF, HttpClients, storage) está centralizada en
  `Flit.Infrastructure`.
- **Un único `FlitDbContext`** (ADR-0018) con migraciones ordenadas por Historia de Usuario.
  Ver `Flit.Infrastructure/Persistence/FlitDbContext.cs`.
- **CQRS manual**: no hay MediatR ni controllers MVC. Todo es **Minimal API** + **handlers
  POCO** inyectados por DI. El mapeo de endpoints está en
  `Flit.Api/Program.cs:177-212`.
- **El JWT se valida en el Gateway**, no en `Flit.Api`. En `Flit.Api` la validación de firma
  está relajada (transitorio): confía en que el tráfico entra por el gateway.
- **Doble noción de "superadmin"** (importante y confuso):
  - Policy `SuperAdmin` → rol JWT `SuperAdmin`. Protege `/api/v1/admin/*` (compañías, OT, docs).
  - Policy `SuperAdminOnly` → **stub por header** `X-Flit-SuperAdmin`. Protege la
    parametrización de trámites en `/api/v1/superadmin/*`.

### Capas / proyectos .NET (`services/core-api/src/`)

| Proyecto | Responsabilidad |
|----------|-----------------|
| `Flit.Api` | Host. Mapea endpoints (Minimal API), middlewares, autorización, DI. |
| `Flit.Gateway` | YARP. Valida JWT real, CORS, rate limit, correlation id, health. |
| `Flit.Infrastructure` | EF Core (`FlitDbContext`), configuraciones, repositorios, migraciones, integraciones HTTP (Verifik, Kyverum, file-manager/S3, email), generación documental. |
| `Flit.Modules.Security.{Application,Domain}` | Auth, RBAC, invitaciones, usuarios. |
| `Flit.Admin.{Application,Domain}` | Compañías/tenants, configuración OT, parametrización documental. |
| `Flit.Tramites.{Application,Domain}` | Motor de trámites: catálogos de diseño + runtime de instancias. |
| `Flit.Analytics.Application` | Dashboards, KPIs, exports Excel/PDF. |

---

## 3. Módulos del sistema

| Módulo | Schema(s) BD | Qué resuelve | Capa Application |
|--------|--------------|--------------|------------------|
| **Security** | `security`, `identity` | Login/JWT, RBAC (roles, permisos, módulos), invitaciones, usuarios, reset de contraseña. | `Flit.Modules.Security.Application` |
| **Admin** | `admin`, `catalogs` | Crear/configurar compañías (tenants), políticas operativas, OTs habilitadas, whitelist, configuración de Organismos de Tránsito, parametrización documental. | `Flit.Admin.Application` |
| **Trámites** | `tramites` | (A) Parametrización global de tipos de trámite (SuperAdmin); (B) Runtime de instancias: wizard, actores, consultas externas, preflight, adjuntos, biométrica, FUR, firma, estados. | `Flit.Tramites.Application` |
| **Analytics** | `analytics` (+ lectura cross-schema) | KPIs del dashboard, ranking de productores, detalle de trámites, exports. | `Flit.Analytics.Application` |

> **Quién consulta tablas de otros módulos**: ver §5. En resumen — casi todo apunta a
> `identity.tenants` (multi-tenant) e `identity.users`; `tramites` y `admin` referencian el
> catálogo global `catalogs.transit_offices`; `admin` define *overrides* sobre los catálogos
> globales de `tramites`; `analytics` lee de `tramites` para sus agregados.

---

## 4. Modelo de datos por schema/módulo

Convenciones: PK `Id` (uuid), **snake_case** en BD, soft delete (`deleted_at`), `row_version`
para concurrencia, `tenant_id` en tablas multi-tenant. Base classes en
`Persistence/Entities/Common/`: `TenantAuditableEntity` (añade `TenantId`),
`AuditableEntity` (created/updated/deleted), `RowVersionEntity`.

> **Catálogo global** = sin `tenant_id`, compartido por todos los tenants (administrado por
> SuperAdmin). **Tenant-scoped** = con `tenant_id`, aislado por empresa.

### Schema `identity`

| Tabla | Tipo | Propósito | Notas |
|-------|------|-----------|-------|
| `tenants` | raíz | Empresa/organización (multi-tenant). | Ancla referenciada por casi todos los `tenant_id`. |
| `users` | global | Cuenta de usuario (cross-tenant). | `home_tenant_id` opcional. El vínculo con tenants es N:N vía `security.user_role_assignments`. |
| `data_protection_keys` | infra | Keyring ASP.NET Data Protection (cifra el secreto del webhook Kyverum). | HU #10233. |

### Schema `security`

| Tabla | Tipo | Propósito |
|-------|------|-----------|
| `modules` (`SecurityModule`) | global | Módulos funcionales del sistema. |
| `permissions` (`RbacAction`) | global | Acción/permiso por módulo (slug, http_method, route_pattern, scope). |
| `roles` | **tenant** | Roles por tenant (`is_system` para los sembrados). |
| `role_permissions` (`RoleGrant`) | **tenant** | Liga rol ↔ permiso. |
| `tenant_module_grants` | **tenant** | Módulos habilitados por tenant. |
| `user_role_assignments` | (por rol) | Asignación usuario ↔ rol. FK a `identity.users`. |
| `user_credentials` | — | Hash de contraseña (Argon2). FK a `identity.users`. |
| `password_reset_tokens` | — | Tokens de reset. FK a `identity.users`. |
| `user_invitations` | **tenant** | Invitaciones (email, role_id, token). |
| `user_temp_suspensions` | **tenant** | Suspensiones temporales. FK a `identity.users` y `identity.tenants`. |

### Schema `catalogs`

| Tabla | Tipo | Propósito |
|-------|------|-----------|
| `transit_offices` (`TransitOffice`) | global | Catálogo de Organismos de Tránsito (OT). Referenciado por `admin.*` y `tramites.*`. |

### Schema `admin` (todas **tenant-scoped**)

| Tabla | Propósito |
|-------|-----------|
| `transit_office_profiles` | Perfil/config de una OT para un tenant. → `catalogs.transit_offices`. |
| `tenant_transit_office_grants` | Qué OTs puede operar un tenant. → `catalogs.transit_offices`. |
| `tenant_operational_policies` | **Políticas operativas del tenant**. Incluye `allow_initial_registration` (¿permite matrícula inicial?), métodos de pago, estrategia de proveedor RUNT, etc. |
| `tenant_whitelist_users` | Emails permitidos por tenant. |
| `tenant_config_audit_logs` | Auditoría de cambios de configuración del tenant. |
| `ot_document_precedence` | Precedencia de documentos por OT. → `tramites.procedure_types` y `tramites.document_types`. |
| `ot_document_tags` | Tags de documentos por tenant. |
| `ot_feature_flags` | Feature flags por tenant. |
| `ot_webhook_subscriptions` | Suscripciones webhook por tenant. |
| `ot_api_call_logs` | Log de llamadas a APIs externas por tenant. |

### Schema `tramites`

**(A) Plantillas / catálogos de diseño — GLOBALES (sin `tenant_id`)** — administrados por
SuperAdmin (ADR-0019):

| Tabla | Propósito | FK principal |
|-------|-----------|--------------|
| `procedure_types` | Tipo de trámite (MATRICULA_NUEVA, TRASPASO_SIMPLE…). Raíz de la plantilla. | — |
| `procedure_steps` | Pasos de un tipo. | → `procedure_types` |
| `procedure_sections` | Secciones de un paso. | → `procedure_steps` |
| `form_fields` | Campos de formulario de una sección. | → `procedure_sections`, `consultation_templates` |
| `procedure_entities` | Entidades/actores tipados (VEHICLE, OWNER, BUYER, LESSEE). | — |
| `conformation_rules` | Reglas de conformación del trámite. | → `procedure_entities`, `procedure_types` |
| `external_data_sources` | Fuentes externas (RUNT, SIMIT, RNMC…). | — |
| `consultation_templates` | Plantillas de consulta a fuente externa. | → `external_data_sources` |
| `field_api_bindings` | Binding campo ↔ API. | → `form_fields`, `external_data_sources` |
| `document_types` | Catálogo de tipos de documento. | — |
| `procedure_document_requirements` | Documentos requeridos por tipo de trámite. | → `procedure_types`, `document_types` |
| `document_order_overrides` | Override de orden de documentos. | → `procedure_types`, `document_types` |
| `document_requirement_overrides` | Override de requisito por OT. | → `procedure_types`, `document_types`, `catalogs.transit_offices` |

**(B) Runtime de instancias — TENANT-SCOPED (con `tenant_id`)** — `procedure_instances` es el
**agregado raíz**; todo lo demás cuelga de él con FK (Cascade):

| Tabla | Propósito |
|-------|-----------|
| `procedure_instances` | Instancia ejecutada del trámite. Estado (Draft, Submitted…). FK a `procedure_types`; lógicas a `tenants`, `users` (creador), `catalogs.transit_offices`. |
| `procedure_instance_actors` | Actores con datos snapshot (nombre/doc/email). NO referencia `users`. → `procedure_entities`. |
| `procedure_instance_field_values` | Valores de campos capturados (incluye datos hidratados por consultas, `source`). → `form_fields`. |
| `procedure_instance_attachments` | Adjuntos (metadata; binarios en S3). |
| `procedure_instance_preflight_snapshots` | Snapshots del semáforo preflight. |
| `procedure_instance_commercial` | Datos comerciales (1:1). |
| `procedure_instance_events` | Timeline / eventos. |
| `procedure_instance_status_history` | Histórico de transiciones de estado. |
| `procedure_instance_biometric_validations` | Validaciones biométricas. |
| `procedure_instance_signatures` | Firmas electrónicas. |
| `procedure_instance_participants` | Participantes para firma remota (token, email). NO referencia `users`. |
| `procedure_document_snapshots` | Snapshot inmutable de documentos de la instancia. |
| `identity_validation_outbox` | Outbox de eventos de validación de identidad (patrón outbox, Kyverum event-driven). |

### Schemas `analytics` y `audit`

Declarados en `SchemaNames.cs` pero **sin entidades EF mapeadas** en la capa de persistencia:
existen como vistas/SQL crudo. Analytics se sirve vía `IAnalyticsReadRepository` (lectura) y
`audit` se llena por triggers/SQL.

---

## 5. Relaciones cross-módulo (FKs que cruzan schemas)

Esta es la parte que más confunde a un dev nuevo: **qué módulo lee tablas de otro**.

### FKs explícitas cross-schema

| Origen | Columna | Destino | Constraint |
|--------|---------|---------|------------|
| `security.user_role_assignments` | `user_id` | `identity.users` | (EF default) |
| `security.user_credentials` | `user_id` | `identity.users` | `fk_user_credentials_users` |
| `security.password_reset_tokens` | `user_id` | `identity.users` | `fk_password_reset_tokens_users` |
| `security.user_temp_suspensions` | `user_id` | `identity.users` | `fk_user_temp_suspensions_users` |
| `security.user_temp_suspensions` | `tenant_id` | `identity.tenants` | `fk_user_temp_suspensions_tenants` |

### FKs lógicas cross-schema (columna `*_id` + índice, por convención)

| Origen | Columna | Destino |
|--------|---------|---------|
| `tramites.procedure_instances` | `tenant_id` | `identity.tenants` |
| `tramites.procedure_instances` | `created_by_user_id` | `identity.users` |
| `tramites.procedure_instances` | `transit_office_id` | `catalogs.transit_offices` |
| `tramites.document_requirement_overrides` | `transit_office_id` | `catalogs.transit_offices` |
| `admin.transit_office_profiles` | `transit_office_id` | `catalogs.transit_offices` |
| `admin.tenant_transit_office_grants` | `transit_office_id` | `catalogs.transit_offices` |
| `admin.ot_document_precedence` | `procedure_type_id` | `tramites.procedure_types` |
| `admin.ot_document_precedence` | `document_type_id` | `tramites.document_types` |
| `admin.*` (todas) | `tenant_id` | `identity.tenants` |
| `security.roles` / `role_permissions` / `tenant_module_grants` / `user_invitations` | `tenant_id` | `identity.tenants` |

### Patrones a recordar

- **`identity.users` es global** (no tenant-scoped). Un usuario se vincula a uno o varios
  tenants vía `security.user_role_assignments` + `security.roles.tenant_id`.
- **`catalogs.transit_offices` es el catálogo global de OTs**; tanto `admin` (config por
  tenant) como `tramites` (instancia) lo referencian.
- **Personalización tenant sobre catálogo global**: las plantillas de `tramites` son globales,
  pero `admin` añade *overrides por tenant* (`ot_document_precedence`,
  `document_*_overrides`). Ese es el mecanismo para que cada empresa/OT ajuste el catálogo sin
  duplicarlo.
- **Los actores y participantes de un trámite NO referencian `identity.users`**: guardan datos
  personales planos (snapshot), porque son ciudadanos/terceros, no usuarios del sistema.

---

## 6. Autenticación y RBAC

### Login y emisión de JWT

- `POST /api/v1/auth/login` → `LoginHandler`. Verifica usuario `active`, valida hash Argon2,
  comprueba suspensión vigente.
- El snapshot de auth se arma en `AuthUserRepository`: une `users` + `user_credentials` +
  `user_role_assignments`→`roles` + `user_temp_suspensions` + `role_permissions`→`permissions`
  (slugs activos).
- JWT RS256 (12h), emitido por `RsaJwtTokenIssuer`. **Claims**: `sub`, `email`, `tenant_id`,
  `role_id`, `role_code`, `role` (duplicado de `role_code`, lo usa la policy `SuperAdmin` con
  `RequireRole`), y **N claims `permissions`** (un slug por claim).
- La verificación real de firma ocurre en el **Gateway** (YARP). En `Flit.Api` la validación
  está relajada (`ApiSecurityExtensions.cs`).

### Modelo RBAC

```
SecurityModule ──< RbacAction (permiso: slug, http_method, route_pattern, scope)
                        │
Role (por tenant) ──< RoleGrant >── RbacAction          (qué permisos tiene el rol)
   │
   └──< UserRoleAssignment >── User                     (qué usuarios tienen el rol)

Tenant ──< TenantModuleGrant >── SecurityModule          (qué módulos ve el tenant)
```

### Evaluación en runtime

- **Por permiso** (`PermissionAuthorizationHandler`): concede si `role_code == SuperAdmin`
  (bypass total) **o** si el claim `permissions` contiene el slug requerido.
- **Por rol** (policies en `ApiSecurityExtensions.cs`):
  - `SuperAdmin` → rol JWT `SuperAdmin`.
  - `AdminCompany` → `AdminCompanyRequirement` (roles de empresa + bypass SuperAdmin).
  - `OtAdmin` / `OtModule` → rol `ot_admin` (o SuperAdmin).
  - `SuperAdminOnly` → **stub por header** `X-Flit-SuperAdmin` (parametrización de trámites).
- **Importante**: los permisos viven **dentro del JWT**. Cambiar un rol/permiso requiere
  **reemitir el token** (re-login) para que surta efecto.

### Multi-tenancy en runtime (`TenantEnforcementMiddleware`)

Para rutas `/instances*`, `/transit-offices`, `/biometric-validations`:
- Usuario de empresa → el `tenant_id` se **fuerza desde el token** (ignora/sobrescribe
  `X-Tenant-Id`).
- SuperAdmin → respeta el header o ve todos los tenants.

---

## 7. Inventario de endpoints por módulo

> Todo bajo `/api/v1`. Routing = Minimal API, mapeado en `Flit.Api/Program.cs:177-212`.
> Sin MVC ni MediatR: cada grupo de endpoints tiene su archivo en `Flit.Api/Endpoints/`.

### Security / Auth

| Verbo | Ruta | Auth | Qué hace |
|-------|------|------|----------|
| POST | `/auth/login` | anónimo | Login → JWT |
| POST | `/auth/forgot-password` | anónimo | Solicitar reset (202 genérico) |
| POST | `/auth/reset-password` | anónimo | Fijar contraseña con token |
| POST | `/auth/activate` | anónimo | Activar cuenta desde invitación |
| PUT | `/auth/change-password` | auth | Cambiar contraseña (usuario actual) |
| POST | `/auth/admin/reset-password` | auth | Admin restablece contraseña de otro |
| GET | `/auth/me` | auth | Claims actuales |
| POST | `/security/invitations` | auth | Crear invitación (SuperAdmin fuerza rol `AdminCompany`) |
| GET | `/security/modules` | auth | Módulos accesibles del caller |
| GET/POST | `/security/roles` | auth / `AdminCompany` | Listar / crear roles del tenant |
| PUT | `/security/roles/{id}/permissions` | `AdminCompany` | Set permisos del rol (solo los que el caller posee) |
| DELETE | `/security/roles/{id}` | `AdminCompany` | Borrar rol |
| GET | `/security/users` | auth | Listar usuarios (SuperAdmin ve todos los tenants) |
| PUT | `/security/users/{userId}/role` | auth | Asignar/reemplazar rol |
| POST/DELETE | `/security/users/{userId}/suspend` | `AdminCompany` | Suspender/reactivar |
| CRUD | `/superadmin/modules`, `/permissions`, `/roles` | `SuperAdminOnly` | RBAC global. Incluye `/modules/{id}/grants/{tenantId}` (habilitar módulo a tenant). |

### Admin

| Verbo | Ruta | Auth | Qué hace |
|-------|------|------|----------|
| GET | `/admin/companies/index` | `SuperAdmin` | Listar empresas |
| POST | `/admin/companies` | `SuperAdmin` | Crear empresa (siembra rol `AdminCompany`) |
| PUT | `/admin/companies/{tenantId}` | `SuperAdmin` | Editar empresa |
| PUT | `/admin/companies/{tenantId}/status` | `SuperAdmin` | Activar/suspender |
| GET/PUT | `/admin/companies/{tenantId}/settings` | `SuperAdmin` | Políticas operativas (incl. `allow_initial_registration`) |
| POST/GET | `/admin/companies/{tenantId}/whitelist` | `SuperAdmin` | Whitelist de emails |
| POST/DELETE/GET | `/admin/companies/{tenantId}/transit-grants[/{id}]` | `SuperAdmin` | Habilitar/revocar/listar OTs del tenant |
| GET | `/admin/companies/{tenantId}/audit-log` | `SuperAdmin` | Auditoría de config |
| CRUD | `/admin/document-types` | `SuperAdmin` | Catálogo de tipos de documento |
| CRUD | `/admin/procedure-document-requirements` | `SuperAdmin` | Documentos requeridos por trámite |
| CRUD | `/admin/document-order-overrides` | `SuperAdmin` | Override de orden |
| GET/PUT | `/admin/document-requirement-overrides` | `SuperAdmin` | Override de requisito |
| GET | `/admin/resolved-document-matrix` | `SuperAdmin` | Matriz documental resuelta |
| GET/PATCH/CRUD | `/admin/ot/*` | `OtModule` | Perfil OT, feature-flags, webhooks, api-logs, client-procedures (approve/reject), rules, document-precedence, document-tags |
| GET | `/admin/transit-offices` | `OtModule` | Buscar OTs |
| POST | `/integrations/ot/webhooks/{subscriptionId}/callback` | **anónimo** | Callback entrante de OT externos |

### Trámites — parametrización (SuperAdmin, `/superadmin`, policy `SuperAdminOnly`)

| Verbo | Ruta | Qué hace |
|-------|------|----------|
| CRUD | `/superadmin/procedure-types` | Tipos de trámite + `/publish`, `/archive`, `/validate` |
| GET/PUT | `/superadmin/procedure-types/.../conformation-rules`, `/steps` | Reglas y pasos |
| GET | `/superadmin/procedure-entities`, `/external-data-sources`, `/consultation-templates` | Catálogos |
| POST | `/superadmin/consultation-templates/{id}/apply-fields` | Aplicar campos de plantilla |

### Trámites — runtime (`/tramites`, protegido por `TenantEnforcementMiddleware`)

| Área | Endpoints |
|------|-----------|
| Instances | POST/GET `/instances`, GET `/instances/{id}`, PATCH `/instances/{id}/field-values`, POST `/instances/{id}/finalize-draft`, POST `/instances/{id}/submit`, GET `/transit-offices` |
| Actors | GET/PUT `/instances/{id}/actors` |
| Attachments | POST upload, POST `/presign`, POST `/register`, GET list, GET `/{id}/download`, DELETE, GET `/checklist` |
| Consultations | POST `/instances/{id}/consultations/{templateCode}`, POST `/instances/{id}/runt-person` |
| Preflight | POST/GET `/instances/{id}/preflight` |
| Commercial | GET/PUT `/instances/{id}/commercial` |
| Biométrica | POST/GET `/instances/{id}/biometric`, `/biometric/simulate`, `/identity/ensure`, GET `/biometric-validations`, `/identity-validation/stuck*` (requeue) |
| Participants | POST invitar, GET list, POST reinvite |
| Firma | POST `/instances/{id}/signatures`, POST `/signatures/{id}/simulate` |
| FUR / Consolidado | POST `/instances/{id}/fur`, POST `/instances/{id}/consolidado` |
| Wizard | GET `/instances/{id}/wizard` (estado server-driven) |

### Trámites — públicos (anónimos, token en URL)

| Verbo | Ruta | Qué hace |
|-------|------|----------|
| GET | `/tramites/procedure-types` | Tipos publicados |
| GET | `/procedure-types/{code}/configuration` | Configuración del tipo |
| GET/POST | `/public/biometric/{token}` | Captura biométrica del participante |
| POST | `/webhooks/kyverum-verify/{validationId}` | Webhook resultado Kyverum (HMAC sobre cuerpo crudo) |
| GET/POST | `/portal/{token}` (+ `/consent`, `/documentos`, `/firma`, `/finalizar`) | Portal del participante |

### Analytics (`/analytics`, policy `AdminCompany`)

| Verbo | Ruta | Qué hace |
|-------|------|----------|
| GET | `/analytics/overview` | KPIs del dashboard |
| GET | `/analytics/productivity/top` | Ranking de productores |
| GET | `/analytics/procedures` | Detalle de trámites |
| GET | `/analytics/export/excel` | Export Excel |
| POST | `/analytics/export/executive-pdf` | PDF ejecutivo |

---

## 8. Integraciones externas

Registradas en `Flit.Infrastructure/InfrastructureExtensions.cs` (HttpClients tipados) y
`Flit.Infrastructure/Consultations/`. Cada proveedor de consulta implementa
`IConsultationProvider` (ADR-0020) y se resuelve por *key* en `ConsultationProviderRegistry`.
El **modo real/mock** es configurable por proveedor (sección `Consultations` en appsettings;
ej. `VerifikVehicleMode`, `VerifikConductorMode`, `VerifikSimitMode`, `VerifikRnmcMode`,
`IntempoMode`).

| Servicio | Key | Qué consulta | Clase |
|----------|-----|--------------|-------|
| **Verifik** | `verifik` | Vehículo / RUNT (por VIN o placa) | `VerifikConsultationProvider` |
| **Verifik SIMIT** | `verifik_simit` | Comparendos SIMIT | `VerifikSimitConsultationProvider` |
| **Verifik RNMC** | `verifik_rnmc` | Medidas correctivas (RNMC) | `VerifikRnmcConsultationProvider` |
| **Verifik Conductor** | `verifik_conductor` | Persona/conductor RUNT (lookup persona) | `VerifikConductorConsultationProvider` |
| **Intempo** | `intempo` | Vehículo (proveedor alterno) | `IntempoConsultationProvider` |
| **Flit Integrations Gateway** | `flit_integrations` | Proxy futuro a integraciones FLIT (stub) | `FlitIntegrationsGatewayProvider` |
| **Kyverum** | — | Validación de identidad / biométrica (captura remota + webhook) | `KyverumVerifyClient`, `KyverumIdentityValidationProvider` |
| **file-manager / S3** | — | Almacenamiento de adjuntos vía presigned URLs a S3 | `FileManagerAttachmentStorage` |
| **SMTP / Email** | — | Invitaciones y reset (Console en dev, SMTP en prod) | `IEmailSender` |
| **Generación documental** | — | FUR (overlay PDF), consolidado de expediente | `FurOverlayDocumentGenerator`, `PdfExpedienteConsolidadoMerger` |

> Diseño clave (ADR-0020): el motor de trámites **no conoce a Verifik**. Trabaja contra el
> resultado normalizado `ConsultationResult` (`overall` green/yellow/red + `checks[]` +
> `hydratedFields[]`). Migrar al gateway propio = agregar un provider, sin tocar handlers ni
> frontend.

---

## 9. Flujo de negocio de inicio a fin

Recorrido cronológico completo, con el endpoint/handler de cada paso. En cada paso se indica
**🔎 Persistencia a revisar**: la(s) tabla(s) donde debería verse el efecto del paso, para ir
validando la data contra la BD (todos los nombres son físicos: `schema.tabla`).

### Fase A — Onboarding (SuperAdmin)

1. **SuperAdmin loguea** → `POST /auth/login` → JWT con `role=SuperAdmin`.
   - 🔎 **Persistencia a revisar**: paso de **lectura** (no crea filas). Se consultan
     `identity.users`, `security.user_credentials`, `security.user_role_assignments`,
     `security.roles`, `security.role_permissions`, `security.permissions` y
     `security.user_temp_suspensions` (para verificar que no haya suspensión vigente).

2. **Crea la compañía** → `POST /admin/companies` (`CreateCompanyHandler`).
   Valida razón social / NIT / code / `tenant_type` (RENTING | CONCESIONARIO | FLIT).
   **Efecto clave** (`CompanyWriteRepository`): además del `Tenant`, **siembra automáticamente
   el rol de sistema `AdminCompany`** y le asigna **todos** los permisos activos excepto
   `rbac.manage`. Esto es lo que permite invitar luego al admin de esa empresa.
   - 🔎 **Persistencia a revisar**: `identity.tenants` (nueva fila), `security.roles` (rol
     `AdminCompany` con `is_system=true` para ese `tenant_id`), `security.role_permissions`
     (un grant por cada permiso asignado al rol sembrado).

3. **Configura el tenant** (grupo `/admin/companies/{tenantId}`):
   - **Políticas operativas** → `PUT /settings` (`UpdateTenantSettingsHandler`). Aquí vive
     **`allow_initial_registration`** (¿permite matrícula inicial?), métodos de pago,
     estrategia de proveedor RUNT, etc. → tabla `admin.tenant_operational_policies`.
     - 🔎 **Persistencia a revisar**: `admin.tenant_operational_policies` (fila del tenant) +
       `admin.tenant_config_audit_logs` (registro del cambio).
   - **OTs disponibles** → `POST /transit-grants` (`AddTransitGrantHandler`) →
     `admin.tenant_transit_office_grants`.
     - 🔎 **Persistencia a revisar**: `admin.tenant_transit_office_grants` (fila por OT
       habilitada, `transit_office_id` → `catalogs.transit_offices`).
   - **Módulos habilitados** → `POST /superadmin/modules/{id}/grants/{tenantId}` →
     `security.tenant_module_grants`.
     - 🔎 **Persistencia a revisar**: `security.tenant_module_grants` (fila por módulo
       concedido al tenant).
   - **Whitelist** → `POST /whitelist`.
     - 🔎 **Persistencia a revisar**: `admin.tenant_whitelist_users` (una fila por email).

4. **Invita al AdminCompany** → `POST /security/invitations` (`CreateInvitationHandler`).
   Si el caller es SuperAdmin, **debe** pasar `TargetTenantId` y el rol se **fuerza a
   `AdminCompany`** del tenant destino (el sembrado en el paso 2). Se genera token y se envía
   email con el link de activación.
   - 🔎 **Persistencia a revisar**: `security.user_invitations` (nueva fila con `status=pending`,
     `tenant_id`, `role_id` y el hash del token).

### Fase B — Activación y administración del tenant (AdminCompany)

5. **El invitado activa su cuenta** → `POST /auth/activate` (`ActivateAccountHandler`).
   En un solo `SaveChanges` (`UserActivationRepository`) crea: `User` (active, home tenant) +
   `UserCredential` (hash) + `UserRoleAssignment` (tenant + rol de la invitación), y marca la
   `UserInvitation` como `accepted`. Luego loguea y obtiene JWT con su `tenant_id` y permisos.
   - 🔎 **Persistencia a revisar**: `identity.users` (nueva fila `status=active`),
     `security.user_credentials` (hash Argon2), `security.user_role_assignments` (usuario↔rol↔tenant)
     y `security.user_invitations` (la fila pasa a `status=accepted`).

6. **AdminCompany administra su empresa**: invita operadores (`/security/invitations` a su
   propio tenant con el `role_id` que elija), crea roles y define permisos
   (`/security/roles`, `/security/roles/{id}/permissions` — solo puede delegar permisos que él
   mismo posee).
   - 🔎 **Persistencia a revisar**: `security.user_invitations` (invitaciones de operadores),
     `security.roles` (roles custom del tenant), `security.role_permissions` (permisos del rol),
     y al asignar rol a un usuario `security.user_role_assignments`.

### Fase C — Ejecución de un trámite (Operador) — ej. **Matrícula inicial**

7. **Crea la instancia** → `POST /tramites/instances` (`CreateProcedureInstanceCommand`).
   El endpoint resuelve el tenant efectivo desde el contexto y, si la modalidad es matrícula
   inicial, **valida la política**: si `allow_initial_registration == false` → **422** "La
   compañía no tiene habilitada la matrícula inicial". Crea la instancia en estado **Draft**
   con `reference_number` único y la primera fila de `procedure_instance_status_history`
   (null → Draft).
   - 🔎 **Persistencia a revisar**: `tramites.procedure_instances` (nueva fila `status=Draft`,
     `tenant_id`, `procedure_type_id`, `reference_number`, `created_by_user_id`) y
     `tramites.procedure_instance_status_history` (fila inicial null→Draft). La validación de
     política **lee** `admin.tenant_operational_policies`.

8. **Llena el wizard (Draft)** — server-driven:
   - `GET /instances/{id}/wizard` devuelve los pasos, `canSubmit` y `blockers`. El front nunca
     recalcula gates.
   - **Actores** (comprador/vendedor) → `PUT /instances/{id}/actors`.
   - **Field values** → `PATCH /instances/{id}/field-values` (solo en draft).
   - 🔎 **Persistencia a revisar**: `GET /wizard` es solo lectura. Actores →
     `tramites.procedure_instance_actors` (una fila por actor, datos snapshot). Field values →
     `tramites.procedure_instance_field_values` (una fila por campo, con `source`).

9. **Consultas externas (RUNT/Verifik)**:
   - `POST /instances/{id}/consultations/{templateCode}` (`RunConsultationHandler`): resuelve
     el provider desde el template y persiste los `hydratedFields` como `field_values` con
     `source="consultation"`.
   - `POST /instances/{id}/runt-person`: autopobla datos de persona (no persiste).
   - 🔎 **Persistencia a revisar**: `tramites.procedure_instance_field_values` (filas con
     `source="consultation"` — así distingues lo hidratado por consulta de lo capturado a mano).
     `runt-person` **no persiste**. (Si el tenant tiene logging de OT activo, las llamadas se
     registran en `admin.ot_api_call_logs`.)

10. **Preflight (semáforo)** → `POST /instances/{id}/preflight` (`RunPreflightHandler`).
    Fan-out por modalidad: matrícula → vehículo por VIN; traspaso → vehículo por placa + SIMIT
    comprador/vendedor + RNMC. Compone `overall` green/yellow/red y persiste
    `procedure_instance_preflight_snapshots`. En matrícula, `estado_vehiculo=REGISTRADO` se
    degrada de fail → warn.
    - 🔎 **Persistencia a revisar**: `tramites.procedure_instance_preflight_snapshots` (snapshot
      con `overall` y el detalle de `checks`).

11. **Adjuntos a S3**:
    - Directo (multipart, ≤20MB): `POST /instances/{id}/attachments`.
    - Presigned (PDFs grandes): `POST /attachments/presign` → el navegador sube directo a S3 →
      `POST /attachments/register` registra la metadata.
    - Checklist documental: `GET /instances/{id}/checklist`.
    - 🔎 **Persistencia a revisar**: `tramites.procedure_instance_attachments` (metadata + key de
      S3; el binario vive en S3, no en BD). `GET /checklist` es lectura computada. `presign` solo
      no crea fila — la fila aparece tras `register`.

12. **Biométrica / validación de identidad** → `POST /instances/{id}/biometric`.
    Según `Biometrics:Provider`: **mock** (magic-link 3 fotos) o **Kyverum** (captura remota +
    webhook; 202 si encolado). El resultado llega por el webhook público
    `POST /webhooks/kyverum-verify/{validationId}` (verificado por HMAC). `identity/ensure`
    reutiliza una validación vigente (≤30 días).
    - 🔎 **Persistencia a revisar**: `tramites.procedure_instance_biometric_validations` (fila por
      validación con su estado/score); con Kyverum, `tramites.identity_validation_outbox` (evento
      encolado, patrón outbox) — el webhook actualiza el estado de la validación. Si la captura es
      remota por participante, ver también `tramites.procedure_instance_participants`.

13. **FUR y firma**:
    - `POST /instances/{id}/fur` (`GenerarFurHandler`): gated por biométrica aprobada +
      organismo seleccionado; persiste el FUR como adjunto tipo `fur`.
    - Firma electrónica (sobre todo en traspaso): `POST /instances/{id}/signatures` +
      `/simulate`.
    - 🔎 **Persistencia a revisar**: FUR → `tramites.procedure_instance_attachments` (fila con
      `document_type='fur'`). Firma → `tramites.procedure_instance_signatures` (fila por firmante);
      participantes de firma remota en `tramites.procedure_instance_participants`. El consolidado
      de expediente también queda como adjunto.

14. **Radicar (Submit)** → `POST /instances/{id}/submit` (`SubmitProcedureInstanceCommand`).
    Aplica `SubmitGate`:
    - **Completitud**: documentos obligatorios completos (`ChecklistEngine`) + biométrica del
      **comprador** aprobada y vigente ≤30 días.
    - **OT habilitado**: lee `transit_office_id` de field_values y valida contra
      `ITransitOfficeGrantGate` → `organismo_no_habilitado` (422) si la empresa no lo tiene
      concedido. Promueve el id a la columna `transit_office_id`.
    - **Reglas OT**: `IOtRuleGate` puede bloquear (`ot_rule_blocked`, `biometria_requerida_ot`).
    - Si pasa: transición **Draft → Submitted**, sella `submitted_at`, escribe
      `procedure_instance_status_history`, y notifica vía `IProcedureStateChangeNotifier`
      (que puede disparar webhooks salientes a la OT).
    - 🔎 **Persistencia a revisar**: `tramites.procedure_instances` (`status=Submitted`,
      `submitted_at` sellado, `transit_office_id` promovido a columna),
      `tramites.procedure_instance_status_history` (transición Draft→Submitted), y
      `tramites.procedure_instance_events` (evento de timeline). El gate **lee**
      `tramites.procedure_instance_field_values`, `..._attachments` y `..._biometric_validations`.

> **Estados**: cada transición escribe en `procedure_instance_status_history`. El enum
> `ProcedureInstanceStatus` arranca en Draft → Submitted; estados posteriores se gestionan en
> el runtime/OT.

---

## 10. Frontend: rutas y wizard

Next.js 16 (App Router, React client-heavy SPA). Base de API vía `NEXT_PUBLIC_API_BASE_URL`
(apunta al Gateway). Token JWT en cookie `flit_token` (la lee el middleware en el Edge) +
respaldo `localStorage["flit:jwt"]`.

### Rutas principales

| Zona | Rutas |
|------|-------|
| **Auth** | `/login`, `/auth/forgot-password`, `/auth/reset-password`, `/invite/activate`, `/profile/change-password`, `/403` |
| **SPA operación** | `/` con módulos por query `?m=` (`dashboard`, `tramites`, `reportes`, `validaciones`, `usuarios`, `ayuda`, `rbac`) — **no hay rutas separadas** por módulo |
| **Empresa (AdminCompany)** | `/empresa` → `/empresa/usuarios`, `/empresa/roles` |
| **Admin global (SuperAdmin)** | `/admin/companies[/[tenantId]]`, `/admin/rbac`, `/admin/documents[/procedures/...]`, `/admin/transit-offices/[id]/{tramites,documents,client-procedures,rules,webhooks}` |
| **Trámites** | `/tramites` (listado), `/tramites/nuevo/[modalidad]` (crea draft → redirige), `/tramites/[instanceId]` (wizard) |
| **Portales públicos** | `/portal/[token]`, `/biometric/[token]` |

### Gating (3 capas)

1. **Edge (middleware.ts)** por rol grueso: `/empresa/*` → AdminCompany o SuperAdmin;
   `/admin/*` → SuperAdmin (ot_admin solo en `/admin/transit-offices/*`); si no → `/403`.
   Decodifica el JWT **sin verificar firma** (solo UX; el backend valida de verdad).
2. **Permisos finos (UI)**: `usePermissions` + `<PermissionGate permission="slug">`
   (SuperAdmin hace bypass).
3. **Módulos accesibles (navegación)**: `GET /security/modules` + `buildValidModules()`
   filtran el dock de la SPA.

### Wizard de trámite (`components/operacion/TramiteWizard.tsx`)

Modelo **server-driven**: `GET /instances/{id}/wizard` manda `steps`, `canSubmit`, `blockers`;
el front re-consulta (`refresh()`) tras cada acción que mueva gates. Step keys:
`consulta` (placa) → `consulta_vin` → `comprador` → `vendedor` → `documentos` → `comercial` →
`identidad` → `fur` (paso terminal de decisión). El orden real lo dicta el backend según
modalidad (matrícula vs. traspaso difieren).

> **Dos clientes HTTP en el front**: `lib/api/client.ts` (genérico: auth/security/admin) y
> `lib/api/tramites-client.ts` (operación/OT: maneja el header de tenant y construye la URL
> para no duplicar `/api/v1`).

---

## 11. Decisiones de arquitectura (ADRs) y deuda

ADRs en `services/core-api/docs/adr/`:

- **ADR-0018** — Un único `FlitDbContext` + migraciones por HU + DDL embebido. Trade-off:
  entrega incremental sobre mapeo EF completo.
- **ADR-0019** — Catálogos de trámites (`procedure_types`, `external_data_sources`,
  `consultation_templates`, `procedure_entities`) **sin `tenant_id`**: son globales,
  administrados solo por SuperAdmin. Protección por RBAC (`tramites:catalogs:write`), no por RLS.
- **ADR-0020** — Capa multi-proveedor de consultas (`IConsultationProvider` + registry +
  `ConsultationResult` normalizado). Desacopla el motor de trámites de Verifik; migrar al
  gateway propio = agregar un provider.

Deuda / cosas no obvias a tener presentes:

- **AOT está desactivado** en `Flit.Api`/Infra/Application pese al `Directory.Build.props`
  global; la (de)serialización de payloads externos funciona por reflexión. Si algún día se
  activa AOT real, hará falta JSON source-gen.
- **Doble "superadmin"**: rol JWT `SuperAdmin` (Admin/Companies) vs. header stub
  `X-Flit-SuperAdmin` (parametrización `/superadmin/*`). No confundir.
- **Permisos en el JWT**: cambios de rol/permiso requieren re-login.
- **Mocks vs. real**: biométrica, firma, scoring y algunos proveedores de consulta tienen
  implementación mock conmutable por configuración (sección `Consultations`,
  `Biometrics:Provider`). En `appsettings.Development.json` conviven modos `real`/`mock` por
  proveedor.

---

## 12. Glosario

| Término | Significado |
|---------|-------------|
| **Tenant** | Empresa cliente (concesionario, renting, gestor). Unidad de aislamiento multi-tenant. |
| **OT** | Organismo de Tránsito. Catálogo global (`catalogs.transit_offices`); cada tenant habilita las que opera. |
| **FUR** | Formato Único de Registro — documento oficial generado al final del trámite. |
| **RUNT** | Registro Único Nacional de Tránsito (Colombia); se consulta vía Verifik/Intempo. |
| **SIMIT** | Sistema de comparendos; consulta vía Verifik. |
| **RNMC** | Registro Nacional de Medidas Correctivas; consulta vía Verifik. |
| **Kyverum** | Proveedor de validación de identidad / biométrica. |
| **Preflight** | Panel semáforo (green/yellow/red) que valida estado del vehículo/actores antes de avanzar. |
| **ProcedureType** | Plantilla de tipo de trámite (global, parametrizada por SuperAdmin). |
| **ProcedureInstance** | Instancia ejecutada de un trámite (tenant-scoped, agregado raíz del runtime). |
| **Field value** | Valor capturado de un campo del formulario; puede venir de captura manual o de una consulta externa (`source`). |
| **SubmitGate** | Conjunto de validaciones que deben pasar para radicar (documentos, biométrica, OT habilitado, reglas OT). |
</content>
</invoke>
