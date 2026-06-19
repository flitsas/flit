# HU #10173 — Flujos de recuperación y cambio de contraseña (frontend)

**Feature:** #10113 · **Tipo:** Frontend (Next.js) · **Rama:** feature/AB-10173-frontend-recuperacion

## Qué se implementó
- **`/auth/forgot-password`** — solicita el enlace de recuperación (`POST /auth/forgot-password`). Siempre muestra confirmación **genérica** (anti-enumeración).
- **`/auth/reset-password?token=…`** — define nueva contraseña validando la **política** en cliente (8+ con may/min/dígito) y confirmación; al éxito (AC1) ofrece **ir a login**. Token ausente/expirado/usado (AC2) → error claro **sin exponer datos**.
- **`/profile/change-password`** — cambio voluntario desde el perfil (`PUT /auth/change-password`) con validación de política y manejo de contraseña actual incorrecta.
- Utilidad compartida `lib/auth/password-policy.ts` (espejo del backend).

## Casos de uso
1. Olvidé mi contraseña → solicito enlace → defino una nueva → vuelvo al login.
2. Abro un enlace caducado/usado → veo error claro sin detalles sensibles.
3. Desde mi perfil, cambio mi contraseña aportando la actual.

## Cómo probar
1. `/auth/forgot-password`: ingresar correo → "Si el correo está registrado, enviaremos instrucciones".
2. Tomar el enlace del correo (en dev, del log del backend) → `/auth/reset-password?token=<TOKEN>` → nueva contraseña que cumpla política → éxito + "Ir a iniciar sesión".
3. Token inválido o sin `token` en la URL → mensaje "El enlace de recuperación es inválido o expiró".
4. `/profile/change-password`: actual + nueva (cumpliendo política) → éxito; actual incorrecta → "La contraseña actual es incorrecta".

## Respuestas esperadas
| Escenario | Resultado UI |
|---|---|
| reset OK (AC1) | confirmación + enlace a login |
| token inválido/usado (AC2) | error claro sin exponer datos |
| nueva no cumple política | mensaje de política, sin llamar API |
| cambio perfil OK | confirmación |

## Pruebas ejecutadas
- Vitest + RTL: 20 nuevas (`ResetPasswordForm` 6, `ForgotPasswordForm` 3, `ChangePasswordForm` 3, `password-policy` 8). Suite frontend total **55/55**.
- `tsc --noEmit` ✅.

## Notas
- "Recordar usuario" (mencionado en la descripción) se entrega en la HU **#10204** (pantalla dedicada), relacionada con esta.
