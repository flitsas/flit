# Diseño técnico — Autorización AdminCompany (auth-only)

> Feature local: `FEATURE.md` · Schema: **NO** · ADR entidades nuevas: **N/A**

## 1. Principio

Reutilizar el patrón HU #11228 ya aplicado a RL, baúl, mandatarios y settings:

```csharp
.RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
.AddEndpointFilter<CompanyOwnTenantFilter>()
```

No se crean tablas, handlers de dominio, storage ni contratos OpenAPI nuevos (salvo notas de autorización).

## 2. Alternativas consideradas

| Opción | Descripción | Decisión |
|---|---|---|
| A — Alinear deeds a AdminCompanyPolicy + OwnTenant | Copia exacta de signature-vault | **Elegida** (mínimo impacto) |
| B — Nuevo permiso `admin.deeds.manage` | RequirePermission por slug | Descartada: inconsistente con RL/baúl (usan rol, no slug) |
| C — Dejar deeds SuperAdmin-only y ocultar UI | Evita IDOR | Descartada: UI ya muestra escrituras a AdminCompany → 403 |

Para reset:

| Opción | Descripción | Decisión |
|---|---|---|
| A — Seed `security.users.reset_password` + grant AdminCompany + UI | Cumple HU #10170 | **Elegida** |
| B — Solo SuperAdmin reset | Status quo | Descartada: gap de negocio |
| C — Reabrir suspender | Más autonomía | Fuera de alcance (riesgo operativo) |

## 3. Cambios por capa

### Backend HU-A

- Archivo: `Flit.Api/Endpoints/AdminDeedsEndpoints.cs`
- Cambiar `SuperAdminPolicy` → `AdminCompanyPolicy` + `CompanyOwnTenantFilter`
- Actualizar comentarios XML
- Handlers `*Deed*` **sin cambios** (ya reciben `tenantId`)

### Backend HU-B

- `DevelopmentAuthSeeder`: método idempotente que crea slug `security.users.reset_password` en módulo `usuarios` y lo concede a `AdminCompany` (y SuperAdmin para catálogo)
- `AdminResetPasswordHandler.EnsureScope`: además del permiso, aceptar rol `AdminCompany` + mismo tenant (defensa si el claim `permissions` JSON no se expande)
- `AuthEndpoints` admin reset: resolver `role_code` con `Any` sobre claims (paridad multi-rol con SuperAdmin)

### Frontend HU-B

- Constante + helper en `lib/auth/jwt.ts`
- `ResetPasswordDialog` en módulo Usuarios (patrón DeleteUserDialog)
- Botón por fila activa si SuperAdmin o AdminCompany (o permiso)

## 4. Matriz post-cambio

| Capacidad | SuperAdmin | AdminCompany | Radicador |
|---|---|---|---|
| CRUD deeds su tenant | ✅ | ✅ | ❌ |
| CRUD deeds otro tenant | ✅ | ❌ 403 | ❌ |
| Reset password su tenant | ✅ | ✅ | ❌ |
| Suspender / CRUD roles | ✅ | ❌ | ❌ |

## 5. Riesgos residuales y mitigación

| Riesgo | Mitigación |
|---|---|
| IDOR en deeds | `CompanyOwnTenantFilter` + tests de ForbidIfForeignTenant existentes |
| Convención primer `Guid` = tenantId | No cambiar firmas de handlers deeds |
| Permiso no sembrado fuera de Development | Documentar SQL de grant; rol AdminCompany también autoriza en handler |
| Re-login para ver permiso en JWT | UI también usa `isAdminCompany` |

## 6. Archivos a tocar

```
services/core-api/src/Flit.Api/Endpoints/AdminDeedsEndpoints.cs
services/core-api/src/Flit.Api/Endpoints/AuthEndpoints.cs
services/core-api/src/Flit.Modules.Security.Application/Auth/AdminResetPassword/AdminResetPasswordHandler.cs
services/core-api/src/Flit.Infrastructure/Security/DevelopmentAuthSeeder.cs
services/core-api/tests/Flit.Modules.Security.Application.Tests/Auth/AdminResetPasswordHandlerTests.cs
frontend/lib/auth/jwt.ts
frontend/components/atom/modules/Usuarios.tsx
frontend/components/atom/modules/users/ResetPasswordDialog.tsx  (nuevo)
frontend/components/atom/modules/users/__tests__/ResetPasswordDialog.test.tsx (nuevo)
docs/ado-drafts/feature-admincompany-auth-parity/*
```

## 7. QA / Security

- Security-agent: IDOR deeds + scope reset
- QA: AdminCompany propio vs ajeno; reset mismo tenant; sin acción de suspender
- Schema validator: **N/A**
