# Handoff de VPS: desplegar y encender la FLIT Suite

Epic #13217. Para el líder técnico (Jorman), que opera la VPS. Es el documento maestro: qué corre, en qué puertos,
qué variables lleva el `.env` (qué hace cada una y su valor en cada ambiente), qué cambia en nginx, qué DNS y
certificados hacen falta, en qué orden se enciende y cómo se vuelve atrás de cada paso. La identidad aparte
(`core-identity`) tiene su propio paso a paso en [handoff-vps-identidad.md](handoff-vps-identidad.md); aquí se dice
cuándo toca.

Reglas de siempre: **DEV primero**; QA y PDN solo con aprobación y cuando DEV lleve días estable. Nada de esto se
hace a mano en el compose de la VPS: el CD copia `docker-compose.prod.yml` en cada despliegue y borraría el cambio. Lo
que va a mano es el `.env`, nginx, DNS y certificados.

---

## 0. Paso a paso: antes y después de fusionar

La configuración de la VPS se parte en dos: lo que no cambia nada para los usuarios va **antes** de fusionar; lo que
enciende la suite va **después**, porque necesita el código desplegado. En DEV, **fusionar el PR en `develop` es
desplegar**: el CD arranca solo. Por eso la preparación tiene que estar terminada antes de fusionar.

### Antes de fusionar (no cambia nada para nadie)

- [ ] **1. Samuel** — Abrir el PR contra `develop`; revisión y CI en verde. **No fusionar todavía.**
- [ ] **2. Jorman** — Fase 0 (§8): copia del `.env`, bloque «Lo que exporta el CD» (§4.2), `FLIT_SESSION_SECRET`
  nueva, `FLIT_INTERNAL_API_KEY` con valor, `docker compose -f docker-compose.prod.yml config` sin errores. Las
  banderas de la suite siguen apagadas.
- [ ] **3. Jorman** — Respaldo de la base de DEV (`pg_dump`).
- [ ] **4. Jorman** — Fase 2 (§5, §6.1): DNS y certificado de `dev.tramites.flitsas.online` y su server{} en nginx.
  Sin efecto hasta que el hub redirija; se hace antes porque el DNS y el certificado pueden tardar.
- [ ] **5. Jorman** — Avisar a Samuel que 2, 3 y 4 están listos.
- [ ] **6. Samuel** — Avisar al equipo y a los usuarios de DEV: tras el despliegue inician sesión una vez más, y el
  enlace de recuperación de contraseña pasa a `/auth/reset-password`.

### Fusionar (el CD despliega con todo apagado)

- [ ] **7. Samuel** — Fusionar el PR. El CD despliega la fase 1 (§8): Trámites sigue igual, con su login de siempre.
- [ ] **8. Samuel y Jorman** — Comprobar la fase 1: CD en verde, todo `healthy`, migraciones aplicadas, y en el
  navegador login, trámites, reportes, usuarios y roles, ICT y configuración de compañía. Si algo falla aquí es del
  código, no de la configuración: vuelta atrás de la fase 1.

### Después de fusionar (enciende la suite)

- [ ] **9. Jorman** — Fase 3 (§8), en horario de bajo uso: variables de la suite, `docker compose up -d`, raíz de
  nginx al hub. Necesita la imagen del hub, que solo existe después del paso 7.
- [ ] **10. Samuel y Jorman** — Comprobar la fase 3 (§6.3, §7.2 y la lista de §8). Si algo falla: vuelta atrás de la
  fase 3, en minutos.
- [ ] **11. Jorman** — Con la fase 3 estable, fase 4: identidad aparte ([handoff-vps-identidad.md](handoff-vps-identidad.md)).
- [ ] **12. Jorman** — Fase 5: `FLIT_DEPLOY_ROLLING`, medidas de recursos para un PR, monitoreo.

### QA y PDN

El mismo orden por ambiente, con una diferencia: el despliegue no lo dispara el PR sino el paso de rama
(`develop` → `staging` para QA, `staging` → `release` para PDN).

- [ ] **13.** DEV encendido y estable varios días antes de pasar a QA.
- [ ] **14. Jorman** — Pasos 2 a 5 con los valores de QA, **antes** del paso de rama a `staging`.
- [ ] **15.** Paso de rama a `staging` y pasos 8 a 12 en QA.
- [ ] **16.** PDN: lo mismo con los valores de PDN, ventana coordinada con el negocio y respaldo verificado antes del
  paso a `release`.

**Lo que nunca va antes de desplegar:** `FLIT_OIDC_ENABLED=true`, `COMPOSE_PROFILES=suite`, `FLIT_SESSION_MODE=oidc`,
`FLIT_TRAMITES_HOST_ENABLED=true`, `FLIT_IDENTITY_CLUSTER_ENABLED=true` ni la raíz de nginx al hub. Sin el código
nuevo, el hub y las tablas de login no existen y el ambiente se queda sin login.

## 1. Resumen en una página

**Qué trae la suite.** Un **hub** (`frontend-hub`) que pasa a ocupar la raíz de cada ambiente (`dev.flitsas.online`,
`qa.flitsas.online`, `flitsas.online`): portada, login único (OIDC) e inicio con los productos de la empresa.
**Trámites** se muda a su propio host (`dev.tramites.flitsas.online`…) y entra con la sesión del hub sin pedir
contraseña. **Comparendos** y **Diagnóstico** aparecen en el menú como «Próximamente»: todavía no tienen app ni
necesitan nada en la VPS. Opcionalmente, el login pasa a un servicio aparte (`core-identity`) que sigue en pie aunque
`core-api` se caiga.

**Qué hay que hacer, en orden** (detalle en §8):

| Fase | Qué | Efecto para los usuarios | ¿Se puede volver atrás? |
|---|---|---|---|
| 0 | Preparar el `.env` (variables de §4.2 y §4.3), respaldo, aviso a usuarios | Ninguno | — |
| 1 | Fusionar la rama y que el CD despliegue con **todo apagado** | **Inician sesión una vez más** (los tokens viejos dejan de valer, §4.4) | Sí, una variable |
| 2 | DNS y certificado de `<ambiente>.tramites.flitsas.online` | Ninguno | — |
| 3 | Encender el hub: OIDC, perfil `suite`, Trámites en su host, nginx | **Inician sesión una vez más**, ahora en el hub | Sí, en minutos |
| 4 | Identidad aparte (`core-identity`) | Ninguno | Sí, una variable |
| 5 | Despliegue por servicio, límites de recursos, monitoreo | Ninguno | Sí |

Las fases 1 y 3 hacen iniciar sesión otra vez. Si se hacen en la misma ventana, los usuarios lo notan una sola vez.

**Lo que no puede faltar** (si falta, algo se rompe sin avisar):

1. `FLIT_SUITE_ENV` en el `.env` con el valor del ambiente (`dev`, `qa`, vacía en PDN). El compose ya no arranca sin
   ella. Si tuviera un valor equivocado, el login se rompe en ese ambiente (§7.2).
2. `FLIT_INTERNAL_API_KEY` con valor (no vacía) y `FLIT_SESSION_SECRET` de 32+ caracteres.
3. nginx: pasar `Host` y `X-Forwarded-Proto` tal cual; **no** añadir `X-Frame-Options` ni CSP propia en los hosts del
   hub y de los productos; **no** dejar una `location /api/v1/` directa al gateway en esos hosts (§6).
4. Certificado propio para `dev.tramites.flitsas.online` y `qa.tramites.flitsas.online`: un comodín
   `*.flitsas.online` **no** los cubre (§5).

---

## 2. Qué corre

Todo vive en `docker-compose.prod.yml` (proyecto `flitdev`), un stack por ambiente. Los puertos se publican **solo en
`127.0.0.1`**: el único que expone hacia afuera es nginx. Postgres corre en el host (`host.docker.internal:5432`).

| Servicio | Imagen (`ghcr.io/flitsas/flitdev/…`) | Perfil de compose | Qué hace | Nuevo |
|---|---|---|---|---|
| `core-api` | `core-api` | siempre | La API. **Es el único que migra la base** al arrancar | cambia |
| `core-identity` | `core-identity` | `identity` | Solo login/OIDC, mismo código y variables que core-api; no migra | **sí** |
| `gateway` | `core-api` (otro binario) | siempre | YARP: único punto de entrada a la API; sella el dominio (`X-Flit-Domain`) | cambia |
| `frontend` | `frontend` | siempre | Trámites (Next.js) | cambia |
| `frontend-hub` | `frontend-hub` | `suite` | Hub: portada, login, inicio, proxy de `/connect`, `/.well-known` y `/api/v1` | **sí** |
| `core-ict` | `core-ict` | siempre | ICT, sin cambios de la suite | — |
| `migracion-api`, `migrador` | `core-api` | siempre / `migracion` | Migración V1→V2, sin cambios | — |
| `python-ml` | `python-ml` | siempre | Sin cambios | — |

Camino de una petición con la suite encendida:

```
navegador ──https──▶ nginx ──▶ frontend-hub (raíz)  ──┐
                          └──▶ frontend (tramites.)  ──┤ /api/v1, /connect, /.well-known (red interna, con sello y Bearer)
                                                       ▼
                         api.<amb>.flitsas.online ──▶ gateway ──▶ core-api
                                                             └──▶ core-identity (login, si FLIT_IDENTITY_CLUSTER_ENABLED)
```

El hub y Trámites llaman a la API **por dentro** (contenedor → `gateway`), no por nginx. `api.<ambiente>` sigue
igual que hoy para integradores y el portal ICT.

---

## 3. Puertos

Todos en `127.0.0.1` del VPS (puerto del host = puerto interno). Los exporta el CD por rama; con el bloque de §4.2
quedan también en el `.env`.

| Servicio | Variable | DEV (`develop`) | QA (`staging`) | PDN (`release`) |
|---|---|---|---|---|
| Trámites (`frontend`) | `FRONTEND_PORT` | 4001 | 5001 | 6001 |
| `gateway` | `GATEWAY_PORT` | 4002 | 5002 | 6002 |
| `core-api` | `CORE_API_PORT` | 4003 | 5003 | 6003 |
| **`core-identity`** | `CORE_IDENTITY_PORT` | **4025** | **5025** | **6025** |
| `python-ml` | `PYTHON_ML_PORT` | 4012 | 5012 | 6012 |
| `core-ict` | `CORE_ICT_PORT` | 4020 | el que ya tenga el `.env` | el que ya tenga el `.env` |
| `migracion-api` (no publicado) | `MIGRACION_API_PORT` | 4030 | 5030 | 6030 |
| **Hub (`frontend-hub`)** | `HUB_PORT` | **4022** | **5022** | **6022** |
| gRPC interno de core-api (no publicado) | `CORE_API_GRPC_PORT` | 8082 | 8082 | 8082 |
| gRPC interno de core-identity (no publicado, Epic #13316) | `CORE_IDENTITY_GRPC_PORT` | vacío = apagado; propuesto 8083 | igual | igual |
| core-consultas REST (no publicado, perfil `consultas`) | `CORE_CONSULTAS_PORT` | 4026 (propuesto) | igual | igual |
| core-consultas gRPC (no publicado) | `CORE_CONSULTAS_GRPC_PORT` | 8084 (propuesto) | igual | igual |
| Reservados, sin servicio todavía | — | Comparendos 4023; Diagnóstico 4024 | 5023; 5024 | 6023; 6024 |

Notas:
- `CORE_ICT_PORT` no lo exporta el CD (ya era así antes de la suite). En QA y PDN, **no lo cambies**: deja el valor que
  ya tenga el `.env`. Si no está, hoy corre en 4020 (`docker ps` lo confirma).
- Red interna por stack (`FLIT_INTERNAL_SUBNET`): DEV `10.114.40.0/24`, QA `10.114.50.0/24`, PDN `10.114.60.0/24`.
  Son los que usa el CD; `172.28.x`, que aparecía en documentos viejos, ya estaba ocupado en la VPS.

---

## 4. Variables de entorno

Dónde va cada una:
- **`.env`**: el archivo junto al compose, en `$HOSTINGER_DEPLOY_PATH`. El CD **no** lo toca.
- **CD**: el CD la exporta solo durante su despliegue (sobrescribe la del `.env` en ese momento). Para que un
  `docker compose` hecho a mano use el mismo valor, también va en el `.env` (§4.2).
- **GitHub**: variable o secreto del Environment (`develop`, `staging`, `production`).

Ningún valor secreto va en este documento. Plantilla completa y comentada: [`.env.prod.example`](../../.env.prod.example).

### 4.1 Las variables de la suite

| Variable | Dónde | Qué hace | DEV | QA | PDN | Secreto |
|---|---|---|---|---|---|---|
| `FLIT_SUITE_ENV` | CD + `.env` | Prefijo de ambiente de los hosts de la suite: `[env.]<producto>.flitsas.online`. Con él la API arma el menú de productos, el emisor OIDC y **las direcciones de retorno del login de cada producto**. **Obligatoria**: el compose no arranca sin ella | `dev` | `qa` | vacía (`FLIT_SUITE_ENV=`) | no |
| `FLIT_HUB_URL` | CD + `.env` | URL pública del hub (emisor OIDC). Trámites y el hub mandan ahí a iniciar sesión | `https://dev.flitsas.online` | `https://qa.flitsas.online` | `https://flitsas.online` | no |
| `FLIT_TRAMITES_URL` | CD + `.env` | URL pública de Trámites en su host; el hub redirige ahí (308) cuando `FLIT_TRAMITES_HOST_ENABLED=true` | `https://dev.tramites.flitsas.online` | `https://qa.tramites.flitsas.online` | `https://tramites.flitsas.online` | no |
| `HUB_PORT` | CD + `.env` | Puerto del hub | 4022 | 5022 | 6022 | no |
| `CORE_IDENTITY_PORT` | CD + `.env` | Puerto de core-identity (también lo usa el gateway para alcanzarlo) | 4025 | 5025 | 6025 | no |
| `FRONTEND_HUB_TAG`, `CORE_IDENTITY_TAG` | CD + `.env` | Tag de imagen. Sin valor caen en `latest`, que **solo publica PDN** | `dev` | `qa` | `sha-<commit>` desplegado | no |
| `FLIT_SESSION_SECRET` | `.env` | Cifra la cookie de sesión del hub y de cada producto. Mínimo 32 caracteres; si es más corta o falta, el hub (y Trámites en modo `oidc`) responden error 500 en todo lo que use la sesión. Distinta por ambiente. Cambiarla cierra todas las sesiones | `openssl rand -base64 48` | otra | otra | **sí** |
| `FLIT_INTERNAL_API_KEY` | `.env` (ya existía) | Llave que el gateway exige para creer el dominio que le mandan el hub y Trámites desde la red interna. **Vacía, la suite no sabe en qué dominio está** (Marca Blanca y el producto se deducen mal) | ya definida | ya definida | ya definida | **sí** |
| `FLIT_OIDC_ENABLED` | `.env` | Enciende el servidor de login de la suite (`/connect/*`, `/.well-known/openid-configuration`) y registra los clientes de login al arrancar. Apagado: 404 y el login de siempre no cambia | `false` → `true` en la fase 3 | igual | igual | no |
| `COMPOSE_PROFILES` | `.env` | `suite` levanta el hub; `identity` levanta core-identity. Se combinan: `suite,identity` | vacío → `suite` (fase 3) → `suite,identity` (fase 4) | igual | igual | no |
| `FLIT_SESSION_MODE` | `.env` | Trámites: `legacy` (login de siempre) u `oidc` (sesión del hub; las llamadas a la API pasan por el servidor de Trámites con el token) | `legacy` → `oidc` (fase 3) | igual | igual | no |
| `FLIT_TRAMITES_HOST_ENABLED` | `.env` | Hub: `true` = redirige (308) a `FLIT_TRAMITES_URL` toda ruta que no sea del hub, así funcionan los enlaces viejos (`/tramites/…`, `/admin`…) | `false` → `true` (fase 3) | igual | igual | no |
| `FLIT_IDENTITY_CLUSTER_ENABLED` | `.env` | Gateway: el login y OIDC van a core-identity, con core-api de respaldo automático | `false` → `true` (fase 4) | igual | igual | no |
| `JWT_PERSIST_SIGNING_KEY` | `.env` | La llave que firma los tokens se guarda cifrada en la base (antes era nueva en cada arranque). Dejar en `true` | `true` (default) | igual | igual | no |
| `JWT_VALIDATE_ISSUED_TOKENS` | `.env` | La API valida firma, emisor, audiencia y vencimiento de cada token. Ver §4.4 | `true` (default) | igual | igual | no |
| `FLIT_HOSTS` | `.env`, opcional | Qué hosts son de FLIT para el hub (el resto se trata como Marca Blanca). El default del compose sirve; no ponerla salvo un dominio nuevo de FLIT | default | default | default | no |

### 4.2 Lo que exporta el CD: copiarlo al `.env`

El CD exporta estos valores **solo durante su despliegue**. El handoff de identidad pide comandos a mano
(`docker compose up -d --no-deps …`): sin estas líneas en el `.env`, el compose usaría los de DEV (puertos 40xx,
subred, URLs) o `latest`. Valores para **DEV**:

```bash
# ── Lo que exporta el CD (mismo valor aquí) ──
FLIT_SUITE_ENV=dev
FLIT_HUB_URL=https://dev.flitsas.online
FLIT_TRAMITES_URL=https://dev.tramites.flitsas.online
FLIT_INTERNAL_SUBNET=10.114.40.0/24
FRONTEND_PORT=4001
GATEWAY_PORT=4002
CORE_API_PORT=4003
CORE_IDENTITY_PORT=4025
PYTHON_ML_PORT=4012
MIGRACION_API_PORT=4030
HUB_PORT=4022
CORE_API_TAG=dev
CORE_IDENTITY_TAG=dev
CORE_ICT_TAG=dev
FRONTEND_TAG=dev
FRONTEND_HUB_TAG=dev
PYTHON_ML_TAG=dev
```

- **QA**: `FLIT_SUITE_ENV=qa`, URLs con `qa.`, subred `10.114.50.0/24`, puertos 50xx, tags `qa`.
- **PDN**: `FLIT_SUITE_ENV=` (vacía, pero **presente**), `https://flitsas.online`, `https://tramites.flitsas.online`,
  subred `10.114.60.0/24`, puertos 60xx, tags `sha-<commit>` del último despliegue (`docker ps` los muestra). En PDN,
  después de cada despliegue hay que actualizar los tags en el `.env` antes de un comando a mano, o se baja otra
  versión.

### 4.3 Variables que ya existían y cambian de significado

| Variable | Qué cambia | Qué hacer |
|---|---|---|
| `CORS_ORIGIN` (obligatoria) | Sigue siendo la URL pública de la raíz del ambiente (sin `/` final). Con el hub en la raíz, los enlaces de los correos (activar invitación, recuperar contraseña, imágenes) los atiende el hub. El enlace de recuperación pasa a `<CORS_ORIGIN>/auth/reset-password` (antes `/reset-password`, que daba 404; el hub redirige la ruta vieja). Ahora también la lee core-identity | Nada: el valor no cambia (`https://dev.flitsas.online`, `https://qa.flitsas.online`, `https://flitsas.online`) |
| `FLIT_INTERNAL_API_KEY` | Ahora también la necesita el hub | Confirmar que no está vacía |
| `FLIT_INTERNAL_SUBNET` | La usa también el hub para el sello de dominio | Poner el del CD (§3) |
| `Jwt__DevGenerate` | Se eliminó del compose. La llave ya no se genera nueva en cada arranque | Nada |

### 4.4 Inicio de sesión forzado en el primer despliegue

Hoy (develop) la API genera una llave nueva en cada arranque y **no valida** los tokens que recibe. Con la rama,
`JWT_PERSIST_SIGNING_KEY=true` y `JWT_VALIDATE_ISSUED_TOKENS=true` vienen encendidas por defecto: la API rechaza
tokens sin firma válida, vencidos o de otro emisor. Consecuencias:
- En el primer despliegue, **todos los usuarios inician sesión una vez más** (sus tokens se firmaron con una llave que
  ya no existe).
- La sesión dura lo que diga el token (12 h): antes, un token vencido seguía sirviendo.
- Si algo saliera mal: `JWT_VALIDATE_ISSUED_TOKENS=false` en el `.env` y `docker compose -f docker-compose.prod.yml up
  -d --no-deps core-api` vuelve al comportamiento anterior sin redesplegar.

### 4.5 Seguridad: valores por defecto que conviene revisar en QA y PDN

No cambian con la suite (hoy todo corre como `Development`), pero ahora tienen variable para apagarlos:

| Variable | Default | Recomendado en QA/PDN | Por qué |
|---|---|---|---|
| `FLIT_SEED_DEMO_USERS` | `true` | `false` | Crea cuentas demo con contraseña fija; con el login del hub siguen siendo válidas |
| `FLIT_SWAGGER_ENABLED` | `true` | `false` en PDN | `/swagger` público en core-api y core-ict |
| `SMTP_USE_CONSOLE_WHEN_NO_HOST` | `true` | `false` | Sin `SMTP_HOST`, el correo «sale» por consola en vez de fallar |
| `FLIT_DOTNET_ENVIRONMENT` | `Development` | dejar como está en esta entrega | Cambiarlo afecta varias cosas a la vez (ver `docs/suite/frentes/a-inventario-ambientes.md`); va por separado |

### 4.6 GitHub

| Nombre | Tipo | Qué hace | Cuándo |
|---|---|---|---|
| `FLIT_DEPLOY_ROLLING` | Variable del Environment | `true`: el CD despliega servicio por servicio, sin `down` de todo el stack. Desde la HU #13329 solo recrea los contenedores cuya imagen cambió (despliega `tag@digest`) y, si falla el build de un servicio, despliega los demás y ese conserva su versión (el CD termina en rojo para que se vea) | Fase 5 |
| `HOSTINGER_SSH_*`, `HOSTINGER_DEPLOY_PATH`, `GHCR_PAT`, `GHCR_USERNAME` | Secretos | Sin cambios | — |

Las imágenes nuevas (`core-identity`, `frontend-hub`) las construye y publica el mismo CD. Se construyen por primera
vez cuando la rama se fusiona en `develop`.

### 4.7 Lo demás

Las demás variables (Verifik, Kyverum, Fasecolda, SMTP, Renting, ICT, migrador, Anthropic, file-manager) **no
cambian** con la suite. core-identity las recibe todas por el mismo bloque del compose que core-api: no hay que
duplicar nada.

### 4.8 ICT: del secreto HMAC al token de Identidad (Epic #13316, HU #13335)

Hoy core-ict firma un JWT con `ICT_SERVICE_TOKEN_SECRET`, el mismo secreto que tiene core-api. Pasa a pedir su token a
Identidad (cliente `svc-ict`, scope `platform.tramites.ict`). Sin tocar nada, todo sigue como hoy. El paso, por ambiente
y en este orden:

1. `SVC_ICT_CLIENT_SECRET` en el `.env` (ver §4 de clientes de servicio) y recrear core-api y core-identity.
2. `ICT_SERVICE_TOKEN_ACCEPT_IDENTITY=true` y recrear core-api: acepta los dos tokens.
3. `ICT_SERVICE_TOKEN_USE_IDENTITY=true` y recrear core-ict. Pide el token en `ICT_IDENTITY_TOKEN_ENDPOINT` (por
   defecto el gateway, `http://gateway:<GATEWAY_PORT>/connect/token`). Si falta el secreto, core-ict no arranca y lo
   dice en el log.
4. Comprobar que los trámites de ICT siguen entrando (log de core-ict sin `Unauthenticated`).
5. El corte: `ICT_SERVICE_TOKEN_ACCEPT_LEGACY=false` y recrear core-api. Desde ahí el token HMAC se rechaza.

Volver atrás: en orden inverso. El esquema HMAC se retira del código cuando el corte esté verificado en PDN. El
reflejo de estado core-api → core-ict (`Ict:StateCallback`) sigue con el secreto HMAC: no es parte de esta HU.

### 4.9 Consultas y bus de eventos: obligatorios desde el corte (Epic #13316, HUs #13343-#13351 y #13348)

Con el corte (HU #13348) core-api ya no tiene proveedores ni sus secretos: toda consulta (RUNT, SIMIT, RNMC, RUES),
avalúo, impronta, certificado RUES, RUNT de la Confirmación RUNT y validación de identidad de Kyverum va a
core-consultas por gRPC, **sin respaldo en proceso**. Los avisos de Kyverum vuelven a Trámites por el bus, así que
RabbitMQ y el bus de Trámites también son obligatorios. ICT consulta directo a core-consultas con su token de
Identidad. En el compose ya no hay perfiles `bus` ni `consultas` (arrancan siempre) ni banderas para encenderlos.

**Antes de desplegar la rama, por ambiente, en este orden** (si falta algo, `docker compose` o el servicio no arranca
y dice qué):

1. **Broker:** `RABBITMQ_ADMIN_USER` y `RABBITMQ_ADMIN_PASSWORD` en el `.env`. El vhost `flit` y los exchanges salen de
   `deploy/rabbitmq/definitions.json` al arrancar (`deploy/rabbitmq/README.md`).
2. **Base de Consultas:** usuario dueño del esquema `consultas` (`deploy/postgres/README.md`):
   `psql "$ADMIN_URL" -v servicio=consultas -v conexiones=20 -v clave="$CLAVE" -f deploy/postgres/servicio-con-esquema-propio.sql`
   y con eso `CONNECTION_STRING_CONSULTAS` (usuario `flit_consultas`) en el `.env`.
3. **Secretos de clientes de servicio**, uno distinto por ambiente: `SVC_TRAMITES_CLIENT_SECRET` (obligatoria: con él
   core-api llama a Consultas), `SVC_CONSULTAS_CLIENT_SECRET` y `SVC_ICT_CLIENT_SECRET`.
4. **Secretos de proveedores:** los mismos nombres que ya tenía core-api (`VERIFIK_*`, `KYVERUM_*`, `KYVERUM_RUNT_*`,
   `FASECOLDA_*`); ahora solo los lee core-consultas. Si a uno en modo real le falta su secreto, core-consultas no
   arranca y lo dice en el log. El certificado RUES sigue opcional (`RUES_ENABLED`, `RUES_BASE_URL`, `RUES_API_KEY`);
   sin él, Trámites cae a la carga manual como hoy.
5. **Aviso de Kyverum:** `CONSULTAS_KYVERUM_WEBHOOK_CALLBACK_URL=https://<host del ambiente>/api/v1/consultas/avisos/kyverum-verify`.
   El gateway ya enruta `/api/v1/consultas/avisos/*` a core-consultas; si nginx filtra rutas, abrir esa también.
6. **Usuarios del broker** (con el broker arriba; si es el primer despliegue con broker, levantarlo antes con
   `docker compose up -d rabbitmq`): `deploy/rabbitmq/usuario-de-servicio.sh tramites "$CLAVE_TRAMITES"` y
   `deploy/rabbitmq/usuario-de-servicio.sh consultas "$CLAVE_CONSULTAS"`; las cadenas van a `RABBITMQ_URL_TRAMITES`
   (obligatoria) y `RABBITMQ_URL_CONSULTAS`. Cada usuario escribe solo en su exchange.

**Desplegar** (el CD construye core-consultas en `build-core-consultas`). core-consultas no publica puertos: REST
`CORE_CONSULTAS_PORT` (4026) y gRPC `CORE_CONSULTAS_GRPC_PORT` (8084) quedan en la red de Docker; el CD revisa su
`/health/ready` desde dentro del contenedor. Al arrancar aplica sus migraciones (solo su esquema).

**Apenas termine el despliegue:**

7. **Configuración por empresa:** `psql "$ADMIN_URL" -v ON_ERROR_STOP=1 -f deploy/postgres/migrar-configuracion-consultas.sql`.
   Hasta correrlo, las consultas usan la cadena global (`Consultations:DefaultChains`), no la de cada empresa.
8. **Solo DEV/QA:** `psql "$ADMIN_URL" -v ON_ERROR_STOP=1 -f deploy/postgres/migrar-valores-mock-avaluo.sql` (los
   valores de avalúo de prueba que se hayan agregado a mano; los de siempre ya los siembra la migración).

**Verificar:**

- Una consulta de vehículo en un trámite de prueba responde y queda en `consultas.consumo`; el SuperAdmin ve el consumo
  en `GET /api/v1/superadmin/consultas/consumo`. Si core-consultas no responde, la pantalla muestra «Consulta no
  disponible» (y el log de core-api, `flit.consultas.no_disponible`): no hay respaldo.
- El valor comercial sugerido de un trámite carga (y en DEV/QA, con el VIN de prueba `93Y9SR333RJ563653`, trae valor).
- Una impronta de prueba se genera; la Confirmación RUNT («Consultar ahora») deja su intento.
- Trámites publica: `rabbitmqctl list_exchanges -p flit`; en `tramites.outbox`, `published_at IS NULL` = pendientes
  (con el broker caído esperan en la tabla y salen en orden al volver).
- Una validación de identidad de prueba queda en `consultas.validaciones_kyverum`, su aviso en `consultas.avisos`
  (`resultado = publicado`) y el trámite se actualiza. Un aviso con firma inválida queda como `firma_invalida`.
- ICT sigue entrando: log de core-ict sin `Unauthenticated` ni errores de Consultas. core-ict ahora pide su token a
  Identidad (`Ict__ServiceToken__UseIdentity` fijo en el compose) y core-api lo acepta siempre; el HMAC sigue
  entrando hasta `ICT_SERVICE_TOKEN_ACCEPT_LEGACY=false` (§4.8, paso 5).

**Las validaciones de Kyverum creadas antes del corte** siguen avisando al webhook de core-api, que se conserva para
ellas; core-api consulta su estado por Consultas.

**Volver atrás:** desplegar la imagen anterior de core-api y core-ict (con su `.env` de antes: banderas apagadas).
core-consultas y el broker pueden seguir corriendo. Las variables nuevas no estorban a la versión anterior.

### 4.10 Notificaciones: obligatorio desde el corte (Epic #13316, Feature #13324 y HU #13359)

Con el corte (HU #13359), core-api y core-identity ya no tienen transportes de correo. **SMTP, el canal Renting y su
certificado viven solo en core-notificaciones**, que envía todos los correos y entrega los webhooks del OT. Los correos
llegan ya armados y con su canal resuelto.

- **Lo que va por el bus:**
  - Todos los correos.
  - Los webhooks del OT, que core-api sigue firmando: viaja la firma, no la llave.
- **Lo que necesita respuesta inmediata va por gRPC:** pantalla de canales, buzón de pruebas, registro de entregas de una
  empresa y consola de mensajes muertos.
- **Lo que no es de ninguna empresa va con la empresa «plataforma»** (`00000000-0000-0000-0000-0000000f1170`) y por FLIT:
  simulación de mandato, buzón de pruebas, recuperación de contraseña de un usuario sin rol y reportes de alcance
  SuperAdmin.
- **Ya no existen:** el perfil `notificaciones` ni las banderas `NOTIFICACIONES_REMOTO_HABILITADO`,
  `NOTIFICACIONES_REMOTO_WEBHOOKS` y `TRAMITES_BUS_ENTREGA_EN_PROCESO`.

**Antes de desplegar la rama, por ambiente** (junto con los pasos de §4.9):

1. **Base:** crear el usuario `flit_notificaciones` con
   `psql "$ADMIN_URL" -v servicio=notificaciones -v conexiones=20 -v clave="$CLAVE" -f deploy/postgres/servicio-con-esquema-propio.sql`
   y poner `CONNECTION_STRING_NOTIFICACIONES` en el `.env`.
2. **Secretos:**
   - `SVC_NOTIFICACIONES_CLIENT_SECRET`.
   - SMTP: las mismas variables `SMTP_*` de siempre.
   - Renting: las mismas `RENTING_API_*` y el mismo certificado `.pfx`, montado ahora en core-notificaciones. core-api y
     core-identity ya no montan el certificado.
3. **Broker:**
   - `deploy/rabbitmq/usuario-de-servicio.sh notificaciones "$CLAVE"` → `RABBITMQ_URL_NOTIFICACIONES`.
   - Los que dejan trabajos necesitan escribir en `flit.notificaciones`:
     - volver a correr `deploy/rabbitmq/usuario-de-servicio.sh tramites "$CLAVE_TRAMITES" notificaciones`, con la misma
       clave que ya tiene;
     - crear el de core-identity: `deploy/rabbitmq/usuario-de-servicio.sh plataforma "$CLAVE" notificaciones` →
       `RABBITMQ_URL_PLATAFORMA` (obligatoria).

**Desplegar** (`build-core-notificaciones` en el CD). No publica puertos: REST `CORE_NOTIFICACIONES_PORT` (4027) y gRPC
`CORE_NOTIFICACIONES_GRPC_PORT` (8085). El CD revisa su `/health/ready` desde dentro del contenedor.

**Verificar:**

- Un cambio de estado de prueba deja su fila en `notificaciones.entregas`.
- Una invitación y una recuperación de contraseña llegan.
- La simulación de mandato llega y queda con la empresa «plataforma».
- En la pantalla de notificaciones del SuperAdmin:
  - los canales muestran su remitente;
  - el buzón de pruebas envía.
- Un webhook del OT llega al destino y queda en `notificaciones.webhooks`.
- Correos (`notificaciones.email.send`): reintentos a 10 s, 1 min y 10 min; si se agotan, el mensaje pasa a la
  `.dlq`.
- Webhooks (`notificaciones.webhooks.salientes`): lo que no sale queda en su `.dlq`.
- La alerta `deploy/rabbitmq/alerta-dlq.sh` avisa de lo que llega a cualquier `.dlq`. El SuperAdmin lista, reintenta y
  descarta en `/api/v1/superadmin/notificaciones/mensajes-muertos?cola=correos|webhooks`.
- Los webhooks de ICT siguen saliendo de core-ict hasta que tengan su llave de firma (pendiente anotado en la HU #13356).

**Lo que no se mueve:**
- El reflejo de estado a ICT es gRPC a core-ict, no un webhook: sigue en core-api.
- La orquestación del cambio de estado se queda en core-api: decide los destinatarios y arma el correo y el webhook. Su
  tabla `tramites.procedure_state_change_outbox` se sigue llenando.

**Volver atrás:** desplegar la imagen anterior de core-api y core-identity con su `.env` de antes (`SMTP_*` y `RENTING_*`
siguen definidos). core-notificaciones y el broker pueden seguir corriendo.

### 4.11 Epic #13316: orden único por ambiente

El PR de la Epic (Consultas y Notificaciones como servicios) llega con los cortes incluidos: al desplegarlo, core-api ya
no consulta proveedores ni envía correos por sí mismo. Por eso **toda la preparación va antes de fusionar** (en DEV,
fusionar es desplegar) y no hay banderas que encender después. El mismo orden se repite en QA y PDN antes de cada paso
de rama.

**Antes de fusionar o pasar de rama** (no cambia nada para los usuarios):

1. Respaldo de la base (`pg_dump`).
2. **Broker** (§4.9, paso 1): `RABBITMQ_ADMIN_USER` y `RABBITMQ_ADMIN_PASSWORD`; levantar solo `rabbitmq`
   (`docker compose up -d rabbitmq`), que importa el vhost `flit` y los exchanges al arrancar.
3. **Usuarios del broker** (§4.9, paso 6 y §4.10, paso 3), cada uno con su clave:
   - `tramites` (con destino `notificaciones`) → `RABBITMQ_URL_TRAMITES`;
   - `consultas` → `RABBITMQ_URL_CONSULTAS`;
   - `notificaciones` → `RABBITMQ_URL_NOTIFICACIONES`;
   - `plataforma` (con destino `notificaciones`) → `RABBITMQ_URL_PLATAFORMA`.
4. **Bases propias** (§4.9, paso 2 y §4.10, paso 1): usuarios `flit_consultas` y `flit_notificaciones` →
   `CONNECTION_STRING_CONSULTAS` y `CONNECTION_STRING_NOTIFICACIONES`.
5. **Clientes de servicio**, uno distinto por ambiente: `SVC_TRAMITES_CLIENT_SECRET`, `SVC_CONSULTAS_CLIENT_SECRET`,
   `SVC_NOTIFICACIONES_CLIENT_SECRET`, `SVC_ICT_CLIENT_SECRET`.
6. **Secretos de proveedores y de correo:** los mismos de siempre (`VERIFIK_*`, `KYVERUM_*`, `KYVERUM_RUNT_*`,
   `FASECOLDA_*`, `RUES_*` opcional, `SMTP_*`, `RENTING_API_*`). Ahora los leen core-consultas y core-notificaciones; el
   certificado `.pfx` se monta solo en core-notificaciones.
7. **Aviso de Kyverum:** `CONSULTAS_KYVERUM_WEBHOOK_CALLBACK_URL` y, si nginx filtra rutas, abrir
   `/api/v1/consultas/avisos/*`.
8. **Comprobar el compose:** `docker compose -f docker-compose.prod.yml config` sin errores. Las variables obligatorias
   nuevas (`RABBITMQ_URL_TRAMITES`, `RABBITMQ_URL_PLATAFORMA`, `SVC_TRAMITES_CLIENT_SECRET`) hacen que falle aquí, no
   al desplegar.

**Fusionar o pasar de rama:** el CD construye y levanta core-consultas y core-notificaciones junto con core-api;
core-consultas aplica sus migraciones al arrancar.

**Apenas termine el despliegue:**

9. `deploy/postgres/migrar-configuracion-consultas.sql` (§4.9, paso 7); en DEV y QA también
   `migrar-valores-mock-avaluo.sql` (§4.9, paso 8).
10. Verificar con las listas de §4.9 y §4.10: consulta, avalúo, impronta y Confirmación RUNT; validación de identidad;
    correo de cambio de estado, invitación, recuperación y simulación de mandato; webhook del OT; canales y buzón de
    pruebas; consola de mensajes muertos; ICT entrando sin errores.

**Volver atrás:** imagen anterior de core-api, core-identity y core-ict con el `.env` anterior (las variables viejas
siguen ahí). core-consultas, core-notificaciones y el broker pueden quedar corriendo.

---

## 5. Hosts, DNS y certificados

| Superficie | DEV | QA | PDN | Contenedor | Qué hace falta |
|---|---|---|---|---|---|
| Hub (raíz) | `dev.flitsas.online` | `qa.flitsas.online` | `flitsas.online` | `frontend-hub` | Ya existe (hoy sirve Trámites): solo cambia el `proxy_pass` (§6) |
| Trámites | `dev.tramites.flitsas.online` | `qa.tramites.flitsas.online` | `tramites.flitsas.online` | `frontend` | **DNS + certificado + bloque nginx nuevos** |
| API | `api.dev.flitsas.online` | `api.qa.flitsas.online` | `api.flitsas.online` | `gateway` | Sin cambios |
| Comparendos | `dev.comparendos.flitsas.online` | `qa.comparendos.flitsas.online` | `comparendos.flitsas.online` | — | **Nada todavía** (§9) |
| Diagnóstico | `dev.diagnostico.flitsas.online` | `qa.diagnostico.flitsas.online` | `diagnostico.flitsas.online` | — | **Nada todavía** (§9) |
| Marca Blanca de prueba | `marcablancadev.flitsas.online` | `marcablancaqa…` | `marcablancapdn…` | — | Sin cambios |

**DNS**: un registro A (o CNAME) por host nuevo, a la IP de la VPS.

**Certificados**: uno por host, igual que el actual de `dev.flitsas.online` (ruta esperada en la plantilla:
`/etc/letsencrypt/live/<host>/`). Ojo: **un comodín `*.flitsas.online` no cubre `dev.tramites.flitsas.online`**
(dos niveles); sí cubriría `tramites.flitsas.online` en PDN. Si se prefiere comodín para DEV/QA, tendría que ser
`*.dev.flitsas.online` (reto DNS-01). Ejemplo con el webroot que ya usa la VPS para los retos:

```bash
certbot certonly --webroot -w /var/www/acme-challenge -d dev.tramites.flitsas.online
```

(o el método con el que se emitió el certificado de `dev.flitsas.online`; para renovarlo, el mismo de siempre).

**Comprobar:** `dig +short dev.tramites.flitsas.online` devuelve la IP de la VPS y
`openssl s_client -connect dev.tramites.flitsas.online:443 -servername dev.tramites.flitsas.online </dev/null | openssl x509 -noout -subject -dates`
muestra el host correcto.

---

## 6. nginx

Plantilla en el repo: [`deploy/edge/nginx/flit-suite-hosts.conf.example`](../../deploy/edge/nginx/flit-suite-hosts.conf.example).
Para QA y PDN cambian los hosts y los puertos.

### 6.1 Bloques (DEV)

```nginx
# HTTP: reto ACME y redirección a https del host nuevo (si el catch-all del puerto 80 ya lo hace, sobra).
server {
    listen 80;
    listen [::]:80;
    server_name dev.tramites.flitsas.online;
    location /.well-known/acme-challenge/ { root /var/www/acme-challenge; }
    location / { return 301 https://$host$request_uri; }
}

# Trámites en su host
server {
    listen 443 ssl;
    listen [::]:443 ssl;
    http2 on;
    server_name dev.tramites.flitsas.online;

    ssl_certificate     /etc/letsencrypt/live/dev.tramites.flitsas.online/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/dev.tramites.flitsas.online/privkey.pem;

    proxy_read_timeout 60s;            # consultas RUNT/SIMIT tardan 8-10 s
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Real-IP $remote_addr;
    client_max_body_size 50m;
    large_client_header_buffers 4 32k; # la cookie de sesión puede crecer (ver 6.2)

    location / { proxy_pass http://127.0.0.1:4001; }
}

# Hub en la raíz: es el server{} que HOY sirve dev.flitsas.online, con el destino cambiado.
server {
    listen 443 ssl;
    listen [::]:443 ssl;
    http2 on;
    server_name dev.flitsas.online;

    ssl_certificate     /etc/letsencrypt/live/dev.flitsas.online/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/dev.flitsas.online/privkey.pem;

    proxy_read_timeout 60s;
    proxy_http_version 1.1;
    proxy_set_header Host $host;       # el hub sella el dominio con este Host: tiene que llegar tal cual
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Real-IP $remote_addr;
    client_max_body_size 50m;
    large_client_header_buffers 4 32k;

    location / { proxy_pass http://127.0.0.1:4022; }   # antes: 4001 (FRONTEND_PORT)
}
```

| | DEV | QA | PDN |
|---|---|---|---|
| Host de Trámites → | `127.0.0.1:4001` | `127.0.0.1:5001` | `127.0.0.1:6001` |
| Raíz (hub) → | `127.0.0.1:4022` | `127.0.0.1:5022` | `127.0.0.1:6022` |

### 6.2 Reglas que no están a la vista (y por qué)

| Regla | Si no se cumple |
|---|---|
| Una sola `location /` por host. **Quitar** del server{} de la raíz cualquier `location /api/v1/`, `/connect/` o `/.well-known/` que apunte al gateway | El hub y Trámites ponen el token del usuario en el servidor; si nginx manda `/api/v1` directo al gateway, las llamadas llegan **sin token** (401) |
| `proxy_set_header X-Forwarded-Proto $scheme` | El login arma direcciones `http://…` y el servidor OIDC las rechaza (`redirect_uri` inválido) |
| `proxy_set_header Host $host` (sin reescribirlo) | El dominio se deduce mal: producto y Marca Blanca equivocados |
| **No** poner `X-Frame-Options`, `Content-Security-Policy` ni `add_header … frame-ancestors` en los hosts del hub ni de los productos, ni `proxy_hide_header Content-Security-Policy` | Al cerrar sesión, el hub carga `/auth/frontchannel-logout` de cada producto en un iframe; con una CSP o `X-Frame-Options` de nginx, el producto no se entera del cierre y queda con la sesión vieja («Reconectando tu sesión…» cada vez) |
| No fijar ni borrar `X-Flit-Domain` ni `X-Internal-Key` | Las pone y valida el propio sistema por la red interna |
| No usar `proxy_cookie_domain` ni reescribir cookies | Las cookies son de cada host (`flit_hub`, `flit_session_<producto>`, `flit_oidc_tx_<producto>`), sin `Domain`, `Secure`, `SameSite=Lax` |
| `large_client_header_buffers 4 32k` | La sesión de cada app puede ocupar varios KB en la cookie; el default de nginx (8 KB) respondería 400. Hoy pesa ~1,3 KB, así que es margen, no urgencia |
| `client_max_body_size 50m`, `proxy_read_timeout 60s` | Los mismos de hoy: adjuntos y consultas lentas |
| WebSocket | No hace falta en estos dos hosts. `api.<ambiente>` (`/hubs/`) sigue igual |

### 6.3 Comprobar

```bash
nginx -t && nginx -s reload
curl -sI https://dev.flitsas.online/healthz                     # 200 (lo responde el hub)
curl -s  https://dev.flitsas.online/.well-known/openid-configuration | head -c 200   # JSON con "issuer":"https://dev.flitsas.online/"
curl -sI https://dev.tramites.flitsas.online/auth/frontchannel-logout | grep -i -E "^HTTP|content-security-policy|x-frame"
#   → HTTP 200 con "frame-ancestors *" y SIN X-Frame-Options
curl -sI https://dev.flitsas.online/tramites | grep -i -E "^HTTP|location"   # 308 → https://dev.tramites.flitsas.online/tramites
```

### 6.4 Dominios de red (Marca Blanca)

La plantilla `flit-network-domains.conf.example` (dominios propios de clientes, catch-all) **todavía no está
adaptada a la suite**: manda `/` a Trámites y `/api/v1` directo al gateway. Mientras ningún dominio de cliente esté en
uso con la suite, no se toca. Antes de encender la suite para un dominio de cliente hay que actualizarla (queda en
§11 como pendiente).

---

## 7. Base de datos

### 7.1 Migraciones

Las corre **solo `core-api`**, solo, al arrancar. No hay SQL a mano. Las que trae la suite:

| Migración | Qué hace |
|---|---|
| `HU12964_RbacPorProducto` (DDL 120) | Módulos y roles por producto; rol `admin_tramites` con los permisos de Trámites del AdminCompany |
| `HU12967_BooleansAHabilitacion` (121) | Copia las banderas de Trámites/Comparendos por empresa a `platform.tenant_products` |
| `HU12968_TenantDomainsPurpose` (122) | Propósito de cada dominio de red (hub o producto) |
| `HU12896_JwtSigningKey` (123) | Tabla `security.jwt_signing_keys` (llaves cifradas) |
| `HU12990_OidcStores` (124) | Tablas `identity.oidc_*` del servidor de login |
| `Suite_RetirarProductoDemo` (125) | Retira el producto de prueba `demo` |
| `HU12967_AdminPorProducto` (126) | Roles `admin_comparendos`/`admin_diagnostico`, asignados a cada AdminCompany |

core-identity **no migra**: si arranca antes de que core-api termine, su `/health/ready` responde 503
`pending_migrations` hasta que la base esté al día (es esperado).

**Antes de la fase 1**, respaldo de la base del ambiente (`pg_dump`), como en cualquier despliegue con migraciones.

### 7.2 Clientes de login (OIDC): automáticos, y por qué importa `FLIT_SUITE_ENV`

Con `FLIT_OIDC_ENABLED=true`, cada vez que core-api o core-identity arrancan registran (o **reescriben**) un cliente de
login por producto, con sus direcciones de retorno armadas desde `FLIT_SUITE_ENV`. **Gana el último proceso que
arranca.** Si alguno arranca con el ambiente equivocado, el login de ese ambiente se rompe hasta que se reinicie con
el valor bueno. Por eso el compose exige la variable.

Comprobar (solo lectura):

```sql
SELECT client_id, redirect_uris, post_logout_redirect_uris
  FROM identity.oidc_applications ORDER BY client_id;
```

Esperado en DEV: `plataforma` → `https://dev.flitsas.online/auth/callback`, `tramites` →
`https://dev.tramites.flitsas.online/auth/callback`, `comparendos` y `diagnostico` con sus hosts `dev.`. En PDN, sin
prefijo. Si aparecen hosts de otro ambiente: revisar `FLIT_SUITE_ENV` y reiniciar core-api (y core-identity).

Nunca correr las pruebas .NET ni un core-api local contra la base de un ambiente: reescribirían estos clientes.

### 7.3 Conexiones

core-identity abre su propio pool contra la misma base y el mismo usuario. Revisar `max_connections` contra lo que ya
usan core-api y core-ict (paso 0 de [handoff-vps-identidad.md](handoff-vps-identidad.md)).

---

## 8. Orden de activación

### Fase 0 — Preparar (sin efecto)

1. Foto del estado: `cp .env ".env.antes-suite.$(date +%Y%m%d%H%M)"`, `docker compose -f docker-compose.prod.yml ps`,
   `free -m`, conexiones de Postgres.
2. En el `.env`, agregar el bloque de §4.2 del ambiente y `FLIT_SESSION_SECRET` (nuevo, 32+ caracteres). Confirmar que
   `FLIT_INTERNAL_API_KEY` tiene valor.
3. **No** encender nada todavía: `FLIT_OIDC_ENABLED`, `FLIT_SESSION_MODE`, `FLIT_TRAMITES_HOST_ENABLED`,
   `FLIT_IDENTITY_CLUSTER_ENABLED` ausentes o en su valor apagado; `COMPOSE_PROFILES` sin `suite` ni `identity`.
4. Comprobar que el compose resuelve: `docker compose -f docker-compose.prod.yml config >/dev/null && echo ok`.
5. Respaldo de la base.
6. Avisar a los usuarios: tendrán que iniciar sesión otra vez (fases 1 y 3).

### Fase 1 — Desplegar la rama con todo apagado

Se fusiona el PR en `develop` y el CD despliega como siempre.

**Comprobar:**
- El CD termina en verde; `docker compose -f docker-compose.prod.yml ps` todo `healthy`.
- `docker compose -f docker-compose.prod.yml logs core-api | grep -i -E "migrat|error"`: aplicó las migraciones de §7.1.
- En el navegador, en `https://dev.flitsas.online`: Trámites como siempre (con su login de siempre). Iniciar sesión,
  abrir trámites, reportes, usuarios, configuración de compañía.
- `https://dev.flitsas.online/connect/authorize` responde 404 (OIDC todavía apagado).

**Volver atrás:** si el problema es de sesión (401 en todo), `JWT_VALIDATE_ISSUED_TOKENS=false` y
`up -d --no-deps core-api` (§4.4). Si es otra cosa, el despliegue anterior como siempre.

### Fase 2 — DNS y certificado de Trámites

§5. Agregar ya el server{} de `dev.tramites.flitsas.online` (§6.1) y recargar nginx: el host queda servido por Trámites
pero nadie llega todavía ahí (el hub aún no redirige).

**Comprobar:** `curl -sI https://dev.tramites.flitsas.online/` responde (200 o 307) con certificado válido.

### Fase 3 — Encender el hub (la ventana de cambio)

Hacerlo en horario de bajo uso. Son unos minutos.

1. Dejar preparado (sin recargar) el cambio del server{} de la raíz: `proxy_pass` a `HUB_PORT` y sin `location`
   extras (§6.1, §6.2).
2. En el `.env`:
   ```bash
   FLIT_OIDC_ENABLED=true
   COMPOSE_PROFILES=suite
   FLIT_SESSION_MODE=oidc
   FLIT_TRAMITES_HOST_ENABLED=true
   ```
3. Recrear:
   ```bash
   docker compose -f docker-compose.prod.yml up -d
   ```
   (levanta `frontend-hub`, y recrea core-api, gateway y frontend con las variables nuevas).
4. En cuanto el hub esté `healthy` (`docker compose -f docker-compose.prod.yml ps frontend-hub`):
   `nginx -t && nginx -s reload`.

**Comprobar** (§6.3 más):
- `https://dev.flitsas.online` muestra la portada del hub. «Iniciar sesión» → login → entra a Trámites en
  `dev.tramites.flitsas.online` sin pedir otra vez la contraseña.
- Un enlace viejo (`https://dev.flitsas.online/tramites/…`) lleva a la misma pantalla en el host de Trámites.
- Menú ▦: Trámites, y Comparendos y Diagnóstico como «Próximamente».
- Cerrar sesión en el hub: Trámites pide login en su siguiente página. Cerrar sesión en Trámites: igual en el hub.
- Correos: invitar un usuario y pedir recuperación de contraseña; los enlaces abren en `dev.flitsas.online`.
- SQL de §7.2 con los hosts `dev.`.
- Un SuperAdmin prende/apaga un producto a una empresa en Compañías → Productos.

**Volver atrás** (minutos):
1. nginx: `proxy_pass` de la raíz otra vez a `FRONTEND_PORT`; `nginx -s reload`.
2. `.env`: `FLIT_SESSION_MODE=legacy`, `FLIT_TRAMITES_HOST_ENABLED=false` (y si se quiere, `FLIT_OIDC_ENABLED=false` y
   quitar `suite` de `COMPOSE_PROFILES`); `docker compose -f docker-compose.prod.yml up -d`.
3. Los usuarios inician sesión una vez más. Las redirecciones 308 que algún navegador haya guardado se van al borrar
   caché; mientras tanto, el host de Trámites sigue respondiendo.

### Fase 4 — Identidad aparte

Con la fase 3 estable: [handoff-vps-identidad.md](handoff-vps-identidad.md), pasos 0 a 2 (`COMPOSE_PROFILES=suite,identity`,
`FLIT_IDENTITY_CLUSTER_ENABLED=true`). Requiere el bloque de §4.2 en el `.env` (usa comandos a mano).

### Fase 5 — Operación

- `FLIT_DEPLOY_ROLLING=true` en el GitHub Environment (paso 4 del handoff de identidad).
- Límites de memoria y conexiones: medir y mandarlos al equipo para un PR (paso 3 del handoff de identidad).
- Monitoreo: alerta si `core-identity` o `frontend-hub` dejan de estar `healthy`. El chequeo del CD después de
  desplegar no prueba el hub: su salud la da el healthcheck del compose (`/healthz`).

### QA y PDN

Las mismas fases, con los valores de cada ambiente (§3, §4.2, §5, §6). En PDN, además, coordinar la ventana con el
negocio y tener el respaldo de la base verificado.

---

## 9. Comparendos y Diagnóstico

Hoy **no** hay que hacer nada en la VPS: no tienen servicio en el compose y el hub los muestra como «Próximamente»
(lista `Suite:Hosts:ComingSoon` en la configuración de la API). Su cliente de login ya se registra con su host futuro.

Cuando uno se despliegue (por PR, no a mano):
- Su servicio y su front entran al compose con los puertos reservados (Comparendos 4023, Diagnóstico 4024;
  QA 50xx, PDN 60xx). Su host ya tiene DNS, certificado y server{} en DEV: hoy redirige a `/proximamente/<producto>`
  del hub; al desplegar, ese server{} pasa a `proxy_pass` al puerto del producto.
- DNS + certificado de `<ambiente>.comparendos.flitsas.online` (mismas reglas de §5) y un server{} igual al de
  Trámites (§6.1), con las mismas reglas de §6.2.
- Se quita de la lista «Próximamente» para ese ambiente (cambio de configuración por PR).
- El SuperAdmin lo enciende por empresa en Compañías → Productos.

---

## 10. Si algo sale mal

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| `docker compose` dice `required variable FLIT_SUITE_ENV is missing a value` | Falta la línea en el `.env` | Agregarla (§4.2). En PDN: `FLIT_SUITE_ENV=` |
| Después de la fase 1, todos reciben 401 / vuelven al login | Esperado una vez (§4.4). Si persiste tras iniciar sesión, la llave no se está guardando | Revisar logs de core-api; salida rápida: `JWT_VALIDATE_ISSUED_TOKENS=false` |
| «El redirect_uri / post_logout_redirect_uri no es válido» | Los clientes de login tienen hosts de otro ambiente, o falta `X-Forwarded-Proto` | SQL de §7.2; corregir `FLIT_SUITE_ENV` y reiniciar core-api (y core-identity); revisar nginx |
| El hub o Trámites responden 500; el log dice `FLIT_SESSION_SECRET debe tener al menos 32 caracteres` | Vacía o de menos de 32 caracteres | Generarla (§4.1) |
| Las pantallas del hub o de Trámites dan 401 en `/api/v1` con la sesión abierta | nginx manda `/api/v1` directo al gateway | Quitar esa `location` (§6.2) |
| Marca Blanca o producto equivocados con la suite | `FLIT_INTERNAL_API_KEY` vacía, `Host` reescrito o subred distinta a la de la red Docker | §4.3 y §6.2; `docker network inspect flitdev_default` para ver la subred real |
| Al salir del hub y volver, Trámites muestra «Reconectando tu sesión…» cada vez | El cierre no llega al producto: CSP o `X-Frame-Options` de nginx | §6.2 y la comprobación de §6.3 |
| 400 «Request Header Or Cookie Too Large» | Cookies grandes | `large_client_header_buffers 4 32k` (§6.1) |
| `core-identity` en `unhealthy` con `pending_migrations` | core-api no ha terminado de migrar | Esperar a que core-api esté `healthy` y reiniciar core-identity |
| Docker: `Pool overlaps with other one` | `FLIT_INTERNAL_SUBNET` distinta a la que ya usa otro stack o la red actual | Usar la del CD (§3) |

---

## 11. Pendientes conocidos (no bloquean DEV)

| Tema | Estado |
|---|---|
| Dominios de red (Marca Blanca) con la suite | La plantilla `flit-network-domains.conf.example` no está adaptada (§6.4). Antes de usar la suite en un dominio de cliente |
| Trámites en su host con `FLIT_SESSION_MODE=legacy` | No soportado: el CORS del gateway no incluye `<ambiente>.tramites.flitsas.online`. Por eso, en el rollback de la fase 3 la raíz vuelve a Trámites |
| Límite de peticiones del gateway por IP | Detrás de nginx y del servidor de Trámites, todas las peticiones comparten IP: el tope (600/min) es casi global. Vigilar 429 en DEV |
| Duración de sesión | La sesión del producto dura 14 días y la del hub 12 h (caso H1 de `matriz-pruebas.md`); decisión pendiente |
| `CORE_ICT_PORT` | El CD no lo exporta (de antes de la suite): en QA/PDN vale lo del `.env` |
| Contraseña SMTP en `docker-compose.yml` (local) | Está en texto plano en el repo; conviene rotarla |

---

## 12. Lista de chequeo

- [ ] `.env` con el bloque de §4.2 del ambiente (incluida `FLIT_SUITE_ENV`, vacía en PDN)
- [ ] `FLIT_SESSION_SECRET` nueva (32+), `FLIT_INTERNAL_API_KEY` con valor
- [ ] `docker compose -f docker-compose.prod.yml config` sin errores
- [ ] Respaldo de la base y aviso a usuarios
- [ ] Fase 1 desplegada y probada (login de siempre, módulos de siempre)
- [ ] DNS y certificado de `<ambiente>.tramites.flitsas.online`
- [ ] nginx: server{} de Trámites; raíz → `HUB_PORT`; sin `location /api/v1` extra; `Host` y `X-Forwarded-Proto`; sin CSP/`X-Frame-Options`
- [ ] Fase 3 encendida y comprobada (§6.3, §7.2, §8)
- [ ] Fase 4 (identidad) cuando la 3 esté estable
- [ ] `FLIT_DEPLOY_ROLLING`, límites de recursos, monitoreo del hub y de core-identity
