# HU #10170 — Reset administrativo y bloqueo temporal

**Feature:** #10113 · **Tipo:** Backend · **Rama:** feature/AB-10170-reset-admin-bloqueo

## Qué se implementó
- **`POST /api/v1/auth/admin/reset-password`** (autenticado) — un administrador con ámbito sobre el usuario restablece su contraseña: genera una **contraseña temporal** (14 chars, complejidad garantizada), actualiza el hash (Argon2), marca **`must_change_password = true`** y **notifica al usuario por correo**.
  - **Ámbito (autorización):** Superadmin (rol/permiso global) puede sobre cualquier tenant; un admin de compañía requiere el permiso `security.users.reset_password` **y** que el usuario sea de su mismo tenant. En otro caso → **403 FORBIDDEN_SCOPE**.
- **Bloqueo temporal en login (AC2):** si existe una suspensión vigente en `security.user_temp_suspensions`, el login responde **403 ACCOUNT_TEMPORARILY_BLOCKED**. La verificación se hace **después** de validar la contraseña (no revela el bloqueo a terceros).

## Casos de uso
1. Un usuario está de vacaciones → el admin programa una suspensión → sus intentos de login devuelven 403.
2. Un usuario olvidó la contraseña y llama a soporte → el admin la restablece → el usuario recibe una temporal por correo y debe cambiarla.
3. Un Admin de compañía intenta resetear a un usuario de **otro** tenant → 403 sin aplicar cambios.

## Cómo probar

### AC2 — Bloqueo temporal
```sql
-- Insertar suspensión vigente para el usuario:
INSERT INTO security.user_temp_suspensions (id, tenant_id, user_id, starts_at, ends_at, reason, created_at)
VALUES (uuidv7(), '<TENANT>', '<USER>', now()-interval '1 hour', now()+interval '1 hour', 'vacaciones', now());
```
```bash
curl -X POST http://localhost:5005/api/v1/auth/login \
  -H 'Content-Type: application/json' -d '{"email":"demo@flit.local","password":"DemoPass1!"}'
# → 403 {"code":"ACCOUNT_TEMPORARILY_BLOCKED", ...}
# Con contraseña incorrecta + suspensión → 401 INVALID_CREDENTIALS (no revela el bloqueo)
```

### AC1 — Reset administrativo (requiere JWT de admin)
```bash
# Sin token → 401
curl -X POST http://localhost:5005/api/v1/auth/admin/reset-password \
  -H 'Content-Type: application/json' -d '{"email":"demo@flit.local"}'

# Token de admin SIN ámbito (rol no SuperAdmin / otro tenant) → 403 FORBIDDEN_SCOPE
# Token SuperAdmin, email inexistente → 404 USER_NOT_FOUND
# Token SuperAdmin, usuario válido → 200; el usuario queda con must_change_password=true
curl -X POST http://localhost:5005/api/v1/auth/admin/reset-password \
  -H "Authorization: Bearer <JWT_SUPERADMIN>" \
  -H 'Content-Type: application/json' -d '{"email":"demo@flit.local"}'
# → 200 {"message":"Contraseña restablecida; se notificó al usuario."}
# En dev, la contraseña temporal aparece en el log del backend (ConsoleEmailSender).
```

## Respuestas esperadas
| Escenario | HTTP | Código |
|---|---|---|
| login con suspensión vigente (pw correcto) | 403 | `ACCOUNT_TEMPORARILY_BLOCKED` |
| admin reset sin token | 401 | — |
| admin reset fuera de ámbito | 403 | `FORBIDDEN_SCOPE` |
| admin reset usuario inexistente | 404 | `USER_NOT_FOUND` |
| admin reset OK | 200 | mensaje + `must_change_password=true` |

## Pruebas ejecutadas
- Unitarias: 6 nuevas (5 `AdminResetPasswordHandlerTests` + 1 suspensión en login). Suite total 89/89.
- E2E en DEV (API local + PostgreSQL docker): AC2 (403/401/200) y AC1 (401/403/404/200 + must_change) verificados.

## Notas
- La **aplicación** del `must_change_password` en el login (forzar cambio en el próximo acceso) no estaba cubierta por un AC de esta HU; el flag se fija y será consumido por el flujo de cambio de contraseña (HU #10171 / frontend #10173).
