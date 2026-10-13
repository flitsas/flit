# Handoff de VPS: Gestión de Comparendos

Epic #12878 (GDC). Para el líder técnico, con su sesión de Claude conectada a la VPS. Cada paso trae qué hacer, cómo
comprobarlo y cómo volver atrás. Se aplica **primero en DEV**; QA y PDN solo con aprobación del líder y después de que
DEV esté estable.

**Qué llega.** Dos contenedores nuevos en el perfil `comparendos` del compose:

- `core-comparendos` (.NET 10): API del producto. Habla con la plataforma por el gateway (tokens, manifiesto), manda
  los correos por `core-notificaciones` (gRPC) y guarda archivos en el file-manager corporativo.
- `frontend-comparendos` (Next.js): la app web en `<amb>.comparendos.flitsas.online`, con el login del hub.

**Por qué es seguro fusionar antes de encender.** El perfil `comparendos` no arranca hasta que el `.env` lo pida
(`COMPOSE_PROFILES=…,comparendos`). Fusionar despliega las imágenes, pero no levanta nada.

**Qué no hace todavía (se conecta al final, con información que hay que pedir):** las fuentes reales (SmartCloud por
identificación y por placa, API municipal), la API de contratos de Renting y la llave del OCR. Mientras no estén, fuera
de DEV **nada se simula**: los ciclos de ingesta se omiten (y lo dicen en el log), el OCR rechaza paquetes y la consulta
de contraventor responde error. En DEV se simulan para poder recorrer todo el producto.

## Requisitos

- La suite encendida en el ambiente (fases 0 a 3 de [handoff-vps-suite.md](handoff-vps-suite.md)).
- `core-notificaciones` y RabbitMQ funcionando ([handoff-vps-consultas-notificaciones.md](handoff-vps-consultas-notificaciones.md)).
- Acceso a la carpeta del despliegue (`$HOSTINGER_DEPLOY_PATH`), a su `.env` y a una conexión de administrador de
  Postgres (`$ADMIN_URL`).

| Ambiente | Web (`COMPARENDOS_PORT`) | API interna (`CORE_COMPARENDOS_PORT`) | gRPC | Host |
|---|---|---|---|---|
| DEV | 4023 | 4028 | 8086 | `dev.comparendos.flitsas.online` |
| QA | 5023 | 5028 | 8086 | `qa.comparendos.flitsas.online` |
| PDN | 6023 | 6028 | 8086 | `comparendos.flitsas.online` |

La API y el gRPC no se publican: viven en la red de Docker y se llega a la API por el gateway (`/api/v1/comparendos/*`).

## 1. Base de datos: usuario dueño del esquema `comparendos`

```bash
CLAVE=$(openssl rand -base64 32 | tr -d '/+=')
psql "$ADMIN_URL" -v servicio=comparendos -v conexiones=20 -v clave="$CLAVE" \
     -f deploy/postgres/servicio-con-esquema-propio.sql
```

En el `.env`:

```
CONNECTION_STRING_COMPARENDOS=Host=postgres;Port=5432;Database=flit;Username=flit_comparendos;Password=<CLAVE>;Maximum Pool Size=20
```

El servicio crea y migra su esquema al arrancar (una sola migración, `GDC_ModeloInicial`) y siembra calendario
2015–2040, 298 organismos de tránsito, 11 tipologías de DP, plantillas FLIT y Renting, el prompt OCR v1 y los trabajos
programados. La semilla es idempotente.

**Comprobar:** `psql "$ADMIN_URL" -c "\du flit_comparendos"`. **Volver atrás:** `DROP SCHEMA comparendos CASCADE; DROP ROLE flit_comparendos;`
(solo si nunca se usó).

## 2. RabbitMQ: usuario del servicio

```bash
deploy/rabbitmq/usuario-de-servicio.sh comparendos <clave> notificaciones
```

En el `.env`: `RABBITMQ_URL_COMPARENDOS=amqp://comparendos:<clave>@rabbitmq:5672/flit`.

## 3. Cliente de servicio `svc-comparendos`

Ya está declarado en la configuración de la API con sus scopes (`platform.manifest`, `platform.consultas`,
`platform.identidad.read`, `platform.notificaciones.send`). Falta el secreto, el mismo valor en los dos lados:

```
SVC_COMPARENDOS_CLIENT_SECRET=<openssl rand -base64 48>
```

El compose lo pasa a core-api/core-identity (`Suite__Oidc__ServiceClients__svc-comparendos__Secret`) y a
core-comparendos (`Platform__ServiceClient__ClientSecret`). **Reiniciar core-api** (y core-identity si está aparte)
después de ponerlo.

El cliente OIDC `comparendos` (login de la app web) ya se registra solo con el host del ambiente (§7.2 de
handoff-vps-suite.md).

## 4. Resto del `.env`

```
COMPOSE_PROFILES=<los que ya tenga>,comparendos
COMPARENDOS_SESSION_SECRET=<openssl rand -base64 48, distinto del FLIT_SESSION_SECRET del hub>
COMPARENDOS_URL=https://dev.comparendos.flitsas.online      # sin prefijo en PDN
# Opcionales (vacíos = valor por defecto o apagado):
# COMPARENDOS_FILE_MANAGER_CATEGORY=comparendos             # sin FILE_MANAGER_BASE_URL los archivos van a la base (solo DEV)
# COMPARENDOS_OCR_API_KEY=                                  # llave de Anthropic; sin ella el OCR no acepta paquetes fuera de DEV
# COMPARENDOS_SIMULACION=                                   # vacío = solo en Dev; true para una demo en QA; nunca en PDN
COMPARENDOS_IDENTIDAD_ADDRESS=http://core-identity:8083     # obligatoria: sin ella ninguna empresa cuenta como habilitada
```

**Activación controlada (HU #13521).** Encender el producto no consulta nada. Una empresa se consulta solo si: tiene
Comparendos habilitado (Identidad, gRPC: requiere `CORE_IDENTITY_GRPC_PORT` en core-identity), el SuperAdmin le hizo
su configuración de consultas en el parametrizador (canal, proveedor y agenda) y la pasó a **Activa**, y el proveedor
está conectado en core-consultas. Los avisos, reportes y la consulta de contraventor también se saltan las empresas sin
el producto.

**Fuentes (HU #13520).** Van por core-consultas (`FuentesComparendosService`, el mismo gRPC 8084 y scope
`platform.consultas` que ya tiene `svc-comparendos`). Para usar Verifik SIMIT en un ambiente: en core-consultas
`Consultations__VerifikSimitMode=real` y la llave `VERIFIK_*` (las mismas de Trámites). SmartCloud y municipal
aparecen como «no conectados» hasta tener contrato.

`NOTIFICACIONES_REMOTO_ADDRESS` (la misma de core-api) apunta los correos a core-notificaciones. Si quedara vacía, los
correos se escriben en el log y no salen: revisarla.

## 5. Encender

```bash
cd $HOSTINGER_DEPLOY_PATH
docker compose pull core-comparendos frontend-comparendos
docker compose up -d core-comparendos frontend-comparendos
docker compose logs -f core-comparendos     # debe decir que migró, sembró y registró el manifiesto
```

Si falta una variable, el servicio no arranca y el log dice exactamente cuál y de qué variable del `.env` sale.

**Comprobar:**

```bash
docker compose exec core-comparendos wget -qO- http://localhost:${CORE_COMPARENDOS_PORT:-4028}/health/ready
curl -s http://127.0.0.1:${GATEWAY_PORT:-4002}/api/v1/comparendos/sesion -o /dev/null -w '%{http_code}\n'   # 401 sin token = la ruta llega
curl -s http://127.0.0.1:${COMPARENDOS_PORT:-4023}/healthz
```

En la base: `SELECT count(*) FROM comparendos.organismo_transito;` → 298, y el manifiesto en la plataforma
(`SELECT * FROM platform.product_manifests WHERE product_code = 'comparendos';` o el nombre de tabla vigente).

## 6. nginx

El server{} de `<amb>.comparendos.flitsas.online` ya existe y hoy redirige a `/proximamente/comparendos` del hub
(§9 de handoff-vps-suite.md). Cambiar su `location /` por un `proxy_pass` al puerto web, con las mismas reglas que
Trámites (§6.1 y §6.2 de handoff-vps-suite.md):

```nginx
    proxy_read_timeout 120s;             # la carga de 200 archivos OCR y la de pagos tardan
    client_max_body_size 50m;            # paquetes OCR y Excel de pagos
    proxy_set_header X-Real-IP $remote_addr;   # el límite del portal del conductor usa esta cabecera
    location / { proxy_pass http://127.0.0.1:4023; }
```

`nginx -t && systemctl reload nginx`. **Volver atrás:** restaurar la redirección a `/proximamente/comparendos`.

## 7. Mostrarlo en el hub

1. Por PR: quitar `comparendos` de `Suite:Hosts:ComingSoon` en `services/core-api/src/Flit.Api/appsettings.json` para
   ese ambiente (o por variable de entorno `Suite__Hosts__ComingSoon__0=diagnostico` si se quiere solo en uno).
2. El SuperAdmin enciende Comparendos por empresa en Compañías → Productos y asigna `admin_comparendos` a quien
   corresponda (los roles `admin_comparendos`, `operador_comparendos` y `consulta_comparendos` salen del manifiesto).
3. En Comparendos → Parametrizador, el SuperAdmin activa por empresa las capacidades (DP, OCR, consulta y cambio de
   contraventor, reportes), el conjunto de plantillas y la integración de contraventor.

## 8. Lo que queda para el final (información que hay que pedir)

| Pieza | Qué falta | Variables |
|---|---|---|
| SmartCloud por identificación y por placa (#13465, #13466) | WSDL, credenciales y una respuesta real (también la de un NIT sin comparendos) | Adaptador en core-consultas; variables de core-consultas |
| API municipal (#13467) | Esquema de la respuesta y autenticación | Adaptador en core-consultas; variables de core-consultas |
| API de contratos de Renting | Contrato (placa + fecha → conductor) | `COMPARENDOS_RENTING_API_URL`, `COMPARENDOS_RENTING_API_KEY` |
| OCR | Llave de Anthropic y comparación con una muestra real | `COMPARENDOS_OCR_API_KEY` |

Hasta entonces los trabajos de ingesta corren y se omiten solos (sin alertas), y las pantallas lo dicen.

## 9. Si algo sale mal

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| El contenedor reinicia y el log dice «falta configuración: …» | Variable sin poner | Agregarla al `.env` (el mensaje dice cuál) |
| 401 en todo desde la app | Secreto de `svc-comparendos` distinto en core-api y en el servicio, o core-api sin reiniciar | Igualar y reiniciar core-api |
| La app no lista el producto / 403 | La empresa no tiene Comparendos encendido o el usuario no tiene rol del producto | §7 |
| Los correos no llegan | `NOTIFICACIONES_REMOTO_ADDRESS` vacía (modo consola) o core-notificaciones caído | Revisar §4 y el Centro de Mensajes del producto |
| «La fuente no está conectada» en el log | Esperado fuera de DEV hasta conectar SmartCloud/municipal | §8 |

**Volver atrás en general:** quitar `comparendos` de `COMPOSE_PROFILES` y `docker compose up -d --remove-orphans`. Los
datos quedan en su esquema; nada de core-api depende de él.
