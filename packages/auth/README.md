# @flit/auth

Sesión OIDC de las apps Next.js de la FLIT Suite (tarea **A-09**, contrato de plataforma v1 §8). Cada app inicia
sesión por el hub (authorization code con PKCE), guarda la sesión cifrada en su servidor y llama a la API con el token
sin que el navegador lo vea nunca.

## Uso

```ts
// lib/auth.server.ts
import { createApiProxy, createAuthRoutes } from "@flit/auth/server";
export const authRoutes = createAuthRoutes({ productCode: "tramites" });
export const apiProxy = createApiProxy({ productCode: "tramites" });

// app/auth/login/route.ts (y callback, logout, refresh, session)
export const GET = (request: Request) => authRoutes.login(request);

// app/api/v1/[...path]/route.ts
export async function GET(request: Request, { params }: { params: Promise<{ path: string[] }> }) {
  return apiProxy(request, (await params).path);
}

// Server Component
const user = await getSession("tramites");

// Client Component
const { user, status } = useSession(); // de "@flit/auth/client"
```

| Ruta | Qué hace |
|---|---|
| `GET /auth/login?returnTo=/ruta` | Al `authorize` del hub con PKCE; la transacción va en una cookie cifrada de 10 minutos |
| `GET /auth/callback` | Verifica `state`, canjea el código y abre la sesión. Si el hub niega el acceso, va a `/403?code=…` |
| `GET\|POST /auth/logout` | Borra la sesión y cierra la del hub (`/connect/logout`) |
| `POST /auth/refresh` | Renueva si hace falta; `401 SESSION_EXPIRED` si ya no se puede |
| `GET /auth/session` | `SessionUser` para `useSession()` |

## Sesión sin Redis

- **Dónde vive:** en la cookie `flit_session_<producto>`: comprimida, cifrada con AES-GCM (llave derivada de
  `FLIT_SESSION_SECRET`), `HttpOnly`, `SameSite=Lax` y **sin `Domain`**. Si pasa de ~3,8 KB se parte en `flit_session_<producto>.1`, …
- **Tamaño:** el payload del JWT se guarda como texto (comprime mejor que su base64) y el refresh token es una
  referencia opaca que el hub guarda en su base. Una sesión típica pesa ~1,3 KB, lo que importa porque Node limita
  toda la cabecera `Cookie` a 16 KB.
- **Renovación:** el access token dura 15 minutos. El proxy y `/auth/session` lo renuevan cuando le falta menos de
  un minuto. Si el refresh ya no sirve (usuario suspendido, sin acceso, sesión cerrada en el hub) la sesión se borra y
  la API responde `SESSION_EXPIRED`, que el frontend ya maneja.
- `getSession()` en un Server Component no renueva (no puede escribir cookies); solo lee.

## Variables (servidor, en runtime)

| Variable | Para qué | Por defecto |
|---|---|---|
| `FLIT_HUB_URL` | URL pública del hub (emisor) | `http://localhost:4040` |
| `FLIT_OIDC_INTERNAL_URL` | Canje y renovación por la red interna | `FLIT_HUB_URL` |
| `CORE_API_ORIGIN` | Destino del proxy `/api/v1/*` | `http://localhost:4002` |
| `FLIT_SESSION_SECRET` | Cifra la cookie; obligatoria en producción (≥ 32 caracteres) | en `next dev`, una fija de desarrollo |
| `FLIT_INTERNAL_API_KEY` | Para que el gateway acepte el sello `X-Flit-Domain` | vacía |
| `FLIT_APP_URL` | URL pública de la app, si el Host no la refleja | el Host de la petición |
