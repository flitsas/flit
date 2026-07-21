# Despliegue, conexión a BD y puertos de FLIT

Guía de **dónde se configura cada cosa** en los tres escenarios de ejecución:

| Escenario | Qué es | Archivo(s) de configuración |
|-----------|--------|------------------------------|
| **Local** | Cada servicio corre en tu máquina con hot-reload (`pnpm dev`), sin contenedores de app | `appsettings.json`, `package.json` (scripts `dev:*`), `launchSettings.json` |
| **Local con Docker** | Stack levantado con `docker compose` (BD incluida) | [docker-compose.yml](../docker-compose.yml) |
| **DEV (VPS)** | Despliegue real en el servidor vía CD | [docker-compose.prod.yml](../docker-compose.prod.yml) + `.env` (ver [.env.prod.example](../.env.prod.example)) |

> **Convención de puertos:** cada servicio usa **el mismo puerto expuesto e interno**
> (expuesto == interno) y el número **cambia por ambiente** en los despliegues:
>
> | Servicio | DEV | QA | PDN |
> |----------|-----|-----|-----|
> | frontend | 4001 | 5001 | 6001 |
> | gateway | 4002 | 5002 | 6002 |
> | core-api | 4003 | 5003 | 6003 |
> | python-ml | 4012 | 5012 | 6012 |
>
> Postgres siempre en `5432`. En el compose los puertos son variables
> (`FRONTEND_PORT`/`GATEWAY_PORT`/`CORE_API_PORT`/`PYTHON_ML_PORT`) con defaults DEV;
> el CD las inyecta según la rama (ver §6.3).

---

## 1. Conexión a la base de datos

La cadena de conexión la consume **core-api** (.NET / Npgsql). La clave de
configuración es `ConnectionStrings:Core` (o la variable de entorno equivalente
`ConnectionStrings__Core`, que **siempre tiene prioridad** sobre el JSON).

> `core-api` es el **único dueño** de la BD y de las migraciones (ADR-0014).
> Gateway y frontend **no** se conectan directo a Postgres.

### a) Local (sin Docker)

- **Archivo:** [services/core-api/src/Flit.Api/appsettings.json](../services/core-api/src/Flit.Api/appsettings.json)
- **Clave:** `ConnectionStrings.Core`
- **Valor por defecto:**
  ```
  Host=localhost;Port=5432;Database=flit_dev;Username=postgres;Password=postgres
  ```
- Para sobrescribir sin tocar el archivo versionado, crea
  `appsettings.Development.json` (hay plantilla `*.example`) o exporta la variable
  `ConnectionStrings__Core`.
- Necesitas un Postgres escuchando en `localhost:5432` (instalado en tu máquina o
  el contenedor `postgres` del compose, ver punto b).

### b) Local con Docker

- **Archivo:** [docker-compose.yml](../docker-compose.yml), servicio `backend`
- **Clave:** variable de entorno `ConnectionStrings__Core`
- **Valor:**
  ```
  Host=postgres;Port=5432;Database=flit_dev;Username=postgres;Password=postgres
  ```
- El host es **`postgres`** (el nombre del servicio en la red de Docker), **no**
  `localhost`. La BD es el servicio `postgres` definido en el mismo compose; sus
  credenciales se fijan en el bloque `environment` de ese servicio.

### c) DEV (VPS)

- **Archivos:** [docker-compose.prod.yml](../docker-compose.prod.yml) (servicio
  `core-api`) + el `.env` que vive junto al compose en el servidor.
- **En el compose:** `ConnectionStrings__Core: ${CONNECTION_STRING_CORE}` — solo
  referencia la variable.
- **El valor real** se define en `.env` (ver plantilla [.env.prod.example](../.env.prod.example)):
  ```
  CONNECTION_STRING_CORE=Host=host.docker.internal;Port=5432;Database=flit_dev;Username=flit;Password=...;SSL Mode=Disable
  ```
- Postgres **no** está en el compose: corre en el **HOST** del VPS. Por eso se usa
  `host.docker.internal` (habilitado con `extra_hosts: host.docker.internal:host-gateway`).
- El `.env` real **no se commitea**; el `.example` solo documenta las variables.

**Resumen del host de BD según escenario:**

| Escenario | Host en la cadena |
|-----------|-------------------|
| Local | `localhost` |
| Local Docker | `postgres` (nombre del servicio) |
| DEV (VPS) | `host.docker.internal` (Postgres en el host) |

---

## 2. Puertos de comunicación por servicio

### a) Local (sin Docker)

Los puertos los fijan los scripts `dev:*` de [package.json](../package.json) y los
`launchSettings.json` de cada proyecto .NET:

| Servicio | Puerto | Dónde se configura |
|----------|--------|--------------------|
| frontend | `3000` (default de Next) | `pnpm dev:frontend` → `next dev` |
| gateway  | `4002` | `dev:gateway` (`--urls http://localhost:4002`) y [Flit.Gateway/Properties/launchSettings.json](../services/core-api/src/Flit.Gateway/Properties/launchSettings.json) |
| core-api | `4003` | `dev:core-api` (`--urls http://localhost:4003`) y [Flit.Api/Properties/launchSettings.json](../services/core-api/src/Flit.Api/Properties/launchSettings.json) |
| python-ml| `4012` | `dev:python` (`uvicorn ... --port 4012`) |

Para arrancar todo: `pnpm dev` (front + gateway + core-api) o
`pnpm dev:with-python` (incluye python-ml).

> El frontend en local usa `3000`; en Docker/DEV usa `4001`. Si quieres unificarlo
> a `4001` también en local, exporta `PORT=4001` antes de `pnpm dev:frontend`.

### b) Local con Docker

- **Archivo:** [docker-compose.yml](../docker-compose.yml), bloque `ports` + variable
  de puerto interno de cada servicio.
- Mapeo (expuesto == interno): `backend` → `4003:4003` (`ASPNETCORE_URLS=http://+:4003`),
  `frontend` → `4001:4001` (`PORT=4001`), `postgres` → `5432:5432`.
- Este compose **no incluye** `gateway` ni `python-ml` (es un stack mínimo de dev): el
  frontend pega directo al `backend` vía `NEXT_PUBLIC_API_URL=http://localhost:4003`.
  Si necesitas la topología completa con gateway/ML, usa la de DEV (punto c) o levanta
  esos servicios en local con `pnpm dev:with-python`.

### c) DEV / QA / PDN (VPS) — puerto expuesto == interno, parametrizado

- **Archivo:** [docker-compose.prod.yml](../docker-compose.prod.yml). Los puertos son
  **variables** (default DEV); el mismo número se usa en el mapeo `ports` y en el
  puerto interno. El número cambia por ambiente (40xx / 50xx / 60xx).

| Servicio | Variable | Mapeo (`ports`) | Puerto interno fijado por |
|----------|----------|-----------------|----------------------------|
| frontend | `FRONTEND_PORT` | `127.0.0.1:${FRONTEND_PORT}:${FRONTEND_PORT}` | `PORT` (Next.js) |
| gateway  | `GATEWAY_PORT` | `127.0.0.1:${GATEWAY_PORT}:${GATEWAY_PORT}` | `ASPNETCORE_URLS: http://+:${GATEWAY_PORT}` |
| core-api | `CORE_API_PORT` | `127.0.0.1:${CORE_API_PORT}:${CORE_API_PORT}` | `ASPNETCORE_URLS: http://+:${CORE_API_PORT}` |
| python-ml| `PYTHON_ML_PORT` | `127.0.0.1:${PYTHON_ML_PORT}:${PYTHON_ML_PORT}` | `command: uvicorn ... --port ${PYTHON_ML_PORT}` |

- Además el gateway apunta sus destinos YARP a `core-api:${CORE_API_PORT}` y
  `python-ml:${PYTHON_ML_PORT}`, y cada `healthcheck` usa el puerto de su servicio.
- **De dónde salen los valores:** el **CD los exporta** según la rama (§6.3). Para
  operación manual, fíjalos en el `.env` del VPS de ese ambiente.
- El prefijo `127.0.0.1:` publica el puerto **solo en loopback** del VPS: el
  reverse-proxy del host (Nginx/Caddy) termina TLS y enruta los dominios a esos
  puertos locales. Nunca quedan expuestos públicamente.
- **Importante:** "expuesto == interno" se mantiene porque la **misma variable** alimenta
  el mapeo `ports` y el puerto interno (`ASPNETCORE_URLS` / `PORT` / `--port`) y los
  destinos YARP. Cambiar el puerto de un ambiente = cambiar un solo número en el `setup`
  del CD (o en el `.env`).

---

## 3. Comunicación entre servicios

El flujo es el mismo en los tres escenarios; solo cambian los hostnames/puertos.

```
  Navegador
     │  HTTPS (DEV) / HTTP (local)
     ▼
  Frontend (Next.js)  4001 / 3000
     │  llama a la API pública
     ▼
  Gateway (YARP)  4002          ← único punto de entrada de la API
     ├── /api/**      ─────────►  core-api  4003   (REST)
     ├── /hubs/**     ─────────►  core-api  4003   (SignalR)
     └── /ml/**       ─────────►  python-ml 4012   (OCR/ML)
                                     │
  core-api  4003  ───────────────►  Postgres 5432
```

### Reglas clave

- **El Gateway es el único punto de entrada público de la API.** El frontend habla
  **solo** con el gateway (`/api`, `/hubs`, `/ml`); nunca con core-api o python-ml
  directamente.
- **El enrutamiento del gateway (YARP)** se define en
  [Flit.Gateway/appsettings.json](../services/core-api/src/Flit.Gateway/appsettings.json),
  sección `ReverseProxy`:
  - `core-api-cluster` → core-api
  - `python-ml-cluster` → python-ml
  - Rutas: `/api/**`, `/hubs/**`, `/api/v1/auth/**` (público), `/ml/**`.
- **Hostnames de los destinos** según escenario:

  | Origen → Destino | Local | Local Docker | DEV (VPS) |
  |------------------|-------|--------------|-----------|
  | Front → Gateway | `http://localhost:4002` | (gateway no está en el compose) | dominio público `https://api.<dominio>` |
  | Gateway → core-api | `http://localhost:4003` (appsettings) | n/a | `http://core-api:4003` (nombre de servicio) |
  | Gateway → python-ml | `http://localhost:4012` | n/a | `http://python-ml:4012` (nombre de servicio) |
  | core-api → Postgres | `localhost:5432` | `postgres:5432` | `host.docker.internal:5432` |

- **En DEV los destinos internos del YARP** se sobrescriben desde el compose
  (no se edita el `appsettings.json` baked en la imagen), vía variables:
  ```
  ReverseProxy__Clusters__core-api-cluster__Destinations__core-api-1__Address: http://core-api:4003/
  ReverseProxy__Clusters__python-ml-cluster__Destinations__python-ml-1__Address: http://python-ml:4012/
  ```
  Docker resuelve `core-api` y `python-ml` por DNS interno (nombres de servicio del
  compose), por eso no se usan IPs ni `localhost`.

- **El frontend** debe apuntar a la URL **pública del gateway** (no a core-api). La
  base de la API se inyecta como variable de build/entorno (`NEXT_PUBLIC_*` en Next).
  En el pipeline CD se pasa la URL pública del gateway como build-arg.

### CORS

- **core-api:** `Cors:AllowedOrigins` en su `appsettings.json` / variable
  `Cors__AllowedOrigins` (incluye `http://localhost:4001` para dev).
- **gateway:** `Cors__AllowedOrigins__0` en el compose (= `CORS_ORIGIN` del `.env`).
- El origen permitido debe ser la URL desde la que sirve el **frontend**.

### Health checks (para verificar conectividad)

| Servicio | Endpoint |
|----------|----------|
| core-api | `/api/v1/health` |
| gateway  | `/health` (y `/ready`, que valida que core-api responda) |
| python-ml| `/health` |

---

## 4. Archivos `appsettings*.json` (configuración de los servicios .NET)

`core-api` (Flit.Api) y `gateway` (Flit.Gateway) son apps ASP.NET Core: su
configuración se arma por **capas que se sobreescriben en orden**. La última capa
que define una clave gana.

### Orden de precedencia (de menor a mayor prioridad)

1. **`appsettings.json`** — base, **siempre** se carga. Valores por defecto.
2. **`appsettings.{Environment}.json`** — se carga según `ASPNETCORE_ENVIRONMENT`
   (`Development`, `QA`, `Production`). Sobreescribe al base.
3. **Variables de entorno** — máxima prioridad. La notación con doble guion bajo
   mapea la jerarquía del JSON: `ConnectionStrings__Core`,
   `ReverseProxy__Clusters__core-api-cluster__Destinations__core-api-1__Address`,
   `Cors__AllowedOrigins__0`, etc.

> En **local** el entorno lo fija `launchSettings.json` (`ASPNETCORE_ENVIRONMENT=Development`).
> En **Docker/DEV** lo fija el `environment:` del compose. Las variables del compose
> son la capa 3 y por eso mandan sobre el JSON.

### Inventario por servicio

**core-api (`services/core-api/src/Flit.Api/`)**

| Archivo | Entorno | Qué define |
|---------|---------|------------|
| [appsettings.json](../services/core-api/src/Flit.Api/appsettings.json) | base | `ConnectionStrings.Core` (`localhost:5432`), `Cors`, `Smtp`, `Serilog`, feature flags |
| [appsettings.QA.json](../services/core-api/src/Flit.Api/appsettings.QA.json) | QA | BD `flit_qa`, CORS `qa.flitsas.com` |
| `appsettings.Development.json` *(no versionado, ver [.example](../services/core-api/src/Flit.Api/appsettings.Development.json.example))* | local | BD local, CORS `localhost:4001`, `Jwt.DevGenerate`, MinIO, SMTP de dev |

**gateway (`services/core-api/src/Flit.Gateway/`)**

| Archivo | Entorno | Qué define |
|---------|---------|------------|
| [appsettings.json](../services/core-api/src/Flit.Gateway/appsettings.json) | base | **Rutas y clusters YARP**, `Cors`, `Jwt`, `RateLimit`. Destinos por **nombre de servicio Docker**: `http://core-api:4003/`, `http://python-ml:4012/` |
| [appsettings.QA.json](../services/core-api/src/Flit.Gateway/appsettings.QA.json) | QA | CORS `localhost:5001`, destinos `localhost:5003` / `localhost:5012` (esquema 5xxx de QA) |
| `appsettings.Development.json` *(no versionado, ver [.example](../services/core-api/src/Flit.Gateway/appsettings.Development.json.example))* | local | CORS `localhost:4001`, destinos `localhost:4003` / `localhost:4012` |

### Detalles que importan

- **Los puertos de los destinos YARP dependen del entorno:**
  - **base (Docker)** → `core-api:4003`, `python-ml:4012` (DNS interno de Docker; solo
    resuelve dentro de la red del compose).
  - **local** → `localhost:4003`, `localhost:4012` (vía `appsettings.Development.json`).
  - **QA** → `localhost:5003`, `localhost:5012`.
- **Para correr el gateway en local DEBES** copiar el `.example` a
  `appsettings.Development.json`; si no, hereda los hostnames Docker del base
  (`core-api`/`python-ml`) que no resuelven fuera de contenedores.
- **En DEV/QA/PDN (VPS)** el compose corre con `ASPNETCORE_ENVIRONMENT=Development`,
  pero el archivo `appsettings.Development.json` **no se incluye en la imagen** (está
  gitignored). El gateway parte del **base** y las variables
  `ReverseProxy__...__Address` del compose **lo sobreescriben con el puerto del
  ambiente** (`core-api:${CORE_API_PORT}` / `python-ml:${PYTHON_ML_PORT}`). Por eso no
  hay que tocar el JSON al cambiar de ambiente: basta el número de puerto que inyecta el CD.

---

## 5. Comunicación detallada Front → Gateway → Core

### 5.1 Front → Gateway

1. El **frontend (Next.js)** nunca habla con `core-api` ni `python-ml` directamente:
   construye todas sus llamadas contra la **URL pública del gateway**.
2. Esa URL base se inyecta como variable de entorno/build (`NEXT_PUBLIC_*`):
   - **local / local-docker:** `http://localhost:4002` (gateway local) — o
     `http://localhost:4003` en el stack mínimo de Docker, que no trae gateway.
   - **DEV (VPS):** el dominio público del gateway, p. ej. `https://api.<dominio>/api/v1`
     (se pasa como build-arg en el CD).
3. El navegador hace, por ejemplo, `GET https://api.<dominio>/api/v1/usuarios`.
   El reverse-proxy del VPS termina TLS y entrega a `127.0.0.1:4002` (gateway).
4. **CORS:** el origen del frontend debe estar permitido en el gateway
   (`Cors__AllowedOrigins__0` ← `CORS_ORIGIN`) y también en core-api
   (`Cors:AllowedOrigins`). Si no, el navegador bloquea la respuesta.
5. **Auth (cuando esté activo):** el front envía `Authorization: Bearer <jwt>`. El
   gateway valida la firma con su llave pública. *Hoy el login está deshabilitado*
   (`Jwt.DevGenerate=true` y sin llave), así que el gateway no exige token.

### 5.2 Gateway → Core (enrutamiento YARP)

El gateway es un **reverse-proxy YARP**. No tiene lógica de negocio: hace *match* por
ruta (definido en `appsettings.json → ReverseProxy.Routes`) y reenvía al cluster
correspondiente. Reglas actuales:

| Ruta entrante | Cluster destino | Transformación | Política |
|---------------|-----------------|----------------|----------|
| `/api/v1/auth/**` | core-api `:4003` | — | **pública** (sin auth) |
| `/api/public/idsecure/**` | core-api `:4003` | reescribe a `/public/idsecure/**` | pública |
| `/api/**` | core-api `:4003` | passthrough `/api/**` | `JwtRequired` |
| `/hubs/**` | core-api `:4003` | — (WebSocket/SignalR) | `JwtRequired` |
| `/ml/**` | python-ml `:4012` | quita el prefijo `/ml` → `/**` | `JwtRequired` |

> Las rutas más específicas (`/api/v1/auth`, `/api/public/idsecure`) se evalúan antes
> que la genérica `/api/**`, por eso los endpoints públicos no caen bajo `JwtRequired`.

Flujo de una petición autenticada típica:

```
Front  ──GET /api/v1/usuarios──►  Gateway(:4002)
                                    │  match "/api/**" → core-api-cluster
                                    │  destino: http://core-api:4003/  (Docker)
                                    ▼
                                  core-api(:4003)
                                    │  ejecuta la lógica + EF Core
                                    ▼
                                  Postgres(:5432)
                                    │
   Front  ◄────── respuesta ◄────── Gateway ◄────── core-api
```

- El **hostname y puerto del destino** salen del cluster YARP y cambian por entorno
  (ver §4): `core-api:4003` en Docker, `localhost:4003` en local, `localhost:5003` en QA.
- El gateway aplica además **rate limiting** (`RateLimit` en su appsettings) y CORS
  antes de reenviar.
- `/ml/**` es el único que sale a **python-ml**; el resto va a **core-api**.
- El endpoint `/ready` del gateway hace un *health probe* contra
  `core-api:4003/health` para reportar si el backend está disponible.

---

## 6. Despliegue en el VPS (DEV): `.env`, compose y pasos

En el VPS, dentro del **deploy path** (`HOSTINGER_DEPLOY_PATH`) conviven dos archivos:

| Archivo | Cómo llega | Persistencia |
|---------|------------|--------------|
| `docker-compose.prod.yml` | Lo **copia el CD** en cada deploy (`scp`) | Se sobreescribe en cada deploy |
| `.env` | Se **crea a mano una vez** en el VPS (el CD **no** lo toca) | Permanece entre deploys |

> Clave: el CD nunca sube el `.env`. Si falta o tiene un valor malo, los contenedores
> arrancan con variables vacías y fallan (BD, CORS, Verifik). El `.env` es responsabilidad
> del operador del servidor.

### 6.1 Cómo configurar el `.env` (junto al compose)

Basado en la plantilla [.env.prod.example](../.env.prod.example). Variables a tener en cuenta:

| Variable | Obligatoria | Consideraciones |
|----------|:-----------:|-----------------|
| `CONNECTION_STRING_CORE` | ✅ | Formato **Npgsql .NET** (no la URL `postgres://`). Host = `host.docker.internal` (Postgres corre en el HOST, no en el compose). El `Username`/`Password`/`Database` deben existir en ese Postgres. `SSL Mode=Disable` si es conexión local sin TLS. |
| `CORS_ORIGIN` | ✅ | URL pública **exacta** del frontend (esquema + host, **sin** barra final), p. ej. `https://dev.flitsas.online`. Si no coincide con el origen real, el navegador bloquea las respuestas. |
| `VERIFIK_API_TOKEN` | ✅ | Token Verifik (RUNT). Secreto. |
| `VERIFIK_BASE_URL` | ⛔ opc. | Default `https://api.verifik.co`. |
| `VERIFIK_TIMEOUT_SECONDS` | ⛔ opc. | Default `30`. |
| `FRONTEND_PORT` / `GATEWAY_PORT` / `CORE_API_PORT` / `PYTHON_ML_PORT` | ⛔ opc.* | Puertos del ambiente (DEV 40xx / QA 50xx / PDN 60xx). *El **CD los inyecta** automáticamente; defínelos en el `.env` solo para operación manual. Defaults DEV. |
| `CORE_API_TAG` / `FRONTEND_TAG` / `PYTHON_ML_TAG` | ⛔ opc.* | Default `latest`. El **CD los inyecta**: tag móvil (`dev`/`qa`) en DEV/QA y `sha-<commit>` inmutable en PDN. **Para rollback manual**, fija el `sha-<commit>` deseado. |
| `COMPOSE_PROJECT_NAME` | ⛔ opc. | El compose ya fija `name: flitdev`. Solo defínela si necesitas aislar varias instancias en el mismo VPS (evita que `up --remove-orphans` borre contenedores de otro stack). |

Consideraciones del archivo en sí:
- Debe llamarse exactamente **`.env`** y estar en el **mismo directorio** que el compose
  (Docker Compose lo carga automáticamente desde ahí).
- Permisos restrictivos: `chmod 600 .env` (contiene secretos).
- **Nunca** se commitea. Solo se versiona el `.example`.

### 6.2 Consideraciones del `docker-compose.prod.yml` en el VPS

1. **Postgres vive en el HOST**, no en el compose. El host debe:
   - Aceptar conexiones desde la red de Docker (`listen_addresses` y una regla en
     `pg_hba.conf` para la subred del bridge de Docker).
   - Tener creados el usuario, password y base que usa `CONNECTION_STRING_CORE`.
   - El compose ya resuelve el host con `extra_hosts: host.docker.internal:host-gateway`.
2. **Puertos solo en loopback** (`127.0.0.1:4001/4002/4003/4012`). No son accesibles
   desde fuera: hace falta un **reverse-proxy en el HOST** (Nginx/Caddy) que termine TLS
   y enrute cada dominio → su puerto local:
   - `frontend` → `127.0.0.1:4001`
   - `api.<dominio>` (gateway) → `127.0.0.1:4002`
   - `core.<dominio>` (core-api, opcional/debug) → `127.0.0.1:4003`
3. **Firewall:** abre solo `80/443` al público. Los puertos `4001–4012` quedan internos.
4. **Registry / autenticación:** todas las imágenes se publican y se consumen desde
   **GHCR** (`ghcr.io/flitsas/flitdev/*`), con el mismo nombre en el CD y en el compose.
   El VPS hace `docker login ghcr.io` con `GHCR_PAT` antes de `pull` (lo hace el CD; para
   un `pull` manual debes loguearte tú). `GHCR_PAT` necesita scope `read:packages`
   (y `write:packages` para el push del CD).
5. **python-ml** ahora es parte del stack: su imagen debe estar publicada (job
   `build-python-ml` del CD) o `docker compose pull` fallará.
6. **Health check del deploy:** el CD valida que `127.0.0.1:4001/` y
   `127.0.0.1:4002/health` respondan. Si cambias puertos, actualiza también esa
   verificación en [cd.yml](../.github/workflows/cd.yml).

### 6.3 Ambientes por rama (CD multi-entorno)

El CD ([cd.yml](../.github/workflows/cd.yml)) despliega a **3 ambientes según la rama**.
Un job `setup` resuelve el mapeo y los jobs siguientes lo consumen:

| Rama | Ambiente (`env_name`) | Front | API (gateway) | Tag con el que **despliega** |
|------|:---------------------:|-------|---------------|------------------------------|
| `develop` | `dev` | `dev.flitsas.online` | `api.dev.flitsas.online` | `dev` (móvil) |
| `staging` | `qa` | `qa.flitsas.online` | `api.qa.flitsas.online` | `qa` (móvil) |
| `release` | `pdn` | `pdn.flitsas.online` | `api.pdn.flitsas.online` | `sha-<commit>` (**inmutable**) |

- **Tags publicados vs. tag de deploy:** cada build publica **3 tags** — `sha-<commit>`
  (inmutable, formato largo), el tag móvil del ambiente (`dev`/`qa`/`pdn`) y `latest`
  (solo `release`). Lo que el VPS **despliega** lo decide el output `deploy_tag` del CD:
  - **DEV / QA** → tag móvil (`dev`/`qa`): siempre lo último de la rama.
  - **PDN** → `sha-<commit>` **inmutable**: sabes exactamente qué corre y el rollback es
    determinista (re-desplegar un commit anterior por su SHA).
- El deploy exporta `CORE_API_TAG`/`FRONTEND_TAG`/`PYTHON_ML_TAG` con ese `deploy_tag`
  (override del `.env` del VPS), así no hace falta fijar el tag a mano.
- La **URL del API** se inyecta como build-arg del frontend según el ambiente
  (`https://api.<env>.flitsas.online/api/v1`).
- El `CORS_ORIGIN` del `.env` de cada VPS debe ser el dominio del front de **ese**
  ambiente (p. ej. `https://qa.flitsas.online` en el VPS de QA).

> **Puertos por ambiente:** cada ambiente usa su propio rango (DEV 40xx, QA 50xx,
> PDN 60xx), inyectados por el CD. Esto permite incluso convivir en un mismo VPS sin
> colisión de puertos (cada stack escucha en su rango y se aísla con
> `COMPOSE_PROJECT_NAME`).

### 6.4 Secrets de GitHub

El job `deploy-to-vps` usa **GitHub Environments** para seleccionar la carpeta destino
del VPS según la rama. Los nombres de los Environments coinciden con la rama (no con el
`env_name` interno dev/qa/pdn): el workflow mapea `rama → Environment` con el output
`gh_env`.

| Rama (push) | `env_name` (tags/display) | GitHub Environment (secrets) |
|-------------|---------------------------|------------------------------|
| `develop`   | `dev`                     | `develop`                    |
| `staging`   | `qa`                      | `staging`                    |
| `release`   | `pdn`                     | `production`                 |

**Secrets de Repositorio** (*Settings → Secrets and variables → Actions → Repository
secrets*) — compartidos por todos los ambientes (un solo VPS):

| Nombre | Para qué |
|--------|----------|
| `HOSTINGER_SSH_HOST` / `HOSTINGER_SSH_USER` / `HOSTINGER_SSH_KEY` | Acceso SSH al VPS |
| `GHCR_PAT` | Login a GHCR en el VPS para `pull` (scope `read:packages`) |
| `GHCR_USERNAME` | Usuario GitHub **dueño del PAT** usado en `docker login` en el VPS (no usar `github.actor` — rompe deploys si mergea otro usuario). Secret de repositorio o variable `GHCR_USERNAME`. |

**Secrets de Environment** (*Settings → Environments → {develop, staging, production}*)
— específicos por ambiente:

| Nombre | Para qué |
|--------|----------|
| `HOSTINGER_DEPLOY_PATH` | Carpeta del VPS donde viven compose + `.env` de ESE ambiente |

> Si algún día separas en varios VPS (uno por ambiente), mueve también
> `HOSTINGER_SSH_*` a secrets de Environment para que cada rama apunte a su host.

> La URL del entorno en GitHub se arma sola desde el dominio del front (`front_domain`),
> ya no se usa la variable `PUBLIC_DOMAIN`.

### 6.5 Pasos de despliegue

**A) Preparación de CADA VPS (una sola vez por ambiente dev/qa/pdn):**
1. Instalar Docker + Compose plugin.
2. Crear el deploy path (`HOSTINGER_DEPLOY_PATH`).
3. Configurar Postgres del host (usuario/BD, `listen_addresses`, `pg_hba.conf`).
4. Configurar el reverse-proxy del host (Nginx/Caddy) con TLS para los dominios de ese
   ambiente (`<env>.flitsas.online` y `api.<env>.flitsas.online`) → puertos loopback.
5. `docker login ghcr.io` con el `GHCR_PAT`.
6. Crear el `.env` (§6.1) con el `CORS_ORIGIN` del front de ese ambiente y `chmod 600 .env`.

**B) Configurar GitHub (una sola vez):** crear los Environments `develop`/`staging`/
`production` con su secret `HOSTINGER_DEPLOY_PATH`, y los Repository secrets compartidos
(§6.4).

**C) Deploy continuo (automático):** un `push` a `develop`/`staging`/`release` dispara el
CD, que:
1. `setup` resuelve el ambiente según la rama.
2. Construye y publica las 3 imágenes con el tag del ambiente.
3. Selecciona el Environment correcto (sus secrets de VPS).
4. Copia `docker-compose.prod.yml` al VPS y hace `pull` + `up -d --remove-orphans`
   con el tag del ambiente.
5. Verifica health en `4001` y `4002`.

**D) Deploy / operación manual (en el VPS):**
```bash
cd "$HOSTINGER_DEPLOY_PATH"
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d --remove-orphans
docker compose -f docker-compose.prod.yml ps      # estado
docker compose -f docker-compose.prod.yml logs -f # logs
```

**E) Rollback:**
- **PDN** ya despliega por `sha-<commit>` inmutable. Para volver a una versión anterior,
  re-lanza el CD sobre el commit deseado (`workflow_dispatch` desde ese commit), **o**
  manualmente en el VPS fija el SHA previo y re-aplica:
  ```bash
  export CORE_API_TAG=sha-<commit-anterior>
  export FRONTEND_TAG=sha-<commit-anterior>
  export PYTHON_ML_TAG=sha-<commit-anterior>
  docker compose -f docker-compose.prod.yml pull && docker compose -f docker-compose.prod.yml up -d
  ```
  > Nota: un rollback manual dura hasta el siguiente deploy del CD, que volverá a pinear
  > el SHA del último commit de `release`. Para fijarlo de forma permanente, revierte el commit.
- **DEV/QA** usan tag móvil; el rollback es re-desplegar el commit/rama anterior.

---

## Apéndice — checklist al cambiar un puerto en DEV

1. Cambiar el mapeo en `ports:` del servicio.
2. Cambiar el puerto **interno** (`ASPNETCORE_URLS` / `PORT` / `--port`).
3. Ajustar el `healthcheck` del servicio al nuevo puerto.
4. Si es core-api o python-ml, actualizar el destino del YARP en el `gateway`:
   el **base** [Flit.Gateway/appsettings.json](../services/core-api/src/Flit.Gateway/appsettings.json)
   (`core-api:NNNN` / `python-ml:NNNN`) y, según entorno, los overrides
   `appsettings.Development.json` (local) y/o las variables `ReverseProxy__...__Address`
   del compose.
5. Validar: `docker compose -f docker-compose.prod.yml config`.

> **Pipeline:** el CD [.github/workflows/cd.yml](../.github/workflows/cd.yml)
> construye y publica los tres servicios (`build-core-api`, `build-frontend`,
> `build-python-ml`) y `deploy-to-vps` depende de los tres. Los nombres de imagen
> están unificados a `flitsas/flitdev/*` (sin guion) en el CD y en el compose, así
> que el `pull` del VPS coincide con lo que publica el pipeline.
