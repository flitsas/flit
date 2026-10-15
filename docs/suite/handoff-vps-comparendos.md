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
`platform.identidad.read`, `platform.notificaciones.send`, `platform.tramites.catalogos`). El último es para la réplica
diaria del catálogo de organismos de tránsito (trabajo `SincronizacionOrganismos`, 3 a. m.): Comparendos llama al gRPC
de catálogos de core-api en `Comparendos__Tramites__Address` (por defecto `http://core-api:${CORE_API_GRPC_PORT}`, el
mismo puerto que ya usa ICT). Sin esa dirección queda la semilla de 298 organismos. Falta el secreto, el mismo valor en los dos lados:

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
`Consultations__VerifikSimitMode=real` y la llave `VERIFIK_*` (las mismas de Trámites). SmartCloud (los servicios SOAP
del SIMIT) y la API municipal usan el contrato del BackCrudSmartCloud de V1. Todo va en el `.env` y nada tiene valor
por defecto en el código:

| Variable | Valor |
|---|---|
| `SMARTCLOUD_ACCESS_KEY` | La clave de V1 (`SMART_CLOUD_ACCESS_KEY`). Secreta |
| `SMARTCLOUD_URL_IDENTIFICACION` | `https://www3.simit.org.co/ServiciosSimit/EstadoCuentaIdentificacion` |
| `SMARTCLOUD_URL_PLACA` | `https://www3.simit.org.co/ServiciosSimit/EstadoCuentaPlaca` |
| `MUNICIPAL_INFRACCIONES_BASE_URL` | `http://ec2-44-206-131-113.compute-1.amazonaws.com` (la de V1, `MUNICIPALITY_INFRACTION_API_BASE_URL`; la máquina de AWS sigue en producción) |
| `MUNICIPAL_INFRACCIONES_PATH` | `/infraction/api/Infraccion/EntregarInfraccion` (contrato de V1) |

Los timeouts (`*_TIMEOUT_SECONDS`) son opcionales: 60 s. Si falta alguna de las otras, la fuente aparece como «no
conectada» con el nombre de la variable que falta. La fuente municipal solo consulta por identificación.
Probado contra el SIMIT real el 2026-10-13: no pide registrar la IP, solo la clave. El día del despliegue, confirmar
con una consulta desde la VPS.

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
| SmartCloud por identificación y por placa (#13465, #13466) | Probado contra el SIMIT real el 2026-10-13 (vacío = `error_Cod` 0 sin filas; la placa lleva la clave delante; `error_Cod` 1 = clave rechazada). Falta una consulta desde la VPS el día del despliegue | `SMARTCLOUD_ACCESS_KEY` y las dos URLs en core-consultas |
| API municipal (#13467) | Conector probado contra la API de V1 el 2026-10-15 (NIT de Bancolombia): responde por municipio, y un municipio caído (timeout, SSL, HTML) queda como error de ese municipio sin tocar los demás. Con `?placa=` responde vacío: la consulta por placa sigue sin confirmar | `MUNICIPAL_INFRACCIONES_BASE_URL` y `MUNICIPAL_INFRACCIONES_PATH` en core-consultas (valores de V1 arriba) |
| API de contratos de Renting | Contrato (placa + fecha → conductor) | `COMPARENDOS_RENTING_API_URL`, `COMPARENDOS_RENTING_API_KEY` |
| OCR | Llave de Anthropic y comparación con una muestra real | `COMPARENDOS_OCR_API_KEY` |

Hasta entonces los trabajos de ingesta corren y se omiten solos (sin alertas), y las pantallas lo dicen.

### Revisar la API municipal de V1 antes de salir

En la consulta del 2026-10-15 (NIT de Bancolombia) 5 de los 8 municipios fallaron **del lado de la API de V1**, no de
Comparendos. Es probable que V1 en producción tenga el mismo problema:

| Municipio | Lo que respondió la API de V1 en `estadoRespuesta` |
|---|---|
| Medellín, Itagüí, Rionegro | `One or more errors occurred. (A task was canceled.)`: se le acaba el tiempo consultando ese municipio |
| Cali | `The SSL connection could not be established`: falla el certificado o el TLS hacia la fuente de Cali |
| Sabaneta | `Unexpected character encountered while parsing value: <`: la fuente devuelve HTML (página de error o mantenimiento) en vez de JSON |

Bello, Manizales y Envigado respondieron bien. Comparendos deja cada municipio caído como error de ese municipio (no
inactiva nada) y alerta, pero los comparendos de esos municipios no llegan hasta que la API de V1 los traiga.

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
