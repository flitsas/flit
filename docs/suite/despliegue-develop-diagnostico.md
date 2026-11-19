# Desplegar Diagnóstico en develop (ambiente DEV)

> Generado: 2026-11-04
> Rama de referencia: `feature/AB-12881-diagnostico-d0-d2`, commit `78217eeb9`. Las anclas `archivo:línea` son de ese commit, salvo las de `cd.yml` y `docker-compose.prod.yml`.
> Las anclas de `cd.yml` y `docker-compose.prod.yml` se reverificaron el 2026-11-04 sobre `71c1656f7`, con el merge de `origin/develop` ya terminado.

**Para quién es.** Para quien opera el servidor de DEV y no conoce el producto. **Qué es Diagnóstico:** un producto de la
FLIT Suite. Recibe placas o VIN, consulta fuentes (RUNT, SIMIT, avalúo, impuesto) por RabbitMQ y entrega un dictamen.

**Lea esto primero. Cuatro cosas cambian el resto del documento:**

1. **`develop` sí es DEV.** Un push a `develop` despliega en DEV (`.github/workflows/cd.yml:5`, `:42`, `:112-114`). Fusionar
   es desplegar.
2. **DEV no usa `docker-compose.prod.yml` ni un `.env`.** Corre en k3s con Argo CD. Su configuración vive en el repositorio
   `flit-gitops`, carpeta `services/flit2-dev/` (`configmaps.yaml` y `sealed-secrets.yaml`) (`cd.yml:1385-1387`). Una variable
   nueva en el compose **no llega sola**. Aquí el compose sirve de referencia para los nombres y valores. Los pasos con
   `docker compose` de [handoff-vps-diagnostico.md](handoff-vps-diagnostico.md) hay que traducirlos.
3. **Hoy nada de Diagnóstico está en `develop`.** Ver §1.
4. **Este despliegue no enciende el impuesto real.** Ver §9, punto 5.

**Contrastado con `flit-gitops` y la 177 el 2026-11-04.** Donde el resto del documento difiera, manda esta tabla. Los
valores son de ese día: vuelva a medirlos antes de ejecutar.

| Tema | En DEV | Consecuencia |
|---|---|---|
| Espacio de nombres | `flit2-dev`; broker en `deploy/rabbitmq` | Sustituye `<ns>` y `<pod-rabbitmq>` |
| Rol de Diagnóstico | Por ambiente: `flit_diagnostico_dev`, en la base `flitdev` | `servicio-con-esquema-propio.sql` no admite ese nombre: se crea a mano, como `flit_comparendos_dev`. Agregarlo a la línea de `10.42.0.0/16` de `pg_hba.conf` |
| Conexiones de core-consultas | `flit_consultas_dev`: tope 10, `Maximum Pool Size=8`. PostgreSQL: `max_connections` 100, 54 en uso | Ver §5.1. Subir tope y pool **antes** del merge |
| `definitions.json` | En el ConfigMap `rabbitmq-config` de `services/flit2-dev/rabbitmq.yaml`, montado con `subPath` | No se actualiza en caliente. Cambiar el ConfigMap y reiniciar el pod. `import_definitions` con la ruta del pod leería el archivo viejo |
| Proxy | Sin ingress: nginx del host hacia una ClusterIP fija. `10.43.40.25` está libre | El vhost `dev.diagnostico` ya existe, con DNS y certificado, pero hace 302 a «próximamente». Cambiarlo por `proxy_pass` en el paso 7. Las configs de nginx no están en git |
| File-manager | `https://devfilemanager.flitsas.online`, sin token | Mismo valor de Comparendos y Persona Natural |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Cierra PC-3 para DEV |
| gRPC de core-identity | 8083 en la config y en el Service | Cierra PC-12 |
| Cambios de ConfigMap o Secret | Los pods usan `envFrom` | No reinician solos: `rollout restart` o cambiar la anotación `flit/config-rev` |
| Sellado | `kubeseal` corre en la 177, con su llave propia | Modelo: `docs/host-177/personanatural/gen-secretos-dev.sh` de `flit-gitops` |

---

## 1. Alcance y estado

**Qué se despliega:**

| Pieza | Situación en DEV |
|---|---|
| `core-diagnostico` (API, .NET 10) | Nuevo |
| `frontend-diagnostico` (web, Next.js) | Nuevo |
| `core-consultas` | Ya corre. Se actualiza: suma 4 consumidores y 1 migración |
| RabbitMQ | Ya corre. Suma 1 exchange, 5 colas, 6 políticas y 1 usuario |
| core-api y core-identity | Ya corren. Suman el cliente `svc-diagnostico` |
| gateway | Ya corre. Suma la ruta `/api/v1/diagnostico/*` |

**Estado de la rama.** Sin mergear. En el commit de referencia, `git rev-list --left-right --count origin/develop...HEAD` dio
**188 por detrás y 188 por delante** el 2026-11-04, tras un fetch. Antes del fetch daba 186 por detrás. Vuelva a medirlo: las dos
ramas siguen moviéndose. El diff contra `develop` era de 900 archivos.

**Lo que hoy tiene `origin/develop`** (comprobado):

| Pieza | En `origin/develop` |
|---|---|
| `services/core-diagnostico`, `frontend-diagnostico` | Solo un archivo cada uno (carpeta reservada) |
| `docker-compose.prod.yml` | Sin `core-diagnostico` |
| `.github/workflows/cd.yml` | Ninguna mención a Diagnóstico |
| `deploy/rabbitmq/definitions.json` | Sin colas de diagnóstico ni políticas de esas colas |
| Cliente `svc-diagnostico` | No declarado |
| Consumidores de trabajos en core-consultas | Ninguno |

**Qué depende del merge:**

| Paso | ¿Antes o después del merge? |
|---|---|
| Crear rol de base y secretos (§6, pasos 2 y 3) | Antes. No afecta a nada |
| Preparar `flit-gitops` con `replicas: 0` (§6, paso 1) | Antes. Si no, el CD puede fallar |
| Permisos y colas del broker (§6, paso 4) | Antes |
| Imágenes de Diagnóstico y de core-consultas nuevo | Las construye el CD **al fusionar** |
| Migraciones | Al arrancar cada servicio, después del merge |
| Encender (`replicas: 1`, ingress, producto por empresa) | Después |

**El merge tiene condiciones propias** (§9, puntos 1 a 3): sin work item no hay merge, y el `cd.yml` de la rama todavía
no despliega las imágenes de Diagnóstico.

---

## 2. Mapa de servicios

La API y el gRPC no se publican: viven en la red interna.

| Servicio | Imagen (`ghcr.io/flitsas/flitdev/…`) · Dockerfile | Puerto DEV | gRPC | Depende de | Health check |
|---|---|---|---|---|---|
| `core-diagnostico` | `core-diagnostico` · `services/core-diagnostico/Dockerfile` | 4032 | 8088 | Postgres, RabbitMQ, gateway (tokens y manifiesto), core-identity, core-api, file-manager; core-consultas por el bus | Vida `GET /health` · Listo `GET /health/ready` (`Program.cs:119-124`) |
| `frontend-diagnostico` | `frontend-diagnostico` · `frontend-diagnostico/Dockerfile` | 4024 | No | gateway | `GET /healthz` (`docker-compose.prod.yml:1255`) |
| `core-consultas` | `core-consultas` · `services/core-consultas/Dockerfile` | 4026 | 8084 | Postgres, RabbitMQ, gateway (tokens), proveedores | Vida `GET /health` · Listo `GET /health/ready` (`Program.cs:193-198`) |
| `core-api` | `core-api` | 4003 | 8082 | Postgres | `GET /health` |
| `core-identity` | `core-identity` | 4025 | 8083 | Postgres, RabbitMQ | `GET /health/ready` (`docker-compose.prod.yml:373`) |
| `gateway` (YARP) | `core-api`, con otro comando (`docker-compose.prod.yml:978-980`) | 4002 | No | core-api, core-identity, servicios destino | `GET /health` (`cd.yml:1561`) |
| RabbitMQ 4.1 | `rabbitmq:4.1-management-alpine` en compose; en k3s, PC-2 | 5672 | No | Volumen propio | `rabbitmq-diagnostics -q ping` (`docker-compose.prod.yml:404`) |
| PostgreSQL | Fuera de este alcance | 5432 | No | | |

Puertos de DEV: `cd.yml:117-120`. `/health/ready` responde 503 sin base o con migraciones pendientes.

**Trampa.** Los `HEALTHCHECK` de los Dockerfile apuntan al 8080 (`services/core-diagnostico/Dockerfile:28-31`). En k3s defina
las sondas con el puerto real: 4032 y 4026.

---

## 3. Variables de entorno

La columna **Variable** da el nombre que recibe el contenedor. Es lo que va en `flit-gitops`. Entre paréntesis, el nombre del
`.env` si se usara el compose. Las rutas abreviadas son:

- `D/` = `services/core-diagnostico/src/Flit.Diagnostico.Api/`
- `C/` = `services/core-consultas/src/Flit.Consultas.Api/`
- `F/` = `frontend-diagnostico/`
- `DC` = `docker-compose.prod.yml`

Los valores `<secreto>` se generan por ambiente y van a un sealed secret. Nunca a un archivo del repositorio.

### 3.1 core-diagnostico: secretos

| Variable | Oblig. | Defecto | Para qué | Ejemplo | Dónde se lee |
|---|---|---|---|---|---|
| `ConnectionStrings__Servicio` (`CONNECTION_STRING_DIAGNOSTICO`) | Sí | Ninguno | Base, rol dueño del esquema `diagnostico` | `Host=<host-dev>;Port=5432;Database=<base-dev>;Username=flit_diagnostico;Password=<secreto>;Maximum Pool Size=20` | `D/ServicioSettings.cs:16`, `D/Program.cs:47` |
| `Platform__ServiceClient__ClientSecret` (`SVC_DIAGNOSTICO_CLIENT_SECRET`) | Sí | Ninguno | Pedir tokens como `svc-diagnostico`. Mismo valor que §3.5 | `<secreto>` | `D/ServicioSettings.cs:17` |
| `Platform__Messaging__ConnectionString` (`RABBITMQ_URL_DIAGNOSTICO`) | Sí | Ninguno | Usuario `diagnostico` del broker | `amqp://diagnostico:<secreto>@rabbitmq:5672/flit` | `D/ServicioSettings.cs:21` |
| `Diagnostico__DataProtection__CertificadoClave` (`DIAGNOSTICO_DP_CERT_PASSWORD`) | Sí | Ninguno | Clave del `.pfx` | `<secreto>` | `D/Plataforma/ProteccionDocumentos.cs:32`, `:52` |
| Archivo `.pfx` (`DIAGNOSTICO_DP_CERT_FILE` en compose) | Sí | Ninguno | Certificado RSA. Se monta como archivo | `<secreto>` | `DC:1325-1326` |
| `FILE_MANAGER_AUTH_TOKEN` | Opcional | Vacío | Token del file-manager. El mismo de core-api | `<secreto>` | `D/Infraestructura/Archivos/AlmacenArchivos.cs:92` |
| `Diagnostico__Anthropic__ApiKey` | Opcional | Vacío | Llave del redactor del dictamen. **El compose no la pasa** | `<secreto>` | `D/Modulos/Dictamen/RegistroRedactor.cs:32` |

### 3.2 core-diagnostico: configuración

| Variable | Oblig. | Defecto | Para qué | Ejemplo DEV | Dónde se lee |
|---|---|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` (`FLIT_DOTNET_ENVIRONMENT`) | Opcional | `Development` en compose | Ver §4.2 | `Development` | `DC:529` |
| `ASPNETCORE_URLS` | Sí | `http://localhost:5000` si falta: no sirve en un contenedor | Puerto REST | `http://+:4032` | `D/Program.cs:98` |
| `Servicio__GrpcPort` (`CORE_DIAGNOSTICO_GRPC_PORT`) | Sí | Ninguno | Puerto gRPC interno | `8088` | `D/ServicioSettings.cs:12`, `:22` |
| `Platform__ServiceClient__TokenEndpoint` | Sí | Ninguno | Dónde pedir tokens | `http://gateway:4002/connect/token` | `D/ServicioSettings.cs:18` |
| `Platform__Auth__JwksUri` | Sí | Ninguno | Llaves para validar tokens | `http://gateway:4002/.well-known/jwks.json` | `D/ServicioSettings.cs:19` |
| `Platform__Auth__Issuers__0` (`FLIT_HUB_URL` + `/`) | Sí | Ninguno | Emisor aceptado. Lleva la barra final | `https://dev.flitsas.online/` | `D/ServicioSettings.cs:20` |
| `Diagnostico__DataProtection__CertificadoRuta` | Sí | Ninguno | Ruta del `.pfx` dentro del contenedor | `/run/secrets/diagnostico_dp_pfx` | `D/Plataforma/ProteccionDocumentos.cs:31`, `:51` |
| `FILE_MANAGER_BASE_URL` | Sí | Sin valor en código. El compose trae un defecto que **no sirve para DEV** (§4.1) | URL del file-manager. Sin ella no arranca | `<url-file-manager-dev>` | `D/Infraestructura/Archivos/AlmacenArchivos.cs:88`, `D/Modulos/Modulos.cs:144-145` |
| `FILE_MANAGER_FILES_PATH` | Opcional | `api/v1/files` | Ruta de archivos | `api/v1/files` | `AlmacenArchivos.cs:89` |
| `FILE_MANAGER_CATEGORY` (`DIAGNOSTICO_FILE_MANAGER_CATEGORY`) | Opcional | `diagnostico` | Carpeta propia | `diagnostico` | `AlmacenArchivos.cs:90` |
| `FILE_MANAGER_TIMEOUT_SECONDS` | Opcional | Código 60, compose 30 | Tiempo máximo | `30` | `AlmacenArchivos.cs:91` |
| `Diagnostico__Plataforma__GatewayUrl` | Opcional, necesaria en la práctica | Vacío | Registrar el manifiesto de permisos. Sin ella, el servicio arranca y los permisos no se registran | `http://gateway:4002` | `D/Plataforma/RegistroManifiesto.cs:25`, `:50` |
| `Diagnostico__Identidad__Address` (`DIAGNOSTICO_IDENTIDAD_ADDRESS`) | Opcional, necesaria en la práctica | Vacío | gRPC de Identidad. **Vacía = ninguna empresa habilitada: 403** | `http://core-identity:8083` | `D/Plataforma/GuardSuscripcion.cs:45`, `:60` |
| `Diagnostico__Tramites__Address` | Opcional, necesaria en la práctica | Vacío | gRPC de catálogos de core-api. Vacía = tipos de trámite con 503 y sin réplica de organismos | `http://core-api:8082` | `D/Modulos/Catalogos/TiposTramiteEndpoints.cs:20`, `:30` |
| `Diagnostico__Consultas__Address` (`DIAGNOSTICO_CONSULTAS_ADDRESS`) | Opcional | Vacío | **Solo** la réplica de departamentos con impuesto. No enciende fuentes | `http://core-consultas:8084` | `D/Modulos/Consultas/Fuentes/ClienteConsultas.cs:37`, `:116` |
| `Diagnostico__Fuentes__ImpuestoDisponible` (`DIAGNOSTICO_IMPUESTO_DISPONIBLE`) | Opcional | `false` | Ofrecer la fuente de impuesto | `false` | `D/Modulos/Configuracion/DisponibilidadFuentes.cs:14`, `:17` |
| `Diagnostico__Ambiente` (`DIAGNOSTICO_AMBIENTE`) | Opcional | Vacío | Ambiente lógico. Hoy solo decide si el redactor puede simular | `DEV` | `D/Modulos/Modulos.cs:18`, `RegistroRedactor.cs:16` |
| `Diagnostico__Anthropic__Simulado` | Opcional | `false` | Redactor simulado. Solo con `Diagnostico__Ambiente` LOCAL o DEV. **El compose no la pasa** | `true` | `RegistroRedactor.cs:21` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Opcional | Vacío | Trazas | El de los demás servicios | `DC:541` |

**No poner en ningún ambiente:** `Diagnostico__Identidad__SimularSuscripcionActiva` (`GuardSuscripcion.cs:46`) ni
`Diagnostico__Archivos__GuardarEnBase` (`AlmacenArchivos.cs:377`). Son para local y pruebas.

**Afinación. No las toque salvo necesidad:**

| Variable | Defecto | Regla | Dónde se lee |
|---|---|---|---|
| `Servicio__MigrateOnStartup` | `true` | Ver §5.1 | `D/Program.cs:140` |
| `Diagnostico__Consultas__Bus__Concurrencia` | 4 | Si sube, suba también las conexiones del rol | `D/appsettings.json:51` |
| `Diagnostico__Consultas__Bus__Prefetch` | 8 | Mayor o igual que la concurrencia | `D/appsettings.json:52` |
| `Diagnostico__Consultas__Reconciliador__TtlTrabajo` | `1.00:00:00` | Debe coincidir con las políticas `ttl-*` del broker | `D/Modulos/Trabajos/OpcionesReconciliador.cs:43` |
| `Diagnostico__Carga__TamanoMaximoBytes` | 5 MiB | El proxy no debe permitir menos | `D/Modulos/Carga/CargaEndpoints.cs:26-27` |
| `Diagnostico__Organismos__IntervaloSincronizacion` | 24 h | | `D/appsettings.json:27` |
| `Diagnostico__Impuesto__IntervaloSincronizacion` | 24 h | | `D/appsettings.json:30` |

### 3.3 frontend-diagnostico

| Variable | Tipo | Oblig. | Defecto | Para qué | Ejemplo DEV | Dónde se lee |
|---|---|---|---|---|---|---|
| `FLIT_SESSION_SECRET` (`DIAGNOSTICO_SESSION_SECRET`) | Secreto | Sí | Ninguno fuera de desarrollo | Cifra la cookie. Mínimo 32 caracteres. Distinto del hub y de Comparendos | `<secreto>` | `packages/auth/src/config.ts:31` |
| `FLIT_INTERNAL_API_KEY` | Secreto | Opcional | Vacío | Clave interna. La misma de core-api y el gateway | `<secreto>` | `F/lib/config.server.ts:19` |
| `PORT` (`DIAGNOSTICO_PORT`) | Config | Sí | `4024` en compose | Puerto web | `4024` | `DC:1241` |
| `HOSTNAME` | Config | Sí | `0.0.0.0` en compose | Escuchar en todas las interfaces | `0.0.0.0` | `DC:1242` |
| `CORE_API_ORIGIN` | Config | Sí | Local en código | Gateway | `http://gateway:4002` | `F/lib/config.server.ts:18` |
| `BRANDING_INTERNAL_API_URL` | Config | Sí | Local en código | Marca | `http://gateway:4002` | `packages/brand/src/resolve-brand.server.ts:38` |
| `FLIT_OIDC_INTERNAL_URL` | Config | Opcional, recomendada | La URL del hub | OIDC por red interna | `http://gateway:4002` | `packages/auth/src/config.ts:38` |
| `FLIT_HUB_URL` | Config | Sí | Local en código | Login y menú | `https://dev.flitsas.online` | `F/lib/config.server.ts:20` |
| `TRAMITES_URL` (`FLIT_TRAMITES_URL`) | Config | Sí | Local en código | Enlaces a Trámites | `https://dev.tramites.flitsas.online` | `F/lib/config.server.ts:21` |
| `FLIT_HOSTS` | Config | Recomendada | El del compose | Hosts FLIT. El mismo valor del hub en DEV | El mismo del hub | `packages/brand/src/resolve-brand.server.ts:54` |

La configuración del front se lee al atender cada petición. No hace falta reconstruir la imagen por ambiente
(`F/next.config.ts:7-9`).

### 3.4 core-consultas

Ya corre en DEV. Lo marcado **nuevo** es lo que este despliegue agrega.

| Variable | Tipo | Oblig. | Defecto | Para qué | Ejemplo DEV | Dónde se lee |
|---|---|---|---|---|---|---|
| `ConnectionStrings__Servicio` (`CONNECTION_STRING_CONSULTAS`) | Secreto | Sí | Ninguno | Rol dueño del esquema `consultas` | `Host=<host-dev>;…;Username=flit_consultas;Password=<secreto>` | `C/ServicioSettings.cs:16` |
| `Platform__ServiceClient__ClientSecret` (`SVC_CONSULTAS_CLIENT_SECRET`) | Secreto | Sí | Ninguno | Tokens como `svc-consultas` | `<secreto>` | `C/ServicioSettings.cs:17` |
| `Platform__Messaging__ConnectionString` (`RABBITMQ_URL_CONSULTAS`) | Secreto | Sí | Compose: valor de relleno, no usar | Usuario `consultas` | `amqp://consultas:<secreto>@rabbitmq:5672/flit` | `C/ServicioSettings.cs:21` |
| `KYVERUM_RUNT_API_KEY` | Secreto | **Sí, siempre** | Ninguno | Sin ella no arranca, aunque todo se simule | `<secreto>` | `C/ProveedoresSettings.cs:45-46` |
| `VERIFIK_API_TOKEN` | Secreto | Sí si un `VERIFIK_*_MODE` es `real` | Ninguno | `VERIFIK_VEHICLE_MODE` es `real` por defecto | `<secreto>` | `C/ProveedoresSettings.cs:22`, `:31` |
| `Servicio__GrpcPort` | Config | Sí | Ninguno | gRPC | `8084` | `C/ServicioSettings.cs:22` |
| `Consultas__Ambiente` **(nuevo)** | Config | Sí con simulación | Vacío | LOCAL, DEV, QA o PDN. **El compose no la pasa** | `DEV` | `C/Simulacion/OpcionesSimulacion.cs:22`, `:46` |
| `Consultas__Simulacion__Habilitada` **(nuevo)** | Config | Opcional | `false` | Todas las fuentes del bus simuladas. **El compose no la pasa** | `true` | `OpcionesSimulacion.cs:19`, `:47` |
| `Consultas__Simulacion__Fuentes` **(nuevo)** | Config | Opcional | Vacío | Lista por comas. Un nombre mal escrito **no da error** y esa fuente va al proveedor real | `avaluo` | `OpcionesSimulacion.cs:34`, `:48` |
| `INTEMPO_API_BASE_URL` **(nuevo)** | Config sensible | Opcional | Vacío = no conectada | RUNT real. El endpoint no autentica: trátela como secreto. La entrega el Líder Técnico | `<secreto>` | `C/RuntIntempo/ProveedorRuntIntempo.cs:18`, `.env.intempo.example:11-13` |
| `INTEMPO_API_VEHICLE_PLATE_PATH` **(nuevo)** | Config | Opcional | `/api/RuntConsulta/consultar-vehiculo` | Ruta por placa | El defecto | `ProveedorRuntIntempo.cs:19`, `:22` |
| `INTEMPO_API_VEHICLE_VIN_PATH` **(nuevo)** | Config | Opcional | `/api/RuntConsulta/consultar-vin` | Ruta por VIN | El defecto | `ProveedorRuntIntempo.cs:20`, `:23` |
| `INTEMPO_API_SECONDS_TIMEOUT` **(nuevo)** | Config | Opcional | 20 | Tiempo máximo | `20` | `ProveedorRuntIntempo.cs:21` |
| `IMPUESTO_API_BASE_URL` **(nuevo)** | Config sensible | No poner en este despliegue | Vacío = no listo | Impuesto real | Vacía | `C/Impuesto/OpcionesImpuesto.cs:35` |
| `IMPUESTO_API_PATH` **(nuevo)** | Config | No poner en este despliegue | Vacío | Ruta del impuesto | Vacía | `OpcionesImpuesto.cs:36` |
| `IMPUESTO_API_TIMEOUT_SECONDS` **(nuevo)** | Config | Opcional | 280 | **Con 300 o más, core-consultas no arranca** | `280` | `OpcionesImpuesto.cs:37-38`, `:56-57` |
| `IMPUESTO_CONTACTO_*` (18 variables) **(nuevo)** | Config sensible | No poner en este despliegue | Vacío | Datos fijos del paso 3. Lista exacta en `.env.impuesto.example:6-23` | Vacías | `OpcionesImpuesto.cs:67-73` |

**Proveedores reales** (`VERIFIK_*`, `KYVERUM_*`, `FASECOLDA_*`, `RUES_*`): ya existen en DEV. Remítase a
[handoff-vps-consultas-notificaciones.md](handoff-vps-consultas-notificaciones.md). Este despliegue no los cambia.

**Afinación de los consumidores** (`C/appsettings.json:27-45`, validados en
`C/Bus/OpcionesConsumidoresDiagnostico.cs:112-117`). Cada una como `Consultas__Diagnostico__<Cola>__Concurrencia` y
`__Prefetch`:

| Cola | Concurrencia | Prefetch |
|---|---|---|
| `Lote` | 8 | 16 |
| `Express` | 4 | 8 |
| `Impuesto` | 4 | 8 |
| `ImpuestoExpress` | 1 | 2 |

`Consultas__Diagnostico__ProductoresPermitidos__0` es `diagnostico`. No lo cambie.

### 3.5 Servicios que ya corren: lo que Diagnóstico les pide

| Variable | Servicio | Tipo | Oblig. | Defecto | Para qué | Ejemplo | Dónde se lee |
|---|---|---|---|---|---|---|---|
| `Suite__Oidc__ServiceClients__svc-diagnostico__Secret` (`SVC_DIAGNOSTICO_CLIENT_SECRET`) | core-api y core-identity | Secreto | Sí | Vacío = el cliente no se registra | Mismo valor de §3.1 | `<secreto>` | `DC:217` |
| `Suite__Oidc__ServiceClients__svc-diagnostico__Scopes__4` | core-api y core-identity | Config | Sí si `Diagnostico__Consultas__Address` tiene valor | No existe | Agrega `platform.consultas`. **Falta en el repositorio** (§9, punto 4). **El compose no la pasa** | `platform.consultas` | `services/core-api/src/Flit.Api/appsettings.json:232`, `D/Modulos/Consultas/Fuentes/ClienteConsultas.cs:36` |
| `Suite__Hosts__Environment` (`FLIT_SUITE_ENV`) | core-api y core-identity | Config | Sí | Ninguno | Arma los retornos del login | `dev` | `DC:204` |
| `Suite__Oidc__Enabled` (`FLIT_OIDC_ENABLED`) | core-api y core-identity | Config | Sí | `false` | Servidor OIDC del hub | `true` | `DC:206` |
| `Identidad__GrpcPort` (`CORE_IDENTITY_GRPC_PORT`) | core-identity | Config | Sí | Vacío = sin gRPC | Lo llama Diagnóstico. Publique también el puerto en su Service | `8083` | `DC:364` |
| `Suite__Hosts__Overrides__diagnostico` | core-api y core-identity | Config | Ver PC-7 | Ninguno | Saca a Diagnóstico de «Próximamente» | `https://dev.diagnostico.flitsas.online` | `services/core-api/src/Flit.Suite.AspNetCore/Platform/ConfiguredProductHosts.cs:31` |
| `ReverseProxy__Clusters__core-diagnostico-cluster__Destinations__core-diagnostico-1__Address` | gateway | Config | Opcional si el puerto es 4032 | `http://core-diagnostico:4032/` | Destino de la API | `http://core-diagnostico:4032/` | `DC:1008`, `services/core-api/src/Flit.Gateway/appsettings.json:215` |

---

## 4. Valores para DEV frente a QA y PDN

### 4.1 Tabla comparativa

Recomendación para el **primer** despliegue en DEV: todo simulado, para probar la cadena sin gastar saldo ni tocar proveedores.

| Variable | DEV | QA | PDN | Motivo |
|---|---|---|---|---|
| `Diagnostico__Ambiente` | `DEV` | `QA` | `PDN` | Hoy solo afecta al redactor |
| `Consultas__Ambiente` | `DEV` | `QA` | `PDN` | La exige la simulación |
| `Consultas__Simulacion__Habilitada` | `true` | **Nunca** | **Nunca** | En QA o PDN el servicio no arranca (`OpcionesSimulacion.cs:55-61`) |
| `Consultas__Simulacion__Fuentes` | Vacía | **Nunca** | **Nunca** | Misma regla |
| `INTEMPO_API_BASE_URL` | Vacía mientras se simula | Por decidir | Por decidir | Sin ella la fuente queda «no conectada» y el servicio arranca igual |
| `Diagnostico__Fuentes__ImpuestoDisponible` | `false` | `false` | `false` | Ver §4.3 |
| `IMPUESTO_API_*`, `IMPUESTO_CONTACTO_*` | Sin valor | Sin valor | Sin valor | El encendido real no entra (§9, punto 5) |
| `Diagnostico__Anthropic__*` | Ver PC-9 | Por decidir | Por decidir | Sin llave ni simulación, el redactor responde «no configurado» (`RedactorLlm.cs:189-197`) |
| `FILE_MANAGER_BASE_URL` | El de core-api en DEV | El de core-api en QA | El de core-api en PDN | **No use el defecto del compose: es la URL de PDN** (`DC:560`). Ver PC-5 |
| Certificado `.pfx` | Propio | Propio | Propio | No copiar entre ambientes |
| `DIAGNOSTICO_PORT` | 4024 | 5024 | 6024 | `cd.yml:93`, `:107`, `:120` |
| API (`CORE_DIAGNOSTICO_PORT`) | 4032 | 5032 | 6032 | Mismas líneas |
| Host web | `dev.diagnostico.flitsas.online` | `qa.diagnostico.flitsas.online` | `diagnostico.flitsas.online` | handoff, tabla inicial |

**La simulación es segura para el resto de la suite.** Solo responde los trabajos que llegan por el bus. Trámites y
Comparendos siguen por gRPC con sus `*_MODE` (`docs/diagnosticos/guia-entorno-local-suite.md:242`). Por eso **no** cambie
`VERIFIK_*_MODE`, `FASECOLDA_MODE` ni los demás modos para este despliegue.

Valores de `Consultas__Simulacion__Fuentes` (`C/Bus/TrabajoConsultaDiagnostico.cs:11-19`): `runt`, `runt_propietario`,
`runt_locatario`, `simit_vehiculo`, `simit_propietario`, `simit_locatario`, `rues`, `avaluo`, `impuesto`.

### 4.2 `ASPNETCORE_ENVIRONMENT` e `IsProduction()`

**Lo comprobado:**

- El compose fija `ASPNETCORE_ENVIRONMENT: ${FLIT_DOTNET_ENVIRONMENT:-Development}` en los servicios (`DC:148`, `:529`, `:593`,
  `:987`). Sin la variable, **todo corre como `Development`**.
- La nota «los tres ambientes corren como Development» es un valor por defecto, no una garantía. `.env.prod.example:67-70`
  documenta cómo cambiarlo. Qué valor tiene realmente `flit-gitops` es **Por confirmar** (PC-3). Lo único escrito sobre DEV
  es `Development` (`despliegue-dev-k3s-comparendos.md:53`).
- **Diagnóstico no depende de eso.** `D/` no llama a `IsProduction` ni a `IsDevelopment`. Por eso existe
  `Diagnostico__Ambiente`.
- La simulación de core-consultas tampoco. Mira `Consultas__Ambiente` (`OpcionesSimulacion.cs:8-9`).

**Dos cosas sí cambian con `Development`:**

| Dónde | Efecto |
|---|---|
| `C/Program.cs:150` | Permite proveedores simulados de **Comparendos** (salvo `Consultations__ComparendosSimulados`) |
| `services/core-api/src/Flit.Platform.Sdk/Grpc/PlatformGrpcServerExtensions.cs:56` | Publica la reflexión gRPC, solo en la red interna |

### 4.3 Impuesto en DEV: tres niveles

| Nivel | Qué hace falta | ¿Entra en este despliegue? |
|---|---|---|
| Apagado | `Diagnostico__Fuentes__ImpuestoDisponible=false` | **Sí, es el punto de partida** |
| Simulado | `true`, `Consultas__Simulacion__Habilitada=true`, `Diagnostico__Consultas__Address` y el scope de §3.5 | Opcional, para probar colas y réplica. No sale nada a internet |
| Real | `IMPUESTO_API_*`, contacto y los avales de §9, punto 5 | **No** |

Sin URL ni simulación, la lista queda vacía aunque la bandera esté en `true` (`C/Impuesto/ProveedorImpuesto.cs:19`).

**Advertencia.** La semilla enciende 50, 52 y 94. Si alguien pone `IMPUESTO_API_BASE_URL` y `IMPUESTO_API_PATH` sin simulación,
esos tres departamentos quedan activos **de inmediato** (§9, punto 7).

---

## 5. Infraestructura

### 5.1 Base de datos

| Tema | Diagnóstico | Consultas |
|---|---|---|
| Esquema | `diagnostico` | `consultas` |
| Rol | `flit_diagnostico` | `flit_consultas` (ya existe) |
| Quién lo crea | Una persona con usuario administrador, con `deploy/postgres/servicio-con-esquema-propio.sql` | Ya hecho |
| Migraciones nuevas | Las 21 de la rama (primera vez) | 1: `20261104150611_ImpuestoDepartamentosHabilitados` |
| Cuándo se aplican | Al arrancar el servicio | Al arrancar el servicio |

Crear el rol y comprobar el aislamiento: [handoff-vps-diagnostico.md](handoff-vps-diagnostico.md) §1. El comando es el mismo.
No use `conexiones` menor que el `Maximum Pool Size` de la cadena.

**Cómo migran.** `Program.Main` llama a `MigrateAsync` antes de atender (`D/Program.cs:29`, `:137-145`; `C/Program.cs:32`,
`:213-221`). Se controla con `Servicio__MigrateOnStartup`, `true` por defecto. **Déjelo así:** no hay otro mecanismo de
migración para estos dos esquemas. Cada historial vive en su esquema (`__EFMigrationsHistory`). Las migraciones de un
servicio no tocan al otro.

**Qué agrega la de consultas.** Crea `consultas.vehicle_tax_departments` y siembra 5 filas: activas 50, 52 y 94; apagadas 17 y 63.
No tiene datos personales. Con la lista vacía o sin URL, no se consulta nada.

**Conexiones.** Diagnóstico cabe en 20 (handoff §2.4). **core-consultas no cabe con lo que tiene DEV:** tope 10 y pool 8, y los
nuevos consumidores suman 17 mensajes a la vez (8 + 4 + 4 + 1). Cada consumidor abre la transacción antes del efecto
(`services/core-api/src/Flit.Platform.Sdk/Messaging/PlatformConsumer.cs`, `ProcessAsync`), así que retiene una conexión durante toda
la llamada al proveedor, hasta 90 s, y comparte el pool con el gRPC de Trámites e ICT. Un lote con fuentes reales puede dejar a
Trámites sin conexiones. ADR-0075 pide 30.

Para DEV, antes del merge: tope 25 y pool 20, y bajar la concurrencia a 8 en total (`Consultas__Diagnostico__Lote__*` 4/8, `Express`
2/4, `Impuesto` 1/2, `ImpuestoExpress` 1/2; concurrencia/prefetch). El PostgreSQL de la 177 es compartido: revise `max_connections`
antes de subir topes. Vigile `too many connections for role "flit_consultas_dev"`.

### 5.2 RabbitMQ

El virtual host es `flit`. `deploy/rabbitmq/definitions.json` es la fuente. Lo que falta hoy en `develop`:

**Exchange nuevo:** `flit.diagnostico`. Ya existe `flit.consultas`.

**Colas y bindings** (`definitions.json:12-16`, `:19-23`), todas atadas a `flit.consultas`:

| Cola | La consume | Clave de enrutamiento | Concurrencia |
|---|---|---|---|
| `consultas.diagnostico` | core-consultas | `consultas.diagnostico.*` | 8 |
| `consultas.diagnostico-express` | core-consultas | `consultas.diagnostico-express.*` | 4 |
| `consultas.diagnostico-impuesto` | core-consultas | `consultas.diagnostico-impuesto.*` | 4 |
| `consultas.diagnostico-impuesto-express` | core-consultas | `consultas.diagnostico-impuesto-express.*` | 1 |
| `diagnostico.consultas-resultados` | core-diagnostico | `consultas.resultado.diagnostico` | 4 |

**Políticas** (`definitions.json:27-76`), todas con prioridad 10:

| Política | Se aplica a | Qué hace |
|---|---|---|
| `dlq-7d` | Las cinco `.dlq` | Borra a los 7 días. Control de Ley 1581: **no la retire en un rollback** |
| `ttl-consultas-diagnostico`, `ttl-consultas-diagnostico-express`, `ttl-consultas-diagnostico-impuesto`, `ttl-consultas-diagnostico-impuesto-express`, `ttl-diagnostico-consultas-resultados` | Cada cola principal | 24 h. Lo vencido va a su `.dlq` |

RabbitMQ aplica **una sola** política por cola. Si otra política del ambiente alcanza estas colas, debe tener prioridad menor que 10
o repetir lo mismo.

Las `.retry.<n>`, las `.dlq` y el exchange `<cola>.reintentos` los crea cada consumidor al arrancar. Hasta entonces, lo que venza
en una cola principal **se descarta** en vez de ir a su `.dlq`. Por eso los consumidores van antes que el tráfico.

**Usuarios y permisos** (`deploy/rabbitmq/usuario-de-servicio.sh:38-49`):

| Usuario | Configura | Escribe | Lee |
|---|---|---|---|
| `diagnostico` | `^(flit\.diagnostico\|diagnostico\..*)$` | `^(flit\.diagnostico\|flit\.consultas\|diagnostico\..*)$` | `^(flit\..*\|diagnostico\..*)$` |
| `consultas` (no cambia) | `^(flit\.consultas\|consultas\..*)$` | `^(flit\.consultas\|consultas\..*)$` | `^(flit\..*\|consultas\..*)$` |

**El punto que más falla.** Sin `flit\.consultas` en la escritura de `diagnostico`, el broker rechaza cada publicación con 403
(`ACCESS_REFUSED`). El servicio arranca y parece sano, pero la outbox se queda reintentando y las celdas no avanzan.

**Los nombres de usuario no son libres.** core-consultas solo acepta trabajos del usuario `diagnostico`, y core-diagnostico solo
acepta resultados del usuario `consultas` (`C/Bus/OpcionesConsumidoresDiagnostico.cs:45`, `:66`; `D/Modulos/Modulos.cs:130`).

**Trampas del script:**

- Solo funciona con `docker compose` (`usuario-de-servicio.sh:22`, `:51`). `COMPOSE` no sirve para `kubectl`: la orden que
  arma lleva `exec -T`. En k3s use `rabbitmqctl` directo (§6, paso 4).
- Volver a correrlo **cambia la clave** (`usuario-de-servicio.sh:53-54`). Con la clave distinta de la del secreto, el servicio
  pierde el broker.

### 5.3 Certificado de Data Protection

Sin él `core-diagnostico` **no arranca**, en ningún ambiente (`D/Plataforma/ProteccionDocumentos.cs:53-54`). Procedimiento
completo: [handoff-vps-diagnostico.md](handoff-vps-diagnostico.md) §4.1.

- Uno por ambiente, RSA, con llave privada.
- **Guarde `.pfx` y clave en el gestor de secretos antes de encender.** Perderlos deja ilegibles para siempre los documentos
  ya cifrados.
- En k3s: sealed secret, con el `.pfx` montado como archivo. La ruta debe coincidir con
  `Diagnostico__DataProtection__CertificadoRuta` (PC-11).
- **No rotar.** Hoy no hay soporte para certificados anteriores. Si se compromete, escale al Líder Técnico.

### 5.4 gRPC entre servicios

Va por h2c en la red interna. Nada se publica. Cada llamada lleva un token de `svc-diagnostico`.

| Origen → destino | Dirección | Scope | Para qué | Si falta |
|---|---|---|---|---|
| core-diagnostico → core-identity | `http://core-identity:8083` | `platform.identidad.read`, `platform.identidad.empresas` | Suscripción, nombres, empresas | 403 en todo el negocio |
| core-diagnostico → core-api | `http://core-api:8082` | `platform.tramites.catalogos` | Tipos de trámite, organismos | 503 en tipos y sin réplica |
| core-diagnostico → core-consultas | `http://core-consultas:8084` | `platform.consultas` | **Solo** lista de departamentos | Réplica vacía. El resto funciona |

El 8082 es el valor por defecto de `CORE_API_GRPC_PORT` (`DC:281`, `:548`); use el de core-api en DEV. Las consultas de las celdas **no** van por gRPC. Van por el bus (ADR-0075). `svc-diagnostico` también debe estar en
`Catalogos:TiposTramite:ClientesPermitidos`: ya lo está (`services/core-api/src/Flit.Api/appsettings.json:200`).

### 5.5 Identidad y OIDC

**Clientes:**

| Cliente | Tipo | Quién lo registra |
|---|---|---|
| `svc-diagnostico` | De servicio, client credentials | Scopes en `appsettings.json:232`. Solo falta su secreto (§3.5) |
| `diagnostico` | Público, PKCE, login de la web | **Solo**, al arrancar core-api o core-identity |

**Redirects.** Se arman desde `Suite__Hosts__Environment`. **Gana el último proceso que arranca.** Si core-api y core-identity
reciben valores distintos, se pisan. Comprobar: [handoff-vps-suite.md](handoff-vps-suite.md) §7.2. Esperado en DEV: `diagnostico`
con host `dev.`.

### 5.6 Dominio, proxy y CORS

| Tema | Qué hacer |
|---|---|
| Dominio | `dev.diagnostico.flitsas.online`. Ya tiene DNS y certificado según `handoff-vps-suite.md`, §9 |
| Proxy | En k3s, un ingress hacia `frontend-diagnostico:4024` (PC-6). El handoff §6 describe el equivalente en nginx |
| API | **Sin ingress propio.** El navegador llama a `/api/v1/*` en el mismo host, Next.js lo pasa al gateway y este a core-diagnostico (`F/next.config.ts:8-9`, `services/core-api/src/Flit.Gateway/appsettings.json:131-133`) |
| CORS | **No hace falta tocar nada.** El navegador no llama a otro origen. No agregue el host a `Cors__AllowedOrigins__*` |
| Tamaño de carga | El proxy debe aceptar al menos 5 MiB (`D/Modulos/Carga/CargaEndpoints.cs:27`) |
| «Próximamente» | Hoy el hub muestra Diagnóstico como próximamente (`appsettings.json:209`). Ver PC-7 |

---

## 6. Orden de despliegue, paso a paso

**La regla** (`docs/diagnosticos/plan-consulta-impuesto.md:322-329`): **el broker primero, luego core-consultas, luego
core-diagnostico, y al final la configuración de encendido.** Si se invierte, core-consultas respondería `InvalidArgument` a cada
trabajo de impuesto.

**Fusionar a `develop` despliega core-consultas sin más confirmación.** Por eso los pasos 1 a 4 van antes del merge.

Sustituya `<ns>` por el espacio de nombres de DEV (PC-1) y `<pod-rabbitmq>` por el pod del broker (PC-2).

**Paso 1. Preparar `flit-gitops` (antes del merge).**

1. En `services/flit2-dev/`, agregar los manifiestos de `core-diagnostico` y `frontend-diagnostico` con las variables de §3.
2. Declarar las dos imágenes en `kustomization.yaml`. Si el CD intenta escribir un digest y la imagen no está, **corta el
   despliegue de todo el ambiente** (`cd.yml:1488-1489`).
3. `replicas: 0` en los dos Deployments.

Modelo a seguir: [despliegue-dev-k3s-comparendos.md](despliegue-dev-k3s-comparendos.md) §0, §3 y §4.
**Comprobar:** Argo CD en `Synced`. Sin pods de Diagnóstico.

**Paso 2. Base de datos (antes del merge).** Rol y esquema, §5.1. **Comprobar:** `psql "$ADMIN_URL" -c "\du flit_diagnostico"`.

**Paso 3. Secretos (antes del merge).** Sellar en `flit-gitops`: los de §3.1, §3.3 y §3.5. Certificado respaldado (§5.3).
Reiniciar core-api y core-identity con el secreto de `svc-diagnostico` y `Identidad__GrpcPort`.
**Comprobar:** los dos reinician sanos. El login del hub sigue funcionando.

**Paso 4. Broker (antes del merge).**

Cargar las definiciones. **Agrega lo nuevo y no borra nada.** Por confirmar cómo llega el archivo al pod (PC-2):

```bash
kubectl -n <ns> exec <pod-rabbitmq> -- rabbitmqctl import_definitions <ruta-de-definitions.json>
```

Crear el usuario si no existe, con clave nueva guardada en el sealed secret:

```bash
# Solo si list_users no lo muestra. Lea la clave de una variable: no la deje en el historial.
kubectl -n <ns> exec <pod-rabbitmq> -- rabbitmqctl add_user diagnostico "$CLAVE_MQ_DIAGNOSTICO"
```

Dar los permisos. Si el usuario ya existe, esto **no** cambia su clave:

```bash
kubectl -n <ns> exec <pod-rabbitmq> -- rabbitmqctl set_permissions -p flit diagnostico \
  '^(flit\.diagnostico|diagnostico\..*)$' \
  '^(flit\.diagnostico|flit\.consultas|diagnostico\..*)$' \
  '^(flit\..*|diagnostico\..*)$'
```

**Comprobar:**

```bash
kubectl -n <ns> exec <pod-rabbitmq> -- rabbitmqctl list_user_permissions -p flit diagnostico
kubectl -n <ns> exec <pod-rabbitmq> -- rabbitmqctl list_queues -p flit name messages consumers policy
kubectl -n <ns> exec <pod-rabbitmq> -- rabbitmqctl list_policies -p flit
```

Deben verse las 5 colas, con 0 consumidores por ahora, y las 6 políticas.

**Paso 5. Fusionar a `develop`.** Requiere las condiciones de §9, puntos 1 a 3, y confirmación humana. El CD construye las imágenes
y escribe los digests. **Este paso despliega core-consultas nuevo.**

Agregue en la misma sincronización `Consultas__Ambiente=DEV` y `Consultas__Simulacion__Habilitada=true`. Así core-consultas arranca
una sola vez y nunca hay una ventana con proveedores reales. Sin tráfico de Diagnóstico todavía, el riesgo es nulo.

**Comprobar:** `kubectl -n <ns> exec deploy/core-consultas -- wget -qO- http://localhost:4026/health/ready` responde `ready`. Las
**cuatro** colas `consultas.diagnostico*` muestran un consumidor. Trámites sigue consultando con normalidad.

**Paso 6. core-diagnostico.** `replicas: 1` en `flit-gitops`. Argo sincroniza.
**Comprobar:** el log dice `Manifiesto de Diagnóstico registrado`, y `health/ready` responde `ready` (§7).

**Paso 7. frontend-diagnostico e ingress.** `replicas: 1`. Ingress hacia el puerto 4024.
**Comprobar:** `curl -s https://dev.diagnostico.flitsas.online/healthz`.

**Paso 8. Encendido.**

1. Sacar a Diagnóstico de «Próximamente» en el hub (PC-7).
2. El SuperAdmin enciende Diagnóstico por empresa: Compañías → Productos.
3. Habilitar las fuentes en el contrato de la empresa de prueba.
4. **Impuesto queda apagado.** Si se quiere probar simulado, §4.3. Es un solo cambio, al final, y se verifica antes de otro.

---

## 7. Verificación tras desplegar

### 7.1 Salud

```bash
kubectl -n <ns> exec deploy/core-diagnostico -- wget -qO- http://localhost:4032/health/ready
kubectl -n <ns> exec deploy/core-consultas  -- wget -qO- http://localhost:4026/health/ready
curl -s https://dev.diagnostico.flitsas.online/healthz
curl -s https://api.dev.flitsas.online/api/v1/diagnostico/sesion -o /dev/null -w '%{http_code}\n'
```

Los dos primeros dicen `ready`. La última debe ser **401**: el gateway llega al servicio y este pide sesión. Con 502 o 503, no llega.

Haga esa prueba por el host de la API. Por el host web, el proxy de Next.js responde 401 sin llamar a la API
(`frontend-diagnostico/app/api/v1/[...path]/route.ts:3-4`), así que no demuestra nada.

### 7.2 Colas y consumidores

```bash
kubectl -n <ns> exec <pod-rabbitmq> -- rabbitmqctl list_queues -p flit name messages consumers policy | grep -e consultas.diagnostico -e consultas-resultados
```

| Cola | Consumidores | Mensajes | Política |
|---|---|---|---|
| Las 5 principales | 1 o más | 0 en reposo | Su `ttl-*` |
| Las 5 `.dlq` | 0 | 0 | `dlq-7d` |

Las `.dlq` aparecen cuando cada consumidor arranca. **Un mensaje en una `.dlq` es una alerta**, no un dato normal.

### 7.3 Outbox, réplicas y OIDC

```sql
-- Debe bajar a 0 y no crecer. Si crece, revise permisos del broker.
SELECT count(*) FROM diagnostico.outbox WHERE published_at IS NULL;
-- Réplica de organismos, desde core-api. Debe ser mayor que 0 tras arrancar.
SELECT count(*) FROM diagnostico.organismos;
-- Réplica de departamentos. Vacía si no hay Address o scope. Es correcto con el impuesto apagado.
SELECT * FROM diagnostico.departamentos_impuesto;
-- Lista efectiva en consultas. Debe devolver 50, 52 y 94.
SELECT department_code FROM consultas.vehicle_tax_departments WHERE is_active AND deleted_at IS NULL;
-- Cliente de login con host dev.
SELECT client_id, redirect_uris FROM identity.oidc_applications WHERE client_id = 'diagnostico';
-- Conexiones. Debe quedar por debajo del tope del rol.
SELECT usename, count(*) FROM pg_stat_activity WHERE usename IN ('flit_diagnostico','flit_consultas') GROUP BY 1;
```

Las dos réplicas sincronizan al arrancar y luego cada 24 h. **Para adelantarlas, reinicie core-diagnostico.**

### 7.4 Recorrido de humo, en simulación

**Antes:** `Consultas__Simulacion__Habilitada=true`. Sin eso, estas placas irían a un proveedor real.

1. Entrar por el hub con un usuario de la empresa de prueba y abrir Diagnóstico.
2. En el inicio: «Placa o VIN» → `TST101` → «Diagnosticar» → «Continuar». Si aparece el estimado, «Lanzar diagnóstico».
3. Termina en `/diagnosticos/<id>`. Las celdas pasan de `PENDIENTE` a `COMPLETADO` en segundos.
4. Abra el detalle y descargue PDF y Excel. Eso prueba el file-manager.

| Placa | Qué se espera (`guia-entorno-local-suite.md:298-299`) |
|---|---|
| `TST101` | Todo verde, RUNT de persona natural |
| `TST102` | Propietario con NIT, SIMIT rojo |
| `TST107` | Tiempo agotado en RUNT, SIMIT y avalúo. Debe verse como error reprocesable |

Son datos inventados. Aun así, no use datos reales para probar, ni pegue respuestas de proveedores en ADO, logs o commits.

### 7.5 Qué mirar en los logs

| Mensaje o evento | Servicio | Qué significa | Qué hacer |
|---|---|---|---|
| `no puede arrancar; falta configuración: …` | Ambos | Falta una variable. El mensaje la nombra | Póngala (ojo con §9, punto 8) |
| `no puede arrancar: las llaves de Data Protection…` | diagnóstico | Certificado o clave mal | §5.3 |
| `faltan secretos de proveedores` | consultas | Falta `KYVERUM_RUNT_API_KEY` o `VERIFIK_API_TOKEN` | §3.4 |
| `solo se admite en LOCAL o DEV` | consultas | Simulación sin `Consultas__Ambiente` | §4.1 |
| `debe quedar por debajo del tope de la fuente` | consultas | `IMPUESTO_API_TIMEOUT_SECONDS` ≥ 300 | Use 280 |
| `ACCESS_REFUSED` / 403 | Ambos | Permisos del broker | §5.2 |
| `PRECONDITION_FAILED` | Ambos | Argumentos de cola distintos | Revise `definitions.json` |
| `Manifiesto de Diagnóstico registrado (N permisos)` | diagnóstico | Normal | Ninguna acción |
| `Manifiesto de Diagnóstico sin registrar` | diagnóstico | Falta `Diagnostico__Plataforma__GatewayUrl` | §3.2 |
| Evento 137280, `Reconciliador de consultas en marcha: … TTL <valor>` | diagnóstico | Normal. El TTL debe ser el de las políticas | Debe decir 24 h |
| Evento 137287 | diagnóstico | Celda cerrada por TTL | ¿Se cayó core-consultas? |
| Evento 137333, `La sincronización de organismos falló` | diagnóstico | Reintenta en 24 h | Revise core-api y el token |
| Evento 137330, `Sincronización de organismos omitida` | diagnóstico | Falta `Diagnostico__Tramites__Address` | §3.2 |
| Evento 137331, `catálogo de organismos vacío` | diagnóstico | La plataforma no devolvió nada | Revise core-api |
| Evento 138613, `sincronización de departamentos … falló` | diagnóstico | Con `Address` puesta, lo más probable es que falte el scope | §9, punto 4 |
| `too many connections for role` | Ambos | Tope del rol | §5.1 |

---

## 8. Reversión

**Regla general.** Primero apagar, luego dejar drenar, y solo entonces desplegar a la inversa
(`plan-consulta-impuesto.md:329`). En DEV, el rollback de una imagen es **revertir el commit en `flit-gitops`** (`cd.yml:1380`).
Requiere confirmación del Líder Técnico, paso a paso. Termine verificando salud (§7.1) y avise a QA y al Líder Técnico con
fecha, hora y commit restaurado.

### 8.1 Apagar solo Diagnóstico (lo normal)

`replicas: 0` en `core-diagnostico` y `frontend-diagnostico`. Nada del resto de la suite depende de ellos. El esquema y los datos
quedan. Los resultados pendientes esperan en su cola hasta 24 h; después pasan a la `.dlq`.

### 8.2 Volver atrás por completo, en orden

1. **Apagar la fuente.** `Diagnostico__Fuentes__ImpuestoDisponible=false`, si estaba encendida. **Para apagar un departamento no se
   vacía una variable.** Se guarda con `activo=false` por `GuardarDepartamentoImpuesto` (ADR-0080). No edite
   `diagnostico.departamentos_impuesto`: la sincronización la pisa.
2. **Dejar de publicar.** Desactive Diagnóstico por empresa y espere a que no entren lotes.
3. **Dejar drenar.** Espere a que las colas `consultas.diagnostico*` y `diagnostico.consultas-resultados` estén en 0
   (§7.2). **Con core-consultas arriba y core-diagnostico también.**
4. **core-diagnostico:** `replicas: 0`.
5. **core-consultas:** revertir su digest. La imagen anterior no consume estas colas.
6. **Broker:** dejarlo como está. Ver abajo.

### 8.3 No perder mensajes

| Situación | Qué pasa | Qué hacer |
|---|---|---|
| Quedan trabajos y core-consultas ya no los consume | Esperan 24 h y pasan a `.dlq`. Allí duran 7 días | Vuelva a subir core-consultas para que drene |
| Se quiere vaciar una cola | `purge_queue` **destruye** los trabajos | Solo con confirmación del Líder Técnico |
| Se quiere borrar una cola, un exchange o una política | | **No lo haga.** Es la regla de no eliminar recursos persistentes |
| `dlq-7d` estorba | Es el control de Ley 1581 sobre datos en claro | Solo por PR revisado por Seguridad. Nunca `clear_policy` |

Los permisos del usuario `diagnostico` pueden quedarse. No afectan a nada sin el servicio.

### 8.4 Migraciones

**Los servicios no revierten migraciones al bajar de versión.** Solo aplican hacia adelante.

| Migración | ¿Bloquea volver atrás? | Qué hacer |
|---|---|---|
| core-consultas: `ImpuestoDepartamentosHabilitados` | No. Crea una tabla nueva. La imagen anterior no la usa y no ve migraciones pendientes | Dejar la tabla |
| core-diagnostico, las 21 | No aplica: **DEV no tiene una versión anterior** | `replicas: 0`. Dejar el esquema |

**No ejecute `DROP SCHEMA` ni `dotnet ef database update` hacia atrás.** Hay documentos cifrados y anillo de llaves en ese esquema.
El `DROP` del handoff §1 vale solo si el servicio nunca se usó. **Nunca borre** el certificado ni
`diagnostico.data_protection_keys`.

Si el arranque falla **durante** una migración, el pod reinicia en bucle y `/health/ready` da 503. Lea el log. EF serializa réplicas.
No corrija el esquema a mano sin el Líder Técnico. Si la causa es de código, se escala al agente implementador.

---

## 9. Pendientes que afectan el despliegue

Nada de esto se corrigió en este documento. Las rutas se verificaron en el commit de referencia.

**Bloquean el merge o el despliegue:**

1. **Sin work item registrado no hay merge a `develop`.** La rama arrancó sin él (`docs/decisions/ADR-0080-departamentos-impuesto-por-tabla.md:7`).
   Además, las reglas piden PR de hasta 800 líneas, build verde, Code Review y Security, y un reviewer humano real
   (`CLAUDE.md`, §1 y §2). El diff actual son 900 archivos: **hay que resolver cómo se parte o se exceptúa**. Lo cierra el Líder Técnico.
2. **Resuelto: el CD ya escribe los digests de Diagnóstico.** `deploy-gitops` incluye sus dos builds. Si una kustomization no lista
   las imágenes, se omiten sin fallar, igual que Persona Natural: QA y PDN siguen desplegando. En DEV, la imagen debe estar listada
   antes del merge para recibir su digest (§6, paso 1).
3. **Manifiestos en `flit-gitops` sin hacer.** Comprobado el 2026-11-04: no hay nada de Diagnóstico en `services/flit2-dev/`.
4. **`svc-diagnostico` no tiene `platform.consultas`.** El cliente pide ese scope (`ClienteConsultas.cs:36`, `:126-127`) y
   core-consultas lo exige (`C/Grpc/ConsultasGrpcService.cs:35`). `appsettings.json:232` no lo incluye y
   `.github/scripts/test_despliegue_diagnostico.py:254-259` **exige que no esté**. En local funciona porque se inyecta a mano
   (`guia-entorno-local-suite.md:107`, `:156`). Efecto: con `Address` puesta, la réplica de departamentos no sincroniza (evento 138613).
   El resto de fuentes no se afecta. Para DEV: la variable de §3.5 en core-api **y** core-identity (PC-8).

**Impuesto real, fuera de este despliegue:**

5. **El encendido real no forma parte de este despliegue.** Exige avales de Seguridad, Habeas Data y Legal, y la ratificación del
   Líder Técnico (`handoff-vps-diagnostico.md:303-307`, `ADR-0079…md:78`). Hoy viajarían nombre y documento en claro por outbox, cola y DLQ.
6. **`ADR-0075`, `ADR-0079` y `ADR-0080` siguen en Propuesto** (línea 4 de `ADR-0075`, `ADR-0079` y `ADR-0080`). Solo el Líder Técnico
   puede aceptarlos.
7. **La semilla contradice la decisión P7.** La migración enciende 50, 52 y 94 (`20261104150611_ImpuestoDepartamentosHabilitados.cs:42-51`,
   `ADR-0080…md:14`, `:86`). El usuario eligió Bolívar (13) y Huila (41) (`plan-consulta-impuesto.md:512`). Antes de cualquier encendido
   real hay que reconciliarlo: los tres departamentos sembrados **no** son los elegidos, y 13 y 41 no están en la tabla.

**Documentos, compose y código que no coinciden:**

8. **Nombres errados en el mensaje de arranque.** `D/ServicioSettings.cs:24-25` pide `DIAGNOSTICO_DP_CERTIFICADO_RUTA` y `_CLAVE`.
   El compose usa `DIAGNOSTICO_DP_CERT_FILE` y `DIAGNOSTICO_DP_CERT_PASSWORD` (`DC:569`, `:1326`). Si un operador sigue el log, crea variables
   que nadie lee. Lo correcto en el contenedor: `Diagnostico__DataProtection__CertificadoRuta` y `__CertificadoClave`.
9. **`IMPUESTO_CONTACTO_*`.** El compose no las pasa (`DC:681-684`). Además, `handoff-vps-diagnostico.md:283-288` usa
   `IMPUESTO_CONTACTO_DEPARTAMENTO`, `_CIUDAD` y `_DIRECCION`, y el código lee `_DIRECCION_DEPARTAMENTO`, `_DIRECCION_CIUDAD` y
   `_DIRECCION_COMPLETA` (`OpcionesImpuesto.cs:70-73`). Con los nombres del handoff, quedan vacías sin error.
10. **`handoff-vps-diagnostico.md:282` rompe el arranque.** Propone `IMPUESTO_API_TIMEOUT_SECONDS=300`, y con 300 core-consultas **no
    arranca** (`OpcionesImpuesto.cs:56-57`). El defecto real es 280 (`DC:683`).
11. **`IMPUESTO_DEPARTAMENTOS` ya no tiene efecto** (ADR-0080). Sigue en `DC:684` y `.env.impuesto.example:5`, y el handoff aún habla de «vaciar la
    lista» (`:292-293`, `:317`). `OpcionesImpuesto.cs:31` y `:49-63` ya no la leen. Para apagar: la tabla.
12. **Comentario desfasado en el compose.** `DC:551-555` dice que `Diagnostico__Consultas__Address` enciende juntas todas las fuentes y que
    la simulación se valida en Diagnóstico. Desde ADR-0075, solo sirve para la réplica, y la simulación vive en core-consultas
    (`ClienteConsultas.cs:19-21`).
13. **Faltan variables en el compose y en `.env.prod.example`.** `DC:588-684` no pasa `Consultas__Ambiente` ni `Consultas__Simulacion__*`.
    `DC:525-569` no pasa `Diagnostico__Anthropic__*`. `.env.prod.example:374-400` no trae `DIAGNOSTICO_CONSULTAS_ADDRESS`, `DIAGNOSTICO_AMBIENTE` ni
    `DIAGNOSTICO_IMPUESTO_DISPONIBLE`. No bloquea DEV, que usa `flit-gitops`, pero sí a quien use el compose.
14. **Colas y handoff.** `handoff-vps-diagnostico.md:75-86` y `deploy/rabbitmq/README.md:67` hablan de «tres colas». `definitions.json:12-16` declara cinco.
15. **Perfil `bus` inexistente.** `deploy/rabbitmq/README.md:8`, `:17` lo pide. El servicio `rabbitmq` no tiene `profiles` (`DC:390-411`).
16. **Handoffs viejos.** `handoff-vps-suite.md:582-583` dice que Diagnóstico no tiene servicio en el compose.
    `despliegue-dev-k3s-comparendos.md:6` dice que QA sigue con compose, y `cd.yml:1370-1375` dice que no. `handoff-vps-diagnostico.md:59-62` dice que
    el `.env` debe tener `RABBITMQ_URL_DIAGNOSTICO` antes de fusionar: solo aplica a quien usa el compose. Manda el `cd.yml`.
17. **`Diagnostico:Simulacion:Habilitada` sigue leyéndose.** `guia-entorno-local-suite.md:11` dice que ya no existe. `RegistroRedactor.cs:23` la usa para simular
    el redactor. Prefiera `Diagnostico__Anthropic__Simulado`.
18. **Quitar «Próximamente» por PR.** `handoff-vps-diagnostico.md:386` pide un PR «para ese ambiente», pero `appsettings.json:209` es uno solo para todos.
    Un PR sacaría a Diagnóstico también de QA y PDN.

**Otros:**

19. **El script local no es parte del despliegue.** `scripts/dev/levantar-diagnostico-local.sh` está ignorado por git. Nada aquí depende de
    él. Quien lo tome de modelo: usa valores de LOCAL, inyecta el scope a mano y guarda archivos en la base.
20. **La alerta de DLQ asume compose** (`deploy/rabbitmq/alerta-dlq.sh:17`, `:28`). En k3s no funciona tal cual (PC-10). Sin ella, los
    mensajes muertos no avisan.
21. **Rotación del certificado sin procedimiento** (`handoff-vps-diagnostico.md:359-361`).

---

## 10. Por confirmar

Quedaron abiertos porque dependen de `flit-gitops` o de decisiones que no están en este repositorio.

| Id | Qué falta | Lo cierra |
|---|---|---|
| PC-1 | **Cerrado para DEV:** ver la tabla inicial | |
| PC-2 | **Cerrado para DEV:** ver la tabla inicial | |
| PC-3 | Valor real de `ASPNETCORE_ENVIRONMENT` en QA y PDN. DEV: `Development` | Líder Técnico |
| PC-4 | **Respondido para DEV:** cada mensaje retiene una conexión mientras espera al proveedor, y 8 no alcanzan (§5.1). Falta decidir los valores de QA y PDN | Líder Técnico con backend-agent |
| PC-5 | **Cerrado para DEV:** ver la tabla inicial | |
| PC-6 | Tiempo de lectura que necesita la descarga de PDF y ZIP. El clúster del gateway corta a 30 s y nginx de Comparendos a 120 s: medir en el humo. No hay ingress: ver la tabla inicial | Líder Técnico |
| PC-7 | Sacar a Diagnóstico de «Próximamente» solo en DEV: `Suite__Hosts__Overrides__diagnostico` en core-api y core-identity (`ConfiguredProductHosts.cs:31`). **No quite** `Suite__Hosts__ComingSoon__0=diagnostico`: Comparendos volvería a «Próximamente», porque ese índice lo reemplaza en `appsettings.json`. No se probó en un ambiente | Líder Técnico |
| PC-8 | Si el scope `platform.consultas` queda como variable por ambiente o entra a `appsettings.json` (con su prueba) | Líder Técnico con backend-agent |
| PC-9 | Redactor en DEV: llave real de Anthropic, simulado o sin configurar | Líder Técnico con PO |
| PC-10 | Alerta de DLQ en k3s | Líder Técnico |
| PC-11 | Cómo se monta el `.pfx` en k3s y con qué ruta | Líder Técnico |
| PC-12 | **Cerrado:** `core-identity` expone 8083 en DEV | |
| PC-13 | Cuándo y con qué credenciales pasa DEV de simulación a fuentes reales (`INTEMPO_API_BASE_URL`, saldo) | Líder Técnico con PO |

---

## 11. Documentos relacionados

- [handoff-vps-diagnostico.md](handoff-vps-diagnostico.md): detalle de base, políticas, TTL y certificado. Tiene errores conocidos: §9, puntos 9 a 11, 14 y 16.
- [despliegue-dev-k3s-comparendos.md](despliegue-dev-k3s-comparendos.md): el mismo procedimiento para otro producto en k3s. El mejor modelo.
- [handoff-vps-consultas-notificaciones.md](handoff-vps-consultas-notificaciones.md): proveedores y usuarios del broker.
- [handoff-vps-suite.md](handoff-vps-suite.md): hosts, DNS y clientes OIDC.
- [../diagnosticos/guia-entorno-local-suite.md](../diagnosticos/guia-entorno-local-suite.md): modos de fuentes y datos simulados.
- [ADR-0075](../decisions/ADR-0075-diagnostico-consultas-por-rabbitmq.md), [ADR-0079](../decisions/ADR-0079-diagnostico-impuesto-por-fachada.md), [ADR-0080](../decisions/ADR-0080-departamentos-impuesto-por-tabla.md).
