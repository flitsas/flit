# frontend-hub

Hub de la FLIT Suite en `flitsas.online` (tarea **B-09**, plan maestro §4). Hoy es el esqueleto: portada con la
marca del host y el proxy hacia la API. Lo que falta llega por tareas:

| Tarea | Qué agrega |
|---|---|
| A-06 | Login, recuperación y activación de invitación en `app/(auth)` sobre el servidor OIDC (A-05) |
| B-11 | Inicio con los productos del usuario (opción 4, «Hub con entrada directa») y menú de productos |
| B-12 | Administración de plataforma: empresa, usuarios y roles, productos, marca y dominio, auditoría |

## Correr en local

```bash
pnpm run dev:hub          # http://localhost:4040
```

Con la API en `http://localhost:4002` (gateway) basta. Para apuntar a otro lado, variables del servidor:

| Variable | Para qué | Por defecto |
|---|---|---|
| `CORE_API_ORIGIN` | Destino del proxy `/api/v1/*` (el gateway) | `http://localhost:4002` |
| `BRANDING_INTERNAL_API_URL` | Dónde se pide la marca de un dominio de red | `http://localhost:4002` |
| `FLIT_INTERNAL_API_KEY` | Clave para que el gateway acepte el sello `X-Flit-Domain` | vacía (no se envía) |
| `FLIT_HOSTS` | Hosts FLIT, sin marca de red (admite `*.dominio` y `!host`) | los de `@flit/brand` |
| `TRAMITES_URL` / `HUB_LOGIN_URL` | Adónde lleva «Iniciar sesión» hasta A-06 | `http://localhost:3000/login` |

## Reglas

- **Nada por ambiente se hornea en el build.** Sin `NEXT_PUBLIC_*`: la misma imagen corre en DEV, QA y PDN y lee
  su configuración al atender cada petición (`lib/config.server.ts`).
- **El navegador solo habla con su propio host.** `/api/v1/*` pasa por `app/api/v1/[...path]`, que descarta el
  `X-Flit-Domain` y el `X-Internal-Key` del navegador y pone el host real con la clave interna.
- **La marca sale del host** con `@flit/brand`, en el servidor y antes de pintar.
- En los servidores el servicio `frontend-hub` del compose está bajo el perfil `suite`: no arranca hasta poner
  `COMPOSE_PROFILES=suite` en el `.env`, cuando el borde tenga el host del hub.
