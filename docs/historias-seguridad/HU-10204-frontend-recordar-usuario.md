# HU #10204 — Recordar usuario registrado (frontend)

**Feature:** #10113 · **Tipo:** Frontend (Next.js) · **Rama:** feature/AB-10204-frontend-recordar-usuario

## Qué se implementó
- **`/auth/remember-username`** — pantalla con `RememberUsernameForm`: el usuario ingresa su **número de documento**; si es válido, solicita el recordatorio (`POST /auth/remember-username`) y muestra una **confirmación genérica** (AC1, anti-enumeración).
- **Validación accesible (AC2):** documento vacío o con formato inválido (no numérico) → mensaje de error (`role="alert"`, `aria-invalid`) **sin enviar** la solicitud.
- Enlace desde el login ("¿Olvidaste tu usuario?") ya apunta a esta pantalla.

## Casos de uso
1. El usuario no recuerda con qué correo se registró → ingresa su documento → recibe su usuario por correo.
2. El usuario deja el campo vacío o escribe un valor inválido → ve un error claro sin enviar nada.

## Cómo probar
1. Ir a `/auth/remember-username` (o desde el login → "¿Olvidaste tu usuario?").
2. Ingresar un documento válido (4–20 dígitos) → "Si el documento corresponde a una cuenta, enviaremos el usuario al correo registrado".
3. Dejar vacío → "Ingresa tu número de documento". Ingresar `abc-12` → "El documento debe contener solo números".

## Respuestas esperadas
| Escenario | Resultado UI |
|---|---|
| documento válido (AC1) | confirmación genérica |
| vacío / formato inválido (AC2) | error de validación accesible, sin enviar |

## Pruebas ejecutadas
- Vitest + RTL: 3 nuevas (`RememberUsernameForm.test.tsx`). Suite frontend total **62/62**.
- `tsc --noEmit` ✅ · ESLint ✅.

## Notas
- Consume el endpoint backend de la HU **#10203** (`POST /auth/remember-username`), que responde 202 genérico.
