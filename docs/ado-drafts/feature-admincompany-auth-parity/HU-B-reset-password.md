# HU-B — [BACKEND]+[FRONTEND] Reset admin operable para AdminCompany

> Feature local: AdminCompany auth parity · **Sin ID ADO** · SP: 3

## Description

**Como** administrador de una compañía gestora,  
**quiero** restablecer la contraseña de un colaborador de mi empresa desde Usuarios,  
**para** garantizar continuidad operativa sin escalar a SuperAdmin.

## Acceptance Criteria

### AC1 — Seed del permiso y grant

```gherkin
Given un entorno Development tras SeedAsync
When se consulta el catálogo de permisos y grants del rol AdminCompany
Then existe el slug security.users.reset_password y está concedido a AdminCompany
```

### AC2 — AdminCompany mismo tenant puede resetear

```gherkin
Given un AdminCompany del tenant T y un usuario activo del tenant T
When POST /api/v1/auth/admin/reset-password con el email de ese usuario
Then 200, se actualiza el hash, must_change_password=true y se notifica por correo
```

### AC3 — Fuera de ámbito

```gherkin
Given un AdminCompany del tenant T1 y un usuario del tenant T2
When POST /api/v1/auth/admin/reset-password con ese email
Then 403 FORBIDDEN_SCOPE y no se modifica la contraseña
```

### AC4 — UI en módulo Usuarios

```gherkin
Given un usuario autenticado como AdminCompany o SuperAdmin
When abre el módulo Usuarios y una fila con status distinto de pending
Then ve la acción "Restablecer contraseña" y al confirmar se llama adminResetPassword(email)
```

### AC5 — Sin ampliar suspender/eliminar

```gherkin
Given la implementación de esta HU
When se revisa Usuarios.tsx
Then suspender/eliminar siguen exclusivos de SuperAdmin
```

## Notas técnicas

- Handler existente: `AdminResetPasswordHandler` (HU #10170)
- Cliente FE existente: `adminResetPassword` en `lib/api/auth.ts`
- Defensa: rol `AdminCompany` + mismo tenant además del permiso (JWT)
- Commit: `HU-B: reset de contraseña operable para AdminCompany`
