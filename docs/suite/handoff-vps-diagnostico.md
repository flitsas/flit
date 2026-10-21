# Handoff de VPS: Diagnóstico Vehicular

HU #13734 (F00.1-H8, Feature #13715). Para el líder técnico. Cada paso trae qué hacer, cómo comprobarlo y cómo volver
atrás. Se aplica **primero en DEV**; QA y PDN solo con aprobación del líder y después de que DEV esté estable. Mismo
modelo que [handoff-vps-comparendos.md](handoff-vps-comparendos.md).

**Qué llega.** Dos contenedores en el perfil `diagnostico` del compose:

- `core-diagnostico` (.NET 10): API del producto, detrás del gateway (`/api/v1/diagnostico/*`). Registra su manifiesto
  por el gateway y pregunta a Identidad (gRPC) si la empresa del token tiene Diagnóstico.
- `frontend-diagnostico` (Next.js): la app web en `<amb>.diagnostico.flitsas.online`, con el login del hub.

**Por qué es seguro fusionar antes de encender.** El perfil `diagnostico` no arranca hasta que el `.env` lo pida
(`COMPOSE_PROFILES=…,diagnostico`). Fusionar construye las imágenes, pero no levanta nada.

| Ambiente | Web (`DIAGNOSTICO_PORT`) | API interna (`CORE_DIAGNOSTICO_PORT`) | gRPC | Host |
|---|---|---|---|---|
| DEV | 4024 | 4029 | 8087 | `dev.diagnostico.flitsas.online` |
| QA | 5024 | 5029 | 8087 | `qa.diagnostico.flitsas.online` |
| PDN | 6024 | 6029 | 8087 | `diagnostico.flitsas.online` |

La API y el gRPC no se publican: viven en la red de Docker. Los puertos los exporta el CD por ambiente
([despliegue-y-puertos.md](../despliegue-y-puertos.md) §2.d).

**DEV y QA por GitOps.** DEV y QA se despliegan con `deploy-gitops` (Argo CD en la 177) y su
configuración vive en `flit-gitops/services/flit2-<ambiente>/`. Ese job todavía **no** lleva las imágenes de
Diagnóstico: antes de sumarlas a su lista, la `kustomization.yaml` de cada ambiente tiene que declarar
`ghcr.io/flitsas/flitdev/core-diagnostico` y `ghcr.io/flitsas/flitdev/frontend-diagnostico` (si no, `yq -e` corta el
despliegue de todo el ambiente) y los manifiestos tienen que traer las variables de abajo.

## 1. Base de datos: usuario dueño del esquema `diagnostico`

```bash
CLAVE=$(openssl rand -base64 32 | tr -d '/+=')
psql "$ADMIN_URL" -v servicio=diagnostico -v conexiones=20 -v clave="$CLAVE" \
     -f deploy/postgres/servicio-con-esquema-propio.sql
psql "postgresql://flit_diagnostico:$CLAVE@<host>/<base>" -v servicio=diagnostico -v ajeno=tramites \
     -f deploy/postgres/verificar-aislamiento.sql
```

En el `.env`: `CONNECTION_STRING_DIAGNOSTICO=Host=postgres;Port=5432;Database=flit;Username=flit_diagnostico;Password=<CLAVE>;Maximum Pool Size=20`.
El servicio migra su esquema al arrancar.

**Comprobar:** `psql "$ADMIN_URL" -c "\du flit_diagnostico"`. **Volver atrás:** `DROP SCHEMA diagnostico CASCADE; DROP ROLE flit_diagnostico;`
(solo si nunca se usó).

## 2. RabbitMQ: exchange y usuario

El exchange `flit.diagnostico` está en `deploy/rabbitmq/definitions.json`; en un broker que ya corre, recargarlo:

```bash
docker compose -f docker-compose.prod.yml exec rabbitmq rabbitmqctl import_definitions /etc/rabbitmq/definitions.json
deploy/rabbitmq/usuario-de-servicio.sh diagnostico <clave>
```

En el `.env`: `RABBITMQ_URL_DIAGNOSTICO=amqp://diagnostico:<clave>@rabbitmq:5672/flit`.

## 3. Cliente de servicio `svc-diagnostico`

Declarado en `services/core-api/src/Flit.Api/appsettings.json` con `platform.manifest` (manifiesto),
`platform.identidad.read` (suscripción por petición) y `platform.tramites.catalogos` (tipos de trámite publicados).
Falta el secreto, el mismo valor en los dos lados:

```
SVC_DIAGNOSTICO_CLIENT_SECRET=<openssl rand -base64 48>
```

El compose lo pasa a core-api/core-identity (`Suite__Oidc__ServiceClients__svc-diagnostico__Secret`) y a
core-diagnostico (`Platform__ServiceClient__ClientSecret`). **Reiniciar core-api** (y core-identity si está aparte).

El cliente OIDC público `diagnostico` (login de la app web, PKCE) se registra solo con el host del ambiente
(`Suite:Hosts`, §7.2 de [handoff-vps-suite.md](handoff-vps-suite.md)).

## 4. Resto del `.env`

```
COMPOSE_PROFILES=<los que ya tenga>,diagnostico
DIAGNOSTICO_SESSION_SECRET=<openssl rand -base64 48, distinto del del hub y del de Comparendos>
DIAGNOSTICO_IDENTIDAD_ADDRESS=http://core-identity:8083   # obligatoria: vacía = ninguna empresa habilitada (403)
```

La suscripción simulada es solo para pruebas en local y no se pone en ningún ambiente.

## 5. Encender

```bash
docker compose pull core-diagnostico frontend-diagnostico
docker compose up -d core-diagnostico frontend-diagnostico
docker compose logs -f core-diagnostico     # debe decir que migró y registró el manifiesto
```

Si falta una variable, el servicio no arranca y el log dice cuál y de qué variable del `.env` sale.

**Comprobar:**

```bash
docker compose exec core-diagnostico wget -qO- http://localhost:${CORE_DIAGNOSTICO_PORT:-4029}/health/ready
curl -s http://127.0.0.1:${GATEWAY_PORT:-4002}/api/v1/diagnostico/ -o /dev/null -w '%{http_code}\n'   # distinto de 502/503 = el gateway llega al servicio
curl -s http://127.0.0.1:${DIAGNOSTICO_PORT:-4024}/healthz
```

## 6. nginx y hub

1. El server{} de `<amb>.diagnostico.flitsas.online` hoy redirige a `/proximamente/diagnostico` del hub. Cambiar su
   `location /` por `proxy_pass http://127.0.0.1:<DIAGNOSTICO_PORT>;` con las mismas reglas que Comparendos.
   `nginx -t && systemctl reload nginx`. **Volver atrás:** restaurar la redirección.
2. Por PR: quitar `diagnostico` de `Suite:Hosts:ComingSoon` para ese ambiente.
3. El SuperAdmin enciende Diagnóstico por empresa en Compañías → Productos.

## 7. Si algo sale mal

`docker compose stop core-diagnostico frontend-diagnostico` y quitar `diagnostico` de `COMPOSE_PROFILES`: el resto del
stack no depende de ellos.
