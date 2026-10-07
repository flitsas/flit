# Cómo se conecta un servicio de plataforma nuevo

Epic #13316 (HU #13341). Un servicio de plataforma (Consultas, Notificaciones y los que vengan) nace de la plantilla
`services/core-plantilla` y se conecta siempre igual. Esta guía es la lista completa; si un paso no aplica, se dice
por qué en el PR.

Reglas que no cambian (ADR-0064, ADR-0070, contrato de plataforma v1.3):
- Entre servicios, gRPC con token de servicio (`svc-<código>`); hacia afuera de FLIT, REST por el gateway.
- Asíncrono por RabbitMQ con la outbox y la bandeja del SDK. Nunca se leen las tablas de otro servicio.
- Cada servicio es dueño de un solo esquema de Postgres, con su propio usuario de base.

En los ejemplos el servicio es `consultas` (nombre `Consultas`).

## 1. Generar el servicio

```bash
cd services
dotnet new install ./core-plantilla
dotnet new flit-servicio -n Consultas -o core-consultas
```

Queda `services/core-consultas` con el SDK, su esquema `consultas` con outbox e inbox (primera migración), `/health`,
`/health/ready`, `grpc.health.v1`, Dockerfile y pruebas. Compila y pasa sus pruebas sin tocar nada:

```bash
cd core-consultas && dotnet test Flit.Consultas.slnx
```

## 2. Contrato gRPC

1. `contracts/proto/flit/consultas/v1/consultas.proto`, paquete `flit.consultas.v1`, servicio con sufijo `Service`.
   Reglas en `contracts/proto/README.md`.
2. `cd contracts/proto && npx -y @bufbuild/buf@1.73.0 lint`.
3. Agregar la fila del proyecto a la tabla «Código C#» de `contracts/proto/README.md`.

Sin este archivo el CI del servicio falla con «Falta el contrato gRPC de core-consultas».

Si publica eventos: `contracts/asyncapi/consultas-events.v1.yaml` (exchange `flit.consultas`) antes de publicar.

## 3. Cliente de servicio y scope

- **Su cliente** (con el que llama a otros): `svc-consultas` en `Suite:Oidc:ServiceClients` de
  `services/core-api/src/Flit.Api/appsettings.json`, con los scopes que necesita (p. ej. `platform.identidad.read`).
  En el compose, en el bloque de core-api: `Suite__Oidc__ServiceClients__svc-consultas__Secret: ${SVC_CONSULTAS_CLIENT_SECRET:-}`.
- **Su scope** (con el que otros lo llaman): `platform.consultas` en `OidcDefaults.ServiceScopeAudiences` del SDK, con
  su audiencia en `ServiceAudiences`. Un scope nuevo también va al contrato §3 (rama propia del contrato, lo revisa el
  líder).
- En `.env.prod.example`: `SVC_CONSULTAS_CLIENT_SECRET=` (uno por ambiente, `openssl rand -base64 32`).

## 4. Base de datos

El líder crea el usuario dueño del esquema en cada ambiente (HU #13330):

```bash
psql "$ADMIN" -v servicio=consultas -v conexiones=20 -v clave="$CLAVE" -f deploy/postgres/servicio-con-esquema-propio.sql
psql "$CONSULTAS" -f deploy/postgres/verificar-aislamiento.sql
```

Con eso arma `CONNECTION_STRING_CONSULTAS` (usuario `flit_consultas`). El servicio aplica sus migraciones al arrancar,
solo sobre su esquema.

## 5. Puertos

Sin app web no hay puerto en nginx ni mapeo en `ports`: REST y gRPC quedan solo en la red de Docker. Los números se
asignan con el líder y se registran en `docs/despliegue-y-puertos.md` (§2.d, «Puertos gRPC internos»). Propuesta:

| Servicio | REST (interno) | gRPC (interno) |
|---|---|---|
| core-consultas | 4026 / 5026 / 6026 | 8084 |
| core-notificaciones | 4027 / 5027 / 6027 | 8085 |

## 6. Bloque en `docker-compose.prod.yml`

Con perfil propio: no arranca hasta que se pida (`COMPOSE_PROFILES`), como core-identity.

```yaml
  # ── core-consultas (Epic #13316) — CORE_CONSULTAS_PORT / CORE_CONSULTAS_GRPC_PORT, sin publicar ──
  core-consultas:
    profiles: ["consultas"]
    image: ghcr.io/flitsas/flitdev/core-consultas:${CORE_CONSULTAS_TAG:-latest}
    environment:
      ASPNETCORE_ENVIRONMENT: ${FLIT_DOTNET_ENVIRONMENT:-Development}
      ASPNETCORE_URLS: http://+:${CORE_CONSULTAS_PORT:-4026}
      Servicio__GrpcPort: ${CORE_CONSULTAS_GRPC_PORT:-8084}
      ConnectionStrings__Servicio: ${CONNECTION_STRING_CONSULTAS:-}
      Platform__ServiceClient__ClientSecret: ${SVC_CONSULTAS_CLIENT_SECRET:-}
      Platform__ServiceClient__TokenEndpoint: http://gateway:${GATEWAY_PORT:-4002}/connect/token
      Platform__Auth__JwksUri: http://gateway:${GATEWAY_PORT:-4002}/.well-known/jwks.json
      Platform__Auth__Issuers__0: ${FLIT_HUB_URL:-https://dev.flitsas.online}/
      Platform__Messaging__ConnectionString: ${RABBITMQ_URL_CONSULTAS:-}
      OTEL_EXPORTER_OTLP_ENDPOINT: ${OTEL_EXPORTER_OTLP_ENDPOINT:-}
    healthcheck:
      test: ["CMD", "wget", "-qO-", "http://localhost:${CORE_CONSULTAS_PORT:-4026}/health/ready"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 40s
    restart: *restart
    mem_limit: ${CORE_CONSULTAS_MEM_LIMIT:-0}
    cpus: ${CORE_CONSULTAS_CPUS:-0}
    logging: *default-logging
```

Si falta una variable, el contenedor no arranca y su log dice cuál y de dónde sale. `RABBITMQ_URL_CONSULTAS` es la
cadena de su usuario del broker (`deploy/rabbitmq/usuario-de-servicio.sh consultas <clave>`, ver `deploy/rabbitmq`). Documentar las variables nuevas en `.env.prod.example`.

## 7. CD (`.github/workflows/cd.yml`)

1. Un job `build-core-consultas` copiado de `build-core-identity`, con:
   - `image: ${{ env.CORE_CONSULTAS_IMAGE }}` (y `CORE_CONSULTAS_IMAGE: ghcr.io/flitsas/flitdev/core-consultas` en `env`);
   - en `reuse-image`, las rutas que compila su imagen:
     ```
     services/core-consultas
     services/core-api/Directory.Build.props
     services/core-api/Directory.Packages.props
     services/core-api/src/Flit.Platform.Sdk
     services/core-identity/Directory.Build.props
     services/core-identity/Directory.Packages.props
     services/core-identity/src/Flit.Platform.Grpc.Contracts
     contracts/proto
     ```
   - en `build-push-action`: `context: ./services`, `file: ./services/core-consultas/Dockerfile` y
     `build-contexts: contracts=./contracts/proto`.
2. En `deploy-to-vps`: sumarlo a `needs`, `export CORE_CONSULTAS_TAG=...` junto a los demás tags, y agregarlo a
   `SERVICES` y a los `case` de `build_result`, `build_digest` y `tag_var`. Con despliegue por servicio
   (`FLIT_DEPLOY_ROLLING`) un build fallido deja corriendo la versión anterior.
3. Exportar sus puertos en `setup` si cambian por ambiente.

## 8. CI

Automático: `.github/workflows/servicios-plataforma.yml` descubre todo servicio que nació de la plantilla (por su
`ServicioSettings.cs`), exige su contrato, compila y corre sus pruebas. No hay que editarlo.

## 9. Antes del PR

- [ ] `dotnet test` del servicio en verde y `buf lint` limpio.
- [ ] `docker build -f services/core-consultas/Dockerfile --build-context contracts=contracts/proto services`
      (en Mac Apple Silicon, con `--platform linux/amd64`: `protoc` falla en linux/arm64).
- [ ] Sin la configuración, el contenedor no arranca y nombra las variables.
- [ ] Puertos confirmados con el líder y registrados en `docs/despliegue-y-puertos.md`.
- [ ] Bloque de compose, job de CD y variables en `.env.prod.example`; pasos de VPS en el handoff para el líder.
