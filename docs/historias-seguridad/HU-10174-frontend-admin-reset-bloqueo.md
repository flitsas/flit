# HU #10174 — UI admin: reset de contraseña y bloqueo temporal (frontend)

**Feature:** #10113 · **Tipo:** Frontend (Next.js) · **Rama:** feature/AB-10174-frontend-admin-users

## Qué se implementó
- **`/admin/users`** (protegida por middleware, rol SuperAdmin) — gestión de usuarios con acciones por fila.
- **`ResetPasswordModal` (AC1):** confirma el reset (`POST /auth/admin/reset-password`); en éxito informa que el usuario **deberá cambiar la contraseña en su próximo inicio de sesión**. Maneja 403 (fuera de ámbito) y 404 sin aplicar.
- **`BlockUserModal` (AC2):** aplica bloqueo temporal (`POST /auth/admin/block-user`); si la API responde **403** (otro tenant / fuera de ámbito) muestra el error **sin aplicar cambios**.

## Casos de uso
1. Admin restablece la contraseña de un usuario de su ámbito → confirmación + cambio obligatorio.
2. Admin Tenant intenta bloquear a un usuario de **otro** tenant → la UI muestra 403 y no aplica nada.
3. Admin programa un bloqueo temporal (días) dentro de su ámbito.

## Cómo probar
1. Ir a `/admin/users` (con JWT SuperAdmin).
2. "Restablecer" en un usuario → modal → "Confirmar reset" → mensaje "deberá definir una nueva en su próximo inicio de sesión".
3. "Bloquear" en un usuario de otro tenant → "Aplicar bloqueo" → error "Acceso restringido…" sin aplicar.

## Respuestas esperadas
| Escenario | Resultado UI |
|---|---|
| reset OK (AC1) | confirmación + nota de cambio obligatorio |
| reset/bloqueo 403 (AC2) | error "Acceso restringido", sin aplicar |
| 404 usuario | "El usuario no existe" |

## Pruebas ejecutadas
- Vitest + RTL: 4 nuevas (`ResetPasswordModal` 2, `BlockUserModal` 2). Suite frontend total **59/59**.
- `tsc --noEmit` ✅.

## Notas / dependencias
- El **endpoint backend de bloqueo temporal** (`POST /auth/admin/block-user`) está **pendiente** (HU backend separada); el frontend ya implementa y prueba su contrato, incluido el 403 fuera de ámbito.
- El **listado de usuarios** usa un conjunto de ejemplo; la fuente real (endpoint de listado de usuarios por tenant) es una dependencia pendiente.
