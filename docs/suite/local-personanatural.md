# El portal Persona Natural en local, de punta a punta

HU #14069 (Épica #14021). Cómo levantar en tu máquina el conjunto completo con piezas REALES (core-api, core-consultas,
core-notificaciones, core-personanatural, gateway YARP, front del portal, Postgres y RabbitMQ) y recorrer el flujo real:
cotizar, abrir la solicitud, completar los pasos, radicar y ver el trámite BORRADOR nacer en FLIT 2.

Todo externo va simulado. **Nada de esto toca servicios reales**: biometría `mock`, consultas RUNT/Verifik `mock`, SMTP a la
consola, captcha deshabilitado y file-manager simulado. Las claves locales viven solo en
`scripts/e2e-personanatural/.env.local` (no se versiona). Cópiala desde `.env.example` y complétala en tu máquina.

## Qué hace falta

- Postgres nativo en `127.0.0.1:5432` (usuario `postgres`; los scripts usan la clave local de desarrollo `flitdb`, cámbiala
  con `-PgClave`). `psql` se busca en `C:\Program Files\PostgreSQL\17\bin\psql.exe`.
- RabbitMQ del compose local (`docker compose -f deploy/rabbitmq/docker-compose.local.yml up -d`), vhost `flit`.
- .NET SDK 10, Node 24, pnpm 11 y PowerShell 7 (`pwsh`).
- Front: `pnpm install --filter "@flit/frontend-personanatural..."` (una vez).

## Montaje, en orden

```powershell
# 0. Claves locales (una vez) y usuarios del broker. Las claves salen de .env.local, no de este documento.
Copy-Item scripts/e2e-personanatural/.env.example scripts/e2e-personanatural/.env.local
# Edita .env.local y luego cárgala en la sesión:
Get-Content scripts/e2e-personanatural/.env.local | ForEach-Object {
  if ($_ -match '^\s*([^#=]+)=(.*)$') { Set-Item -Path "Env:$($Matches[1].Trim())" -Value $Matches[2].Trim() }
}
$env:COMPOSE = "docker compose -f deploy/rabbitmq/docker-compose.local.yml"   # en Git Bash: export COMPOSE=...
deploy/rabbitmq/usuario-de-servicio.sh tramites       "$env:PN_E2E_MQ_TRAMITES_CLAVE" notificaciones,personanatural,consultas,plataforma
deploy/rabbitmq/usuario-de-servicio.sh personanatural "$env:PN_E2E_MQ_PN_CLAVE" tramites
deploy/rabbitmq/usuario-de-servicio.sh consultas      "$env:PN_E2E_MQ_CONSULTAS_CLAVE"
deploy/rabbitmq/usuario-de-servicio.sh notificaciones "$env:PN_E2E_MQ_NOTIF_CLAVE"

# 1. Compilar (la primera vez) y levantar todo en orden, con PID en scripts/e2e-personanatural/.pids.json
pwsh -File scripts/e2e-personanatural/levantar.ps1 -Compilar -LogDir C:\temp\pn-logs

# 2. Datos de prueba (ficticios) como superadministrador: organismo, tipo, tarifas y una pregunta frecuente
node scripts/e2e-personanatural/sembrar.mjs

# 3. Recorrido de punta a punta con informe PASS/FAIL por paso
$env:BUS_LOG = "C:\temp\pn-logs\bus-mensajes.log"      # opcional: el observador del bus
node scripts/e2e-personanatural/recorrido.mjs

# 4. Apagar por PID exacto (RabbitMQ y Postgres no se tocan)
pwsh -File scripts/e2e-personanatural/apagar.ps1
```

`levantar.ps1` crea la base `flit_pn_e2e` si no existe (core-api la migra al arrancar; core-consultas, core-notificaciones y
core-personanatural migran su esquema propio en la misma base), siembra la empresa «Persona Natural»
(`deploy/postgres/crear-empresa-persona-natural.sql`) y es **re-ejecutable**: lo que ya corre se deja y se completa lo que falte.
La primera vez core-api tarda unos 3 minutos en migrar.

Orden de arranque: `stub-filemanager` → `observar-bus` → `core-api` (migra, emite tokens, fachada gRPC 8082, consumidor) →
empresa Persona Natural → `core-consultas` (4026, gRPC 8084) → `core-notificaciones` (4027, gRPC 8085) →
`core-personanatural` (4029, gRPC 8087) → `gateway` (4002) → `frontend-personanatural` (4031).

core-identity NO se levanta: core-api emite los tokens de servicio y valida los de usuario por sí mismo. core-notificaciones sí,
porque el envío de la cotización por correo es una llamada gRPC real. El front de FLIT 2 (`frontend/`) no hace falta; solo lo
necesitas para ver Plataforma → Persona Natural.

## Configuración exacta por servicio

Todas se pasan por variables de entorno (`levantar.ps1` las pone; no hay `appsettings.Development.json` que mantener).

**core-api** (`http://127.0.0.1:4003`, gRPC `Ict__GrpcPort=8082`)

| Variable | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` (siembra usuarios demo y OT de desarrollo) |
| `ConnectionStrings__Core` | `Host=127.0.0.1;Port=5432;Database=flit_pn_e2e;Username=postgres;Password=<local>` |
| `Suite__Oidc__Enabled` | `true` |
| `Suite__Hosts__Overrides__plataforma` / `__tramites` | `http://127.0.0.1:4022` / `http://127.0.0.1:3000` (el emisor de los tokens es `http://127.0.0.1:4022/`, aunque el hub no corra) |
| `Suite__Oidc__ServiceClients__svc-tramites\|svc-consultas\|svc-notificaciones\|svc-personanatural__Secret` | secretos ficticios de 32+ caracteres (el mismo valor en el servicio que lo usa) |
| `Consultas__Remoto__Address` / `Notificaciones__Remoto__Address` | `http://127.0.0.1:8084` / `http://127.0.0.1:8085` |
| `Tramites__Bus__Habilitado`, `Platform__Messaging__ConnectionString` | `true`, `amqp://tramites:<clave>@127.0.0.1:5672/flit` |
| `Platform__ServiceClient__ClientId/ClientSecret/TokenEndpoint` | `svc-tramites`, su secreto, `http://127.0.0.1:4003/connect/token` |
| `Biometrics__Provider` | `mock` (con `kyverum` Kyverum REAL crea validaciones facturables) |
| `FileManager__BaseUrl` | `http://127.0.0.1:4999/` (el simulado; el real es el S3 corporativo) |
| `PersonaNatural__Fachada__Habilitada` | `true` |
| `PersonaNatural__Consumidor__Habilitado`, `PersonaNatural__Portal__Address` | `true`, `http://127.0.0.1:8087` (gRPC del BFF) |

**core-consultas** (`4026`, gRPC `Servicio__GrpcPort=8084`)

- `ConnectionStrings__Servicio`: la misma base. `Platform__Auth__JwksUri=http://127.0.0.1:4003/.well-known/jwks.json`,
  `Platform__Auth__Issuers__0=http://127.0.0.1:4022/`, `Platform__ServiceClient__ClientSecret` (el de `svc-consultas`),
  `Platform__ServiceClient__TokenEndpoint`, `Platform__Messaging__ConnectionString` (usuario `consultas`).
- Todo en mock: `VERIFIK_VEHICLE_MODE`, `VERIFIK_CONDUCTOR_MODE`, `VERIFIK_SIMIT_MODE`, `VERIFIK_RNMC_MODE`, `VERIFIK_RUES_MODE`,
  `FASECOLDA_MODE`, `INTEMPO_MODE`, `KYVERUM_FINES_MODE` = `mock`.
- **Kyverum RUNT no tiene modo mock y su llave siempre se exige al arrancar** (aunque todo lo demás sea mock) y encabeza la
  cadena por defecto. Para que NADA salga a internet: `KYVERUM_RUNT_API_KEY` ficticia, `KYVERUM_RUNT_BASE_URL=http://127.0.0.1:4998/`
  (dirección muerta) y la cadena sin Kyverum: `Consultations__DefaultChains__vehicle_vin__0=verifik`,
  `...vehicle_plate__0=verifik`, `...conductor__0=verifik_conductor`.
- El mock de vehículo devuelve el mismo vehículo de prueba (TESLA MODELO Y 2026) para **cualquier placa**; el de persona, un
  «JUAN CARLOS PEREZ GOMEZ» para cualquier documento.

**core-notificaciones** (`4027`, gRPC `8085`): igual que consultas con `svc-notificaciones` y el usuario `notificaciones`, más
`Smtp__Host=" "` y `Smtp__UseConsoleWhenNoHost=true`: el correo se escribe en su log (`[DEV EMAIL]`) y no sale.

**core-personanatural** (`4029`, gRPC `Servicio__GrpcPort=8087`)

- `ConnectionStrings__Servicio` (misma base, esquema `personanatural`), `Platform__ServiceClient__ClientSecret` (`svc-personanatural`),
  `Platform__ServiceClient__TokenEndpoint`, `Platform__Auth__JwksUri`, `Platform__Auth__Issuers__0=http://127.0.0.1:4022/`,
  `Platform__Messaging__ConnectionString` (usuario `personanatural`, con escritura en `flit.tramites`).
- `Pii__ClaveHmac` (32+ caracteres) y `Pii__Cifrado__Llaves__1` (base64 de 32 bytes), ficticias.
- `Captcha__Proveedor=deshabilitado` + `Captcha__PermitirDeshabilitado=true` (acepta cualquier token no vacío; nunca en la VPS).
- `Consultas__Remoto__Address=http://127.0.0.1:8084`, `Cotizaciones__CoreApiAddress=http://127.0.0.1:8082`,
  `Cotizaciones__NotificacionesAddress=http://127.0.0.1:8085`, `FileManager__BaseUrl=http://127.0.0.1:4999/`,
  `Abuso__ConfiarEnXForwardedFor=true`.
- Solo para repetir el recorrido muchas veces desde una IP local: `Abuso__LimiteIpPorMinuto=600`, `Abuso__LimiteCedulaPorHora=500`,
  `Abuso__TopeGlobalDiario=100000`, `Sesion__MaxIntentosFallidos=1000`. Con los de `appsettings.json` el segundo recorrido
  seguido recibe 429 (`rate_limited`, `reanudar_bloqueado`, `correo_rate_limit`): los topes antiabuso funcionan.

**gateway** (`4002`): `ReverseProxy__Clusters__core-api-cluster__Destinations__core-api-1__Address=http://127.0.0.1:4003/`, ídem
`core-consultas-cluster` (4026) y `core-personanatural-cluster` (4029). Los nombres de contenedor del `appsettings.json`
(`core-api:4003`…) no resuelven fuera de Docker.

**frontend-personanatural** (`4031`): `CORE_API_ORIGIN=http://127.0.0.1:4002`, `CAPTCHA_PROVIDER=dev`. Se arranca con
`node <ruta>/next dev --port 4031 -H 127.0.0.1` (la ruta del repo tiene un espacio: va entre comillas).

## Usuarios de RabbitMQ

| Usuario | Escribe | Para qué |
|---|---|---|
| `tramites` | `flit.tramites`, trabajos a `flit.notificaciones`, `flit.personanatural`, `flit.consultas`, `flit.plataforma` | core-api. El observador del bus lo reutiliza solo para LEER |
| `personanatural` | `flit.personanatural`, `flit.tramites` (trabajos) | el BFF: publica `radicacion_solicitada` en `flit.tramites` |
| `consultas`, `notificaciones` | su exchange | sus servicios |

El volumen local de RabbitMQ puede no traer el usuario `flit-admin` del compose (si se creó antes de definirlo): la consola
de administración y la API HTTP no están disponibles; se opera con `docker exec flit-local-rabbitmq-1 rabbitmqctl ...`. Por eso
`observar-bus.cs` (colas efímeras y exclusivas con el usuario `tramites`, solo lectura) deja en `bus-mensajes.log` cada mensaje de
`flit.tramites`, `flit.consultas`, `flit.notificaciones` y `flit.plataforma` (correos y números largos enmascarados).
Estado de las colas: `docker exec flit-local-rabbitmq-1 rabbitmqctl list_queues -p flit name messages consumers`.

## Datos de prueba (los siembra `sembrar.mjs`, ficticios)

Como `demo@flit.local` (SuperAdmin del seeder de Development) y por `/api/v1/admin/plataforma/persona-natural/...`:

1. Organismo habilitado para la empresa «Persona Natural»: **`5631000` (Sabaneta)**. Tiene que ser un OT *operable* (perfil y
   tenant OT activos, `OtOperabilityGate`); con un organismo sin OT (p. ej. `5858000`) la solicitud radica pero core-api la rechaza:
   `rechazada_creacion` con `ot_not_authorized_for_type`.
2. Tipo `TRASPASO_STANDARD` en la lista blanca.
3. Tarifas de prueba: `DERECHOS_TRANSITO = 100` y `SERVICIO_FLIT = 50` pesos (total 150), vigentes desde hoy.
4. Una pregunta frecuente «[PRUEBA E2E] …» publicada.

Todo queda solo en la base temporal `flit_pn_e2e`.

## Qué recorre `recorrido.mjs`

Por el proxy del front (`4031`, como lo haría el navegador, con cookie `pn_sesion` HttpOnly) y, donde conviene, por el gateway:
catálogos y contenido; cotización y envío de correo; consulta de vehículo y radicado inexistente (404 genérico); sesión con
consentimiento; definición; actores (con `persona-detalle`); identidad por parte; campos, comercial, requisitos, adjunto y
diagnóstico; radicar con `Idempotency-Key` (202); consumo en core-api (RPC `ObtenerSolicitudParaRadicar`) y creación del
BORRADOR en la empresa Persona Natural; evento de vuelta y espejo `creada` con `numeroReferencia`; colas sin mensajes muertos;
el trámite fuera del feed de Flito; re-radicar con la misma llave sin duplicar; consulta del radicado. Cada corrida usa placa,
correo y documentos de rechazo nuevos (los topes antiabuso y la regla de duplicidad de FLIT 2 son reales).

### Dos notas sobre la identidad con el proveedor mock

- El mock crea la validación en `enviado` y guarda solo el **hash** del token del enlace; en el portal nadie envía ese enlace.
  Para aprobarla el recorrido fija en la base temporal el hash de un token conocido y completa la captura por el endpoint público
  real (`POST /api/v1/public/biometric/{token}` con 3 imágenes; el scorer mock aprueba con 3 fotos no vacías).
- El camino mock NO publica `tramites.identity_validation.completed` (solo `IdentityValidationResultApplier`, el camino Kyverum, lo
  hace). El BFF refleja `aprobada` por consulta (pull) al leer `GET /identidad`. El evento solo se ejerce con Kyverum real.

## Problemas conocidos

- **core-consultas no arranca: «faltan secretos de proveedores: KYVERUM_RUNT_API_KEY»**: ver arriba (llave ficticia + dirección muerta).
- **core-api no arranca o tarda**: la primera vez migra toda la base (unos 3 minutos).
- **`RESOURCE_LOCKED … tramites.e2e-probe-…`**: quedó vivo un observador anterior; ciérralo por PID (`apagar.ps1`).
- **429 repetidos**: topes antiabuso del BFF; `levantar.ps1` los sube para local.
- **Diagnóstico `puedeContinuar=false` (`DUPLICATE_ACTIVE_PROCEDURE`) con una placa ya usada**: es la regla de FLIT 2; usa otra
  placa. Observación: el BFF no impide radicar con ese diagnóstico en rojo (lo debe frenar el front).
- **Almacenamiento de adjuntos**: sin `FileManager__BaseUrl` el BFF responde 503 `almacenamiento_no_disponible` al subir.
