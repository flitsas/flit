# Entorno local de la suite: levantar, simular fuentes y correr las pruebas

> Generado: 2026-10-28. Rama `feature/AB-12881-diagnostico-d0-d2`. Verificado contra el código de la rama y el script local
> `scripts/dev/levantar-diagnostico-local.sh`. Las anclas derivan rápido: reverifícalas antes de citarlas.
> Complementa [guia-runt-intempo-y-entorno-local.md](guia-runt-intempo-y-entorno-local.md),
> [revision-validacion-manual-2026-10-26.md](revision-validacion-manual-2026-10-26.md),
> [pruebas-validacion-diagnostico-local.md](pruebas-validacion-diagnostico-local.md), [../despliegue-y-puertos.md](../despliegue-y-puertos.md),
> [../suite/local.md](../suite/local.md) y [ADR-0075](../decisions/ADR-0075-diagnostico-consultas-por-rabbitmq.md).

> **Cambio frente a la guía de RUNT (§3.3 y §4.2):** desde ADR-0075 la simulación de las fuentes del Diagnóstico **vive en
> core-consultas** (`Consultas:Simulacion:*`). Las claves `Diagnostico:Simulacion:*` ya no existen en core-diagnostico y
> `Diagnostico:Consultas:Address` **no** enciende las fuentes: las consultas van por RabbitMQ y esa dirección solo sirve para la
> réplica de departamentos del impuesto. La tabla de §3.3 de esa guía describe el diseño anterior.

## Índice

1. [Requisitos](#1-requisitos)
2. [Mapa de servicios y puertos](#2-mapa-de-servicios-y-puertos)
3. [Levantar la suite](#3-levantar-la-suite)
4. [Modos de las fuentes](#4-modos-de-las-fuentes)
5. [Datos de prueba simulados](#5-datos-de-prueba-simulados)
6. [Verificación de punta a punta](#6-verificación-de-punta-a-punta)
7. [Pruebas automáticas](#7-pruebas-automáticas)
8. [Problemas frecuentes](#8-problemas-frecuentes)
9. [Parar todo](#9-parar-todo)

---

## 1. Requisitos

| Pieza | Detalle |
|---|---|
| Windows + **Git Bash** | El script y los comandos de esta guía son POSIX (`cygpath`, `netstat -ano`, `taskkill //PID`). |
| SDK **.NET 10** | Cada servicio fija su versión en `services/<servicio>/global.json`. |
| Node + **pnpm** | Monorepo pnpm (`pnpm-workspace.yaml`: `frontend`, `frontend-*`, `packages/*`). **No usar `npm`.** `pnpm install` en la raíz. |
| PostgreSQL 16 nativo | `127.0.0.1:5432`, base `flit_local` (la usa el script). Alternativa: el `postgres` de `docker-compose.yml` (base `flit_dev`, publicado en **5433**). Todos los servicios comparten la base; cada uno en su esquema. |
| Python 3 | El script lo usa para generar claves y aplicar permisos de RabbitMQ (`python -I`). |
| Erlang OTP 27 + RabbitMQ 4.2.9 **sin Docker** | Erlang en `C:\Program Files\Erlang OTP`; RabbitMQ en `~/.flit/rabbitmq/servidor/rabbitmq_server-4.2.9`, datos en `~/.flit/rabbitmq/datos`. Instalación descrita en la guía de RUNT §5.1. Con Docker existe `deploy/rabbitmq/docker-compose.local.yml`. |
| `infra/secrets/diagnostico-dp.pfx` + `.clave` | Certificado de Data Protection de core-diagnostico (local, fuera de git). |
| `appsettings.Development.json` de core-api, core-consultas y gateway | Locales y gitignored. En modo `real`, el de core-consultas trae las credenciales de los proveedores. Para crearlos, partir de cada `appsettings.Development.json.example`. |

### `~/.flit/local.env`

Fuera del repo; el script lo carga con `set -a`. Claves que espera (solo nombres; los valores son secretos aleatorios locales):

| Clave | Para qué |
|---|---|
| `SVC_TRAMITES`, `SVC_CONSULTAS`, `SVC_NOTIFICACIONES`, `SVC_ICT`, `SVC_DIAGNOSTICO`, `SVC_IDENTIDAD`, `SVC_COMPARENDOS` | Secretos de los clientes de servicio OIDC (`Suite__Oidc__ServiceClients__svc-*__Secret` en core-api/core-identity y `Platform__ServiceClient__ClientSecret` en cada servicio). |
| `MQ_TRAMITES`, `MQ_CONSULTAS`, `MQ_NOTIFICACIONES`, `MQ_DIAGNOSTICO`, `MQ_COMPARENDOS` | Claves de los usuarios de RabbitMQ de cada servicio. |
| `MQ_ADMIN` | Clave de `flit-admin` (consola y API de administración del broker). |
| `INTEMPO_API_BASE_URL` | Base del RUNT por Intempo. **Obligatoria solo en modo `real`.** |
| `SIMULAR_FUENTES` | Opcional. Fuentes que se simulan en modo `real` (§4.3). |

`SVC_COMPARENDOS` y `MQ_COMPARENDOS` los genera el script la primera vez si faltan. El resto debes crearlo tú, por ejemplo:

```bash
python -I -c 'import secrets; print(secrets.token_urlsafe(32))'   # un valor por clave
```

## 2. Mapa de servicios y puertos

Orden de arranque del script (cada paso depende del anterior):

| # | Servicio | HTTP | gRPC | Health | Log |
|---|---|---|---|---|---|
| 0 | RabbitMQ | 5672 (AMQP), consola http://127.0.0.1:15672 | — | `rabbitmqctl await_startup` | `rabbitmq.log` |
| 1 | core-api (login OIDC, tokens de servicio, migraciones de la base, gRPC de Trámites) | 4003 | 8082 | `/health` | `core-api.log` |
| 1b | core-identity (productos habilitados, empresas) | 4025 | 8083 | `/health/ready` | `core-identity.log` |
| 2 | core-consultas (proveedores y **simulación**) | 4026 | 8084 | `/health` | `core-consultas.log` |
| 2 | core-notificaciones (correo solo al log) | 4027 | 8085 | `/health` | `core-notificaciones.log` |
| 3 | core-diagnostico | 4029 | 8087 | `/health` | `core-diagnostico.log` |
| 4 | gateway (YARP) | 4002 | — | `/health` | `gateway.log` |
| 5 | frontend-hub (login de la suite) | **4022** | — | `/` | `hub.log` |
| 5 | frontend-diagnostico | 4024 | — | `/` | `frontend-diagnostico.log` |
| 6 | core-comparendos | 4028 | 8086 | `/health` | `core-comparendos.log` |
| 6 | frontend-comparendos | 4023 | — | `/` | `frontend-comparendos.log` |
| 7 | Trámites (`frontend`, `--webpack`) | 3000 | — | `/` | `tramites.log` |

Logs en `~/.flit/logs/<servicio>.log` (compilación en `<servicio>.build.log`). Todo escucha en `127.0.0.1`.

Constantes que comparten todos los servicios:

| Variable | Valor local |
|---|---|
| `Platform__Auth__JwksUri` | `http://127.0.0.1:4003/.well-known/jwks.json` |
| `Platform__Auth__Issuers__0` | `http://127.0.0.1:4022/` |
| `Platform__ServiceClient__TokenEndpoint` | `http://127.0.0.1:4003/connect/token` |
| `Platform__Messaging__ConnectionString` | `amqp://<servicio>:<MQ_SERVICIO>@127.0.0.1:5672/flit` |
| `ConnectionStrings__Servicio` (`__Core` en core-api/core-identity) | `Host=127.0.0.1;Port=5432;Database=flit_local;Username=<usuario>;Password=<clave>` |

## 3. Levantar la suite

### 3.1 Con el script

```bash
bash scripts/dev/levantar-diagnostico-local.sh simulado               # por defecto; primera vez: compila todo
bash scripts/dev/levantar-diagnostico-local.sh simulado --sin-build   # siguientes veces (usa bin/Debug/net10.0)
bash scripts/dev/levantar-diagnostico-local.sh real                   # proveedores reales: CONSUME SALDO
bash scripts/dev/levantar-diagnostico-local.sh parar
```

Qué hace cada bloque:

| Bloque | Qué hace |
|---|---|
| 0. RabbitMQ | Reescribe `~/.flit/rabbitmq/rabbitmq.conf` (listener 127.0.0.1, consola 15672, importa `deploy/rabbitmq/definitions.json`: vhost `flit`, exchanges y colas), habilita `rabbitmq_management` y arranca el nodo. Crea `flit-admin` (administrator) y los usuarios `tramites`→`notificaciones`, `consultas`, `notificaciones`, `diagnostico`→`consultas`, `comparendos`→`notificaciones` con los permisos mínimos de `deploy/rabbitmq/usuario-de-servicio.sh` (la flecha es el destino donde puede dejar trabajos). Los permisos van por la API HTTP de la consola, no por `rabbitmqctl`. |
| 1. core-api | Login OIDC de la suite (`Suite__Oidc__Enabled=true`, overrides de hosts 4022/3000/4024/4023, secretos `svc-*`, y `svc-diagnostico` con el scope `platform.consultas`), gRPC de Trámites en 8082, direcciones remotas de consultas (8084) y notificaciones (8085). Migra la base al arrancar. |
| 1b. core-identity | Misma configuración `Suite__Oidc__*` que core-api (si difieren, se pisan los clientes de login al arrancar). gRPC 8083. |
| 2. core-consultas | Modo de proveedores y simulación según `simulado`/`real` (§4). |
| 2. core-notificaciones | `Smtp__Host` vacío (correo solo al log), Renting apagado. |
| 3. core-diagnostico | `Diagnostico__Ambiente=LOCAL`, migra su esquema, archivos en la base (`Diagnostico__Archivos__GuardarEnBase=true`), Trámites 8082, Identidad 8083, gateway 4002 (manifiesto), certificado de Data Protection. |
| 4. gateway | Clusters de diagnostico (4029), comparendos (4028) y consultas (4026). |
| 5–7. Front | `pnpm exec next dev -H 127.0.0.1 -p <puerto>`. Copia `.env.example` → `.env.local` en frontend-diagnostico y frontend-comparendos si no existe. Trámites con `FLIT_SESSION_MODE=oidc`. |

Reglas del script:

- **No reinicia** lo que ya escucha en su puerto (`· <servicio> ya corre`). Para cambiar de modo o tomar un cambio de código:
  `parar` y volver a levantar (o matar solo ese puerto, §9).
- Espera cada health hasta 120 s (.NET) o 180 s (Next). Si un servicio no responde, mira su log y para la cadena.
- No lee ni modifica `appsettings.Development.json` de ningún servicio, salvo en modo `real`, donde toma de core-consultas la
  llave de Kyverum que reutiliza para SIMIT por NIT.

### 3.2 Equivalente manual (sin el script)

`scripts/dev/` está ignorado por git: si no tienes el script, arranca en este orden, cada servicio en su terminal, desde su
carpeta `src/<Proyecto>` y con `ASPNETCORE_ENVIRONMENT=Development`. Las variables de entorno mandan sobre el
`appsettings.Development.json`.

```bash
set -a; . ~/.flit/local.env; set +a
CS="Host=127.0.0.1;Port=5432;Database=flit_local;Username=<usuario>;Password=<clave>"
AUTH=(Platform__Auth__JwksUri=http://127.0.0.1:4003/.well-known/jwks.json Platform__Auth__Issuers__0=http://127.0.0.1:4022/
      Platform__ServiceClient__TokenEndpoint=http://127.0.0.1:4003/connect/token)
```

1. **RabbitMQ.** Arranca el nodo con `ERLANG_HOME`, `RABBITMQ_BASE` y `RABBITMQ_CONFIG_FILE` en rutas Windows (`cygpath -w`) y un
   `rabbitmq.conf` que importe `deploy/rabbitmq/definitions.json`. Crea los usuarios como en la VPS (la variante local usa
   `rabbitmqctl.bat`, ver §8):

   ```bash
   deploy/rabbitmq/usuario-de-servicio.sh diagnostico "$MQ_DIAGNOSTICO" consultas   # imprescindible (ADR-0075)
   deploy/rabbitmq/usuario-de-servicio.sh consultas   "$MQ_CONSULTAS"
   deploy/rabbitmq/usuario-de-servicio.sh tramites    "$MQ_TRAMITES" notificaciones
   ```

   Ese script asume `docker compose exec rabbitmq` (variable `COMPOSE`); sin Docker, aplica los mismos patrones por la API
   `PUT http://127.0.0.1:15672/api/permissions/flit/<usuario>` con `flit-admin`.
2. **core-api** (`services/core-api/src/Flit.Api`):

   ```bash
   env "ConnectionStrings__Core=$CS" ASPNETCORE_URLS=http://127.0.0.1:4003 Ict__GrpcPort=8082 \
     Suite__Oidc__Enabled=true Suite__Hosts__Overrides__plataforma=http://127.0.0.1:4022 \
     Suite__Hosts__Overrides__diagnostico=http://127.0.0.1:4024 Suite__Hosts__Overrides__tramites=http://127.0.0.1:3000 \
     "Suite__Oidc__ServiceClients__svc-consultas__Secret=$SVC_CONSULTAS" \
     "Suite__Oidc__ServiceClients__svc-diagnostico__Secret=$SVC_DIAGNOSTICO" \
     "Suite__Oidc__ServiceClients__svc-diagnostico__Scopes__4=platform.consultas" \
     "Platform__ServiceClient__ClientSecret=$SVC_TRAMITES" \
     "Platform__Messaging__ConnectionString=amqp://tramites:$MQ_TRAMITES@127.0.0.1:5672/flit" \
     Consultas__Remoto__Address=http://127.0.0.1:8084 Notificaciones__Remoto__Address=http://127.0.0.1:8085 \
     dotnet run --no-launch-profile
   ```

   (Repite un `Suite__Oidc__ServiceClients__svc-<x>__Secret` por cada `SVC_*`; el bloque completo es idéntico para core-identity.)
3. **core-identity** (`services/core-identity/src/Flit.Identity.Api`): `ConnectionStrings__Core`, `ASPNETCORE_URLS=http://127.0.0.1:4025`,
   `Identidad__GrpcPort=8083` y el mismo bloque `Suite__Oidc__*`.
4. **core-consultas** (`services/core-consultas/src/Flit.Consultas.Api`):

   ```bash
   env "ConnectionStrings__Servicio=$CS" Urls=http://127.0.0.1:4026 Servicio__GrpcPort=8084 "${AUTH[@]}" \
     "Platform__ServiceClient__ClientSecret=$SVC_CONSULTAS" \
     "Platform__Messaging__ConnectionString=amqp://consultas:$MQ_CONSULTAS@127.0.0.1:5672/flit" \
     Consultas__Ambiente=LOCAL Consultas__Simulacion__Habilitada=true \
     Consultations__VerifikVehicleMode=mock Consultations__VerifikConductorMode=mock Consultations__VerifikSimitMode=mock \
     Consultations__FasecoldaMode=mock Consultations__KyverumFinesMode=mock Consultations__VerifikRuesMode=mock \
     Consultations__VerifikRnmcMode=mock Consultations__IntempoMode=mock INTEMPO_API_BASE_URL= \
     dotnet run --no-launch-profile
   ```

   Para modo real cambia lo de la §4.2.
5. **core-notificaciones** (`.../Flit.Notificaciones.Api`): `Urls=http://127.0.0.1:4027`, `Servicio__GrpcPort=8085`, `AUTH`, su
   secreto y AMQP, `Smtp__Host=` y `Notifications__Renting__Enabled=false`.
6. **core-diagnostico** (`services/core-diagnostico/src/Flit.Diagnostico.Api`):

   ```bash
   env "ConnectionStrings__Servicio=$CS" Urls=http://127.0.0.1:4029 Servicio__GrpcPort=8087 Servicio__MigrateOnStartup=true \
     "${AUTH[@]}" "Platform__ServiceClient__ClientSecret=$SVC_DIAGNOSTICO" \
     "Platform__Messaging__ConnectionString=amqp://diagnostico:$MQ_DIAGNOSTICO@127.0.0.1:5672/flit" \
     "Diagnostico__DataProtection__CertificadoRuta=<ruta Windows de infra/secrets/diagnostico-dp.pfx>" \
     "Diagnostico__DataProtection__CertificadoClave=<contenido de infra/secrets/diagnostico-dp.clave>" \
     Diagnostico__Ambiente=LOCAL Diagnostico__Archivos__GuardarEnBase=true \
     Diagnostico__Consultas__Address=http://127.0.0.1:8084 \
     Diagnostico__Tramites__Address=http://127.0.0.1:8082 Diagnostico__Identidad__Address=http://127.0.0.1:8083 \
     Diagnostico__Plataforma__GatewayUrl=http://127.0.0.1:4002 \
     dotnet run --no-launch-profile
   ```

   Opcionales de `appsettings.Development.json.example`: `Diagnostico__Fuentes__ImpuestoDisponible=true` (además exige la réplica
   de departamentos, que se sincroniza por `Diagnostico__Consultas__Address`) y `Diagnostico__Identidad__SimularSuscripcionActiva=true`
   (solo local). `Diagnostico__Consultas__Bus__ConsumidorHabilitado` ya es `true` en `appsettings.json`.
7. **gateway** (`services/core-api/src/Flit.Gateway`): `ASPNETCORE_URLS=http://127.0.0.1:4002` y
   `ReverseProxy__Clusters__core-diagnostico-cluster__Destinations__core-diagnostico-1__Address=http://127.0.0.1:4029/` (ídem
   `core-consultas-cluster` → 4026 y `core-comparendos-cluster` → 4028).
8. **Front:**

   ```bash
   cd frontend-hub && PORT=4022 CORE_API_ORIGIN=http://127.0.0.1:4003 BRANDING_INTERNAL_API_URL=http://127.0.0.1:4003 \
     FLIT_HUB_URL=http://127.0.0.1:4022 TRAMITES_URL=http://127.0.0.1:3000 pnpm exec next dev -H 127.0.0.1 -p 4022
   cd frontend-diagnostico && cp -n .env.example .env.local && pnpm exec next dev -H 127.0.0.1 -p 4024
   cd frontend && NEXT_PUBLIC_API_BASE_URL= CORE_API_ORIGIN=http://127.0.0.1:4003 BRANDING_INTERNAL_API_URL=http://127.0.0.1:4003 \
     FLIT_SESSION_MODE=oidc FLIT_HUB_URL=http://127.0.0.1:4022 pnpm exec next dev --webpack -H 127.0.0.1 -p 3000
   ```

Comparendos (core-comparendos 4028/8086 y frontend-comparendos 4023) es opcional para el Diagnóstico; su configuración está en el
bloque 6 del script y en [../suite/local.md](../suite/local.md).

## 4. Modos de las fuentes

El Diagnóstico publica un trabajo por celda en `flit.consultas` (llave `consultas.diagnostico[-express].<fuente>`); core-consultas
lo consume y, por cada trabajo, decide en `Bus/EjecutorTrabajo.cs`: si `OpcionesSimulacion.Simula(fuente)` responde
`SimuladorConsultas`; si no, llama al proveedor real según sus `Consultations__*Mode`.

Nombres de fuente del bus (`FuentesBus`, iguales en `core-diagnostico/.../Consultas/Bus/TrabajoConsultaDiagnostico.cs` y
`core-consultas/.../Bus/TrabajoConsultaDiagnostico.cs`):

| Fuente del bus | Qué consulta | rpc de core-consultas |
|---|---|---|
| `runt` | Vehículo por placa o VIN (Intempo) | `RuntIntempoService/ConsultarPorPlaca` / `ConsultarPorVin` |
| `runt_propietario`, `runt_locatario` | Persona natural en RUNT (Kyverum) | `ConsultasService/ConsultarConductor` |
| `rues` | Persona con NIT: las celdas RUNT de persona con NIT van a esta fuente | `ConsultasService/ConsultarRues` |
| `simit_vehiculo`, `simit_propietario`, `simit_locatario` | Multas SIMIT | `ConsultasService/ConsultarMultas` |
| `avaluo` | Fasecolda, Mercado Libre, base gravable | `ConsultasService/ConsultarAvaluos` |
| `impuesto` | Impuesto vehicular departamental | `ConsultasService/ConsultarImpuestoVehicular` |

Configuración de core-consultas (`Simulacion/OpcionesSimulacion.cs`):

| Clave (variable de entorno) | Defecto | Efecto |
|---|---|---|
| `Consultas:Ambiente` (`Consultas__Ambiente`) | vacío | `LOCAL`, `DEV`, `QA` o `PDN`. Con simulación y un valor distinto de `LOCAL`/`DEV` (o vacío), **el servicio no arranca**. No mira `IHostEnvironment` (los tres ambientes corren como Development). |
| `Consultas:Simulacion:Habilitada` | `false` | `true`: **todas** las fuentes del bus simuladas. |
| `Consultas:Simulacion:Fuentes` | vacío | Lista separada por comas (se recortan espacios y se pasa a minúsculas). Simula solo esas, aunque `Habilitada=false`. |

La simulación solo afecta a los trabajos del bus: el gRPC en línea (Trámites, Comparendos) sigue con sus `Consultations__*Mode`.

### 4.1 Todo simulado (por defecto)

```bash
bash scripts/dev/levantar-diagnostico-local.sh simulado
```

core-consultas con `Consultas__Ambiente=LOCAL`, `Consultas__Simulacion__Habilitada=true`, todos los `Consultations__*Mode=mock` e
`INTEMPO_API_BASE_URL` vacía. No sale nada a internet.

### 4.2 Todo real

> **Consume saldo.** Cada consulta real a Intempo, Verifik, Kyverum o Fasecolda se cobra. Usa placas y documentos que tengas
> autorizados, no pegues las respuestas en ADO, logs ni commits (Ley 1581).

```bash
bash scripts/dev/levantar-diagnostico-local.sh real
```

Diferencias en core-consultas: `Consultas__Simulacion__Habilitada=false`, `Consultations__VerifikVehicleMode`, `VerifikConductorMode`,
`VerifikSimitMode`, `FasecoldaMode`, `KyverumFinesMode` y `VerifikRuesMode` en `real`, `KyverumFines__ApiKey=<llave>` e
`INTEMPO_API_BASE_URL=<url de local.env>` (el script falla si falta). Se quedan en `mock`: `VerifikRnmcMode` (el Diagnóstico no
usa RNMC) e `IntempoMode` (es el proveedor heredado de core-api, no el RUNT del Diagnóstico). RUES va **real**: es la consulta de
las personas con NIT (HU #14153). Credenciales: en `appsettings.Development.json` de core-consultas (gitignored).

### 4.3 Real con fuentes simuladas sueltas

En modo `real` el script pasa `Consultas__Simulacion__Fuentes=${SIMULAR_FUENTES}`. Defínela en `~/.flit/local.env`:

```bash
SIMULAR_FUENTES=avaluo                                   # el resto real; el avalúo lo responde el simulador
SIMULAR_FUENTES=avaluo,impuesto                          # varias, separadas por coma
SIMULAR_FUENTES=simit_vehiculo,simit_propietario,simit_locatario   # todo SIMIT: no hay comodín, van las tres
```

Después `parar` (o mata solo :4026, §9) y `real` otra vez: core-consultas lee la variable al arrancar. Los nombres deben ser
exactamente los de la tabla de §4; **un nombre mal escrito no da error**: esa fuente sigue yendo al proveedor real (y cobra).

Manual: `Consultas__Ambiente=LOCAL Consultas__Simulacion__Habilitada=false Consultas__Simulacion__Fuentes=avaluo` en core-consultas.

## 5. Datos de prueba simulados

`SimuladorConsultas` es determinista por el **último carácter** del identificador. Para RUNT, SIMIT, avalúo e impuesto se mira el
último carácter de la placa o el VIN; para RUNT persona y RUES, el **último dígito** del documento o NIT (sin dígitos cuenta como
`0`). Placas de prueba: `TST###` (las E2E usan `TST101`); documentos y NIT ficticios en el rango `99999…`.

| Fuente | Escenario por último carácter |
|---|---|
| RUNT (vehículo) | `1` persona natural · `2` NIT · `3` leasing, locatario C · `4` leasing, locatario N · `5` servicio público · `6` departamento no soportado · `7` `TIMEOUT` · `8` respuesta inválida · otro: persona natural. El vehículo lleva la placa o VIN consultado. |
| RUNT persona | `2` inscrita sin licencias · `3` licencia suspendida (B1) · `7` tiempo agotado · `8` respuesta inválida · otro: inscrita con B1 activa |
| SIMIT | `2` rojo, 2 comparendos · `3` amarillo, acuerdo de pago · `6` rojo, 60 comparendos · `7` tiempo agotado · `8` respuesta inválida · otro: verde |
| RUES | `6` cancelada · `7` tiempo agotado · `8` respuesta inválida · otro: activa |
| Avalúo | `2` solo Fasecolda (48 M) · `4` sin datos · `7` tiempo agotado · otro: tres fuentes (52 M) |
| Impuesto | `0` pagado · `7` tiempo agotado · otro: con deuda (400 000) |

Ejemplos: `TST101` todo verde con RUNT de persona natural; `TST102` RUNT con propietario NIT y SIMIT rojo; `TST107` tiempo agotado
en RUNT, SIMIT, avalúo e impuesto; `TST100` impuesto pagado.

Fixtures (recursos del ensamblado, `services/core-consultas/src/Flit.Consultas.Api/Simulacion/Fixtures/`):

- `intempo-*.json` — respuestas de RUNT por escenario (`FixturesIntempo.cs`).
- `proveedor-kyverum-conductor.json`, `proveedor-kyverum-conductor-sin-licencias.json`, `proveedor-verifik-rues.json` — respuesta
  **cruda** inventada del proveedor (HU #14153, `FixturesProveedor.cs`), con marcas `{{documento}}`, `{{nit}}`, `{{estado}}` y
  `{{renovacion}}` que se reemplazan con lo consultado. Solo viaja si el trabajo la pide (`incluir_respuesta_cruda`; el Diagnóstico
  la pide en `runt_propietario`, `runt_locatario` y `rues`) y es lo que pinta el bloque «respuesta del proveedor» del detalle.
  Datos ficticios: nombres «FICTICI…», documentos 99999…; nunca se construyen desde una muestra real.

## 6. Verificación de punta a punta

```bash
for u in 4003/health 4025/health/ready 4026/health 4027/health 4029/health 4002/health; do
  printf '%-22s %s\n' "$u" "$(curl -s -o /dev/null -w '%{http_code}' -m 3 http://127.0.0.1:$u)"; done
```

1. Abre **http://127.0.0.1:4022**, nunca `localhost`: las cookies de otras apps en `localhost` rompen las peticiones.
2. Primera vez en una base nueva: enciende el producto Diagnóstico para la compañía desde Trámites (3000) como SuperAdmin y entra
   con el administrador de la compañía (guía de RUNT §5.3; contraseñas de semilla en
   `services/core-api/src/Flit.Infrastructure/Security/DevelopmentAuthSeeder.cs`). Habilita las fuentes en el contrato de la compañía.
3. En el inicio de Diagnóstico: «Placa o VIN» → `TST101` → «Diagnosticar» → «Continuar» (y «Lanzar diagnóstico» si muestra el
   estimado). Termina en `/diagnosticos/<id>`.
4. Las celdas pasan de `PENDIENTE`/`EN_CURSO` a `COMPLETADO` en segundos. En el detalle de la persona debe verse la respuesta del
   proveedor (fixture de Kyverum o Verifik).
5. Si se quedan en `EN_CURSO`: consola http://127.0.0.1:15672 → colas `consultas.diagnostico` / `consultas.diagnostico-express`
   (¿hay mensajes sin consumidor?) y `~/.flit/logs/core-consultas.log` / `core-diagnostico.log` (§8).

## 7. Pruebas automáticas

> Ritmo recomendado: compila una vez y filtra por el símbolo que tocaste (`--filter`); la regresión amplia, al final.

### 7.1 .NET por servicio

```bash
# Base para las pruebas con Postgres (la misma en todos los servicios)
export ConnectionStrings__Core="Host=127.0.0.1;Port=5432;Database=<base migrada>;Username=<usuario>;Password=<clave>"

dotnet build services/core-diagnostico/Flit.Diagnostico.slnx
dotnet test  services/core-diagnostico/Flit.Diagnostico.slnx --no-build
dotnet test  services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter "FullyQualifiedName~PdfVehiculo"
dotnet test  services/core-consultas/Flit.Consultas.slnx
dotnet test  services/core-notificaciones/Flit.Notificaciones.slnx
dotnet test  services/core-identity/tests/Flit.Identity.Tests
dotnet test  services/core-comparendos/Flit.Comparendos.slnx
```

| Servicio | Qué tener en cuenta |
|---|---|
| core-diagnostico | Sin `ConnectionStrings__Core` las pruebas con base **se omiten en silencio** en local (apunta a un puerto cerrado); con `CI=true` y sin la variable **fallan** a propósito. Crea bases efímeras. `FLIT_PDF_MUESTRA=<ruta absoluta fuera del repo>` escribe el PDF de muestra de `PdfVehiculoTests` para revisión visual. |
| core-consultas | Las pruebas de bus/Kyverum usan `ConnectionStrings__Core` (por defecto `Host=127.0.0.1;Port=5432;Username=<usuario de Windows>`); sin Postgres se omiten en local. |
| Pruebas de broker (core-diagnostico, core-consultas, core-notificaciones, core-api Integration/Sdk) | `FLIT_TEST_RABBITMQ=amqp://<admin>:<clave>@127.0.0.1:5672/` (usuario administrador: crea y borra un vhost desechable por la API) y `FLIT_TEST_RABBITMQ_ADMIN` (por defecto `http://<host>:15672/`). En local, `flit-admin`. Sin broker se omiten en local y fallan en CI. `FLIT_TEST_RABBITMQ_CONSULTAS=amqp://consultas:<clave>@<host>:<puerto>/flit` activa `PermisosPorServicioTests` (Flit.Platform.Sdk.Tests). |
| core-api | 13 proyectos en `services/core-api/tests/`. `Flit.Admin.Tests` y `Flit.Integration.Tests` necesitan la **base migrada** de `ConnectionStrings__Core` (Integration también acepta `FLIT_IT_PG_ADMIN`). Migrar: arrancar core-api una vez (`Database:AutoMigrate`, true por defecto) o `dotnet ef database update --project src/Flit.Infrastructure --startup-project src/Flit.Api`. Integration se niega a usar `flit_local`, `flit_dev` y `postgres` como base propia (crea las suyas). `FLIT_UPDATE_DI_SNAPSHOT=1` regenera el snapshot de DI de `CoreApiServiceRegistrationSnapshotTests` (revisar el diff en el PR). |

```bash
cd services/core-api
dotnet build Flit.slnx
dotnet test tests/Flit.Tramites.Application.Tests --no-build --filter "FullyQualifiedName~<Clase>"
dotnet test tests/Flit.Admin.Tests --no-build          # con ConnectionStrings__Core a una base migrada
```

**No compiles mientras el servicio corre** desde `bin/Debug`: el build falla por archivos bloqueados (§8).

### 7.2 Front

```bash
pnpm --filter @flit/frontend-diagnostico test                     # vitest run
pnpm --filter @flit/frontend-diagnostico exec vitest run __tests__/<archivo>
pnpm --filter @flit/frontend-diagnostico typecheck                # tsc --noEmit
pnpm --filter @flit/frontend-diagnostico lint                     # eslint, scoped a la app
pnpm --filter @flit/frontend-diagnostico build                    # next build
```

Igual con `@flit/frontend-hub`, `@flit/frontend-comparendos` y `@flit/frontend` (Trámites).

**E2E (Playwright)** en `frontend-diagnostico/e2e/*.spec.ts`, excluidos de `tsc` y Vitest. No hay `playwright.config`; necesitan la
suite levantada en modo simulado y usuarios de prueba por variables de entorno:

```bash
DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=<usuario> E2E_CLAVE=<clave> \
  pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/listado.spec.ts
```

Otras variables según el spec: `E2E_ADMIN`/`E2E_CLAVE_ADMIN`, `E2E_BACKOFFICE`/`E2E_CLAVE_BACKOFFICE`,
`E2E_USUARIO_SUSPENDIDO`/`E2E_CLAVE_SUSPENDIDO`, `E2E_EMPRESA_CLIENTE_NOMBRE`, `E2E_PLACA_EXPRESS` (defecto `TST101`),
`E2E_PLACA_IMPUESTO`. Ver §8 sobre `@playwright/test`.

### 7.3 Qué corre el CI

| Workflow | Qué hace |
|---|---|
| `.github/workflows/servicios-plataforma.yml` | Descubre los servicios `services/core-*` con `ServicioSettings.cs` (`.github/scripts/servicios-plataforma.sh listar`), exige su `.proto` en `contracts/proto`, y por servicio: build Release + `dotnet test <slnx> --no-build`, con Postgres 16 (`flit_dev`) y RabbitMQ 4.1 con management; exporta `ConnectionStrings__Core`, `FLIT_TEST_RABBITMQ` y `FLIT_TEST_RABBITMQ_ADMIN`. |
| `.github/workflows/core-api.yml` | Build Release, `dotnet ef database update` y pruebas en carriles (`.github/scripts/ci-core-api-pruebas.sh`): `admin`, `integration` en 4 shards y `resto` (los demás proyectos en paralelo). Postgres + RabbitMQ 4.1. |
| `.github/workflows/ci.yml` | `pnpm install --frozen-lockfile` y, en todo el workspace, `format:check`, `lint`, `typecheck` y `build` (`pnpm -r --if-present`). **No corre Vitest ni E2E.** |
| `contracts.yml`, `security-scan.yml`, `python-ml.yml`, `despliegue-config.yml`, `cd.yml` | Contratos, seguridad, ML, config de despliegue y CD. |

## 8. Problemas frecuentes

| Síntoma | Causa y arreglo |
|---|---|
| `dotnet build` falla por archivos en uso (`MSB3027`, `bin/Debug/... being used by another process`) | El servicio corre desde `bin/Debug/net10.0`. Para ese puerto (§9), compila y vuelve a levantar con `--sin-build`. |
| Lote o Express con celdas pegadas en `EN_CURSO` y en el log de diagnóstico `ACCESS_REFUSED` / `NOT_ALLOWED` | Al usuario `diagnostico` le falta escritura en `flit.consultas` (destino `consultas`), o core-consultas está caído. Reaplica los permisos (bloque 0 del script o `usuario-de-servicio.sh diagnostico <clave> consultas`) y comprueba :4026. Sin cola declarada (`definitions.json`) el broker descarta el trabajo. |
| Todo va al proveedor real (o «no conectado») aunque «encendiste la simulación» | La pusiste en core-diagnostico. Desde ADR-0075 es `Consultas__Simulacion__*` **en core-consultas**, con `Consultas__Ambiente=LOCAL`. |
| core-consultas no arranca: «… solo se admite en LOCAL o DEV» | Simulación (total o por fuentes) sin `Consultas__Ambiente` o con QA/PDN. |
| `SIMULAR_FUENTES` sin efecto | Solo la lee el modo `real`; core-consultas ya estaba arriba (no se reinicia); o el nombre no coincide con la tabla de §4. |
| El impuesto no aparece entre las fuentes (422 `FUENTE_NO_DISPONIBLE`) | Exige `Diagnostico__Fuentes__ImpuestoDisponible=true` **y** réplica de departamentos con al menos uno, que se sincroniza por `Diagnostico__Consultas__Address`. |
| Proveedor de vehículo real sin pedirlo (Trámites) | En `Flit.Modules.Consultas`, `Consultations:VerifikVehicleMode` tiene defecto **`real`** (el resto, `mock`). Fíjalo explícito. |
| `Flit.Admin.Tests` / `Flit.Integration.Tests` fallan en masa por tablas o columnas | La base de `ConnectionStrings__Core` no está migrada a la rama. Migra (§7.1). |
| Pruebas de core-diagnostico «pasan» demasiado rápido | Se omitieron por falta de `ConnectionStrings__Core`. Revisa el conteo de omitidas. |
| `rabbitmqctl.bat ... -p /` o un vhost con `/` falla en Git Bash | MSYS convierte el argumento que empieza por `/` en una ruta de Windows. Usa `MSYS_NO_PATHCONV=1`, `//`, o la API HTTP de la consola. Por esto (y porque `cmd.exe` parte las expresiones en cada `\|`) el script aplica los permisos por la API. |
| `CoreApiServiceRegistrationSnapshotTests` falla en local y pasa en CI | El snapshot de DI depende del runtime y de la configuración OIDC local (`Suite__Oidc__*`, `appsettings.Development.json`). Compara con CI antes de regenerarlo; **no** lo regeneres con `FLIT_UPDATE_DI_SNAPSHOT=1` desde una configuración local. |
| «Manifiesto de Diagnóstico sin registrar» | Falta `Diagnostico__Plataforma__GatewayUrl`; el servicio reintenta solo. |
| El login rebota o las peticiones fallan con cookies | Entraste por `localhost`. Usa `127.0.0.1`. |
| core-api y core-identity se pisan los clientes de login | Arrancaron con `Suite__Oidc__*` distintos. Deben recibir exactamente lo mismo. |
| Equipo lento | La suite completa pesa. Para Trámites/Comparendos si no los usas (§9). |

## 9. Parar todo

```bash
bash scripts/dev/levantar-diagnostico-local.sh parar
```

Mata por puerto (3000, 4023, 4028, 4024, 4022, 4029, 4002, 4027, 4026, 4025, 4003) con `taskkill //T //F`, detiene RabbitMQ con
`rabbitmqctl stop` (con el mismo `ERLANG_HOME`/`RABBITMQ_BASE`/`RABBITMQ_CONFIG_FILE` del arranque) y mata `epmd` (:4369). No
borra datos de la base ni del broker.

Un solo servicio (p. ej. para recompilarlo o cambiar `SIMULAR_FUENTES`):

```bash
pid=$(netstat -ano | grep LISTENING | grep ':4026 ' | awk '{print $NF}' | head -1); [ -n "$pid" ] && taskkill //PID "$pid" //T //F
bash scripts/dev/levantar-diagnostico-local.sh real --sin-build   # solo arranca lo que no está arriba
```
