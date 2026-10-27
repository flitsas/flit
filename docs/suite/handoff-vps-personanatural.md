# Handoff de VPS: Portal Persona Natural (BFF `core-personanatural`)

Épica #14021, Feature #14022, HU #14038. Para el líder técnico, con su sesión de Claude conectada a la VPS. Cada paso
trae qué hacer, cómo comprobarlo y cómo volver atrás. Se aplica **primero en DEV**; QA y PDN solo con aprobación del
líder y después de que DEV esté estable.

**Qué llega.** Un contenedor nuevo, `core-personanatural` (.NET 10), en el perfil `personanatural` del compose: el BFF
del portal público de personas naturales. Crea y consulta trámites en FLIT 2 sin cuenta de usuario, con una sesión
efímera por trámite. Su front (`transfer-step-trail`) vive en otro repositorio y llega con el Feature E.

**Por qué es seguro fusionar antes de encender.**

- El perfil `personanatural` no arranca hasta que el `.env` lo pida (`COMPOSE_PROFILES=…,personanatural`).
- La ruta del gateway `/api/v1/persona-natural/*` solo atiende ese prefijo, que nadie usa hoy.
- En `core-api`, el cliente `svc-personanatural` no se registra mientras su secreto esté vacío, y la fachada gRPC del
  portal viene apagada (`PERSONA_NATURAL_FACHADA_HABILITADA=false`).
- El CD **solo construye y publica la imagen** (`build-core-personanatural`). No está en el despliegue GitOps: ese paso
  falla si la imagen no está en la kustomization de `flit-gitops`, así que se agrega allí al encender el portal (paso 7).

## Puertos (propuestos, por confirmar con el líder)

| Ambiente | API interna (`CORE_PERSONANATURAL_PORT`) | gRPC (`CORE_PERSONANATURAL_GRPC_PORT`) |
|---|---|---|
| DEV | 4029 | 8087 |
| QA | 5029 | 8087 |
| PDN | 6029 | 8087 |

Ni el REST ni el gRPC se publican: viven en la red de Docker. Hacia afuera solo se llega por el gateway
(`/api/v1/persona-natural/*`).

## 1. Base de datos: usuario dueño del esquema `personanatural`

```bash
CLAVE=$(openssl rand -base64 32 | tr -d '/+=')
psql "$ADMIN_URL" -v servicio=personanatural -v conexiones=20 -v clave="$CLAVE" \
     -f deploy/postgres/servicio-con-esquema-propio.sql
```

En el `.env`:

```
CONNECTION_STRING_PERSONANATURAL=Host=postgres;Port=5432;Database=flit;Username=flit_personanatural;Password=<CLAVE>;Maximum Pool Size=20
```

El servicio migra su esquema al arrancar (solo `personanatural`). **Comprobar:** `psql "$ADMIN_URL" -c "\du flit_personanatural"`.
**Volver atrás:** `DROP SCHEMA personanatural CASCADE; DROP ROLE flit_personanatural;` (solo si nunca se usó).

## 2. RabbitMQ: usuario del servicio

```bash
deploy/rabbitmq/usuario-de-servicio.sh personanatural <clave>
```

En el `.env`: `RABBITMQ_URL_PERSONANATURAL=amqp://personanatural:<clave>@rabbitmq:5672/flit`.

## 3. Cliente de servicio `svc-personanatural` y fachada

El cliente está declarado en `appsettings.json` de core-api con el scope `platform.tramites.persona_natural` y el secreto
vacío. Falta el secreto, el mismo valor en los dos lados:

```
SVC_PERSONANATURAL_CLIENT_SECRET=<openssl rand -base64 48>
PERSONA_NATURAL_FACHADA_HABILITADA=true
```

El compose lo pasa a core-api (`Suite__Oidc__ServiceClients__svc-personanatural__Secret`) y a core-personanatural
(`Platform__ServiceClient__ClientSecret`). **Reiniciar core-api.** La empresa «Persona Natural» (id fijo) la siembra el
DDL 152 al arrancar core-api.

## 4. Secretos de datos personales y captcha

```
PERSONANATURAL_PII_HMAC_KEY=<openssl rand -base64 48>   # huella de cédula e IP; mínimo 32 caracteres
PERSONANATURAL_PII_AES_KEY=<openssl rand -base64 32>    # cifrado AES-256 de los datos personales: base64 de 32 bytes
PERSONANATURAL_CAPTCHA_PROVEEDOR=turnstile              # o recaptcha
PERSONANATURAL_CAPTCHA_SECRET=<secreto del sitio del proveedor>
```

- **Guardar las dos llaves en el gestor de secretos del ambiente.** Perder la de cifrado deja ilegibles los datos
  personales ya guardados; perder la HMAC impide reanudar los trámites existentes.
- Rotar el cifrado: agregar `Pii__Cifrado__Llaves__2` y `Pii__Cifrado__VersionActiva=2`; conservar la versión 1 para leer lo viejo.
- **Nunca** `PERSONANATURAL_CAPTCHA_PROVEEDOR=deshabilitado` en la VPS: corre como Development y dejaría el portal abierto.
- Sin alguna de estas variables el contenedor no arranca y el log dice cuál falta.

## 5. Origen del front (CORS)

```
CORS_ORIGIN_PERSONA_NATURAL=https://<dominio-del-front-persona-natural>
```

Se suma a los orígenes del gateway (`Cors__AllowedOrigins__3`). La política CORS del gateway es **global**: el origen
queda permitido en todas sus rutas, que ya exigen su propia credencial (el portal no usa cookies). Si se quiere CORS por
ruta, hay que tocar `Flit.Gateway/Program.cs` (queda como mejora, no es parte de esta HU).

## 6. Encender

```
COMPOSE_PROFILES=...,personanatural
CORE_PERSONANATURAL_TAG=dev
```

`docker compose --profile personanatural up -d core-personanatural && docker compose up -d gateway core-api`.
**Comprobar:** el contenedor `healthy` (usa `/health/ready`), y desde fuera
`curl -s -o /dev/null -w "%{http_code}" -X POST https://<api>/api/v1/persona-natural/sesiones/reanudar` → `400`
(`captcha_required`). **Volver atrás:** quitar `personanatural` de `COMPOSE_PROFILES` y recrear; las demás variables
quedan sin efecto.

## 7. Despliegue GitOps (cuando el portal salga de DEV)

1. En `flit-gitops/services/flit2-<ambiente>/kustomization.yaml`, agregar la imagen `ghcr.io/flitsas/flitdev/core-personanatural`.
2. En `cd.yml` (`deploy-gitops`): sumar `build-core-personanatural` a `needs`, el par `R_`/`D_` y la línea
   `"core-personanatural:${{ env.CORE_PERSONANATURAL_IMAGE }}:CORE_PERSONANATURAL"` a la lista. **No hacerlo antes del
   paso 1**: el despliegue de todo FLIT 2 fallaría.
3. ConfigMap y sealed-secret con las variables de los pasos 1 a 5.

## Pendiente de confirmar

- Puertos 4029/5029/6029 y gRPC 8087.
- Dominio del front y su vhost de nginx (el front es otro repositorio).
- Dirección gRPC de core-api que usará el BFF para consultar y crear trámites (se agrega con las HU del Feature B/D).
