# Handoff de VPS: Impuestos

HU #14694 (ADR-0093, plan [docs/impuestos/plan-implementacion.md](../impuestos/plan-implementacion.md) §8 y §11). Para el
líder técnico. Cada paso trae qué hacer, cómo comprobarlo y cómo volver atrás. Se aplica **primero en DEV**; QA y PDN
solo con aprobación del líder y después de que DEV esté estable. Mismo modelo que
[handoff-vps-diagnostico.md](handoff-vps-diagnostico.md).

> Estado (2026-11-25): escrito con M20 y actualizado en M20b tras M07–M14 (y las claves del file-manager de M11). Todas las
> variables de §4 ya las lee el servicio. Los puertos (G-4) y el cliente OIDC (G-5) están **propuestos** y los confirma el
> líder.

**Qué llega.** Dos contenedores en el perfil `impuestos` del compose:

- `core-impuestos` (.NET 10): API del producto, detrás del gateway (`/api/v1/impuestos/*`). Registra su manifiesto por el
  gateway, pregunta a Identidad (gRPC) si la empresa del token tiene Impuestos, replica los organismos de tránsito de
  core-api (gRPC) y llama **directo** al API de liquidación del proveedor (ADR-0093). Recibe los avisos firmados del API
  en `/api/v1/impuestos/avisos/*`, ruta anónima: la credencial es la firma HMAC.
- `frontend-impuestos` (Next.js): la app web en `<amb>.impuestos.flitsas.online`, con el login del hub.

**Por qué es seguro fusionar antes de encender.** El perfil `impuestos` no arranca hasta que el `.env` lo pida
(`COMPOSE_PROFILES=…,impuestos`), y ninguna variable de Impuestos es obligatoria para el resto del compose (no hay `:?`).
Fusionar construye las imágenes, pero no levanta nada.

| Ambiente | Web (`IMPUESTOS_PORT`) | API interna (`CORE_IMPUESTOS_PORT`) | gRPC (`CORE_IMPUESTOS_GRPC_PORT`) | Host |
|---|---|---|---|---|
| DEV | 4034 | 4033 | 8089 | `dev.impuestos.flitsas.online` |
| QA | 5034 | 5033 | 8089 | `qa.impuestos.flitsas.online` |
| PDN | 6034 | 6033 | 8089 | `impuestos.flitsas.com` (la raíz de PDN es `Suite__Hosts__Root=flitsas.com`) |

La API y el gRPC no se publican: viven en la red de Docker. Los puertos los exporta el CD por ambiente
([despliegue-y-puertos.md](../despliegue-y-puertos.md) §2.d). El host lo arma el hub con `Suite:Hosts`
(`<ambiente>.<producto>.<raíz>`, contrato §1): es el mismo que usa el `redirect_uri` del cliente OIDC `impuestos`.

**Los tres ambientes por GitOps.** DEV, QA (Argo CD en la 177) y PDN (Argo CD en la 169) se despliegan con
`deploy-gitops` de `cd.yml`, que ya lista `core-impuestos` y `frontend-impuestos`. Si la `kustomization.yaml` del
ambiente todavía no declara esas imágenes, el CD las **omite** con un aviso, sin cortar el despliegue de lo demás. La
configuración vive en `flit-gitops/services/flit2-<ambiente>/`, no en el compose: el compose es la referencia de nombres y
valores. El detalle para DEV está en [despliegue-develop-impuestos.md](despliegue-develop-impuestos.md).

## 1. Base de datos: usuario dueño del esquema `impuestos` (gate G-2)

```bash
CLAVE=$(openssl rand -base64 32 | tr -d '/+=')
psql "$ADMIN_URL" -v servicio=impuestos -v conexiones=20 -v clave="$CLAVE" \
     -f deploy/postgres/servicio-con-esquema-propio.sql
psql "postgresql://flit_impuestos:$CLAVE@<host>/<base>" -v servicio=impuestos -v ajeno=tramites \
     -f deploy/postgres/verificar-aislamiento.sql
```

En el `.env`: `CONNECTION_STRING_IMPUESTOS=Host=<host>;Port=5432;Database=<base>;Username=flit_impuestos;Password=<CLAVE>;Maximum Pool Size=20`.
El servicio migra su esquema al arrancar (`Servicio__MigrateOnStartup`, `true` por defecto): 13 tablas más
`data_protection_keys`.

**Comprobar:** `psql "$ADMIN_URL" -c "\du flit_impuestos"`. **Volver atrás:** `DROP SCHEMA impuestos CASCADE; DROP ROLE flit_impuestos;`
(solo si nunca se usó: el esquema guarda credenciales cifradas del API por empresa).

## 2. RabbitMQ: usuario y permisos (gate G-8)

core-impuestos publica en su exchange `flit.impuestos`, consume sus colas `impuestos.*` y deja el correo «Lote terminado»
como trabajo en `flit.notificaciones`. Además, core-api (usuario `tramites`) y core-identity (usuario `plataforma`) le
dejan trabajos en `flit.impuestos` (`impuestos.producto.cambiado`, `impuestos.aprovisionamiento.reintentar`, contrato
[impuestos-events.v1.yaml](../../contracts/asyncapi/impuestos-events.v1.yaml)).

```bash
CLAVE_MQ=$(openssl rand -base64 32 | tr -d '/+=')
deploy/rabbitmq/usuario-de-servicio.sh impuestos "$CLAVE_MQ" notificaciones
```

En el `.env`: `RABBITMQ_URL_IMPUESTOS=amqp://impuestos:<CLAVE_MQ>@rabbitmq:5672/flit`. Sin ella, con el perfil encendido,
core-impuestos no arranca y el log lo dice.

**Escritura de `tramites` y `plataforma` en `flit.impuestos`.** No vuelva a correr `usuario-de-servicio.sh` para esos dos
usuarios: **cambia su clave** y core-api o core-identity pierden el broker. Sume el destino con `set_permissions`,
conservando lo que ya tienen (léalo antes con `list_user_permissions`):

```bash
docker compose exec -T rabbitmq rabbitmqctl list_user_permissions -p flit tramites
docker compose exec -T rabbitmq rabbitmqctl list_user_permissions -p flit plataforma
# Agregar |flit\.impuestos a la expresión de ESCRITURA que devolvió cada uno; configurar y leer quedan iguales.
```

**Exchange y colas por adelantado (M20b).** `deploy/rabbitmq/definitions.json` trae `flit.impuestos` y las colas
`impuestos.plataforma`, `impuestos.credenciales`, `impuestos.liquidaciones` e `impuestos.recibos` con sus bindings (tabla en
[deploy/rabbitmq/README.md](../../deploy/rabbitmq/README.md)). Recárguelo **antes** de encender el perfil: core-api y
core-identity comprueban `flit.impuestos` en modo pasivo al publicar y, si core-impuestos nunca arrancó, el exchange no
existe y su outbox falla.

```bash
docker compose -f docker-compose.prod.yml exec rabbitmq rabbitmqctl import_definitions /etc/rabbitmq/definitions.json
```

Las `.retry.<n>`, las `.dlq` y los exchanges `<cola>.reintentos` los declara cada consumidor al arrancar; el patrón propio
del usuario `impuestos` (`^(flit\.impuestos|impuestos\..*)$`) ya los cubre, también los de `impuestos.recibos` (M11). Los
mensajes de Impuestos solo llevan identificadores (sin PII), así que no entran en la política `dlq-7d`.

**Comprobar:** `rabbitmqctl list_user_permissions -p flit impuestos`, `rabbitmqctl list_exchanges -p flit | grep impuestos`
y, ya arriba el servicio,
`rabbitmqctl list_queues -p flit name consumers | grep impuestos`. **Volver atrás:** `rabbitmqctl delete_user impuestos`
(con el servicio detenido) y quitar `flit\.impuestos` de la escritura de `tramites` y `plataforma`.

## 3. Cliente de servicio `svc-impuestos` y cliente OIDC `impuestos` (gate G-5)

`svc-impuestos` ya está declarado en `services/core-api/src/Flit.Api/appsettings.json` (`Suite:Oidc:ServiceClients`) con
sus scopes; el detalle de cada uno está en el [contrato de plataforma](contrato-plataforma-v1.md) §3. Falta el secreto,
el mismo valor en los dos lados:

```
SVC_IMPUESTOS_CLIENT_SECRET=<openssl rand -base64 48>
```

El compose lo pasa a core-api y core-identity (`Suite__Oidc__ServiceClients__svc-impuestos__Secret`, ancla
`x-core-api-env`) y a core-impuestos (`Platform__ServiceClient__ClientSecret`). **Reiniciar core-api** (y core-identity si
está aparte) para que registre el cliente.

El cliente OIDC público `impuestos` (login de la app web, PKCE) se registra solo, con el host del ambiente
(`Suite__Hosts__Environment`, §7.2 de [handoff-vps-suite.md](handoff-vps-suite.md)). Esperado en DEV: `redirect_uri`
`https://dev.impuestos.flitsas.online/auth/callback`.

## 4. Resto del `.env`

### 4.1 Lo que siempre va

```
COMPOSE_PROFILES=<los que ya tenga>,impuestos
IMPUESTOS_SESSION_SECRET=<openssl rand -base64 48, distinto del del hub y de los de los otros productos>
IMPUESTOS_SVC_IDENTIDAD_ADDRESS=http://core-identity:8083   # obligatoria: vacía = ninguna empresa habilitada (403)
```

La suscripción simulada (`Impuestos__Identidad__SimularSuscripcionActiva`) es solo para pruebas en local y no se pone en
ningún ambiente.

El compose ya fija, sin variable del `.env`: `Impuestos__Tramites__Address=http://core-api:${CORE_API_GRPC_PORT}` (réplica
de organismos para homologar la secretaría; sin ella la réplica no se sincroniza) e
`Impuestos__Plataforma__GatewayUrl=http://gateway:${GATEWAY_PORT}` (manifiesto).

### 4.2 API de liquidación (gate G-9)

Los emite el proveedor (FLIT API) por ambiente y llegan por canal seguro. Además, la integración tiene que estar
habilitada en ese ambiente del proveedor: si no, el alta de cada empresa responde 404.

```
IMPUESTOS_SVC_API_BASE_URL=https://<base del API del ambiente>
IMPUESTOS_SVC_INTEGRATION_TOKEN=<token de integración del ambiente>
IMPUESTOS_SVC_HTTP_TIMEOUT_SECONDS=30
```

La base tiene que ser https (http solo en loopback, para el stub local). **Sin las dos el servicio no arranca** (desde
M07, `ServicioSettings`) y el log dice qué variable falta. El token solo se usa en el alta: las llamadas de cada empresa
van con su propia `api_key`, cifrada en la base.

### 4.3 RUNT, aviso, correo y ritmo (M09–M12)

```
IMPUESTOS_SVC_CONSULTAS_ADDRESS=http://core-consultas:8084   # RUNT de cada placa; vacía = ítems en error reintentable
IMPUESTOS_SVC_AVISOS_URL_BASE=https://<host público>/api/v1/impuestos/avisos   # gate G-6, §8; vacía = solo conciliación por GET
IMPUESTOS_SVC_AVISOS_URL_FRONTEND=https://<prefijo>impuestos.flitsas.online   # enlace del correo «Lote terminado»; opcional
```

`Impuestos__Avisos__PermitirHttpLocal` queda fijo en `false` en el compose. Afinación con los defectos del código (solo si
hace falta): `IMPUESTOS_SVC_CONCURRENCIA_EMPRESA` (2), `IMPUESTOS_SVC_CONCURRENCIA_GLOBAL` (6),
`IMPUESTOS_SVC_TASA_POR_SEGUNDO` (2), `IMPUESTOS_SVC_ALTA_INTERVALO_SEGUNDOS` (10),
`IMPUESTOS_SVC_CONCILIACION_ESPERA_INCIERTO` (`00:30:00`) e `IMPUESTOS_SVC_AVISOS_POR_IP_POR_MINUTO` (60).

### 4.4 Recibos PDF: file-manager (gate G-11, M11)

Sin `FILE_MANAGER_BASE_URL` core-impuestos **no arranca**. Usa las mismas `FILE_MANAGER_*` que core-api y core-diagnostico
(el compose tiene el mismo defecto), con su categoría propia, que hay que registrar en el file-manager antes de encender
(ADR-0057):

```
IMPUESTOS_FILE_MANAGER_CATEGORY=impuestos   # el defecto del compose; solo si el file-manager usa otro nombre
# FILE_MANAGER_AUTH_TOKEN ya está en el .env si core-api lo usa (Contabo, PDN)
```

`Impuestos__Archivos__GuardarEnMemoria` es solo para local y pruebas: nunca en un ambiente.

## 5. Certificado de Data Protection (gate G-10, obligatorio)

Las credenciales del API por empresa (`api_key` y `webhook_secret`), la PII del propietario y el resultado completo se
guardan cifrados. El anillo de llaves vive en `impuestos.data_protection_keys`, cifrado con un certificado `.pfx` RSA que
**no** está en la base, ni en el repo, ni en la imagen. Sin él (o con clave errada) `core-impuestos` **no arranca**, en
ningún ambiente (los tres corren como Development: no hay excepción).

**Uno por ambiente, distinto del de Diagnóstico, con respaldo.** Perder el certificado o su clave deja **ilegibles para
siempre** las credenciales ya guardadas: habría que volver a dar de alta (o cargar a mano) cada empresa. Antes de
encender, guardar `.pfx` + clave en el gestor de secretos del equipo (fuera del VPS). No copiar el de DEV a QA/PDN.

```bash
sudo install -d -m 700 /etc/flit/secrets
CLAVE=$(openssl rand -base64 32 | tr -d '/+=')
openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj "/CN=flit-impuestos-<amb>" -keyout /tmp/dp.key -out /tmp/dp.crt
sudo openssl pkcs12 -export -inkey /tmp/dp.key -in /tmp/dp.crt -out /etc/flit/secrets/impuestos-dp.pfx -passout pass:"$CLAVE"
shred -u /tmp/dp.key /tmp/dp.crt
sudo chmod 400 /etc/flit/secrets/impuestos-dp.pfx   # legible por el usuario con que corre el contenedor
```

En el `.env`:

```
IMPUESTOS_SVC_DP_CERTIFICADO_RUTA=/etc/flit/secrets/impuestos-dp.pfx   # archivo del host; se monta en /run/secrets/impuestos_dp_pfx
IMPUESTOS_SVC_DP_CERTIFICADO_CLAVE=<CLAVE>
```

El compose pasa `Impuestos__DataProtection__CertificadoRuta=/run/secrets/impuestos_dp_pfx` y
`Impuestos__DataProtection__CertificadoClave=${IMPUESTOS_SVC_DP_CERTIFICADO_CLAVE}`. En DEV/QA por GitOps el equivalente es
un sealed secret con el `.pfx` montado como archivo y la clave como variable.

**Comprobar:** el log de arranque no dice que falta el certificado y `/health/ready` responde. **Volver atrás:** detener el
servicio (§9); nunca borrar el certificado ni las filas de `data_protection_keys`. **No rotar** hasta que exista soporte
para certificados anteriores: si se compromete, escalar al líder técnico antes de tocar nada.

## 6. Encender

```bash
docker compose pull core-impuestos frontend-impuestos
docker compose up -d core-impuestos frontend-impuestos
docker compose logs -f core-impuestos     # debe decir que migró y registró el manifiesto
```

Si falta una variable obligatoria, el servicio no arranca y el log dice cuál y de qué variable del `.env` sale.

**Comprobar:**

```bash
docker compose exec core-impuestos wget -qO- http://localhost:${CORE_IMPUESTOS_PORT:-4033}/health/ready
curl -s http://127.0.0.1:${GATEWAY_PORT:-4002}/api/v1/impuestos/sesion -o /dev/null -w '%{http_code}\n'   # 401 = el gateway llega al servicio (sin token)
curl -s http://127.0.0.1:${IMPUESTOS_PORT:-4034}/healthz
```

Un 502/503 en la segunda línea es el gateway sin destino: revisar
`ReverseProxy__Clusters__core-impuestos-cluster__Destinations__core-impuestos-1__Address`.

## 7. Gateway

La ruta `impuestos-route` (`/api/v1/impuestos/{**catch-all}` → `core-impuestos-cluster`) viene en
`services/core-api/src/Flit.Gateway/appsettings.json`, sin política del gateway: el token lo valida el servicio y
`/avisos/*` es anónimo con firma. No choca con `/api/v1/webhooks/*` (core-api). El compose sobrescribe el destino con
`CORE_IMPUESTOS_PORT`. Llega con la imagen de core-api: el gateway se recrea en el mismo despliegue.

## 8. nginx, host del webhook y hub

1. **App web.** Server{} de `<amb>.impuestos.flitsas.online` con `location /` → `127.0.0.1:<IMPUESTOS_PORT>`; plantilla en
   [deploy/edge/nginx/flit-suite-hosts.conf.example](../../deploy/edge/nginx/flit-suite-hosts.conf.example). DNS y
   certificado del host antes. `nginx -t && systemctl reload nginx`. **Volver atrás:** la redirección a
   `/proximamente/impuestos` del hub.
2. **Host público del webhook (gate G-6).** El API de liquidación llama por POST a
   `IMPUESTOS_SVC_AVISOS_URL_BASE/<token>`: https con certificado válido, **sin redirecciones** (no las sigue) y con
   `/api/v1/impuestos/avisos/` llegando al gateway sin pasar por el BFF de la app web. Dos opciones, la decide el líder:
   - `https://api.<amb>.flitsas.online/api/v1/impuestos/avisos` (ese host ya va entero al gateway: no hay que tocar nginx);
   - `https://<amb>.impuestos.flitsas.online/api/v1/impuestos/avisos`, con la `location` dedicada de la plantilla.
   **Comprobar:** `curl -s -o /dev/null -w '%{http_code} %{redirect_url}\n' -X POST https://<host>/api/v1/impuestos/avisos/x`
   no devuelve 3xx ni 502.
3. Por PR: quitar `impuestos` de `Suite:Hosts:ComingSoon` (core-api `appsettings.json`) para ese ambiente.
4. El SuperAdmin enciende Impuestos por empresa en Compañías → Productos; el diálogo muestra la vista previa del alta y el
   chip de aprovisionamiento sigue el estado.

## 9. Si algo sale mal

`docker compose stop core-impuestos frontend-impuestos` y quitar `impuestos` de `COMPOSE_PROFILES`: el resto del stack no
depende de ellos. Los trabajos que core-api y core-identity dejen en `flit.impuestos` mientras tanto esperan en la cola
`impuestos.plataforma` (o se descartan si la cola no se declaró nunca); no se borran las colas.

- **Versión anterior de la imagen:** en GitOps, revertir el commit de digests de `flit-gitops`; a mano, fijar
  `CORE_IMPUESTOS_TAG`/`FRONTEND_IMPUESTOS_TAG` al `sha-<commit>` anterior y `docker compose up -d` de los dos.
- **Migraciones:** viven en su esquema (`impuestos.__EFMigrationsHistory`) y no tocan a ningún otro servicio. No se
  revierten a mano: una imagen anterior arranca sobre el esquema nuevo si la migración fue aditiva; si no, escalar al
  líder técnico.
- **Nunca** borrar `impuestos.data_protection_keys`, el certificado ni el volumen de la base.
