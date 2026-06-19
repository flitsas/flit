# HU #10172 — Pantalla de login y manejo de sesión expirada (frontend)

**Feature:** #10113 · **Tipo:** Frontend (Next.js 16 / React 19) · **Rama:** feature/AB-10172-frontend-login

## Qué se implementó
- **Página `/login`** (`app/login/page.tsx`) con formulario real (`components/auth/LoginForm.tsx`) que llama a `POST /api/v1/auth/login`, **almacena el JWT** (cookie `flit_token` + localStorage) y redirige al `returnUrl` o al dashboard.
- **Cliente de API de auth** (`lib/api/auth.ts`): login, forgot/reset, remember-username, change-password, admin-reset.
- **Manejo de sesión expirada (AC2):** el cliente HTTP (`lib/api/client.ts`) detecta `401 { code: "SESSION_EXPIRED" }`, limpia el token y emite un evento global; `SessionExpiredListener` (montado en el layout) muestra un **modal accesible** (`role="dialog"`, `aria-modal`) y redirige a `/login?returnUrl=<ruta actual>`.
- Manejo de errores en login: credenciales inválidas (401) y **cuenta bloqueada temporalmente (403)** con mensajes claros.

## Casos de uso
1. Usuario ingresa credenciales válidas → entra al dashboard, sesión persistida.
2. El JWT expira durante la navegación → modal "Tu sesión expiró" → vuelve al login conservando la ruta.
3. Credenciales inválidas / cuenta bloqueada → mensaje accesible sin exponer detalles.

## Cómo probar
```
# Local: pnpm --filter @flit/frontend dev  (frontend en :3000)
# Backend en :4003 (o configurar NEXT_PUBLIC_API_BASE_URL)
```
1. Ir a `/login`, ingresar `demo@flit.local` / `DemoPass1!` → redirige al inicio; el JWT queda en cookie `flit_token`.
2. Credenciales incorrectas → mensaje "Correo o contraseña incorrectos".
3. Con una suspensión temporal activa (HU #10170) → mensaje "Tu cuenta está bloqueada temporalmente".
4. Forzar `SESSION_EXPIRED` (token expirado en una llamada protegida) → aparece el modal y redirige a `/login?returnUrl=…`.

## Respuestas esperadas
| Escenario | Resultado UI |
|---|---|
| login OK | redirección + JWT almacenado |
| 401 credenciales | alerta "Correo o contraseña incorrectos" |
| 403 bloqueo | alerta "Tu cuenta está bloqueada temporalmente" |
| SESSION_EXPIRED | modal accesible + redirección con returnUrl |

## Pruebas ejecutadas
- Vitest + RTL: 6 nuevas (`LoginForm.test.tsx` 4, `SessionExpiredListener.test.tsx` 2). Suite frontend total **36/36**.
- `tsc --noEmit` ✅ · ESLint ✅ (0 errores; warnings preexistentes ajenos).
