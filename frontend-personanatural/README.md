# frontend-personanatural

Portal público de **Persona Natural** (Épica #14021, Feature #14026): el ciudadano consulta, cotiza y radica trámites en
FLIT 2 sin cuenta. Habla solo con su BFF `core-personanatural` por el gateway; no usa el login del hub ni `@flit/*`.

- **Estructura:** la de `frontend-comparendos` (Next.js App Router, un puerto, una imagen para todos los ambientes).
- **Diseño:** portado sin cambios visuales del repo de referencia `transfer-step-trail` (tokens y clases en
  `app/globals.css`). Sus simuladores no se portan.
- **API:** `lib/api/cliente.ts` es la única puerta al BFF; los errores (`problem+json`) salen tipados en
  `lib/api/errores.ts`. El navegador llama al proxy de su propio host, `/api/v1/persona-natural/*`
  (`app/api/v1/persona-natural/[...path]`), que guarda el token de la sesión del trámite en una cookie **HttpOnly** y lo
  adjunta al reenviar: ningún JavaScript lo ve.
- **Habeas Data (Ley 1581):** nada de lo que escribe el ciudadano va a `localStorage`, `sessionStorage` ni a consola
  (guardado por `__tests__/datos-personales.test.ts`).

```bash
pnpm --filter @flit/frontend-personanatural dev        # http://127.0.0.1:4031
CORE_API_ORIGIN=http://localhost:4002 pnpm --filter @flit/frontend-personanatural start
```

Variable de entorno (servidor, por ambiente): `CORE_API_ORIGIN` = gateway que enruta `/api/v1/persona-natural/*`.
