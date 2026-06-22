# HU #10169 — Recuperación de contraseña (forgot / reset)

**Feature:** #10113 Autenticación y Autogestión de Credenciales · **Tipo:** Backend · **PR:** GitHub #7

## Qué se implementó
Flujo self-service de recuperación de contraseña sobre el módulo de Seguridad (Clean Architecture, .NET 10):

- **`POST /api/v1/auth/forgot-password`** — genera un token de un solo uso, persiste **solo su hash SHA-256** en `security.password_reset_tokens` (`purpose = password_reset`, vigencia 30 min) y envía el enlace por correo. Responde **202 genérico** sin revelar si el email existe (anti-enumeración).
- **`POST /api/v1/auth/reset-password`** — valida el token vigente, fija la nueva contraseña (Argon2id), marca el token como usado e invalida los demás tokens activos del usuario.
- Infraestructura de correo nueva: `IEmailSender` con `SmtpEmailSender` (MailKit) y `ConsoleEmailSender` (fallback en dev cuando `Smtp:Host` está vacío — registra el enlace en el log).
- Generador de tokens seguro: `SecureTokenGenerator` (32 bytes aleatorios base64url + hash SHA-256).

## Casos de uso
1. Usuario olvidó su contraseña → solicita recuperación → recibe enlace por correo → define nueva contraseña.
2. Atacante intenta enumerar emails → ambas respuestas (existente/inexistente) son idénticas (202).
3. Token caducado o ya usado → la redención falla con 400.

## Cómo probar

### AC1 — Solicitud con email registrado
```bash
curl -X POST http://localhost:4003/api/v1/auth/forgot-password \
  -H 'Content-Type: application/json' -d '{"email":"demo@flit.local"}'
# → 202 {"message":"Si el correo está registrado, enviaremos instrucciones de recuperación."}
# En dev, el enlace aparece en los logs del backend (ConsoleEmailSender):
#   [DEV EMAIL] ... /reset-password?token=<TOKEN>
```

### AC2 — Email no registrado (anti-enumeración)
```bash
curl -X POST http://localhost:4003/api/v1/auth/forgot-password \
  -H 'Content-Type: application/json' -d '{"email":"missing@flit.local"}'
# → 202 (mensaje idéntico, sin crear token)
```

### Redención + verificación
```bash
# Tomar el <TOKEN> del log y fijar nueva contraseña:
curl -X POST http://localhost:4003/api/v1/auth/reset-password \
  -H 'Content-Type: application/json' -d '{"token":"<TOKEN>","newPassword":"NewDemoPass1!"}'
# → 200 {"message":"Contraseña actualizada correctamente."}

# Login con la nueva contraseña → 200 + JWT (12h)
curl -X POST http://localhost:4003/api/v1/auth/login \
  -H 'Content-Type: application/json' -d '{"email":"demo@flit.local","password":"NewDemoPass1!"}'

# Reuso del mismo token → 400 INVALID_RESET_TOKEN
# Contraseña débil (<8) → 400 WEAK_PASSWORD
```

## Respuestas esperadas
| Escenario | HTTP | Cuerpo |
|---|---|---|
| forgot-password (cualquier email) | 202 | `{"message":"Si el correo está registrado..."}` |
| reset-password OK | 200 | `{"message":"Contraseña actualizada correctamente."}` |
| reset token inválido/expirado/usado | 400 | `{"code":"INVALID_RESET_TOKEN", ...}` |
| reset contraseña débil | 400 | `{"code":"WEAK_PASSWORD", ...}` |

## Pruebas ejecutadas
- Unitarias: 7 (xUnit) — `ForgotPasswordHandlerTests` (3), `ResetPasswordHandlerTests` (4). Suite total 83/83.
- E2E en DEV (docker): 6/6 escenarios OK.
