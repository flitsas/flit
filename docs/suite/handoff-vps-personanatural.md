# Handoff de VPS: Portal Persona Natural (BFF `core-personanatural`)

Épica #14021, Feature #14022, HU #14038. Para el líder técnico, con su sesión de Claude conectada a la VPS. Cada paso
trae qué hacer, cómo comprobarlo y cómo volver atrás. Se aplica **primero en DEV**; QA y PDN solo con aprobación del
líder y después de que DEV esté estable.

**Qué llega.** Dos contenedores nuevos en el perfil `personanatural` del compose:

- `core-personanatural` (.NET 10): el BFF del portal público de personas naturales. Crea y consulta trámites en FLIT 2
  sin cuenta de usuario, con una sesión efímera por trámite.
- `frontend-personanatural` (Next.js, vive en este monorepo con la estructura de `frontend-comparendos`): la app web del
  portal. No usa el login del hub; habla con el gateway por su propio proxy, que guarda la sesión en una cookie HttpOnly.

**Por qué es seguro fusionar antes de encender.**

- El perfil `personanatural` no arranca hasta que el `.env` lo pida (`COMPOSE_PROFILES=…,personanatural`).
- La ruta del gateway `/api/v1/persona-natural/*` solo atiende ese prefijo, que nadie usa hoy.
- En `core-api`, el cliente `svc-personanatural` no se registra mientras su secreto esté vacío, y la fachada gRPC del
  portal viene apagada (`PERSONA_NATURAL_FACHADA_HABILITADA=false`).
- El CD **solo construye y publica la imagen** (`build-core-personanatural`). No está en el despliegue GitOps: ese paso
  falla si la imagen no está en la kustomization de `flit-gitops`, así que se agrega allí al encender el portal (paso 7).

## Puertos (propuestos, por confirmar con el líder)

| Ambiente | Web (`PERSONANATURAL_PORT`) | API interna (`CORE_PERSONANATURAL_PORT`) | gRPC (`CORE_PERSONANATURAL_GRPC_PORT`) |
|---|---|---|---|
| DEV | 4031 | 4029 | 8087 |
| QA | 5031 | 5029 | 8087 |
| PDN | 6031 | 6029 | 8087 |

Ni el REST ni el gRPC del BFF se publican: viven en la red de Docker. La web escucha solo en `127.0.0.1` y nginx publica
el dominio del portal (vhost nuevo, por definir) hacia ese puerto; al BFF solo se llega por el gateway
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

## 3. Empresa «Persona Natural», cliente de servicio `svc-personanatural` y fachada

**Primero, la empresa.** Todo trámite del portal nace a nombre de la empresa «Persona Natural» (id fijo
`00000000-0000-0000-0000-0000000f1171`). Por decisión del PO ya **no** la siembra una migración: se crea una vez por
ambiente, **antes de encender la fachada**, con un usuario que escriba en `identity.tenants`:

```
psql "$CONNECTION_STRING_CORE_ADMIN" -v ON_ERROR_STOP=1 -f deploy/postgres/crear-empresa-persona-natural.sql
```

Es idempotente (si la fila ya existe no cambia nada; los ambientes donde la sembró el DDL 152 retirado no necesitan
correrlo). **Comprobar:** `SELECT id, code, is_active FROM identity.tenants WHERE id = '00000000-0000-0000-0000-0000000f1171'`
devuelve una fila con `code` `COMP-XXXX` y `is_active = true`. Sin la fila, la fachada responde con error al crear
cualquier trámite o validación.

**Después, el cliente.** Está declarado en `appsettings.json` de core-api con el scope
`platform.tramites.persona_natural` y el secreto vacío. Falta el secreto, el mismo valor en los dos lados:

```
SVC_PERSONANATURAL_CLIENT_SECRET=<openssl rand -base64 48>
PERSONA_NATURAL_FACHADA_HABILITADA=true
```

El compose lo pasa a core-api (`Suite__Oidc__ServiceClients__svc-personanatural__Secret`) y a core-personanatural
(`Platform__ServiceClient__ClientSecret`). **Reiniciar core-api.**

## 3b. Consultas, catálogo y correo (direcciones internas)

El compose ya trae los valores por defecto de la red de Docker; solo se cambian si el ambiente es distinto:

```
CONSULTAS_REMOTO_ADDRESS=http://core-consultas:8084        # consulta de vehículo y conductor
PERSONANATURAL_CORE_API_ADDRESS=http://core-api:8082       # fachada gRPC de core-api (catálogo y cotización)
NOTIFICACIONES_REMOTO_ADDRESS=http://core-notificaciones:8085   # envío de la cotización por correo
```

- El cliente `svc-personanatural` suma los scopes `platform.consultas` y `platform.notificaciones.send` (declarados en
  `appsettings.json` de core-api); requieren reiniciar core-api.
- **Tarifas:** la tabla `admin.persona_natural_tariffs` nace vacía. Sin una tarifa vigente la cotización responde
  `tarifa_no_vigente` y el catálogo no ofrece tipos: hay que cargarlas con la pantalla del Feature J (J1) o, mientras tanto,
  con la función `admin.persona_natural_tariff_new_version(...)`. No hay precios inventados.
- **Cadena de proveedores de «Persona Natural»:** hoy usa la de por defecto de core-consultas; se configura desde core-api.
- **Organismos:** el catálogo solo ofrece los organismos con permiso (grant) para la empresa «Persona Natural».

## 3c. Consumidor de core-api: crear el trámite y devolver su estado (HU #14050 y #14051)

core-api consume `tramites.persona_natural.radicacion_solicitada`, lee la solicitud por pull al BFF (RPC
`RadicacionService/ObtenerSolicitudParaRadicar`), crea el trámite con los casos de uso de FLIT 2 y publica
`tramites.persona_natural.solicitud_resuelta` y `tramites.persona_natural.estado_cambiado`. **Apagado por defecto**: sin la
bandera, core-api no registra colas, cliente ni publicador y se comporta exactamente como antes. Encender SOLO con la empresa
«Persona Natural» creada (sección 3), el RabbitMQ del servicio (sección 2) y el bus de Trámites encendido
(`Tramites__Bus__Habilitado=true`, ya lo está):

```
PersonaNatural__Consumidor__Habilitado=true                       # en core-api (hoy el compose no lo pasa: agregarlo al environment)
PersonaNatural__Portal__Address=http://core-personanatural:8087   # gRPC del BFF (CORE_PERSONANATURAL_GRPC_PORT)
PersonaNatural__Portal__DeadlineSegundos=15                       # opcional
```

- El cliente `svc-tramites` suma el scope `platform.personanatural.leer` (audiencia `personanatural`), ya declarado en
  `appsettings.json` de core-api; requiere reiniciar core-api (y core-identity si emite los tokens por su cuenta).
- Colas nuevas: en core-api `tramites.personanatural-radicacion` y `tramites.personanatural-estado` (cada una con su
  `.dlq`); en el BFF `personanatural.resolucion` y `personanatural.estado`. Los mensajes agotados quedan en la `.dlq` de la
  cola, visibles en la pantalla de mensajes muertos.
- El usuario del broker del BFF necesita escritura en `flit.tramites` (`usuario-de-servicio.sh personanatural <clave> tramites`).
- Si FLIT 2 todavía debe generar el FUR o la firma de la compraventa, el trámite queda creado en `borrador` (la solicitud
  pasa a `creada` con ese estado); un rechazo de negocio deja el borrador `anulado` con el código en su motivo.

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
- **Nunca** `PERSONANATURAL_CAPTCHA_PROVEEDOR=deshabilitado` en la VPS: corre como Development y dejaría el portal abierto. Desde la HU #14036 el servicio ni arranca con «deshabilitado» si no existe además `Captcha__PermitirDeshabilitado=true` (solo para el equipo del desarrollador; no está en el compose).
- Sin alguna de estas variables el contenedor no arranca y el log dice cuál falta.

### 4.1 IP del ciudadano detrás de los proxies (límite por IP, huella de IP)

El límite por IP y la huella de IP usan la IP que ve el servicio. Cadena real: **nginx → proxy de Next → gateway (YARP) → core-personanatural**.
El servicio (`Abuso__ConfiarEnXForwardedFor=true`, ya en el compose) recorre `X-Forwarded-For` de derecha a izquierda **solo
mientras el salto sea de una red de confianza** y hasta `Abuso__SaltosDeConfianza` saltos (3 por defecto); se detiene en el primer
salto público, que es la IP del ciudadano. Las redes de confianza salen de `Abuso__RedesDeConfianza` (CIDR, lista indexada
`Abuso__RedesDeConfianza__0=…` o CSV en una variable); por defecto loopback, 10/8, 172.16/12, 192.168/16 y fc00::/7 (Docker y RFC1918).
Una lista vacía (`Abuso__RedesDeConfianza__0=`) no confía en nadie: la cabecera se ignora.

Lo que debe cumplir la infraestructura para que esto sea correcto:

- **nginx debe SOBRESCRIBIR `X-Forwarded-For`** con la IP de conexión, no sumarla: `proxy_set_header X-Forwarded-For $remote_addr;`
  (no `$proxy_add_x_forwarded_for`, que conserva lo que mande el cliente). Así el cliente no puede anteponer direcciones falsas.
- **El proxy del front debe tomar el último salto de confianza**: al reenviar al gateway debe **sumar** la dirección del salto que le
  llegó (nginx) a la cabecera, nunca reemplazarla ni copiar la que el navegador envíe tal cual, y no confiar en un `X-Forwarded-For`
  que no venga de nginx.
- Si se añade o quita un salto (CDN, otro balanceador), ajustar `Abuso__SaltosDeConfianza` y, si sus IP no son privadas, `Abuso__RedesDeConfianza`.
- El servicio nunca debe quedar expuesto directo a Internet con `ConfiarEnXForwardedFor`: lo alcanza solo el gateway por la red interna.

Otros topes de la HU #14036: las excepciones no manejadas responden `application/problem+json` con `code=error_interno` (sin trazas ni
rutas, incluso en Development) y el cuerpo JSON no puede pasar de `Abuso__MaxCuerpoJsonBytes` (65536); los archivos (adjuntos y
comprobante) conservan su propio tope.

## 5. Dominio del portal (sin CORS en el gateway)

El navegador llama al proxy del propio portal (`/api/v1/persona-natural/*` en su mismo host), que reenvía al gateway de servidor a
servidor: **no se registra el origen del portal en el CORS del gateway** (la política es global y con credenciales; abrirla
daría al front acceso con credenciales a todas las rutas de FLIT 2). Lo que sí hace falta:

- el vhost de nginx del dominio del portal hacia `127.0.0.1:${PERSONANATURAL_PORT}` (4031/5031/6031, propuestos);
- que nginx **sobrescriba** `X-Real-IP` y `X-Forwarded-For` con `$remote_addr` (ver §4.1 y el README de `frontend-personanatural`).

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

1. En `flit-gitops/services/flit2-<ambiente>/kustomization.yaml`, agregar las imágenes `ghcr.io/flitsas/flitdev/core-personanatural` y `ghcr.io/flitsas/flitdev/frontend-personanatural`.
2. En `cd.yml` (`deploy-gitops`): sumar `build-core-personanatural` y `build-frontend-personanatural` a `needs`, sus pares
   `R_`/`D_` y las líneas `"core-personanatural:${{ env.CORE_PERSONANATURAL_IMAGE }}:CORE_PERSONANATURAL"` y
   `"frontend-personanatural:${{ env.FRONTEND_PERSONANATURAL_IMAGE }}:FRONTEND_PERSONANATURAL"` a la lista. **No hacerlo antes del
   paso 1**: el despliegue de todo FLIT 2 fallaría.
3. ConfigMap y sealed-secret con las variables de los pasos 1 a 5.

## Pendiente de confirmar

- Puertos 4029/5029/6029 y gRPC 8087.
- Dominio del portal y su vhost de nginx; puertos 4031/5031/6031 de la web.
- Dirección gRPC de core-api que usará el BFF para consultar y crear trámites (se agrega con las HU del Feature B/D).
