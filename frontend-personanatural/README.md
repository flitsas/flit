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

Variables de entorno (servidor, por ambiente; ninguna lleva prefijo `NEXT_PUBLIC_`, nada se incrusta en el build):

- `CORE_API_ORIGIN` = gateway que enruta `/api/v1/persona-natural/*`.
- `CAPTCHA_PROVIDER` = `turnstile` | `recaptcha` | `dev`. Sin valor: `dev` fuera de producción y sin captcha (no se envía) en producción.
- `CAPTCHA_SITE_KEY` = clave pública del sitio (Turnstile o reCAPTCHA); no es un secreto.

El layout se renderiza por petición (`connection()`), lee `lib/config.server.ts` y entrega el proveedor y la clave pública del
sitio al navegador por el contexto `ConfigPublica`: la misma imagen sirve en DEV, QA y PDN.

## IP del ciudadano detrás de nginx

El BFF limita por IP, así que el proxy del portal (`lib/server/ip.ts`) NO confía en el primer valor de `X-Forwarded-For`
(lo controla el cliente). Orden: `X-Real-IP`; si falta, el salto de `X-Forwarded-For` número `TRUSTED_PROXY_HOPS`
contando desde la derecha (variable de servidor, por defecto `1`).

nginx debe **sobrescribir** ambas cabeceras con la IP que él ve, nunca anexar a lo que trae el cliente:

```nginx
proxy_set_header X-Real-IP       $remote_addr;
proxy_set_header X-Forwarded-For $remote_addr;   # no usar $proxy_add_x_forwarded_for
```

Si hay otro proxy propio delante de nginx, ajustar `TRUSTED_PROXY_HOPS` al número de proxies propios.

## Cabeceras de seguridad

`next.config.ts` aplica a todas las rutas (incluida `/api/...`) la política de `lib/seguridad/cabeceras.ts`: CSP,
HSTS, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` y `Permissions-Policy`. El captcha permitido en
`script-src`/`frame-src` es una lista fija: Cloudflare Turnstile (`https://challenges.cloudflare.com`) y Google
reCAPTCHA (`https://www.google.com/recaptcha/`, `https://www.gstatic.com/recaptcha/`). Límite conocido: las páginas son
estáticas, sin nonce por petición, por lo que `script-src` y `style-src` llevan `'unsafe-inline'` (scripts de arranque
de Next y estilos en línea); no se permite `unsafe-eval` en producción.
