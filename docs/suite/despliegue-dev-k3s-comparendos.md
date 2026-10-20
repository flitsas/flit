# Comparendos en DEV (k3s de la 177 + Argo CD)

Epic #12878 (GDC). Para el líder técnico. Desde el 2026-10-17 DEV se despliega en el k3s de la 177 con Argo CD y su
configuración vive en `flit-gitops/services/flit2-dev/` (configmaps + sealed-secrets), no en el `.env` ni en
`docker-compose.prod.yml` (commit `66aef83ec`). Este documento dice qué agregar allá para encender Comparendos en DEV.
QA sigue con docker compose: para QA vale [handoff-vps-comparendos.md](handoff-vps-comparendos.md), que además explica
el porqué de cada pieza.

## 0. Orden (importa)

El job `deploy-dev-gitops` de `cd.yml` **falla a propósito** si una imagen que tiene que actualizar no está en
`services/flit2-dev/kustomization.yaml` (evita despliegues a medias). Por eso:

1. **flit-gitops primero:** los manifiestos de §3 y §4 y las dos imágenes en la kustomization (con el tag que haya;
   `replicas: 0` en los dos Deployments hasta tener la base y los secretos), más los cambios de §5 en los servicios
   existentes.
2. **Después el PR de Comparendos a develop:** agrega `core-comparendos` y `frontend-comparendos` a la lista del job
   `deploy-dev-gitops` (los dos builds ya existen en `cd.yml`). Al fusionar, el job escribe sus digests.
3. **Base y RabbitMQ** (§1 y §2), secretos sellados (§3), `replicas: 1` y Argo sincroniza.

## 1. Base de datos

Igual que en docker (el script es el mismo): un rol dueño del esquema `comparendos` en la base de DEV.

```bash
CLAVE=$(openssl rand -base64 32 | tr -d '/+=')
psql "$ADMIN_URL" -v servicio=comparendos -v conexiones=20 -v clave="$CLAVE" -f deploy/postgres/servicio-con-esquema-propio.sql
```

El servicio crea y migra su esquema al arrancar (`GDC_ModeloInicial`) y siembra lo suyo (calendario, 298 organismos,
11 tipologías, plantillas, prompt OCR, trabajos). Idempotente. **Desde este despliegue, cada cambio de esquema es una
migración nueva**; la inicial no se vuelve a regenerar.

## 2. RabbitMQ

Usuario del servicio con permiso de escribir en `flit.notificaciones` (mismo script que en docker):
`deploy/rabbitmq/usuario-de-servicio.sh comparendos <clave> notificaciones`.

## 3. core-comparendos

| | |
|---|---|
| Imagen | `ghcr.io/flitsas/flitdev/core-comparendos` (por digest, lo escribe el CD) |
| Puertos | HTTP `4028` (REST, llega por el gateway) · gRPC `8086` (h2c, interno) |
| Sondas | vida `GET /health` · lista `GET /health/ready` (503 mientras migra o sin base) |
| Service | `core-comparendos` con los dos puertos; **no** se publica por ingress |
| Recursos | sin mínimo conocido; con ~6.000 comparendos reales la bandeja responde en ~350 ms |

**ConfigMap** (valores de DEV):

| Variable | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` (igual que los demás servicios de DEV) |
| `ASPNETCORE_URLS` | `http://+:4028` |
| `Servicio__GrpcPort` | `8086` |
| `Platform__ServiceClient__TokenEndpoint` | `http://gateway:4002/connect/token` |
| `Platform__Auth__JwksUri` | `http://gateway:4002/.well-known/jwks.json` |
| `Platform__Auth__Issuers__0` | `https://dev.flitsas.online/` |
| `Comparendos__Plataforma__GatewayUrl` | `http://gateway:4002` |
| `Comparendos__AppUrl` | `https://dev.comparendos.flitsas.online` |
| `Comparendos__Consultas__Address` | `http://core-consultas:8084` |
| `Comparendos__Notificaciones__Address` | `http://core-notificaciones:8085` |
| `Comparendos__Identidad__Address` | `http://core-identity:8083` (**obligatoria**: sin ella ninguna empresa cuenta como habilitada) |
| `Comparendos__Tramites__Address` | `http://core-api:<puerto gRPC de core-api, el mismo de ICT>` (réplica de organismos) |
| `FILE_MANAGER_BASE_URL` | **la misma URL que usa core-api en DEV** (MinIO). **Obligatoria**: sin ella el servicio no arranca |
| `FILE_MANAGER_FILES_PATH` | `api/v1/files` |
| `FILE_MANAGER_CATEGORY` | `comparendos` (carpeta `flit-files/comparendos/<año>/<mes>/<día>`, junto a `tramites` e `ict`) |
| `FILE_MANAGER_TIMEOUT_SECONDS` | `60` |
| `Comparendos__Simulacion__Habilitada` | vacío (en `Development` los proveedores «simulado» siguen disponibles por empresa) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | el de los demás servicios, si lo hay |

**SealedSecret:**

| Variable | Qué es |
|---|---|
| `ConnectionStrings__Servicio` | `Host=<postgres>;Port=5432;Database=<base de DEV>;Username=flit_comparendos;Password=<§1>;Maximum Pool Size=20` |
| `Platform__ServiceClient__ClientSecret` | secreto de `svc-comparendos`; **el mismo valor** que en core-api/core-identity (§5) |
| `Platform__Messaging__ConnectionString` | `amqp://comparendos:<§2>@rabbitmq:5672/flit` |
| `FILE_MANAGER_AUTH_TOKEN` | el de core-api (MinIO de DEV no lo exige hoy; en PDN Contabo sí) |
| `Comparendos__Ocr__ApiKey` | llave de Anthropic (la de core-api). Sin ella, en `Development` el OCR queda simulado |

**No poner:** `Comparendos__Renting__BaseUrl` / `__ApiKey` (la API de contratos de Renting no está construida; va en
otro corte) ni `Comparendos__Archivos__GuardarEnBase` (solo para pruebas automáticas).

## 4. frontend-comparendos

| | |
|---|---|
| Imagen | `ghcr.io/flitsas/flitdev/frontend-comparendos` |
| Puerto | `4023` · sonda `GET /healthz` |
| Ingress | `dev.comparendos.flitsas.online` → `frontend-comparendos:4023`, TLS como Trámites. **Cuerpo hasta 50 MB** (paquetes OCR, Excel de pagos), **timeout de lectura 120 s** (carga de 200 archivos OCR) y la IP real del cliente en `X-Real-IP` (el límite del portal del conductor la usa) |

| Variable | Dónde | Valor |
|---|---|---|
| `PORT` | ConfigMap | `4023` |
| `HOSTNAME` | ConfigMap | `0.0.0.0` |
| `CORE_API_ORIGIN` | ConfigMap | `http://gateway:4002` |
| `BRANDING_INTERNAL_API_URL` | ConfigMap | `http://gateway:4002` |
| `FLIT_OIDC_INTERNAL_URL` | ConfigMap | `http://gateway:4002` |
| `FLIT_HUB_URL` | ConfigMap | `https://dev.flitsas.online` |
| `TRAMITES_URL` | ConfigMap | `https://dev.tramites.flitsas.online` |
| `FLIT_HOSTS` | ConfigMap | el mismo valor que el hub/Trámites en DEV |
| `FLIT_SESSION_SECRET` | SealedSecret | `openssl rand -base64 48`, **distinto** del del hub |
| `FLIT_INTERNAL_API_KEY` | SealedSecret | el mismo del hub/Trámites en DEV |

El cliente OIDC `comparendos` se registra solo con el host del ambiente.

## 5. Cambios en servicios que ya corren en DEV

| Servicio | Variable | Valor / motivo |
|---|---|---|
| core-api | `Suite__Oidc__ServiceClients__svc-comparendos__Secret` (SealedSecret) | el mismo secreto de §3. Reiniciar core-api |
| core-api | `Suite__Hosts__ComingSoon__0` = `diagnostico` (ConfigMap) | saca a Comparendos de «próximamente» en el hub **solo en DEV**; sin tocar `appsettings.json` |
| core-identity | `Suite__Oidc__ServiceClients__svc-comparendos__Secret` (si corre aparte) | mismo secreto |
| core-identity | `Identidad__GrpcPort` = `8083` + puerto `8083` en su Service | gRPC de empresas habilitadas (`EmpresasService`) |
| core-consultas | `SMARTCLOUD_ACCESS_KEY` (SealedSecret) | la clave de V1. **Evaluar rotarla**: se expuso en una salida local el 2026-10-19 |
| core-consultas | `SMARTCLOUD_URL_IDENTIFICACION` / `SMARTCLOUD_URL_PLACA` | `https://www3.simit.org.co/ServiciosSimit/EstadoCuentaIdentificacion` / `…/EstadoCuentaPlaca` |
| core-consultas | `MUNICIPAL_INFRACCIONES_BASE_URL` / `MUNICIPAL_INFRACCIONES_PATH` | `http://ec2-44-206-131-113.compute-1.amazonaws.com` / `/infraction/api/Infraccion/EntregarInfraccion` |
| gateway | `ReverseProxy__Clusters__core-comparendos-cluster__Destinations__core-comparendos-1__Address` | `http://core-comparendos:4028/` |
| core-notificaciones | — | nada nuevo. **Ojo:** en DEV tiene SMTP real; antes de usar Comparendos se acuerdan los cambios de correos en la base (Samuel) |

## 6. Comprobar

```bash
kubectl -n <ns> logs deploy/core-comparendos | head -50      # migró, sembró y registró el manifiesto
kubectl -n <ns> exec deploy/core-comparendos -- wget -qO- http://localhost:4028/health/ready
curl -s https://dev.flitsas.online/api/v1/comparendos/sesion -o /dev/null -w '%{http_code}\n'   # 401 = la ruta llega
curl -s https://dev.comparendos.flitsas.online/healthz
```

En la base: `SELECT count(*) FROM comparendos.organismo_transito;` → 298. En el hub: el SuperAdmin enciende Comparendos
por empresa (Compañías → Productos) y asigna `admin_comparendos`; en Comparendos → Parametrizador activa capacidades y
la configuración de consultas por empresa (sin eso no se consulta nada).

## 7. Lo que se vio con las fuentes reales en local (2026-10-19)

- SmartCloud por NIT: 5.201 comparendos de un NIT en 8 s; la respuesta pasa de 4 MB (ya resuelto en el cliente gRPC).
- API municipal: Cali falla siempre (SSL del lado de la API de V1); Medellín/Itagüí/Rionegro/Sabaneta a veces. El ciclo
  queda «Parcial» y entra lo demás.
- Catálogo: Obando, Yalí y Santa Catalina no están en los 298 organismos (pendiente de decidir si se agregan en la
  plataforma); textos como «ATLANTICO» se resuelven con un alias en Comparendos → Organismos.

## 8. Volver atrás

`replicas: 0` en los dos Deployments (o revertir el commit en flit-gitops). Los datos quedan en su esquema; nada de
core-api depende de él.
