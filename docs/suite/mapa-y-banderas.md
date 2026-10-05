# Mapa de la suite y sus banderas

Guía para entender, operar y depurar la FLIT Suite: qué pieza hace qué, qué viaja entre ellas, dónde se guarda cada
cosa y qué hace cada bandera en cada ambiente. HU #13222 (Feature #13218, Epic #13217).

Para levantarla en local, ver [local.md](local.md). Para lo que cambia cuando la identidad sale de `core-api`, ver
[identidad-frontera.md](identidad-frontera.md).

## 1. Las piezas

| Pieza | Código | Dirección (DEV) | Qué hace |
|---|---|---|---|
| **nginx del VPS** | fuera del repo; plantillas en `deploy/edge/nginx/` | `dev.flitsas.online`, `dev.tramites.flitsas.online`, `api.dev.flitsas.online` | Termina TLS y manda cada host a su contenedor. |
| **Hub** | `frontend-hub/` | `dev.flitsas.online` (puerto `4040`) | Portada, pantalla de login, inicio con los productos. Reenvía `/connect/*` y `/.well-known/*` al gateway. Con `FLIT_TRAMITES_HOST_ENABLED=true` redirige con 308 las rutas viejas de Trámites a su host. |
| **Trámites** | `frontend/` | `dev.tramites.flitsas.online` (puerto `4001`) | El producto. Con `FLIT_SESSION_MODE=oidc` usa la sesión de la suite y llama a la API por su mismo origen (`/api/v1` → BFF). |
| **@flit/auth** | `packages/auth/` | dentro de cada front | Rutas `/auth/login`, `/auth/callback`, `/auth/refresh`, `/auth/logout`, `/auth/session`, `/auth/claims`. Canjea el código, guarda la sesión cifrada, renueva el token y hace de proxy a la API con el Bearer. |
| **@flit/shell** | `packages/shell/` | dentro de cada front | Barra común (`SuiteShell`): menú de productos, menú de cuenta y dock desde un catálogo (`NavCatalog`). |
| **@flit/brand** | `packages/brand/` | dentro de cada front | Decide la marca por host (FLIT o Marca Blanca) con `FLIT_HOSTS`. |
| **Gateway** | `services/core-api/src/Flit.Gateway` (YARP) | interno, puerto `4002` | Reparte `/api`, `/connect`, `/.well-known`, `/hubs`, `/ml`. Sella el dominio (`X-Flit-Domain`) solo si la petición viene de la red interna. No valida tokens. |
| **core-api** | `services/core-api/src/Flit.Api` | interno, puerto `4003` | La API de negocio. Hoy también atiende la identidad (login, OIDC, usuarios, roles, productos) y valida los tokens. |
| **core-identity** | `services/core-identity` (`Flit.Identity.Api`) | interno, puerto `4004` (perfil `identity`) | El servicio de identidad: `/connect`, `/.well-known`, `/api/v1/auth`, `/api/v1/platform`, `/api/v1/public/branding`. Recibe tráfico solo con `FLIT_IDENTITY_CLUSTER_ENABLED`. |
| **Postgres** | — | host del VPS | Una base. Las tablas de la suite: `platform.*`, `identity.oidc_*`, `security.jwt_signing_keys`, el anillo de Data Protection. |

## 2. Qué viaja entre ellas

### Login cuando alguien abre Trámites sin sesión

```
Navegador ──GET /tramites──▶ Trámites (middleware: no hay flit_session_tramites)
          ◀──302 /auth/login?returnTo=/tramites?filtros
Navegador ──GET /auth/login──▶ Trámites (@flit/auth: crea la transacción PKCE en flit_oidc_tx_tramites, 10 min)
          ◀──302 https://dev.flitsas.online/connect/authorize?client_id=tramites…
Navegador ──GET /connect/authorize──▶ Hub ──▶ Gateway ──▶ core-api (OpenIddict)
          ¿Hay cookie flit_hub (sesión del hub)?
             no → 302 /login del hub → el usuario escribe usuario y contraseña → POST /connect/login → cookie flit_hub
             sí → sigue sin pedir nada (inicio de sesión único)
          core-api revisa: ¿la empresa tiene Trámites? ¿el usuario tiene rol en Trámites?
             no → 302 de vuelta con access_denied (PRODUCT_NOT_ENABLED / PRODUCT_ROLE_REQUIRED) → 403 de Trámites
             sí → 302 https://dev.tramites.flitsas.online/auth/callback?code=…
Navegador ──GET /auth/callback──▶ Trámites
          Trámites ──POST /connect/token (servidor a servidor, FLIT_OIDC_INTERNAL_URL)──▶ Gateway ──▶ core-api
                   ◀── access token (15 min, aud=tramites) + refresh token (14 días)
          Trámites guarda ambos cifrados en flit_session_tramites y responde 302 al returnTo
```

### Una llamada a la API

```
Navegador ──/api/v1/…──▶ Trámites (BFF de @flit/auth)
          ¿El token vence en menos de 60 s? → lo renueva con el refresh (rota el refresh) y reescribe la cookie
          Trámites ──Authorization: Bearer <token>──▶ Gateway ──▶ core-api
          core-api valida firma, emisor y audiencia, y revisa que la sesión no esté cerrada (caché de 15 s)
          401 de la API → @flit/auth borra la sesión local; la siguiente página pide login
```

El navegador **nunca** ve el token: solo la cookie cifrada. Para dibujar menús, Trámites guarda en `localStorage` los
claims **sin firma** (no sirven para llamar a la API).

### Entrar al hub cuando ya hay sesión en un producto

La portada del hub intenta primero un login silencioso (`prompt=none`). Si OpenIddict tiene sesión (`flit_hub`), entra
sin mostrar nada. Si no, vuelve con `sso=0` y muestra la portada, sin repetir el intento.

### Cerrar sesión

`/auth/logout` de cualquier app → `/connect/logout`: se revocan las autorizaciones de **esa** sesión del hub y sus
tokens, y se borra `flit_hub`. La respuesta es una página corta («Cerrando sesión…») que abre en segundo plano
`/auth/frontchannel-logout` de cada producto que inició sesión desde esa sesión del hub (front-channel logout de OIDC):
cada uno borra su cookie al instante y la página sigue a su destino (como máximo 2,5 s). Los productos se anotan en la
sesión del hub al entrar (claim `oidc_rp`, el origen de su `redirect_uri`). En los demás productos de ese navegador, la siguiente llamada a la API da 401 (la
revisión de sesión cerrada tarda como mucho 15 s) y @flit/auth borra su sesión. Otros dispositivos no se tocan.

## 3. Dónde vive cada cosa

| Qué | Dónde | Vida | Quién lo escribe |
|---|---|---|---|
| Sesión del hub (OpenIddict) | cookie `flit_hub`, host del hub | 12 h, se renueva con el uso (`Suite:Oidc:HubSessionHours`) | core-api en `/connect/login` |
| Sesión de un producto | cookie `flit_session_<producto>` (cifrada, partida en trozos si es grande) | 14 días | @flit/auth en el callback y en cada renovación |
| Transacción de login | cookie `flit_oidc_tx_<producto>` | 10 min | @flit/auth en `/auth/login` |
| Access token | dentro de la sesión del producto | 15 min (`Suite:Oidc:AccessTokenMinutes`) | core-api (OpenIddict) |
| Refresh token | dentro de la sesión del producto (por referencia) y en `identity.oidc_tokens` | 14 días, rota en cada uso (`RefreshTokenDays`) | core-api |
| Autorizaciones (una por sesión del hub y producto) | `identity.oidc_authorizations` | hasta el cierre de sesión; se purgan cada 6 h | core-api |
| Llaves de firma | `security.jwt_signing_keys`, cifradas con Data Protection | hasta que se rote el `SigningKeyId` | core-api al arrancar |
| Anillo de Data Protection | tabla de llaves en `FlitDbContext` | permanente | core-api |
| Productos por empresa | `platform.tenant_products` | permanente | SuperAdmin, en Compañías → la compañía → pestaña «Productos» |
| Claims para la UI (modo oidc) | `localStorage` de Trámites | hasta cerrar sesión | Trámites |
| JWT de siempre (modo legacy) | cookie `flit_token` + `localStorage` | 12 h, sin renovación (`Jwt:TokenLifetimeHours`) | core-api en `/api/v1/auth/login` |

Cachés en memoria de core-api (se pierden al reiniciar, no hay que limpiarlas a mano):

| Caché | Vida | Efecto |
|---|---|---|
| Sesión cerrada (`OidcSessionCheck`) | 15 s | Un token de una sesión recién cerrada puede servir hasta 15 s más. |
| Emisores válidos (`OidcIssuerRegistry`) | 5 min | Un dominio de Marca Blanca recién activado tarda hasta 5 min en aceptarse como emisor. |
| Producto encendido (`RequireProduct`) | 30 s | Apagar un producto tarda hasta 30 s en notarse en la API. |

## 4. Banderas y configuración

Todas se leen **en tiempo de ejecución**: cambiar una exige recrear el contenedor, no reconstruir la imagen. En el
servidor, las variables van en el `.env` del VPS y `docker-compose.prod.yml` las pasa a cada contenedor.

### Las que encienden la suite

| Variable (`.env`) | Configuración | Contenedor | Apagada (hoy) | Encendida |
|---|---|---|---|---|
| `FLIT_OIDC_ENABLED` | `Suite:Oidc:Enabled` | core-api | `/connect/*`, el descubrimiento y el JWKS responden 404. El login de siempre sigue igual. | Servidor OIDC activo. |
| `FLIT_SESSION_MODE` | — | frontend | `legacy`: Trámites con su login de siempre (`flit_token`). | `oidc`: Trámites con la sesión de la suite; las páginas protegidas exigen `flit_session_tramites`. |
| `COMPOSE_PROFILES=suite` | — | (compose) | El contenedor `frontend-hub` no se levanta. | Se levanta el hub. |
| `FLIT_TRAMITES_HOST_ENABLED` | — | frontend-hub | El hub no redirige nada. | Las rutas de Trámites en el host del hub responden 308 a `TRAMITES_URL`. |
| — | `Suite:ProductAccess:Enforce` | core-api | `RequireProduct` solo registra en el log si la empresa no tiene el producto. | Responde 403 `PRODUCT_NOT_ENABLED`. No tiene variable en el compose todavía. |

### Las del servicio de identidad aparte (Epic #13217)

| Variable (`.env`) | Configuración | Contenedor | Apagada / vacía (hoy) | Encendida |
|---|---|---|---|---|
| `COMPOSE_PROFILES=identity` | — | (compose) | `core-identity` no se levanta. | Se levanta `core-identity` (su propia imagen). |
| `FLIT_IDENTITY_CLUSTER_ENABLED` | `Gateway:IdentityCluster:Enabled` | gateway | `/connect`, `/.well-known`, `/api/v1/auth`, `/api/v1/platform` y `/api/v1/public/branding` van a `core-api`. | Esas rutas van a `core-identity`; el resto sigue en `core-api`. Volver atrás = `false`. |
| `CORE_IDENTITY_PORT` | — | core-identity, gateway | `4004` | Puerto interno de `core-identity` (por ambiente, como los demás). |
| `CORE_IDENTITY_TAG` | — | core-identity | — | Etiqueta de su imagen. La exporta el CD (si su código no cambió, la imagen es la misma reetiquetada). |

`/health/ready` responde 503 si el servicio no puede atender: en `core-api`, base caída o migraciones pendientes; en
`core-identity`, base caída o falta alguna tabla o columna de su modelo (no migra). Lo usan el healthcheck del compose y
el gateway.

### Las que dicen dónde está cada cosa

| Variable | Contenedor | Qué es | Local | DEV / QA / PDN |
|---|---|---|---|---|
| `FLIT_HUB_URL` | frontend, frontend-hub, core-api (enlaces de correo) | URL pública del hub = emisor OIDC | `http://127.0.0.1:4040` | la pone el CD: `https://dev.flitsas.online`, `https://qa.flitsas.online`, `https://flitsas.online` |
| `FLIT_OIDC_INTERNAL_URL` | frontend, frontend-hub | Adónde canjea y renueva tokens el servidor | igual a `FLIT_HUB_URL` | `http://gateway:<puerto>` (red interna) |
| `CORE_API_ORIGIN` | frontend, frontend-hub | Destino del proxy `/api/v1` | `http://127.0.0.1:4003` | `http://gateway:<puerto>` |
| `TRAMITES_URL` (`FLIT_TRAMITES_URL` en el `.env`) | frontend-hub | URL de Trámites | `http://127.0.0.1:3000` | la pone el CD: `https://dev.tramites.flitsas.online`… |
| `FLIT_SUITE_ENV` → `Suite:Hosts:Environment` | core-api | Prefijo de ambiente para armar los hosts de cada producto | vacío + `Overrides` | `dev`, `qa`, vacío en PDN (la pone el CD; también va en el `.env`, el compose no arranca sin ella) |
| `Suite:Hosts:Overrides:<producto>` | core-api | URL fija por producto | `plataforma`, `tramites` con `127.0.0.1` | no se usa |
| `Suite:Hosts:ComingSoon` | core-api, core-identity | Productos sin app desplegada todavía: su enlace lleva a `/proximamente/<código>` del hub | `comparendos`, `diagnostico` (en `appsettings.json`) | igual; al desplegar un producto en un ambiente, se quita de la lista de ese ambiente |
| `FLIT_APP_URL` | cada front | URL pública de la app, si el `Host` no sirve | no hace falta | no hace falta |
| `FLIT_HOSTS` | frontend-hub | Qué hosts son marca FLIT (el resto, Marca Blanca) | por defecto | por defecto del compose |

### Los secretos

| Variable | Contenedor | Qué es | Si falta |
|---|---|---|---|
| `FLIT_SESSION_SECRET` | frontend, frontend-hub | Cifra las cookies de sesión. Mínimo 32 caracteres, distinto por ambiente. | En `next dev` se usa una clave fija; en producción la app no arranca. Si cambia, todas las sesiones se pierden. |
| `FLIT_INTERNAL_API_KEY` | gateway, core-api, fronts | Permite al gateway confiar en el sello de dominio que mandan los fronts por la red interna. | La Marca Blanca resuelta en servidor no funciona; todo sale con marca FLIT. |

### Las de los tokens

| Configuración | Valor | Qué hace |
|---|---|---|
| `Suite:Oidc:AccessTokenMinutes` | 15 | Vida del access token. |
| `Suite:Oidc:RefreshTokenDays` | 14 | Vida del refresh token; se corre con cada renovación. |
| `Suite:Oidc:RefreshTokenReuseLeewaySeconds` | 0 | Reusar un refresh ya usado revoca toda la cadena (señal de robo). |
| `Suite:Oidc:HubSessionHours` | 12 | Vida de la sesión del hub (`flit_hub`). |
| `Suite:Oidc:SigningKeyId` | `flit-oidc-signing-v1` | Cambiarlo rota la llave de firma: todos vuelven a iniciar sesión. |
| `JWT_PERSIST_SIGNING_KEY` → `Jwt:PersistSigningKey` | `true` | La llave del JWT de siempre se guarda en la base (sobrevive a reinicios). |
| `JWT_VALIDATE_ISSUED_TOKENS` → `Jwt:ValidateIssuedTokens` | `true` | core-api valida la firma de los tokens. En `false` vuelve al comportamiento anterior (acepta cualquiera). |
| `Gateway:DisableJwtPolicy` | `true` solo en Development | El gateway no exige token en sus rutas (la validación la hace core-api). |

La duración de la sesión se va a acordar con el líder técnico y el PO (Feature #13221): hoy, con la suite, alguien que
entre al menos una vez cada 14 días no vuelve a escribir la contraseña.

## 5. Combinaciones

| Estado | `FLIT_OIDC_ENABLED` | `FLIT_SESSION_MODE` | perfil `suite` | `FLIT_TRAMITES_HOST_ENABLED` | nginx |
|---|---|---|---|---|---|
| **Hoy (apagada)** | `false` | `legacy` | no | `false` | raíz → Trámites |
| **Encendida** | `true` | `oidc` | sí | `true` | raíz → hub, host propio → Trámites |

Combinaciones que **no** sirven:

| Combinación | Qué pasa |
|---|---|
| `FLIT_SESSION_MODE=oidc` con `FLIT_OIDC_ENABLED=false` | Trámites manda al login del hub y `/connect/authorize` da 404: nadie entra. |
| `FLIT_SESSION_MODE=oidc` sin el hub levantado | El login no tiene a dónde ir: nadie entra. |
| `FLIT_TRAMITES_HOST_ENABLED=true` sin el host de Trámites en nginx | El 308 lleva a un host que no responde. |
| nginx con la raíz al hub y `FLIT_SESSION_MODE=legacy` | Trámites ya no está en la raíz y su login de siempre queda en un host al que nadie llega. |

Encender en un ambiente sigue el orden de `deploy/edge/nginx/flit-suite-hosts.conf.example`. Volver atrás: nginx de la
raíz otra vez al frontend, `FLIT_SESSION_MODE=legacy` y `FLIT_TRAMITES_HOST_ENABLED=false`. Los usuarios inician
sesión una vez más con el login de siempre.

## 6. Depurar

| Síntoma | Causa probable | Dónde mirar |
|---|---|---|
| Página en blanco o 431 en local | Cookies de `localhost` de otras apps pasan 16 KB | Usar `127.0.0.1` ([local.md](local.md)) |
| «Abriendo tu sesión…» que no termina | La API no está arriba, o no acepta la URL de retorno | `Suite:Hosts:Overrides` (local) o `Suite:Hosts:Environment` (servidor); log de core-api |
| Vuelve al login después de iniciar sesión | La cookie de sesión no se pudo leer: cambió `FLIT_SESSION_SECRET` o la cookie no se guardó (HTTP vs HTTPS) | DevTools → Application → Cookies: ¿existe `flit_session_tramites`? |
| Un producto encendido no le aparece a un usuario | Le falta un rol en ese producto. El Admin de Compañía lo recibe solo (`admin_<producto>`, DDL 126); a los demás se lo asigna él | Pestaña «Productos» de la compañía; `security.user_role_assignments` del usuario con `product_code` |
| 403 «Tu empresa no tiene Trámites» | La empresa no tiene el producto en `platform.tenant_products`, o el usuario no tiene rol en Trámites | `GET /api/v1/platform/me/apps`; configuración de la compañía (SuperAdmin) |
| Entra pero los menús salen vacíos | Los claims del token no traen roles/permisos del producto | Decodificar el token (`/auth/claims` en local) y revisar `roles` y `permissions` |
| «Reconectando tu sesión…» un segundo al abrir un producto | La cookie del producto tenía una sesión revocada (cerrada desde otro lado); el producto pidió una nueva al hub sin errores. Si pasa siempre al salir desde el hub, el front-channel logout no está llegando: algo en el borde bloquea `/auth/frontchannel-logout` dentro de la página del hub | Log del producto: `GET /auth/frontchannel-logout` al cerrar sesión; cabeceras del borde (`X-Frame-Options`) |
| «Tu sesión expiró» justo después de iniciar sesión | La cookie del producto era de una sesión ya cerrada (se salió desde otra app) o se reusó un refresh (H3 y H4 de [matriz-pruebas.md](matriz-pruebas.md)). Hoy la app pide una sesión nueva sola; el aviso solo sale si eso falla dos veces en menos de 10 s | Log de core-identity: `already been redeemed` o `were revoked to prevent a potential token replay attack` |
| Cerró sesión y otro producto sigue abierto | Normal hasta 15 s (caché de sesión cerrada) o hasta la siguiente llamada a la API | Esperar o recargar; si persiste, log de core-api por `SESSION_EXPIRED` |
| Todos los usuarios tienen que volver a entrar | Cambió `FLIT_SESSION_SECRET`, el `SigningKeyId` o el anillo de Data Protection | `.env` del ambiente; tabla de llaves |
| `SESSION_EXPIRED` en todas las llamadas | La API no reconoce el emisor o la firma | `Suite:Hosts:*`, `/.well-known/openid-configuration` del hub, `JWT_VALIDATE_ISSUED_TOKENS` |
