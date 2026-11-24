# Variables de entorno para desplegar (DEV · QA · PDN)

> Generado: 2026-11-24 · fuente: `docker-compose.prod.yml` @ `ded2112ef`

Un solo lugar con todas las variables que usa el despliegue. Primero el checklist, después una tabla por servicio. El
detalle de cada producto sigue en su handoff (columna «Doc»).

**Regenerar o verificar.** Desde la raíz del repo:

```bash
grep -o '\${[A-Z0-9_]*' docker-compose.prod.yml | sort -u | wc -l     # nombres únicos (hoy 324)
python scripts/dev/variables-entorno.py                               # por servicio: default, obligatoria, .example, clave
python scripts/dev/variables-entorno.py --desfases                    # solo los desfases compose ↔ .example
```

El script es de solo lectura. Lee el compose y los `*.example`; nunca un `.env` real. Si el conteo o los desfases no
coinciden con este documento, el compose cambió: actualizar este archivo en el mismo PR.

---

## 0. Antes de nada: por dónde se despliega

| Camino | Ambientes | Dónde vive la configuración | Nombre que se usa |
|---|---|---|---|
| **GitOps (Argo CD)**, el normal | DEV, QA y PDN | `flit-gitops/services/flit2-<dev\|qa\|pdn>/configmaps.yaml` (no secretas) y `sealed-secrets.yaml` (secretas) | La **clave en el contenedor** (columna 2 de las tablas, p. ej. `Consultas__Identidad__Address`) |
| **Compose manual** en un VPS | Operación manual o de respaldo | `.env` junto a `docker-compose.prod.yml` | La **variable** del compose (columna 1, p. ej. `CONSULTAS_IDENTIDAD_ADDRESS`) |

- Ningún ambiente se despliega ya por SSH. El CD solo escribe digests en `flit-gitops` (`.github/workflows/cd.yml:1421-1440`).
- Una variable nueva en el compose **no llega sola** a GitOps. El CD avisa con un warning «Revisar config de <AMB>» cuando
  cambia `docker-compose.prod.yml`. Hay que replicarla a mano en `configmaps.yaml` o `sealed-secrets.yaml` de **cada**
  ambiente.
- Rollback en GitOps: revertir el commit en `flit-gitops` (skill `flit-rollback-procedure`).
- Cuando la clave del contenedor dice «mismo nombre», el contenedor recibe la variable tal cual (variables «crudas» que el
  código lee con `Environment.GetEnvironmentVariable`, como `VERIFIK_*`, `RENTING_API_*` o `IMPUESTO_*`).

---

## 1. Checklist rápido de despliegue

### 1.1 Archivos que debe tener el servidor (compose manual)

`docker-compose.prod.yml` no tiene `env_file`: Compose solo interpola desde el `.env` del directorio del compose (o los
`--env-file` que se pasen). Los `.example` de proveedor no se leen solos: su contenido se copia a ese `.env`.

| Archivo en el servidor | Sale de | Qué aporta | ¿Siempre? |
|---|---|---|---|
| `.env` (junto al compose) | `.env.prod.example` | Base, CORS, puertos, tags, broker, clientes de servicio, productos, migrador | Sí |
| bloque en el mismo `.env` | `.env.verifik.example` | `VERIFIK_*` (RUNT) | Sí (core-consultas) |
| bloque en el mismo `.env` | `.env.kyverum.example` | `KYVERUM_*` (Verify, biometría) | Si `BIOMETRICS_PROVIDER=kyverum` |
| bloque en el mismo `.env` | `.env.kyverum-runt.example` | `KYVERUM_RUNT_*` (improntas) | Si se usa Kyverum RUNT |
| bloque en el mismo `.env` | `.env.fasecolda.example` | `FASECOLDA_*` (avalúo) | Si `FASECOLDA_MODE=real` |
| bloque en el mismo `.env` | `.env.intempo.example` | `INTEMPO_API_*` (RUNT por Intempo) | Si se conecta Intempo |
| bloque en el mismo `.env` | `.env.anthropic.example` | `OCR_PROVIDER`, `ANTHROPIC_*`, `OPENAI_*`, `KIMI_*`, `DEEPSEEK_*` | Si hay OCR o Dr. FLIT real |
| bloque en el mismo `.env` | `.env.renting.example` | `RENTING_API_*` (correo y contratos Renting) | Si `RENTING_API_ENABLED=true` |
| bloque en el mismo `.env` | `.env.impuesto.example` | `IMPUESTO_*`, `CONSULTAS_IDENTIDAD_ADDRESS` | Solo con aval (ver §4) |
| `.pfx` de Renting | fuera del repo | Certificado mTLS en `RENTING_API_PFX_CERTIFICATE_PATH` | **Sí**: core-notificaciones lo monta siempre |
| `.pfx` de Data Protection de Diagnóstico | fuera del repo | Secreto de archivo en `DIAGNOSTICO_DP_CERT_FILE` | Con el perfil `diagnostico` |

No van al servidor: `.env.example` (credenciales de herramientas: ADO, GitHub, Sonar) y los `frontend*/.env.example`
(desarrollo local; las `NEXT_PUBLIC_*` se hornean en el build del CD).

### 1.2 Obligatorias: si faltan, `docker compose` aborta

Compose valida **todo** el archivo, incluidos los perfiles apagados. Estas seis faltan ⇒ no arranca ningún contenedor.

| Variable | Valor esperado |
|---|---|
| `CORS_ORIGIN` | URL pública del front del ambiente, sin barra final |
| `FLIT_SUITE_ENV` | `dev`, `qa` o **vacía** en PDN (`FLIT_SUITE_ENV=`): tiene que estar definida |
| `RABBITMQ_URL_TRAMITES` | `amqp://tramites:<clave>@rabbitmq:5672/flit` |
| `RABBITMQ_URL_PLATAFORMA` | `amqp://plataforma:<clave>@rabbitmq:5672/flit` |
| `RABBITMQ_URL_DIAGNOSTICO` | `amqp://diagnostico:<clave>@rabbitmq:5672/flit`, aunque el perfil `diagnostico` esté apagado |
| `SVC_TRAMITES_CLIENT_SECRET` | Secreto largo del cliente `svc-tramites` |

Comprobar sin levantar nada: `docker compose -f docker-compose.prod.yml config > /dev/null`.

### 1.3 Obligatorias para que cada servicio arranque

| Variable | Servicio | Efecto si falta |
|---|---|---|
| `CONNECTION_STRING_CORE` | core-api, core-identity, core-ict, migrador | No conectan a la base |
| `CONNECTION_STRING_CONSULTAS`, `SVC_CONSULTAS_CLIENT_SECRET` | core-consultas | No arranca. Es obligatorio desde el corte HU #13348 |
| `CONNECTION_STRING_NOTIFICACIONES`, `SVC_NOTIFICACIONES_CLIENT_SECRET` | core-notificaciones | No arranca |
| `RENTING_API_PFX_CERTIFICATE_PATH` (archivo) | core-notificaciones | Si la ruta no existe, Docker monta un directorio vacío; el canal falla al primer envío |
| `SVC_ICT_CLIENT_SECRET` | core-ict | No arranca |
| `RABBITMQ_ADMIN_PASSWORD` | rabbitmq | Broker sin clave de administrador |
| `FLIT_SESSION_SECRET` | frontend-hub (perfil `suite`) | Sin sesión del hub |
| `CONNECTION_STRING_DIAGNOSTICO`, `SVC_DIAGNOSTICO_CLIENT_SECRET`, `DIAGNOSTICO_DP_CERT_PASSWORD` + `.pfx`, `DIAGNOSTICO_IDENTIDAD_ADDRESS`, `DIAGNOSTICO_SESSION_SECRET` | perfil `diagnostico` | No arranca, o 403 para todas las empresas sin Identidad |
| `CONNECTION_STRING_COMPARENDOS`, `SVC_COMPARENDOS_CLIENT_SECRET`, `RABBITMQ_URL_COMPARENDOS`, `FILE_MANAGER_BASE_URL`, `COMPARENDOS_IDENTIDAD_ADDRESS`, `COMPARENDOS_SESSION_SECRET` | perfil `comparendos` | No arranca o ninguna empresa habilitada |
| `CONNECTION_STRING_PERSONANATURAL`, `SVC_PERSONANATURAL_CLIENT_SECRET`, `RABBITMQ_URL_PERSONANATURAL`, `PERSONANATURAL_PII_HMAC_KEY`, `PERSONANATURAL_PII_AES_KEY`, `PERSONANATURAL_CAPTCHA_SECRET` | perfil `personanatural` | No arranca; el log dice cuál falta |
| `FLITMIG_V1_SOURCE`, `FLITMIG_SOURCE_FM_BASE_URL`, `FILE_MANAGER_BASE_URL` | migrador, migracion-api | Solo al migrar. Las tres de origen apuntan al **mismo** ambiente de V1 |

Arrancan **degradados** (no fallan, pero algo queda apagado):

| Variable vacía | Efecto |
|---|---|
| `CORE_IDENTITY_GRPC_PORT` | Sin gRPC de Identidad: Diagnóstico y Comparendos sin empresas habilitadas; impuesto sin credencial |
| `FLIT_INTERNAL_API_KEY` | Sin dominios de Marca Blanca (fail-closed). Mismo valor en core-api, gateway y fronts |
| `ICT_SERVICE_TOKEN_SECRET` | Orquestación y callback ICT fail-closed |
| `FILE_MANAGER_AUTH_TOKEN` | Contabo (PDN) responde 401. Mismo valor en core-api, core-ict y productos |
| `SMTP_HOST` | Correo al log en vez de enviarse |
| `AI_ENTREGA_DIAGNOSTICO_CLAVE_PUBLICA` / `DIAGNOSTICO_IA_CLAVE_PRIVADA` | Dictamen de Diagnóstico con plantilla «sin análisis» |
| `DIAGNOSTICO_CONSULTAS_ADDRESS` | Todas las fuentes del diagnóstico «no conectadas» |

### 1.4 Perfiles (`COMPOSE_PROFILES`)

Sin perfil arrancan: core-api, core-consultas, core-notificaciones, core-ict, rabbitmq, migracion-api, gateway, python-ml y
frontend. El resto se enciende agregando su perfil a `COMPOSE_PROFILES` en el `.env`.

| Perfil | Servicios | Doc |
|---|---|---|
| `identity` | core-identity | [handoff-vps-identidad.md](../suite/handoff-vps-identidad.md) |
| `suite` | frontend-hub | [handoff-vps-suite.md](../suite/handoff-vps-suite.md) |
| `diagnostico` | core-diagnostico, frontend-diagnostico | [handoff-vps-diagnostico.md](../suite/handoff-vps-diagnostico.md) |
| `comparendos` | core-comparendos, frontend-comparendos | [handoff-vps-comparendos.md](../suite/handoff-vps-comparendos.md) |
| `personanatural` | core-personanatural, frontend-personanatural | [handoff-vps-personanatural.md](../suite/handoff-vps-personanatural.md) |
| `observabilidad` | observabilidad (OTel + Grafana) | este documento, §3 |
| `migracion` | migrador (proceso de un uso) | `.env.prod.example`, bloque `FLITMIG_` |

Variables nativas de Compose (no aparecen como `${...}`): `COMPOSE_PROFILES` y `COMPOSE_PROJECT_NAME` (nombre único por
stack en el VPS; el archivo fija `name: flitdev`).

### 1.5 Valores que cambian por ambiente

| Variable | DEV | QA | PDN |
|---|---|---|---|
| `FLIT_SUITE_ENV` | `dev` | `qa` | vacía |
| `CORS_ORIGIN` | `https://dev.flitsas.online` | `https://qa.flitsas.online` | ⚠ ver §5.3 |
| `FLIT_TRAMITES_URL` | `https://dev.tramites.flitsas.online` | `https://qa.tramites.flitsas.online` | `https://tramites.flitsas.com` |
| `FLIT_INTERNAL_SUBNET` | `10.114.40.0/24` | `10.114.50.0/24` | `10.114.60.0/24` |
| `CORS_ORIGIN_ALT` | vacía | vacía | `https://app.flitsas.com` |
| Tags (`*_TAG`) | `dev` | `qa` | `sha-<commit>` |
| `TRAMITE_VALIDATION_DUPLICATE_MODE` | `warn` | `block` | `block` |
| `TRAMITE_VALIDATION_VEHICLE_STATE_MODE` | `warn` | `warn` | `block` |
| `FLIT_SEED_DEMO_USERS` | `true` | `false` | `false` |
| `FLIT_SWAGGER_ENABLED` | `true` | default `true` | `false` |
| `DR_FLIT_DEPLOY_ENVIRONMENT` | `DEV` | `QA` | `PDN` |
| `RENTING_API_CONTRATOS_REAL_CONTACT_ENABLED` | `false` | `false` | `true` (solo PDN) |
| `DIAGNOSTICO_REDACCION_HABILITADO` | `true` | `true` | `false` hasta decisión del PO |
| Puertos | 40xx | 50xx | 60xx (tabla de §2) |

Origen: `cd.yml:83-140` (dominios, puertos, subred, tags) y los comentarios del compose (matriz de validaciones, Renting,
redacción IA).

### 1.6 Secretos: dónde y cómo

- Nunca en el repo, en un Dockerfile ni en un `configmaps.yaml`. En GitOps van a `sealed-secrets.yaml`.
- Uno **distinto por ambiente**: `SVC_*_CLIENT_SECRET`, `*_SESSION_SECRET`, claves del broker, llaves PII.
- Deben coincidir entre servicios: `FLIT_INTERNAL_API_KEY` (core-api, gateway, fronts), `FILE_MANAGER_BASE_URL` y
  `FILE_MANAGER_AUTH_TOKEN` (core-api, core-ict, migrador, productos), `ICT_SERVICE_TOKEN_SECRET` (core-api, core-ict),
  `FLITMIG_MIGRACION_API_KEY` (migracion-api y front).
- Respaldo obligatorio fuera del servidor: `.pfx` de Data Protection de Diagnóstico y `PERSONANATURAL_PII_*`. Perderlos
  deja ilegibles los datos cifrados.

---

## 2. Variables transversales

Se repiten en casi todos los servicios. Las tablas por servicio de §3 no las repiten: cada una dice cuáles usa.

| Familia | Variables | Default | Uso |
|---|---|---|---|
| Ambiente .NET | `FLIT_DOTNET_ENVIRONMENT` | `Development` | `ASPNETCORE_ENVIRONMENT` de todos los .NET. Los tres ambientes corren como Development |
| Telemetría | `OTEL_EXPORTER_OTLP_ENDPOINT` | vacío | Colector OTLP. Con el perfil `observabilidad`: `http://observabilidad:4317` |
| Tag de imagen | `*_TAG` (17) | `latest` | Imagen de GHCR que se despliega |
| Límites | `*_MEM_LIMIT`, `*_CPUS`, `*_OOM_SCORE_ADJ` | `0` (sin límite) | Los fija el líder tras medir ([identidad, paso 3](../suite/handoff-vps-identidad.md)) |
| Puertos | `*_PORT` | DEV | Tabla siguiente |

### 2.1 Puertos

Expuesto == interno, publicado solo en `127.0.0.1`. Los gRPC no se publican y son iguales en los tres ambientes.

| Variable | Servicio | DEV | QA | PDN |
|---|---|---|---|---|
| `FRONTEND_PORT` | frontend | 4001 | 5001 | 6001 |
| `GATEWAY_PORT` | gateway (lo usan todos para token y JWKS) | 4002 | 5002 | 6002 |
| `CORE_API_PORT` | core-api | 4003 | 5003 | 6003 |
| `PYTHON_ML_PORT` | python-ml | 4012 | 5012 | 6012 |
| `CORE_ICT_PORT` | core-ict | 4020 | 5020 | 6020 |
| `HUB_PORT` | frontend-hub | 4022 | 5022 | 6022 |
| `COMPARENDOS_PORT` | frontend-comparendos | 4023 | 5023 | 6023 |
| `DIAGNOSTICO_PORT` | frontend-diagnostico | 4024 | 5024 | 6024 |
| `CORE_IDENTITY_PORT` | core-identity | 4025 | 5025 | 6025 |
| `CORE_CONSULTAS_PORT` | core-consultas (no publicado) | 4026 | 4026 | 4026 |
| `CORE_NOTIFICACIONES_PORT` | core-notificaciones (no publicado) | 4027 | 4027 | 4027 |
| `CORE_COMPARENDOS_PORT` | core-comparendos (no publicado) | 4028 | 5028 | 6028 |
| `CORE_PERSONANATURAL_PORT` | core-personanatural (no publicado) | 4029 | 5029 | 6029 |
| `MIGRACION_API_PORT` | migracion-api (red interna; mismo valor en el gateway) | 4030 | 5030 | 6030 |
| `PERSONANATURAL_PORT` | frontend-personanatural | 4031 | 5031 | 6031 (propuesto) |
| `CORE_DIAGNOSTICO_PORT` | core-diagnostico (no publicado) | 4032 | 5032 | 6032 |
| `RABBITMQ_MGMT_PORT` | consola de RabbitMQ | 4672 | 4672 | 4672 |
| `OBSERVABILIDAD_GRAFANA_PORT` | Grafana | 4090 | 4090 | 4090 |
| `CORE_API_GRPC_PORT` | gRPC core-api | 8082 | 8082 | 8082 |
| `CORE_IDENTITY_GRPC_PORT` | gRPC Identidad (vacío = apagado) | 8083 | 8083 | 8083 |
| `CORE_CONSULTAS_GRPC_PORT` | gRPC core-consultas | 8084 | 8084 | 8084 |
| `CORE_NOTIFICACIONES_GRPC_PORT` | gRPC core-notificaciones | 8085 | 8085 | 8085 |
| `CORE_COMPARENDOS_GRPC_PORT` | gRPC core-comparendos | 8086 | 8086 | 8086 |
| `CORE_PERSONANATURAL_GRPC_PORT` | gRPC core-personanatural | 8087 | 8087 | 8087 |
| `CORE_DIAGNOSTICO_GRPC_PORT` | gRPC core-diagnostico | 8088 | 8088 | 8088 |

Fuentes: `cd.yml:83-140`, cabecera del compose y los handoffs. Los gRPC tienen su fila en la tabla del servicio.

### 2.2 Inventario completo de transversales

| Variable | Default | `.example` | Servicios |
|---|---|---|---|
| `COMPARENDOS_PORT` | `4023` | `.env.prod.example` | frontend-comparendos |
| `CORE_API_CPUS` | `0` | `.env.prod.example` | core-api |
| `CORE_API_MEM_LIMIT` | `0` | `.env.prod.example` | core-api |
| `CORE_API_OOM_SCORE_ADJ` | `0` | `.env.prod.example` | core-api |
| `CORE_API_PORT` | `4003` | `.env.prod.example` | core-api, core-ict, gateway, core-api (ancla) |
| `CORE_API_TAG` | `latest` | `.env.prod.example` | core-api, gateway, migracion-api, migrador |
| `CORE_COMPARENDOS_CPUS` | `0` | ⚠ ninguno | core-comparendos |
| `CORE_COMPARENDOS_MEM_LIMIT` | `0` | ⚠ ninguno | core-comparendos |
| `CORE_COMPARENDOS_PORT` | `4028` | `.env.prod.example` | core-comparendos, gateway |
| `CORE_COMPARENDOS_TAG` | `latest` | ⚠ ninguno | core-comparendos |
| `CORE_CONSULTAS_CPUS` | `0` | `.env.prod.example` | core-consultas |
| `CORE_CONSULTAS_MEM_LIMIT` | `0` | `.env.prod.example` | core-consultas |
| `CORE_CONSULTAS_PORT` | `4026` | `.env.prod.example` | core-consultas, gateway |
| `CORE_CONSULTAS_TAG` | `latest` | `.env.prod.example` | core-consultas |
| `CORE_DIAGNOSTICO_CPUS` | `0` | `.env.prod.example` | core-diagnostico |
| `CORE_DIAGNOSTICO_MEM_LIMIT` | `0` | `.env.prod.example` | core-diagnostico |
| `CORE_DIAGNOSTICO_PORT` | `4032` | `.env.prod.example` | core-diagnostico, gateway |
| `CORE_DIAGNOSTICO_TAG` | `latest` | ⚠ ninguno | core-diagnostico |
| `CORE_ICT_CPUS` | `0` | `.env.prod.example` | core-ict |
| `CORE_ICT_MEM_LIMIT` | `0` | `.env.prod.example` | core-ict |
| `CORE_ICT_PORT` | `4020` | `.env.prod.example` | core-ict, gateway, core-api (ancla) |
| `CORE_ICT_TAG` | `latest` | `.env.prod.example` | core-ict |
| `CORE_IDENTITY_CPUS` | `0` | `.env.prod.example` | core-identity |
| `CORE_IDENTITY_MEM_LIMIT` | `0` | `.env.prod.example` | core-identity |
| `CORE_IDENTITY_OOM_SCORE_ADJ` | `0` | `.env.prod.example` | core-identity |
| `CORE_IDENTITY_PORT` | `4025` | `.env.prod.example` | core-identity, gateway |
| `CORE_IDENTITY_TAG` | `latest` | `.env.prod.example` | core-identity |
| `CORE_NOTIFICACIONES_CPUS` | `0` | `.env.prod.example` | core-notificaciones |
| `CORE_NOTIFICACIONES_MEM_LIMIT` | `0` | `.env.prod.example` | core-notificaciones |
| `CORE_NOTIFICACIONES_PORT` | `4027` | `.env.prod.example` | core-notificaciones |
| `CORE_NOTIFICACIONES_TAG` | `latest` | ⚠ ninguno | core-notificaciones |
| `CORE_PERSONANATURAL_CPUS` | `0` | `.env.prod.example` | core-personanatural |
| `CORE_PERSONANATURAL_MEM_LIMIT` | `0` | `.env.prod.example` | core-personanatural |
| `CORE_PERSONANATURAL_PORT` | `4029` | `.env.prod.example` | core-personanatural, gateway |
| `CORE_PERSONANATURAL_TAG` | `latest` | `.env.prod.example` | core-personanatural |
| `DIAGNOSTICO_PORT` | `4024` | `.env.prod.example` | frontend-diagnostico |
| `FLIT_DOTNET_ENVIRONMENT` | `Development` | `.env.prod.example` | core-comparendos, core-consultas, core-diagnostico, core-ict, core-notificaciones, core-personanatural, gateway, core-api (ancla) |
| `FRONTEND_COMPARENDOS_CPUS` | `0` | ⚠ ninguno | frontend-comparendos |
| `FRONTEND_COMPARENDOS_MEM_LIMIT` | `0` | ⚠ ninguno | frontend-comparendos |
| `FRONTEND_COMPARENDOS_TAG` | `latest` | ⚠ ninguno | frontend-comparendos |
| `FRONTEND_CPUS` | `0` | `.env.prod.example` | frontend |
| `FRONTEND_DIAGNOSTICO_CPUS` | `0` | `.env.prod.example` | frontend-diagnostico |
| `FRONTEND_DIAGNOSTICO_MEM_LIMIT` | `0` | `.env.prod.example` | frontend-diagnostico |
| `FRONTEND_DIAGNOSTICO_TAG` | `latest` | ⚠ ninguno | frontend-diagnostico |
| `FRONTEND_HUB_CPUS` | `0` | `.env.prod.example` | frontend-hub |
| `FRONTEND_HUB_MEM_LIMIT` | `0` | `.env.prod.example` | frontend-hub |
| `FRONTEND_HUB_TAG` | `latest` | `.env.prod.example` | frontend-hub |
| `FRONTEND_MEM_LIMIT` | `0` | `.env.prod.example` | frontend |
| `FRONTEND_PERSONANATURAL_CPUS` | `0` | `.env.prod.example` | frontend-personanatural |
| `FRONTEND_PERSONANATURAL_MEM_LIMIT` | `0` | `.env.prod.example` | frontend-personanatural |
| `FRONTEND_PERSONANATURAL_TAG` | `latest` | `.env.prod.example` | frontend-personanatural |
| `FRONTEND_PORT` | `4001` | `.env.prod.example` | frontend |
| `FRONTEND_TAG` | `latest` | `.env.prod.example` | frontend |
| `GATEWAY_CPUS` | `0` | `.env.prod.example` | gateway |
| `GATEWAY_MEM_LIMIT` | `0` | `.env.prod.example` | gateway |
| `GATEWAY_PORT` | `4002` | `.env.prod.example` | core-comparendos, core-consultas, core-diagnostico, core-ict, core-notificaciones, core-personanatural, frontend, frontend-comparendos, frontend-diagnostico, frontend-hub, frontend-personanatural, gateway, core-api (ancla) |
| `HUB_PORT` | `4022` | `.env.prod.example` | frontend-hub |
| `MIGRACION_API_CPUS` | `0` | `.env.prod.example` | migracion-api |
| `MIGRACION_API_MEM_LIMIT` | `0` | `.env.prod.example` | migracion-api |
| `MIGRACION_API_PORT` | `4030` | `.env.prod.example` | gateway, migracion-api |
| `OBSERVABILIDAD_CPUS` | `0` | ⚠ ninguno | observabilidad |
| `OBSERVABILIDAD_GRAFANA_PORT` | `4090` | `.env.prod.example` | observabilidad |
| `OBSERVABILIDAD_MEM_LIMIT` | `0` | `.env.prod.example` | observabilidad |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | vacío | `.env.prod.example` | core-comparendos, core-consultas, core-diagnostico, core-notificaciones, core-personanatural, gateway, core-api (ancla) |
| `PERSONANATURAL_PORT` | `4031` | `.env.prod.example` | frontend-personanatural |
| `PYTHON_ML_CPUS` | `0` | `.env.prod.example` | python-ml |
| `PYTHON_ML_MEM_LIMIT` | `0` | `.env.prod.example` | python-ml |
| `PYTHON_ML_PORT` | `4012` | `.env.prod.example` | gateway, python-ml |
| `PYTHON_ML_TAG` | `latest` | `.env.prod.example` | python-ml |
| `RABBITMQ_MEM_LIMIT` | `0` | `.env.prod.example` | rabbitmq |
| `RABBITMQ_MGMT_PORT` | `4672` | `.env.prod.example` | rabbitmq |

---

## 3. Variables por servicio

Columnas: **Obligatoria** = `sí (aborta)` (Compose no arranca), `sí` (el servicio no arranca o no funciona), `condicional`
(obligatoria solo si se usa la función) o `no`. **Secreta** = `sí` (sealed-secret, nunca en configmap), `sensible` (dato
personal o de contacto: no versionar, pero no es credencial) o `no`. **Default** = valor si la variable falta (`—` = sin
default; `vacío` = cadena vacía).

### core-api (Trámites)

75 variables: 66 en la tabla y 9 transversales (`CORE_API_CPUS`, `CORE_API_MEM_LIMIT`, `CORE_API_OOM_SCORE_ADJ`, `CORE_API_PORT`, `CORE_API_TAG`, `CORE_ICT_PORT`, `FLIT_DOTNET_ENVIRONMENT`, `GATEWAY_PORT`, `OTEL_EXPORTER_OTLP_ENDPOINT`).

Incluye el ancla `x-core-api-env`, que core-identity hereda entera.

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `AI_ENTREGA_DIAGNOSTICO_CLAVE_PUBLICA` | `Ai__Entrega__Diagnostico__ClavePublica` | condicional | no | vacío | `.env.prod.example` | Pública RSA-3072 de Diagnóstico (base64 DER SPKI). No es secreta. Vacía = Diagnóstico sin IA. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `AI_ENTREGA_DIAGNOSTICO_CLAVE_PUBLICA_SIGUIENTE` | `Ai__Entrega__Diagnostico__ClavePublicaSiguiente` | no | no | vacío | `.env.prod.example` | Pública siguiente, solo durante una rotación. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `AI_ENTREGA_DIAGNOSTICO_KID` | `Ai__Entrega__Diagnostico__Kid` | condicional | no | vacío | `.env.prod.example` | Kid de la pública de Diagnóstico para entregarle la llave de IA cifrada. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `AI_ENTREGA_DIAGNOSTICO_KID_SIGUIENTE` | `Ai__Entrega__Diagnostico__KidSiguiente` | no | no | vacío | `.env.prod.example` | Kid siguiente, solo durante una rotación. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `ANTHROPIC_API_KEY` | mismo nombre | condicional | sí | vacío | `.env.anthropic.example` | Llave de IA de plataforma (OCR, Dr. FLIT y la que se entrega cifrada a Diagnóstico). | `.env.anthropic.example` |
| `ANTHROPIC_BASE_URL` | mismo nombre | no | no | `https://api.anthropic.com` | `.env.anthropic.example` | URL de la API de Anthropic. | `.env.anthropic.example` |
| `ANTHROPIC_DRFLIT_DAILY_MESSAGE_LIMIT` | mismo nombre | no | no | `30` | `.env.anthropic.example`, `.env.prod.example` | Mensajes diarios por usuario en Dr. FLIT. | `.env.anthropic.example` |
| `ANTHROPIC_DRFLIT_ENABLED` | mismo nombre | no | no | `true` | `.env.anthropic.example`, `.env.prod.example` | Enciende el chat Dr. FLIT. | `.env.anthropic.example` |
| `ANTHROPIC_DRFLIT_MODEL` | mismo nombre | no | no | `claude-haiku-4-5` | `.env.anthropic.example` | Modelo de Dr. FLIT. | `.env.anthropic.example` |
| `ANTHROPIC_MAX_TOKENS` | mismo nombre | no | no | `2000` | `.env.anthropic.example` | Tope de tokens de respuesta del OCR. | `.env.anthropic.example` |
| `ANTHROPIC_MODEL` | mismo nombre | no | no | `claude-haiku-4-5` | `.env.anthropic.example` | Modelo del OCR. | `.env.anthropic.example` |
| `ANTHROPIC_TIMEOUT_SECONDS` | mismo nombre | no | no | `60` | `.env.anthropic.example` | Timeout HTTP del OCR. | `.env.anthropic.example` |
| `BIOMETRICS_PROVIDER` | mismo nombre | no | no | `mock` | `.env.kyverum.example` | Proveedor de biometría: `mock` o `kyverum`. | [suite](../suite/handoff-vps-suite.md) |
| `CONNECTION_STRING_CORE` | `ConnectionStrings__Core` | sí | sí | — | `.env.prod.example` | Cadena Npgsql de la base principal (Trámites, Identidad, ICT, migrador). Sin ella no arranca. | [suite](../suite/handoff-vps-suite.md) |
| `CONSULTAS_REMOTO_ADDRESS` | `Consultas__Remoto__Address` | no | no | `http://core-consultas:8084` | `.env.prod.example` | gRPC de core-consultas para Trámites, Persona Natural y Comparendos. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `CONSULTAS_REMOTO_DEADLINE_SEGUNDOS` | `Consultas__Remoto__DeadlineSegundos` | no | no | `180` | `.env.prod.example` | Deadline gRPC hacia core-consultas. Debe ser ≥ 120. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `CORE_API_GRPC_PORT` | `Ict__GrpcPort` | no | no | `8082` | ⚠ ninguno | Puerto gRPC interno de core-api (ICT, Diagnóstico, Comparendos, Persona Natural). Igual en los tres ambientes. | [suite](../suite/handoff-vps-suite.md) |
| `CORS_ORIGIN` | `Cors__AllowedOrigins`, `Invitations__ActivateUrlBase`, `PasswordRecovery__ResetUrlBase`, `Notifications__EmailAssets__BaseUrl`, `PublicBranding__PublicBaseUrl` | sí (aborta) | no | — (aborta) | `.env.prod.example` | URL pública del front del ambiente, sin barra final. CORS y enlaces de los correos. Sin ella el compose aborta. | [suite](../suite/handoff-vps-suite.md) |
| `DEEPSEEK_API_KEY` | mismo nombre | no | sí | vacío | `.env.anthropic.example` | Llave del respaldo DeepSeek. | `.env.anthropic.example` |
| `DEEPSEEK_BASE_URL` | mismo nombre | no | no | `https://api.deepseek.com` | `.env.anthropic.example` | URL de DeepSeek. | `.env.anthropic.example` |
| `DEEPSEEK_MODEL` | mismo nombre | no | no | `deepseek-chat` | `.env.anthropic.example` | Modelo de DeepSeek. | `.env.anthropic.example` |
| `DR_FLIT_ADO_PAT` | mismo nombre | no | sí | vacío | `.env.prod.example` | PAT de la cuenta de servicio Dr. FLIT (casos de soporte). Vacío = los casos responden 502. | [suite](../suite/handoff-vps-suite.md) |
| `DR_FLIT_DEPLOY_ENVIRONMENT` | mismo nombre | no | no | `DEV` | `.env.prod.example` | Ambiente que Dr. FLIT escribe en el caso: DEV, QA o PDN. | [suite](../suite/handoff-vps-suite.md) |
| `FILE_MANAGER_AUTH_TOKEN` | `FileManager__AuthToken` | condicional | sí | vacío | ⚠ ninguno | Bearer del file-manager. Mismo valor en core-api, core-ict y productos. Obligatorio con Contabo (PDN). | [suite](../suite/handoff-vps-suite.md) |
| `FILE_MANAGER_BASE_URL` | `FileManager__BaseUrl` | no | no | `https://ny46zhyeti.execute-api.us-east-1.amazonaws.com/pdn/` | `.env.prod.example` | URL del file-manager de adjuntos. Misma en core-api, core-ict, migrador y productos. | [suite](../suite/handoff-vps-suite.md) |
| `FLITO_SIGNALS_ENABLED` | `Integrations__FlitoSignals__Enabled` | no | no | `false` | `.env.prod.example` | Avisos de sincronización a FLITO. En PDN solo tras acordarlo con FLITO. | [suite](../suite/handoff-vps-suite.md) |
| `FLITO_SIGNALS_SIGNING_KEY` | `Integrations__FlitoSignals__SigningKey` | no | sí | vacío | `.env.prod.example` | Clave HMAC de los avisos a FLITO, una por ambiente. | [suite](../suite/handoff-vps-suite.md) |
| `FLITO_SIGNALS_TARGET_URL` | `Integrations__FlitoSignals__TargetUrl` | no | no | vacío | `.env.prod.example` | URL del webhook de FLITO. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_DEV_SEED` | mismo nombre | no | no | vacío | `.env.prod.example` | Las migraciones de datos demo siembran aunque el ambiente no se llame Development. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_EDGE_IP` | `Domains__EdgeIp` | no | no | vacío | `.env.prod.example` | IP pública del borde (registro A que se pide a cada cliente de Marca Blanca). | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_INTERNAL_API_KEY` | `Internal__ApiKey` | condicional | sí | vacío | `.env.prod.example`, `frontend/.env.example` | Clave interna gateway ↔ core-api ↔ fronts (dominios de Marca Blanca). Vacía = fail-closed. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_OIDC_ENABLED` | `Suite__Oidc__Enabled` | no | no | `false` | `.env.prod.example` | Servidor OIDC del hub. Apagado: `/connect/*` responde 404. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_SEED_DEMO_USERS` | `Seed__DemoUsers` | no | no | `true` | `.env.prod.example` | Crea cuentas demo con contraseña fija. `false` fuera de DEV. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_SEED_RBAC_CATALOG` | `Seed__RbacCatalog` | no | no | `true` | `.env.prod.example` | Siembra roles, módulos y permisos. Dejar en `true`. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_SUITE_ENV` | `Suite__Hosts__Environment` | sí (aborta) | no | — (aborta) | `.env.prod.example` | Prefijo de hosts: `dev`, `qa` o vacía en PDN. Sin definir, el compose aborta. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_SWAGGER_ENABLED` | `Swagger__Enabled` | no | no | `true` | `.env.prod.example` | `/swagger` público. `false` en PDN. | [suite](../suite/handoff-vps-suite.md) |
| `ICT_SERVICE_TOKEN_ACCEPT_LEGACY` | `Ict__ServiceToken__AcceptLegacy` | no | no | `true` | `.env.prod.example` | core-api acepta todavía el token HMAC de ICT. `false` = corte del HMAC. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `ICT_SERVICE_TOKEN_SECRET` | `Ict__ServiceToken__Secret` | no | sí | vacío | ⚠ ninguno | Secreto HMAC del canal gRPC core-api ↔ core-ict. Vacío = fail-closed. | [suite](../suite/handoff-vps-suite.md) |
| `JWT_PERSIST_SIGNING_KEY` | `Jwt__PersistSigningKey` | no | no | `true` | ⚠ ninguno | Llave de firma JWT persistente en base (no efímera). | [suite](../suite/handoff-vps-suite.md) |
| `JWT_VALIDATE_ISSUED_TOKENS` | `Jwt__ValidateIssuedTokens` | no | no | `true` | `.env.prod.example` | Valida firma, emisor, audiencia y vencimiento. `false` solo como vuelta atrás. | [suite](../suite/handoff-vps-suite.md) |
| `KIMI_API_KEY` | mismo nombre | no | sí | vacío | `.env.anthropic.example` | Llave del respaldo Kimi. | `.env.anthropic.example` |
| `KIMI_BASE_URL` | mismo nombre | no | no | `https://api.moonshot.cn/v1` | `.env.anthropic.example` | URL de Kimi. | `.env.anthropic.example` |
| `KIMI_MODEL` | mismo nombre | no | no | `moonshot-v1-8k` | `.env.anthropic.example` | Modelo de texto de Kimi. | `.env.anthropic.example` |
| `KIMI_VISION_MODEL` | mismo nombre | no | no | `moonshot-v1-8k-vision-preview` | `.env.anthropic.example` | Modelo de visión de Kimi. | `.env.anthropic.example` |
| `NOTIFICACIONES_REMOTO_ADDRESS` | `Notificaciones__Remoto__Address` | no | no | `http://core-notificaciones:8085` | `.env.prod.example` | gRPC de core-notificaciones. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `OCR_PROVIDER` | mismo nombre | no | no | `mock` | `.env.anthropic.example` | Proveedor de OCR: `mock` o uno real. | `.env.anthropic.example` |
| `OPENAI_API_KEY` | mismo nombre | no | sí | vacío | `.env.anthropic.example` | Llave del proveedor de respaldo OpenAI. Vacía = sin respaldo. | `.env.anthropic.example` |
| `OPENAI_BASE_URL` | mismo nombre | no | no | `https://api.openai.com/v1` | `.env.anthropic.example` | URL de OpenAI. | `.env.anthropic.example` |
| `OPENAI_MODEL` | mismo nombre | no | no | `gpt-4o-mini` | `.env.anthropic.example` | Modelo de texto de OpenAI. | `.env.anthropic.example` |
| `OPENAI_VISION_MODEL` | mismo nombre | no | no | `gpt-4o` | `.env.anthropic.example` | Modelo de visión de OpenAI. | `.env.anthropic.example` |
| `PERSONA_NATURAL_FACHADA_HABILITADA` | `PersonaNatural__Fachada__Habilitada` | no | no | `false` | `.env.prod.example` | Fachada gRPC de trámites para el portal Persona Natural. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `RABBITMQ_URL_TRAMITES` | `Platform__Messaging__ConnectionString` | sí (aborta) | sí | — (aborta) | `.env.prod.example` | AMQP del usuario `tramites`. Sin ella el compose aborta. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SVC_COMPARENDOS_CLIENT_SECRET` | `Suite__Oidc__ServiceClients__svc-comparendos__Secret` | condicional | sí | vacío | `.env.prod.example` | Secreto del cliente de servicio `svc-comparendos` (registro en Identidad y uso en el servicio). | [comparendos](../suite/handoff-vps-comparendos.md) |
| `SVC_CONSULTAS_CLIENT_SECRET` | `Suite__Oidc__ServiceClients__svc-consultas__Secret` | condicional | sí | vacío | `.env.prod.example` | Secreto de `svc-consultas`. Habilita el token con scope de la credencial de impuesto. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SVC_DIAGNOSTICO_CLIENT_SECRET` | `Suite__Oidc__ServiceClients__svc-diagnostico__Secret` | condicional | sí | vacío | `.env.prod.example` | Secreto de `svc-diagnostico`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `SVC_ICT_CLIENT_SECRET` | `Suite__Oidc__ServiceClients__svc-ict__Secret` | condicional | sí | vacío | `.env.prod.example` | Secreto de `svc-ict`. Sin él, core-ict no consulta en core-consultas. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SVC_IDENTIDAD_CLIENT_SECRET` | `Suite__Oidc__ServiceClients__svc-identidad__Secret` | no | sí | vacío | `.env.prod.example` | Secreto de `svc-identidad`. | [suite](../suite/handoff-vps-suite.md) |
| `SVC_NOTIFICACIONES_CLIENT_SECRET` | `Suite__Oidc__ServiceClients__svc-notificaciones__Secret` | condicional | sí | vacío | `.env.prod.example` | Secreto de `svc-notificaciones`. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SVC_PERSONANATURAL_CLIENT_SECRET` | `Suite__Oidc__ServiceClients__svc-personanatural__Secret` | condicional | sí | vacío | `.env.prod.example` | Secreto de `svc-personanatural`. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `SVC_TRAMITES_CLIENT_SECRET` | `Suite__Oidc__ServiceClients__svc-tramites__Secret`, `Platform__ServiceClient__ClientSecret` | sí (aborta) | sí | — (aborta) | `.env.prod.example` | Secreto de `svc-tramites` (core-api llama a Consultas). Sin ella el compose aborta. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `TRAMITES_DERIVACION_CONCURRENCIA_MAXIMA` | `Tramites__Derivacion__ConcurrenciaMaxima` | no | no | `8` | ⚠ ninguno | Llamadas a la vez del rpc de derivación. ≥ suma de la concurrencia de Diagnóstico. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `TRAMITES_DERIVACION_HABILITADA` | `Tramites__Derivacion__Habilitada` | no | no | `false` | ⚠ ninguno | rpc «crear borrador desde Diagnóstico». Se enciende junto con `DIAGNOSTICO_DERIVACION_HABILITADA`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `TRAMITE_VALIDATION_BODY_TYPE_MODE` | `TramiteValidations__VehicleBodyTypeRequired__Mode` | no | no | `block` | ⚠ ninguno | Validación de carrocería: `block`, `warn` u `off`. | [suite](../suite/handoff-vps-suite.md) |
| `TRAMITE_VALIDATION_DUPLICATE_MODE` | `TramiteValidations__DuplicateActiveProcedure__Mode` | no | no | `block` | ⚠ ninguno | CF-01 trámite duplicado: `block`, `warn` u `off`. Matriz: DEV warn · QA block · PDN block. | [suite](../suite/handoff-vps-suite.md) |
| `TRAMITE_VALIDATION_MANDATARIO_MODE` | `TramiteValidations__MandatarioRequerido__Mode` | no | no | `warn` | ⚠ ninguno | Mandatario requerido. Default `warn`; el paso a `block` lo decide el Líder Técnico. | [suite](../suite/handoff-vps-suite.md) |
| `TRAMITE_VALIDATION_VEHICLE_STATE_MODE` | `TramiteValidations__VehicleRegistrationState__Mode` | no | no | `block` | ⚠ ninguno | CF-03 estado registral: `block`, `warn` u `off`. Matriz: DEV warn · QA warn · PDN block. | [suite](../suite/handoff-vps-suite.md) |

### core-identity (perfil `identity`)

7 variables: 2 en la tabla y 5 transversales (`CORE_IDENTITY_CPUS`, `CORE_IDENTITY_MEM_LIMIT`, `CORE_IDENTITY_OOM_SCORE_ADJ`, `CORE_IDENTITY_PORT`, `CORE_IDENTITY_TAG`).

Hereda las 67 variables del ancla de core-api (tabla anterior): misma base, llave de firma, clientes OIDC y correo. Si difieren, un proceso rechaza los tokens del otro. Aquí solo las propias. Sobrescribe `Platform__Messaging__ConnectionString` con `RABBITMQ_URL_PLATAFORMA`.

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `CORE_IDENTITY_GRPC_PORT` | `Identidad__GrpcPort` | condicional | no | vacío | `.env.prod.example` | Puerto gRPC interno de Identidad (propuesto 8083). Vacío = sin gRPC: Diagnóstico, Comparendos y la credencial de impuesto quedan sin servicio. | [identidad](../suite/handoff-vps-identidad.md) |
| `RABBITMQ_URL_PLATAFORMA` | `Platform__Messaging__ConnectionString` | sí (aborta) | sí | — (aborta) | `.env.prod.example` | AMQP del usuario `plataforma` (correos de core-identity). Sin ella el compose aborta. | [identidad](../suite/handoff-vps-identidad.md) |

### core-consultas

93 variables: 86 en la tabla y 7 transversales (`CORE_CONSULTAS_CPUS`, `CORE_CONSULTAS_MEM_LIMIT`, `CORE_CONSULTAS_PORT`, `CORE_CONSULTAS_TAG`, `FLIT_DOTNET_ENVIRONMENT`, `GATEWAY_PORT`, `OTEL_EXPORTER_OTLP_ENDPOINT`).

El bloque de impuesto se detalla en [§4](#4-impuesto-vehicular-fuente-real-ola-hu-14681). Si un proveedor está en modo real sin su secreto, no arranca y el log dice cuál.

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `CONNECTION_STRING_CONSULTAS` | `ConnectionStrings__Servicio` | sí | sí | vacío | `.env.prod.example` | Base del usuario dueño del esquema `consultas`. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `CONSULTAS_IDENTIDAD_ADDRESS` | `Consultas__Identidad__Address` | condicional | no | vacío | `.env.impuesto.example` | gRPC de Identidad para leer la API key de impuesto por compañía. Vacío = transporte real no listo. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `CONSULTAS_KYVERUM_WEBHOOK_CALLBACK_URL` | `KYVERUM_WEBHOOK_CALLBACK_URL` | no | no | vacío | `.env.prod.example` | URL pública del receptor de avisos de Kyverum Verify en Consultas. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `CORE_CONSULTAS_GRPC_PORT` | `Servicio__GrpcPort` | no | no | `8084` | `.env.prod.example` | Puerto gRPC interno de core-consultas. Debe coincidir con las direcciones `*_CONSULTAS_ADDRESS`. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `FASECOLDA_API_BASE_URL` | mismo nombre | no | no | `https://guiadevalores.fasecolda.com/apifasecolda` | `.env.fasecolda.example` | URL de Fasecolda. | `.env.fasecolda.example` |
| `FASECOLDA_API_GRANT_TYPE` | mismo nombre | no | no | `password` | `.env.fasecolda.example` | Grant type del login de Fasecolda. | `.env.fasecolda.example` |
| `FASECOLDA_API_PASSWORD` | mismo nombre | no | sí | vacío | `.env.fasecolda.example` | Clave de Fasecolda. | `.env.fasecolda.example` |
| `FASECOLDA_API_SECONDS_TIMEOUT` | mismo nombre | no | no | `55` | `.env.fasecolda.example` | Timeout de Fasecolda. | `.env.fasecolda.example` |
| `FASECOLDA_API_USERNAME` | mismo nombre | no | no | vacío | `.env.fasecolda.example` | Usuario de Fasecolda. | `.env.fasecolda.example` |
| `FASECOLDA_AUTH_API_PATH` | mismo nombre | no | no | `/token` | `.env.fasecolda.example` | Ruta del login de Fasecolda. | `.env.fasecolda.example` |
| `FASECOLDA_BY_VIN_API_BASE_URL` | mismo nombre | no | no | `https://fasecoldaback.quantil.co` | `.env.fasecolda.example` | URL de la búsqueda por VIN. | `.env.fasecolda.example` |
| `FASECOLDA_BY_VIN_API_PATH` | mismo nombre | no | no | `/api/busquedaVin` | `.env.fasecolda.example` | Ruta de la búsqueda por VIN. | `.env.fasecolda.example` |
| `FASECOLDA_LIST_CODE_API_PATH` | mismo nombre | no | no | `/api/listacodigosid/consultabycodigo` | `.env.fasecolda.example` | Ruta de la consulta por código. | `.env.fasecolda.example` |
| `FASECOLDA_MODE` | mismo nombre | no | no | `mock` | `.env.fasecolda.example` | `mock` o `real`. En `real` exige usuario y clave. | `.env.fasecolda.example` |
| `FLIT_HUB_URL` | `Platform__Auth__Issuers__0` | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `IMPUESTO_API_BASE_URL` | mismo nombre | condicional | no | vacío | `.env.impuesto.example` | URL base de la fuente de impuesto. Vacía = transporte real no listo. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_API_PATH` | mismo nombre | condicional | no | vacío | `.env.impuesto.example` | Ruta de la fuente de impuesto. Vacía = no listo. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_API_TIMEOUT_SECONDS` | mismo nombre | no | no | `280` | `.env.impuesto.example` | Presupuesto total del sondeo. Con 300 o más no arranca. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_BIS_1` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: bis 1. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_BIS_2` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: bis 2. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_CARDINALIDAD_1` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: cardinalidad 1. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_CARDINALIDAD_2` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: cardinalidad 2. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_CELULAR` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: celular. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_CORREO` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: correo. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_DIRECCION_CIUDAD` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: direccion ciudad. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_DIRECCION_COMPLETA` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: direccion completa. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_DIRECCION_DEPARTAMENTO` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: direccion departamento. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_LETRA_1` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: letra 1. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_LETRA_2` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: letra 2. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_NUMERO_1` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: numero 1. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_NUMERO_2` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: numero 2. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_PLACA` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: placa. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_SUFIJO_1` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: sufijo 1. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_SUFIJO_2` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: sufijo 2. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_TELEFONO` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: telefono. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `IMPUESTO_CONTACTO_TIPO_VIA` | mismo nombre | no | sensible | vacío | `.env.impuesto.example` | Contacto fijo de respaldo del impuesto: tipo via. Nunca versionado. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `INTEMPO_API_BASE_URL` | mismo nombre | no | no | vacío | `.env.intempo.example` | URL del RUNT por Intempo. Vacía = no conectado. | `.env.intempo.example` |
| `INTEMPO_API_SECONDS_TIMEOUT` | mismo nombre | no | no | `20` | `.env.intempo.example` | Timeout de Intempo. | `.env.intempo.example` |
| `INTEMPO_API_VEHICLE_PLATE_PATH` | mismo nombre | no | no | `/api/RuntConsulta/consultar-vehiculo` | `.env.intempo.example` | Ruta de consulta por placa. | `.env.intempo.example` |
| `INTEMPO_API_VEHICLE_VIN_PATH` | mismo nombre | no | no | `/api/RuntConsulta/consultar-vin` | `.env.intempo.example` | Ruta de consulta por VIN. | `.env.intempo.example` |
| `INTEMPO_MODE` | mismo nombre | no | no | `mock` | ⚠ ninguno | Modo del proveedor Intempo heredado de core-api. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `KYVERUM_API_KEY` | mismo nombre | no | sí | vacío | `.env.kyverum.example` | Llave de Kyverum Verify (biometría). | `.env.kyverum.example` |
| `KYVERUM_AUTH_SCHEME` | mismo nombre | no | no | `Bearer` | `.env.kyverum.example` | Esquema de autenticación de Verify. | `.env.kyverum.example` |
| `KYVERUM_BASE_URL` | mismo nombre | no | no | `https://verify.kyverum.com` | `.env.kyverum.example` | URL de Kyverum Verify. | `.env.kyverum.example` |
| `KYVERUM_FINES_TIMEOUT_SECONDS` | mismo nombre | no | no | `120` | `.env.prod.example` | Timeout de Kyverum comparendos. | `.env.kyverum.example` |
| `KYVERUM_RUNT_API_KEY` | mismo nombre | no | sí | vacío | `.env.kyverum-runt.example` | Llave de Kyverum RUNT (improntas). | `.env.kyverum-runt.example` |
| `KYVERUM_RUNT_AUTH_SCHEME` | mismo nombre | no | no | `Bearer` | `.env.kyverum-runt.example` | Esquema de autenticación de Kyverum RUNT. | `.env.kyverum-runt.example` |
| `KYVERUM_RUNT_BASE_URL` | mismo nombre | no | no | `https://runt.kyverum.com` | `.env.kyverum-runt.example` | URL de Kyverum RUNT. | `.env.kyverum-runt.example` |
| `KYVERUM_RUNT_TIMEOUT_SECONDS` | mismo nombre | no | no | `120` | `.env.kyverum-runt.example`, `.env.prod.example` | Timeout de Kyverum RUNT. | `.env.kyverum-runt.example` |
| `KYVERUM_TIMEOUT_SECONDS` | mismo nombre | no | no | `120` | `.env.kyverum.example`, `.env.prod.example` | Timeout de Kyverum Verify. | `.env.kyverum.example` |
| `MUNICIPAL_INFRACCIONES_BASE_URL` | mismo nombre | no | no | vacío | `.env.prod.example` | URL de la API municipal de comparendos. Vacía = no conectada. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `MUNICIPAL_INFRACCIONES_PATH` | mismo nombre | no | no | vacío | `.env.prod.example` | Ruta de la API municipal. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `MUNICIPAL_INFRACCIONES_TIMEOUT_SECONDS` | mismo nombre | no | no | `60` | `.env.prod.example` | Timeout de la API municipal. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `RABBITMQ_URL_CONSULTAS` | `Platform__Messaging__ConnectionString` | no | sí | `amqp://consultas:sin-clave@rabbitmq:5672/flit` | `.env.prod.example` | AMQP del usuario `consultas`. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `RENTING_API_BASE_URL` | mismo nombre | no | no | vacío | `.env.renting.example` | URL del APIM de Renting. | `.env.renting.example` |
| `RENTING_API_CONTRATOS_DEVELOPMENT_EMAIL` | mismo nombre | no | no | vacío | `.env.prod.example` | Buzón al que se desvía el contacto fuera de PDN. | `.env.renting.example` |
| `RENTING_API_CONTRATOS_PATH` | mismo nombre | no | no | vacío | `.env.prod.example` | Ruta de la API de contratos (conductor por placa). | `.env.renting.example` |
| `RENTING_API_CONTRATOS_REAL_CONTACT_ENABLED` | mismo nombre | no | no | `false` | `.env.prod.example` | Contacto real del conductor. Solo PDN. | `.env.renting.example` |
| `RENTING_API_KEY_NAME` | mismo nombre | no | no | `Ocp-Apim-Subscription-Key` | `.env.renting.example` | Nombre de la cabecera de suscripción. | `.env.renting.example` |
| `RENTING_API_KEY_VALUE` | mismo nombre | no | sí | vacío | `.env.renting.example` | Valor de la suscripción del APIM. | `.env.renting.example` |
| `RENTING_API_LOGIN_CACHE_SECONDS_TTL` | mismo nombre | no | no | `3600` | `.env.renting.example` | TTL del token de login. | `.env.renting.example` |
| `RENTING_API_LOGIN_PATH` | mismo nombre | no | no | vacío | `.env.renting.example` | Ruta del login. | `.env.renting.example` |
| `RENTING_API_LOGIN_SECRET_NAME` | mismo nombre | no | no | vacío | `.env.renting.example` | Nombre del secreto que Renting resuelve (no el secreto). | `.env.renting.example` |
| `RENTING_API_LOGIN_SUBJECT` | mismo nombre | no | no | vacío | `.env.renting.example` | Subject del `.pfx`, carácter a carácter. | `.env.renting.example` |
| `RENTING_API_PASSPHRASE` | mismo nombre | no | sí | vacío | `.env.renting.example` | Clave del `.pfx`. | `.env.renting.example` |
| `RENTING_API_PFX_CERTIFICATE_PATH` | mismo nombre | condicional | no | `/opt/certs/email/renting.rc.prod.pfx` | `.env.renting.example` | Ruta absoluta del `.pfx` mTLS (misma en host y contenedor). | `.env.renting.example` |
| `RENTING_API_SECONDS_TIMEOUT` | mismo nombre | no | no | `30` | `.env.renting.example` | Timeout del APIM. | `.env.renting.example` |
| `RUES_API_KEY` | mismo nombre | no | sí | vacío | `.env.prod.example` | Llave del RUES. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `RUES_AUTH_SCHEME` | mismo nombre | no | no | `Bearer` | ⚠ ninguno | Esquema de autenticación del RUES. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `RUES_BASE_URL` | mismo nombre | no | no | vacío | `.env.prod.example` | URL del RUES. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `RUES_ENABLED` | mismo nombre | no | no | `false` | `.env.prod.example` | Autogenera el certificado RUES. Apagado = carga manual. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `RUES_TIMEOUT_SECONDS` | mismo nombre | no | no | `30` | ⚠ ninguno | Timeout del RUES. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMARTCLOUD_ACCESS_KEY` | mismo nombre | no | sí | vacío | `.env.prod.example` | Clave de acceso SOAP del SIMIT (SmartCloud). | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMARTCLOUD_PROXY_URL` | mismo nombre | no | sí | vacío | `.env.prod.example` | Proxy solo para SmartCloud (lleva usuario y clave). | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMARTCLOUD_TIMEOUT_SECONDS` | mismo nombre | no | no | `60` | `.env.prod.example` | Timeout de SmartCloud. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMARTCLOUD_URL_IDENTIFICACION` | mismo nombre | no | no | vacío | `.env.prod.example` | URL SOAP por identificación. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMARTCLOUD_URL_PLACA` | mismo nombre | no | no | vacío | `.env.prod.example` | URL SOAP por placa. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SVC_CONSULTAS_CLIENT_SECRET` | `Platform__ServiceClient__ClientSecret` | sí | sí | vacío | `.env.prod.example` | Secreto de `svc-consultas`. Habilita el token con scope de la credencial de impuesto. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `VERIFIK_API_TOKEN` | mismo nombre | no | sí | vacío | `.env.prod.example`, `.env.verifik.example` | Token de Verifik (RUNT). | `.env.verifik.example` |
| `VERIFIK_BASE_URL` | mismo nombre | no | no | `https://api.verifik.co` | `.env.prod.example`, `.env.verifik.example` | URL de Verifik. | `.env.verifik.example` |
| `VERIFIK_CONDUCTOR_MODE` | mismo nombre | no | no | `mock` | ⚠ ninguno | Modo de conductor. | `.env.verifik.example` |
| `VERIFIK_RNMC_MODE` | mismo nombre | no | no | `mock` | ⚠ ninguno | Modo de RNMC. | `.env.verifik.example` |
| `VERIFIK_RUES_MODE` | mismo nombre | no | no | `mock` | ⚠ ninguno | Modo de RUES por Verifik. | `.env.verifik.example` |
| `VERIFIK_SIMIT_MODE` | mismo nombre | no | no | `mock` | ⚠ ninguno | Modo de SIMIT. | `.env.verifik.example` |
| `VERIFIK_TIMEOUT_SECONDS` | mismo nombre | no | no | `120` | `.env.prod.example`, `.env.verifik.example` | Timeout de Verifik. | `.env.verifik.example` |
| `VERIFIK_VEHICLE_MODE` | mismo nombre | no | no | `real` | ⚠ ninguno | Modo de la consulta de vehículo: `real` o `mock`. | `.env.verifik.example` |

### core-notificaciones

43 variables: 36 en la tabla y 7 transversales (`CORE_NOTIFICACIONES_CPUS`, `CORE_NOTIFICACIONES_MEM_LIMIT`, `CORE_NOTIFICACIONES_PORT`, `CORE_NOTIFICACIONES_TAG`, `FLIT_DOTNET_ENVIRONMENT`, `GATEWAY_PORT`, `OTEL_EXPORTER_OTLP_ENDPOINT`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `CONNECTION_STRING_NOTIFICACIONES` | `ConnectionStrings__Servicio` | sí | sí | vacío | `.env.prod.example` | Base del usuario dueño del esquema `notificaciones`. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `CORE_NOTIFICACIONES_GRPC_PORT` | `Servicio__GrpcPort` | no | no | `8085` | `.env.prod.example` | Puerto gRPC interno de core-notificaciones. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `FLIT_HUB_URL` | `Platform__Auth__Issuers__0` | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `RABBITMQ_URL_NOTIFICACIONES` | `Platform__Messaging__ConnectionString` | no | sí | `amqp://notificaciones:sin-clave@rabbitmq:5672/flit` | `.env.prod.example` | AMQP del usuario `notificaciones`. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `RENTING_API_BASE_URL` | mismo nombre | no | no | vacío | `.env.renting.example` | URL del APIM de Renting. | `.env.renting.example` |
| `RENTING_API_ENABLED` | mismo nombre | no | no | `false` | `.env.renting.example` | Enciende el canal de correo Renting. | `.env.renting.example` |
| `RENTING_API_KEY_NAME` | mismo nombre | no | no | `Ocp-Apim-Subscription-Key` | `.env.renting.example` | Nombre de la cabecera de suscripción. | `.env.renting.example` |
| `RENTING_API_KEY_VALUE` | mismo nombre | no | sí | vacío | `.env.renting.example` | Valor de la suscripción del APIM. | `.env.renting.example` |
| `RENTING_API_LOGIN_CACHE_KEY` | mismo nombre | no | no | `renting-api-login-token` | `.env.renting.example` | Clave de caché del token de login. | `.env.renting.example` |
| `RENTING_API_LOGIN_CACHE_SECONDS_TTL` | mismo nombre | no | no | `3600` | `.env.renting.example` | TTL del token de login. | `.env.renting.example` |
| `RENTING_API_LOGIN_PATH` | mismo nombre | no | no | vacío | `.env.renting.example` | Ruta del login. | `.env.renting.example` |
| `RENTING_API_LOGIN_SECONDS_TIMEOUT` | mismo nombre | no | no | `30` | `.env.renting.example` | Timeout del login. | `.env.renting.example` |
| `RENTING_API_LOGIN_SECRET_NAME` | mismo nombre | no | no | vacío | `.env.renting.example` | Nombre del secreto que Renting resuelve (no el secreto). | `.env.renting.example` |
| `RENTING_API_LOGIN_SUBJECT` | mismo nombre | no | no | vacío | `.env.renting.example` | Subject del `.pfx`, carácter a carácter. | `.env.renting.example` |
| `RENTING_API_PASSPHRASE` | mismo nombre | no | sí | vacío | `.env.renting.example` | Clave del `.pfx`. | `.env.renting.example` |
| `RENTING_API_PFX_CERTIFICATE_PATH` | mismo nombre | sí | no | `/opt/certs/email/renting.rc.prod.pfx` | `.env.renting.example` | Ruta absoluta del `.pfx` mTLS (misma en host y contenedor). | `.env.renting.example` |
| `RENTING_API_SECONDS_TIMEOUT` | mismo nombre | no | no | `30` | `.env.renting.example` | Timeout del APIM. | `.env.renting.example` |
| `RENTING_API_SEND_EMAIL_DEFAULT_SENDER_EMAIL` | mismo nombre | no | no | vacío | `.env.renting.example` | Remitente por defecto. | `.env.renting.example` |
| `RENTING_API_SEND_EMAIL_DEFAULT_SENDER_USERNAME` | mismo nombre | no | no | `Tramites Vehiculos` | `.env.renting.example` | Nombre del remitente por defecto. | `.env.renting.example` |
| `RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_EMAIL` | mismo nombre | no | no | vacío | `.env.renting.example` | Buzón de control. | `.env.renting.example` |
| `RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_USERNAME` | mismo nombre | no | no | `Equipo de desarrollo FLIT` | `.env.renting.example` | Nombre del buzón de control. | `.env.renting.example` |
| `RENTING_API_SEND_EMAIL_PATH` | mismo nombre | no | no | `RentingNotification/emailNotification/sendEmailFile` | `.env.renting.example` | Ruta de envío de correo. | `.env.renting.example` |
| `RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED` | mismo nombre | no | no | `true` | `.env.renting.example` | `true` = destinatarios reales; `false` = buzón de control. Otro valor no arranca. | `.env.renting.example` |
| `RENTING_API_SEND_EMAIL_SECONDS_TIMEOUT` | mismo nombre | no | no | `30` | `.env.renting.example` | Timeout del envío. | `.env.renting.example` |
| `RENTING_API_SEND_EMAIL_SENDER_EMAIL` | mismo nombre | no | no | vacío | `.env.renting.example` | Remitente Renting. | `.env.renting.example` |
| `RENTING_API_SEND_EMAIL_SENDER_USERNAME` | mismo nombre | no | no | `Tramites Renting` | `.env.renting.example` | Nombre del remitente Renting. | `.env.renting.example` |
| `SMTP_CHECK_CERT_REVOCATION` | `Smtp__CheckCertificateRevocation` | no | no | `true` | ⚠ ninguno | Comprueba revocación del certificado SMTP. `false` solo con proxy que inspecciona TLS. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMTP_DISABLE_AUTH` | `Smtp__DisableAuthentication` | no | no | `false` | `.env.prod.example` | SMTP sin autenticación. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMTP_FROM` | `Smtp__DefaultSenderEmail` | condicional | no | vacío | `.env.prod.example` | Remitente SMTP. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMTP_FROM_NAME` | `Smtp__DefaultSenderName` | no | no | `FLIT Tramites` | `.env.prod.example` | Nombre del remitente. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMTP_HOST` | `Smtp__Host` | condicional | no | vacío | `.env.prod.example` | Servidor SMTP. Vacío = correo al log. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMTP_PASSWORD` | `Smtp__DefaultSenderPassword` | condicional | sí | vacío | `.env.prod.example` | Clave SMTP. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMTP_PORT` | `Smtp__Port` | no | no | `587` | `.env.prod.example` | Puerto SMTP. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMTP_USE_CONSOLE_WHEN_NO_HOST` | `Smtp__UseConsoleWhenNoHost` | no | no | `true` | `.env.prod.example` | Sin host, escribe el correo en el log. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SMTP_USE_STARTTLS` | `Smtp__UseStartTls` | no | no | `true` | `.env.prod.example` | Usa STARTTLS. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SVC_NOTIFICACIONES_CLIENT_SECRET` | `Platform__ServiceClient__ClientSecret` | sí | sí | vacío | `.env.prod.example` | Secreto de `svc-notificaciones`. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |

### core-diagnostico (perfil `diagnostico`)

35 variables: 28 en la tabla y 7 transversales (`CORE_DIAGNOSTICO_CPUS`, `CORE_DIAGNOSTICO_MEM_LIMIT`, `CORE_DIAGNOSTICO_PORT`, `CORE_DIAGNOSTICO_TAG`, `FLIT_DOTNET_ENVIRONMENT`, `GATEWAY_PORT`, `OTEL_EXPORTER_OTLP_ENDPOINT`).

Además el secreto de archivo `diagnostico_dp_pfx` (ruta `DIAGNOSTICO_DP_CERT_FILE`, default `/etc/flit/secrets/diagnostico-dp.pfx`). Sin certificado válido no arranca en ningún ambiente.

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `CONNECTION_STRING_DIAGNOSTICO` | `ConnectionStrings__Servicio` | sí | sí | vacío | `.env.prod.example` | Base del usuario dueño del esquema `diagnostico`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `CORE_API_GRPC_PORT` | `Diagnostico__Tramites__Address` | no | no | `8082` | ⚠ ninguno | Puerto gRPC interno de core-api (ICT, Diagnóstico, Comparendos, Persona Natural). Igual en los tres ambientes. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `CORE_DIAGNOSTICO_GRPC_PORT` | `Servicio__GrpcPort` | no | no | `8088` | `.env.prod.example` | Puerto gRPC interno de core-diagnostico. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_AMBIENTE` | `Diagnostico__Ambiente` | no | no | vacío | ⚠ ninguno | LOCAL, DEV, QA o PDN. Solo se valida con simulación. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_CONSULTAS_ADDRESS` | `Diagnostico__Consultas__Address` | condicional | no | vacío | ⚠ ninguno | gRPC de core-consultas. Al definirla se encienden juntas todas las fuentes reales del diagnóstico. | [despliegue diag.](../suite/despliegue-develop-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_CONCURRENCIA` | `Diagnostico__Derivacion__Concurrencia` | no | no | `4` | ⚠ ninguno | Llamadas a la vez por réplica al crear trámites. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_CREDITO_POR_COMPANIA` | `Diagnostico__Derivacion__CreditoPorCompania` | no | no | `2` | ⚠ ninguno | Ítems en vuelo por compañía (≤ concurrencia). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_HABILITADA` | `Diagnostico__Derivacion__Habilitada` | no | no | `false` | ⚠ ninguno | «Crear trámite» individual y masivo. Junto con `TRAMITES_DERIVACION_HABILITADA`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_PREFETCH` | `Diagnostico__Derivacion__Prefetch` | no | no | `8` | ⚠ ninguno | Prefetch de la cola `diagnostico.derivaciones`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_TASA_POR_SEGUNDO` | `Diagnostico__Derivacion__TasaPorSegundo` | no | no | `10` | ⚠ ninguno | Llamadas por segundo al crear trámites. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_URL_TRAMITES` | `Diagnostico__Derivacion__UrlTramites` | no | no | vacío | ⚠ ninguno | Base del enlace «Ver trámite». Vacía = relativo. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DP_CERT_PASSWORD` | `Diagnostico__DataProtection__CertificadoClave` | sí | sí | vacío | `.env.prod.example` | Clave del `.pfx` de Data Protection. Sin certificado válido no arranca. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_FILE_MANAGER_CATEGORY` | `FILE_MANAGER_CATEGORY` | no | no | `diagnostico` | `.env.prod.example` | Categoría de Diagnóstico en el file-manager. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IA_CLAVE_PRIVADA` | `Diagnostico__Ia__ClavePrivada` | no | sí | vacío | `.env.prod.example` | Privada RSA (base64 DER PKCS#8) que descifra la llave de IA. Vacía = dictamen con plantilla. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IA_CLAVE_PRIVADA_ANTERIOR` | `Diagnostico__Ia__ClavePrivadaAnterior` | no | sí | vacío | `.env.prod.example` | Privada anterior, solo durante una rotación. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IA_KID` | `Diagnostico__Ia__Kid` | no | no | vacío | `.env.prod.example` | Kid actual (`diag-<amb>-<aaaamm>`). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IA_KID_ANTERIOR` | `Diagnostico__Ia__KidAnterior` | no | no | vacío | `.env.prod.example` | Kid anterior, solo durante una rotación. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IA_ORIGEN` | `Diagnostico__Ia__Origen` | no | no | `plataforma` | `.env.prod.example` | Origen de la configuración de IA. Default `plataforma`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IDENTIDAD_ADDRESS` | `Diagnostico__Identidad__Address` | sí | no | vacío | `.env.prod.example` | gRPC de Identidad (`http://core-identity:8083`). Vacío = 403 para todas las empresas. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IMPUESTO_DISPONIBLE` | `Diagnostico__Fuentes__ImpuestoDisponible` | no | no | `false` | ⚠ ninguno | Ofrece la fuente de impuesto en Diagnóstico. | [§ Impuesto](#4-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `DIAGNOSTICO_REDACCION_HABILITADO` | `Diagnostico__Redaccion__Habilitado` | no | no | `false` | `.env.prod.example` | Cola de redacción IA del dictamen. En PDN la decide el PO. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FILE_MANAGER_AUTH_TOKEN` | mismo nombre | condicional | sí | vacío | ⚠ ninguno | Bearer del file-manager. Mismo valor en core-api, core-ict y productos. Obligatorio con Contabo (PDN). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FILE_MANAGER_BASE_URL` | mismo nombre | no | no | `https://ny46zhyeti.execute-api.us-east-1.amazonaws.com/pdn/` | `.env.prod.example` | URL del file-manager de adjuntos. Misma en core-api, core-ict, migrador y productos. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FILE_MANAGER_FILES_PATH` | mismo nombre | no | no | `api/v1/files` | ⚠ ninguno | Ruta de archivos del file-manager. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FILE_MANAGER_TIMEOUT_SECONDS` | mismo nombre | no | no | `30` | ⚠ ninguno | Timeout del file-manager. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FLIT_HUB_URL` | `Platform__Auth__Issuers__0` | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `RABBITMQ_URL_DIAGNOSTICO` | `Platform__Messaging__ConnectionString` | sí (aborta) | sí | — (aborta) | `.env.prod.example` | AMQP del usuario `diagnostico`. Sin ella el compose aborta, aunque el perfil esté apagado. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `SVC_DIAGNOSTICO_CLIENT_SECRET` | `Platform__ServiceClient__ClientSecret` | sí | sí | vacío | `.env.prod.example` | Secreto de `svc-diagnostico`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |

### core-comparendos (perfil `comparendos`)

25 variables: 18 en la tabla y 7 transversales (`CORE_COMPARENDOS_CPUS`, `CORE_COMPARENDOS_MEM_LIMIT`, `CORE_COMPARENDOS_PORT`, `CORE_COMPARENDOS_TAG`, `FLIT_DOTNET_ENVIRONMENT`, `GATEWAY_PORT`, `OTEL_EXPORTER_OTLP_ENDPOINT`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `COMPARENDOS_FILE_MANAGER_CATEGORY` | `FILE_MANAGER_CATEGORY` | no | no | `comparendos` | `.env.prod.example` | Categoría de Comparendos en el file-manager. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `COMPARENDOS_IDENTIDAD_ADDRESS` | `Comparendos__Identidad__Address` | sí | no | vacío | `.env.prod.example` | gRPC de Identidad (`http://core-identity:8083`). Vacío = ninguna empresa habilitada. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `COMPARENDOS_OCR_API_KEY` | `Comparendos__Ocr__ApiKey` | no | sí | vacío | `.env.prod.example` | Llave de Anthropic para el OCR de Comparendos. Vacía = OCR simulado. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `COMPARENDOS_SIMULACION` | `Comparendos__Simulacion__Habilitada` | no | no | vacío | `.env.prod.example` | Modo simulado. Vacío = solo en Development. Nunca en QA/PDN. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `COMPARENDOS_TRAMITES_ADDRESS` | `Comparendos__Tramites__Address` | no | no | `http://core-api:${CORE_API_GRPC_PORT:-8082}` | ⚠ ninguno | gRPC de catálogos de core-api (réplica de organismos). | [comparendos](../suite/handoff-vps-comparendos.md) |
| `COMPARENDOS_URL` | `Comparendos__AppUrl` | no | no | `https://dev.comparendos.flitsas.online` | `.env.prod.example` | URL pública de la app de Comparendos (enlaces de correos). | [comparendos](../suite/handoff-vps-comparendos.md) |
| `CONNECTION_STRING_COMPARENDOS` | `ConnectionStrings__Servicio` | sí | sí | vacío | `.env.prod.example` | Base del usuario dueño del esquema `comparendos`. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `CONSULTAS_REMOTO_ADDRESS` | `Comparendos__Consultas__Address` | no | no | `http://core-consultas:8084` | `.env.prod.example` | gRPC de core-consultas para Trámites, Persona Natural y Comparendos. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `CORE_API_GRPC_PORT` | `Comparendos__Tramites__Address` | no | no | `8082` | ⚠ ninguno | Puerto gRPC interno de core-api (ICT, Diagnóstico, Comparendos, Persona Natural). Igual en los tres ambientes. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `CORE_COMPARENDOS_GRPC_PORT` | `Servicio__GrpcPort` | no | no | `8086` | `.env.prod.example` | Puerto gRPC interno de core-comparendos. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `FILE_MANAGER_AUTH_TOKEN` | mismo nombre | condicional | sí | vacío | ⚠ ninguno | Bearer del file-manager. Mismo valor en core-api, core-ict y productos. Obligatorio con Contabo (PDN). | [comparendos](../suite/handoff-vps-comparendos.md) |
| `FILE_MANAGER_BASE_URL` | mismo nombre | sí | no | vacío | `.env.prod.example` | URL del file-manager de adjuntos. Misma en core-api, core-ict, migrador y productos. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `FILE_MANAGER_FILES_PATH` | mismo nombre | no | no | `api/v1/files` | ⚠ ninguno | Ruta de archivos del file-manager. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `FILE_MANAGER_TIMEOUT_SECONDS` | mismo nombre | no | no | `60` | ⚠ ninguno | Timeout del file-manager. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `FLIT_HUB_URL` | `Platform__Auth__Issuers__0` | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [comparendos](../suite/handoff-vps-comparendos.md) |
| `NOTIFICACIONES_REMOTO_ADDRESS` | `Comparendos__Notificaciones__Address` | no | no | `http://core-notificaciones:8085` | `.env.prod.example` | gRPC de core-notificaciones. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `RABBITMQ_URL_COMPARENDOS` | `Platform__Messaging__ConnectionString` | sí | sí | vacío | `.env.prod.example` | AMQP del usuario `comparendos`. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `SVC_COMPARENDOS_CLIENT_SECRET` | `Platform__ServiceClient__ClientSecret` | sí | sí | vacío | `.env.prod.example` | Secreto del cliente de servicio `svc-comparendos` (registro en Identidad y uso en el servicio). | [comparendos](../suite/handoff-vps-comparendos.md) |

### core-personanatural (perfil `personanatural`)

21 variables: 14 en la tabla y 7 transversales (`CORE_PERSONANATURAL_CPUS`, `CORE_PERSONANATURAL_MEM_LIMIT`, `CORE_PERSONANATURAL_PORT`, `CORE_PERSONANATURAL_TAG`, `FLIT_DOTNET_ENVIRONMENT`, `GATEWAY_PORT`, `OTEL_EXPORTER_OTLP_ENDPOINT`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `CONNECTION_STRING_PERSONANATURAL` | `ConnectionStrings__Servicio` | sí | sí | vacío | `.env.prod.example` | Base del usuario dueño del esquema `personanatural`. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `CONSULTAS_REMOTO_ADDRESS` | `Consultas__Remoto__Address` | no | no | `http://core-consultas:8084` | `.env.prod.example` | gRPC de core-consultas para Trámites, Persona Natural y Comparendos. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `CORE_API_GRPC_PORT` | `Cotizaciones__CoreApiAddress` | no | no | `8082` | ⚠ ninguno | Puerto gRPC interno de core-api (ICT, Diagnóstico, Comparendos, Persona Natural). Igual en los tres ambientes. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `CORE_PERSONANATURAL_GRPC_PORT` | `Servicio__GrpcPort` | no | no | `8087` | `.env.prod.example` | Puerto gRPC interno de core-personanatural. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `CORS_ORIGIN` | `Notifications__EmailAssets__BaseUrl` | sí (aborta) | no | — (aborta) | `.env.prod.example` | URL pública del front del ambiente, sin barra final. CORS y enlaces de los correos. Sin ella el compose aborta. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `FLIT_HUB_URL` | `Platform__Auth__Issuers__0` | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [personanatural](../suite/handoff-vps-personanatural.md) |
| `NOTIFICACIONES_REMOTO_ADDRESS` | `Cotizaciones__NotificacionesAddress` | no | no | `http://core-notificaciones:8085` | `.env.prod.example` | gRPC de core-notificaciones. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `PERSONANATURAL_CAPTCHA_PROVEEDOR` | `Captcha__Proveedor` | no | no | `turnstile` | `.env.prod.example` | `turnstile` o `recaptcha`. Nunca `deshabilitado` en un servidor. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `PERSONANATURAL_CAPTCHA_SECRET` | `Captcha__SecretKey` | sí | sí | vacío | `.env.prod.example` | Secreto del sitio del captcha. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `PERSONANATURAL_CORE_API_ADDRESS` | `Cotizaciones__CoreApiAddress` | no | no | `http://core-api:${CORE_API_GRPC_PORT:-8082}` | ⚠ ninguno | gRPC de core-api para catálogo y cotización. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `PERSONANATURAL_PII_AES_KEY` | `Pii__Cifrado__Llaves__1` | sí | sí | vacío | `.env.prod.example` | Llave AES-256 (base64 de 32 bytes) de los datos personales. Perderla deja ilegible lo guardado. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `PERSONANATURAL_PII_HMAC_KEY` | `Pii__ClaveHmac` | sí | sí | vacío | `.env.prod.example` | Llave HMAC de la huella de cédula e IP. Mínimo 32 caracteres. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `RABBITMQ_URL_PERSONANATURAL` | `Platform__Messaging__ConnectionString` | sí | sí | vacío | `.env.prod.example` | AMQP del usuario `personanatural`. | [personanatural](../suite/handoff-vps-personanatural.md) |
| `SVC_PERSONANATURAL_CLIENT_SECRET` | `Platform__ServiceClient__ClientSecret` | sí | sí | vacío | `.env.prod.example` | Secreto de `svc-personanatural`. | [personanatural](../suite/handoff-vps-personanatural.md) |

### core-ict

20 variables: 13 en la tabla y 7 transversales (`CORE_API_PORT`, `CORE_ICT_CPUS`, `CORE_ICT_MEM_LIMIT`, `CORE_ICT_PORT`, `CORE_ICT_TAG`, `FLIT_DOTNET_ENVIRONMENT`, `GATEWAY_PORT`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `CONNECTION_STRING_CORE` | `ConnectionStrings__Core` | sí | sí | — | `.env.prod.example` | Cadena Npgsql de la base principal (Trámites, Identidad, ICT, migrador). Sin ella no arranca. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `CONSULTAS_REMOTO_DEADLINE_SEGUNDOS` | `CoreConsultas__DeadlineSegundos` | no | no | `180` | `.env.prod.example` | Deadline gRPC hacia core-consultas. Debe ser ≥ 120. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `CORE_API_GRPC_PORT` | `CoreApiGrpc__Address` | no | no | `8082` | ⚠ ninguno | Puerto gRPC interno de core-api (ICT, Diagnóstico, Comparendos, Persona Natural). Igual en los tres ambientes. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `FILE_MANAGER_AUTH_TOKEN` | `FileManager__AuthToken` | condicional | sí | vacío | ⚠ ninguno | Bearer del file-manager. Mismo valor en core-api, core-ict y productos. Obligatorio con Contabo (PDN). | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `FILE_MANAGER_BASE_URL` | `FileManager__BaseUrl` | no | no | `https://ny46zhyeti.execute-api.us-east-1.amazonaws.com/pdn/` | `.env.prod.example` | URL del file-manager de adjuntos. Misma en core-api, core-ict, migrador y productos. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `FLIT_SWAGGER_ENABLED` | `Swagger__Enabled` | no | no | `true` | `.env.prod.example` | `/swagger` público. `false` en PDN. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `ICT_CORE_CONSULTAS_ADDRESS` | `CoreConsultas__Address` | no | no | `http://core-consultas:8084` | `.env.prod.example` | gRPC de core-consultas para ICT. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `ICT_IDENTITY_TOKEN_ENDPOINT` | `Ict__ServiceToken__TokenEndpoint` | no | no | `http://gateway:${GATEWAY_PORT:-4002}/connect/token` | `.env.prod.example` | Endpoint de token de Identidad para `svc-ict`. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `ICT_JWT_JWKS_URI` | `Jwt__JwksUri` | no | no | lista larga (ver compose L902) | ⚠ ninguno | JWKS interno de core-api para validar tokens de usuario. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `ICT_RELAX_REGISTER_COMMIT_DURABILITY` | `Ingest__RelaxRegisterCommitDurability` | no | no | `false` | ⚠ ninguno | `synchronous_commit=off` en el registro. Solo pruebas de carga. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `ICT_SERVICE_TOKEN_SECRET` | `Ict__ServiceToken__Secret` | condicional | sí | vacío | ⚠ ninguno | Secreto HMAC del canal gRPC core-api ↔ core-ict. Vacío = fail-closed. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `JWT_VALIDATE_ISSUED_TOKENS` | `Jwt__ValidateIssuedTokens` | no | no | `true` | `.env.prod.example` | Valida firma, emisor, audiencia y vencimiento. `false` solo como vuelta atrás. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |
| `SVC_ICT_CLIENT_SECRET` | `Ict__ServiceToken__ClientSecret` | sí | sí | vacío | `.env.prod.example` | Secreto de `svc-ict`. Sin él, core-ict no consulta en core-consultas. | [consultas-notif.](../suite/handoff-vps-consultas-notificaciones.md) |

### migrador y migracion-api

23 variables: 19 en la tabla y 4 transversales (`CORE_API_TAG`, `MIGRACION_API_CPUS`, `MIGRACION_API_MEM_LIMIT`, `MIGRACION_API_PORT`).

Ancla `x-flitmig-env`: una sola definición para los dos consumidores. Las tres variables de origen (`FLITMIG_V1_SOURCE`, `FLITMIG_SOURCE_FM_BASE_URL`, `FLITMIG_V1_SNAPSHOT_BASE_URL`) apuntan al **mismo** ambiente de V1.

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `CONNECTION_STRING_CORE` | `FLITMIG_ConnectionStrings__V2Target` | sí | sí | — | `.env.prod.example` | Cadena Npgsql de la base principal (Trámites, Identidad, ICT, migrador). Sin ella no arranca. | `.env.prod.example` (bloque FLITMIG_) |
| `FILE_MANAGER_BASE_URL` | `FLITMIG_TargetFileManager__BaseUrl` | condicional | no | — | `.env.prod.example` | URL del file-manager de adjuntos. Misma en core-api, core-ict, migrador y productos. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_ATTACHMENTS_MODE` | `FLITMIG_Attachments__Mode` | no | no | `Copy` | ⚠ ninguno | `Copy` o `Reference` para adjuntos. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_BATCH_ID` | `FLITMIG_Migration__BatchId` | no | no | `sin-lote` | `.env.prod.example` | Lote por defecto. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_MIGRACION_API_ENABLED` | `FLITMIG_MigracionApi__Enabled` | no | no | `false` | `.env.prod.example` | Registra las rutas de la API de migración. `false` = 404. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_MIGRACION_API_KEY` | `FLITMIG_MigracionApi__ApiKey` | condicional | sí | vacío | `.env.prod.example` | Cabecera `X-Migration-Key`: única cerradura de la API de migración. También la usa el front. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_MIGRACION_API_MAX_CONCURRENT` | `FLITMIG_MigracionApi__MaxConcurrentRuns` | no | no | `2` | `.env.prod.example` | Migraciones a la vez. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_SOURCE_FM_AUTH_TOKEN` | `FLITMIG_SourceFileManager__AuthToken` | no | sí | vacío | `.env.prod.example` | Bearer del file-manager de V1. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_SOURCE_FM_BASE_URL` | `FLITMIG_SourceFileManager__BaseUrl` | condicional | no | — | `.env.prod.example` | File-manager de V1 (mismo ambiente que V1Source). | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_SOURCE_FM_FILES_PATH` | `FLITMIG_SourceFileManager__FilesPath` | no | no | `api/v1/files` | ⚠ ninguno | Ruta de archivos de V1. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_SYSTEM_USER_EMAIL` | `FLITMIG_Migration__SystemUserEmail` | no | no | `migracion.v1@flitsas.io` | ⚠ ninguno | Usuario de sistema de la migración. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_TARGET_FM_AUTH_TOKEN` | `FLITMIG_TargetFileManager__AuthToken` | no | sí | vacío | `.env.prod.example` | Bearer del file-manager destino. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_TARGET_FM_FILES_PATH` | `FLITMIG_TargetFileManager__FilesPath` | no | no | `api/v1/files` | ⚠ ninguno | Ruta de archivos destino. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_V1_SNAPSHOT_AUTH_TOKEN` | `FLITMIG_V1Snapshot__AuthToken` | no | sí | vacío | `.env.prod.example` | Token de la API de snapshot de V1. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_V1_SNAPSHOT_BASE_URL` | `FLITMIG_V1Snapshot__BaseUrl` | no | no | vacío | `.env.prod.example` | API de snapshot de V1 (mismo ambiente). | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_V1_SNAPSHOT_CONSOLIDATED` | `FLITMIG_V1Snapshot__Consolidated` | no | no | `auto` | ⚠ ninguno | Consolidado en el snapshot. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_V1_SNAPSHOT_INCLUDE` | `FLITMIG_V1Snapshot__Include` | no | no | `generated` | ⚠ ninguno | Qué documentos trae el snapshot. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_V1_SOURCE` | `FLITMIG_ConnectionStrings__V1Source` | condicional | sí | — | `.env.prod.example` | Cadena de la base V1 (solo lectura). Mismo ambiente que el file-manager y el snapshot. | `.env.prod.example` (bloque FLITMIG_) |
| `FLITMIG_V2_TARGET` | `FLITMIG_ConnectionStrings__V2Target` | no | sí | `${CONNECTION_STRING_CORE}` | `.env.prod.example` | Base destino. Default = `CONNECTION_STRING_CORE`. | `.env.prod.example` (bloque FLITMIG_) |

### gateway (YARP)

21 variables: 6 en la tabla y 15 transversales (`CORE_API_PORT`, `CORE_API_TAG`, `CORE_COMPARENDOS_PORT`, `CORE_CONSULTAS_PORT`, `CORE_DIAGNOSTICO_PORT`, `CORE_ICT_PORT`, `CORE_IDENTITY_PORT`, `CORE_PERSONANATURAL_PORT`, `FLIT_DOTNET_ENVIRONMENT`, `GATEWAY_CPUS`, `GATEWAY_MEM_LIMIT`, `GATEWAY_PORT`, `MIGRACION_API_PORT`, `OTEL_EXPORTER_OTLP_ENDPOINT`, `PYTHON_ML_PORT`).

Además fija el CIDR de la red del stack: `FLIT_INTERNAL_SUBNET` (bloque `networks`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `CORS_ORIGIN` | `Cors__AllowedOrigins__0` | sí (aborta) | no | — | `.env.prod.example` | URL pública del front del ambiente, sin barra final. CORS y enlaces de los correos. Sin ella el compose aborta. | [suite](../suite/handoff-vps-suite.md) |
| `CORS_ORIGIN_ALT` | `Cors__AllowedOrigins__2` | no | no | vacío | ⚠ ninguno | Tercer origen CORS (en PDN `https://app.flitsas.com`). Vacío en DEV/QA. | [suite](../suite/handoff-vps-suite.md) |
| `CORS_ORIGIN_MARCA_BLANCA` | `Cors__AllowedOrigins__1` | no | no | `https://marcablancadev.flitsas.online` | ⚠ ninguno | Segundo origen CORS: dominio de marca blanca del ambiente. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_IDENTITY_CLUSTER_ENABLED` | `Gateway__IdentityCluster__Enabled` | no | no | `false` | ⚠ ninguno | Enruta el login a core-identity. Exige el perfil `identity` arriba. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_INTERNAL_API_KEY` | `Internal__ApiKey` | condicional | sí | vacío | `.env.prod.example`, `frontend/.env.example` | Clave interna gateway ↔ core-api ↔ fronts (dominios de Marca Blanca). Vacía = fail-closed. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_INTERNAL_SUBNET` | `DomainSeal__InternalAllowedNetworks__0` | condicional | no | `10.114.40.0/24` | `.env.prod.example` | CIDR de la red interna del stack. Distinto por ambiente: DEV 10.114.40.0/24 · QA .50 · PDN .60. | [suite](../suite/handoff-vps-suite.md) |

### rabbitmq (broker)

4 variables: 2 en la tabla y 2 transversales (`RABBITMQ_MEM_LIMIT`, `RABBITMQ_MGMT_PORT`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `RABBITMQ_ADMIN_PASSWORD` | `RABBITMQ_DEFAULT_PASS` | sí | sí | vacío | `.env.prod.example` | Clave del administrador del broker. | [consultas-notif. §2](../suite/handoff-vps-consultas-notificaciones.md) |
| `RABBITMQ_ADMIN_USER` | `RABBITMQ_DEFAULT_USER` | no | no | `flit-admin` | `.env.prod.example` | Usuario administrador del broker. | [consultas-notif. §2](../suite/handoff-vps-consultas-notificaciones.md) |

### python-ml

4 variables: 0 en la tabla y 4 transversales (`PYTHON_ML_CPUS`, `PYTHON_ML_MEM_LIMIT`, `PYTHON_ML_PORT`, `PYTHON_ML_TAG`).

Solo variables transversales.

### frontend (Trámites, Next.js)

11 variables: 6 en la tabla y 5 transversales (`FRONTEND_CPUS`, `FRONTEND_MEM_LIMIT`, `FRONTEND_PORT`, `FRONTEND_TAG`, `GATEWAY_PORT`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `FLITMIG_MIGRACION_API_KEY` | `MIGRACION_API_KEY` | no | sí | vacío | `.env.prod.example` | Cabecera `X-Migration-Key`: única cerradura de la API de migración. También la usa el front. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_HUB_URL` | mismo nombre | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_INTERNAL_API_KEY` | mismo nombre | condicional | sí | vacío | `.env.prod.example`, `frontend/.env.example` | Clave interna gateway ↔ core-api ↔ fronts (dominios de Marca Blanca). Vacía = fail-closed. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_NETWORK_DOMAINS_ENABLED` | mismo nombre | no | no | `false` | `.env.prod.example` | Login por el hub de la red en dominios de Marca Blanca. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_SESSION_MODE` | mismo nombre | no | no | `legacy` | `.env.prod.example` | Sesión de Trámites: `legacy` u `oidc`. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_SESSION_SECRET` | mismo nombre | no | sí | vacío | `.env.prod.example` | Cifra la cookie de sesión (front y hub). ≥ 32 caracteres, distinta por ambiente. | [suite](../suite/handoff-vps-suite.md) |

### frontend-hub (perfil `suite`)

12 variables: 7 en la tabla y 5 transversales (`FRONTEND_HUB_CPUS`, `FRONTEND_HUB_MEM_LIMIT`, `FRONTEND_HUB_TAG`, `GATEWAY_PORT`, `HUB_PORT`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `FLIT_HOSTS` | mismo nombre | no | no | lista larga (ver compose L1250) | `.env.prod.example` | Hosts FLIT sin marca de red. Igual que `NEXT_PUBLIC_FLIT_HOSTS` del build. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_HUB_URL` | mismo nombre | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_INTERNAL_API_KEY` | mismo nombre | condicional | sí | vacío | `.env.prod.example`, `frontend/.env.example` | Clave interna gateway ↔ core-api ↔ fronts (dominios de Marca Blanca). Vacía = fail-closed. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_NETWORK_DOMAINS_ENABLED` | mismo nombre | no | no | `false` | `.env.prod.example` | Login por el hub de la red en dominios de Marca Blanca. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_SESSION_SECRET` | mismo nombre | sí | sí | vacío | `.env.prod.example` | Cifra la cookie de sesión (front y hub). ≥ 32 caracteres, distinta por ambiente. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_TRAMITES_HOST_ENABLED` | mismo nombre | no | no | `false` | `.env.prod.example` | El hub ocupa la raíz y redirige Trámites a su host. | [suite](../suite/handoff-vps-suite.md) |
| `FLIT_TRAMITES_URL` | `TRAMITES_URL` | no | no | `https://dev.tramites.flitsas.online` | `.env.prod.example` | URL pública de Trámites. | [suite](../suite/handoff-vps-suite.md) |

### frontend-comparendos (perfil `comparendos`)

10 variables: 5 en la tabla y 5 transversales (`COMPARENDOS_PORT`, `FRONTEND_COMPARENDOS_CPUS`, `FRONTEND_COMPARENDOS_MEM_LIMIT`, `FRONTEND_COMPARENDOS_TAG`, `GATEWAY_PORT`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `COMPARENDOS_SESSION_SECRET` | `FLIT_SESSION_SECRET` | sí | sí | vacío | `.env.prod.example` | Cifra la cookie de sesión de Comparendos. ≥ 32 caracteres, distinta del hub. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `FLIT_HOSTS` | mismo nombre | no | no | lista larga (ver compose L1285) | `.env.prod.example` | Hosts FLIT sin marca de red. Igual que `NEXT_PUBLIC_FLIT_HOSTS` del build. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `FLIT_HUB_URL` | mismo nombre | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [comparendos](../suite/handoff-vps-comparendos.md) |
| `FLIT_INTERNAL_API_KEY` | mismo nombre | condicional | sí | vacío | `.env.prod.example`, `frontend/.env.example` | Clave interna gateway ↔ core-api ↔ fronts (dominios de Marca Blanca). Vacía = fail-closed. | [comparendos](../suite/handoff-vps-comparendos.md) |
| `FLIT_TRAMITES_URL` | `TRAMITES_URL` | no | no | `https://dev.tramites.flitsas.online` | `.env.prod.example` | URL pública de Trámites. | [comparendos](../suite/handoff-vps-comparendos.md) |

### frontend-diagnostico (perfil `diagnostico`)

10 variables: 5 en la tabla y 5 transversales (`DIAGNOSTICO_PORT`, `FRONTEND_DIAGNOSTICO_CPUS`, `FRONTEND_DIAGNOSTICO_MEM_LIMIT`, `FRONTEND_DIAGNOSTICO_TAG`, `GATEWAY_PORT`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `DIAGNOSTICO_SESSION_SECRET` | `FLIT_SESSION_SECRET` | sí | sí | vacío | `.env.prod.example` | Cifra la cookie de sesión del front de Diagnóstico. ≥ 32 caracteres, distinta del hub. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FLIT_HOSTS` | mismo nombre | no | no | lista larga (ver compose L1318) | `.env.prod.example` | Hosts FLIT sin marca de red. Igual que `NEXT_PUBLIC_FLIT_HOSTS` del build. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FLIT_HUB_URL` | mismo nombre | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FLIT_INTERNAL_API_KEY` | mismo nombre | condicional | sí | vacío | `.env.prod.example`, `frontend/.env.example` | Clave interna gateway ↔ core-api ↔ fronts (dominios de Marca Blanca). Vacía = fail-closed. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FLIT_TRAMITES_URL` | `TRAMITES_URL` | no | no | `https://dev.tramites.flitsas.online` | `.env.prod.example` | URL pública de Trámites. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |

### frontend-personanatural (perfil `personanatural`)

5 variables: 0 en la tabla y 5 transversales (`FRONTEND_PERSONANATURAL_CPUS`, `FRONTEND_PERSONANATURAL_MEM_LIMIT`, `FRONTEND_PERSONANATURAL_TAG`, `GATEWAY_PORT`, `PERSONANATURAL_PORT`).

Solo variables transversales.

### observabilidad (perfil `observabilidad`)

3 variables: 0 en la tabla y 3 transversales (`OBSERVABILIDAD_CPUS`, `OBSERVABILIDAD_GRAFANA_PORT`, `OBSERVABILIDAD_MEM_LIMIT`).

Solo variables transversales.

---

## 4. Impuesto vehicular (fuente real, ola HU #14681)

> ⚠ **Encender el impuesto real en DEV, QA o PDN exige aval de Seguridad y de Legal** (también Habeas Data; ver
> [despliegue-develop-diagnostico.md §9, punto 5](../suite/despliegue-develop-diagnostico.md)). Sin esos avales, las
> variables `IMPUESTO_API_*` e `IMPUESTO_CONTACTO_*` quedan **vacías** en el servidor.

Verificado contra `services/core-consultas/src/Flit.Consultas.Api/Impuesto/OpcionesImpuesto.cs`,
`services/core-consultas/src/Flit.Consultas.Api/Program.cs:135-180` y `docker-compose.prod.yml:726-751`.

### 4.1 Qué decide simulado o real

No hay una variable «modo». El modo sale de la simulación de core-consultas:

| Modo | Condición | Transporte | Credencial |
|---|---|---|---|
| Simulado | `Consultas__Simulacion__Habilitada=true`, o `Impuesto` en `Consultas__Simulacion__Fuentes`, con `Consultas__Ambiente` = `LOCAL` o `DEV` | Fixtures embebidos, nada sale a internet | Llave simulada |
| Real | Sin simulación (siempre en QA y PDN) | HTTP a `IMPUESTO_API_BASE_URL` + `IMPUESTO_API_PATH` | La de la compañía, por gRPC de Identidad |
| Real no listo | Real sin base o sin ruta, o sin `CONSULTAS_IDENTIDAD_ADDRESS` (`TransporteImpuestoHttp.cs:26`) | No consulta | — |

- ⚠ Las claves `Consultas__Simulacion__*` y `Consultas__Ambiente` **no están en el compose**. En QA y PDN no se ponen: la
  simulación solo se admite en LOCAL y DEV; en otro ambiente el servicio no arranca.
- Si se definen `IMPUESTO_API_BASE_URL` e `IMPUESTO_API_PATH` sin simulación, los departamentos activos en la tabla
  `consultas.vehicle_tax_departments` (la semilla enciende 50, 52 y 94) quedan en real **de inmediato**.
- En Diagnóstico la fuente solo se ofrece con `DIAGNOSTICO_IMPUESTO_DISPONIBLE=true` y `DIAGNOSTICO_CONSULTAS_ADDRESS`
  definida.

### 4.2 Variables de core-consultas

| Variable | Default | Obligatoria | Secreta | Uso |
|---|---|---|---|---|
| `IMPUESTO_API_BASE_URL` | vacío | condicional (real) | no | URL base de la fuente. Vacía = no listo |
| `IMPUESTO_API_PATH` | vacío en el compose (`api/v1/tax/detail` en el `.example`) | condicional (real) | no | Ruta. Se une a la base sin barras dobles |
| `IMPUESTO_API_TIMEOUT_SECONDS` | `280` | no | no | Presupuesto **total** del sondeo real. Con `>= 300` core-consultas **no arranca**. Cada petición HTTP tiene 30 s fijos |
| `Consultas__Simulacion__ImpuestoPresupuestoSegundos` | `3` (en código) | no | no | Presupuesto del sondeo simulado. ⚠ No está en el compose ni en un `.example` |
| `CONSULTAS_IDENTIDAD_ADDRESS` → `Consultas__Identidad__Address` | vacío | condicional (real) | no | gRPC de Identidad para la credencial: `http://core-identity:8083` |
| `IMPUESTO_CONTACTO_*` (18) | vacío | condicional (real) | sensible | Contacto fijo de respaldo del paso 3. Valores solo en el `.env` o sealed-secret del ambiente |

Las 18 de contacto, en el orden del código (`OpcionesImpuesto.cs`, `LeerContacto`): `CELULAR`, `TELEFONO`, `CORREO`,
`DIRECCION_DEPARTAMENTO`, `DIRECCION_CIUDAD`, `TIPO_VIA`, `NUMERO_1`, `LETRA_1`, `BIS_1`, `SUFIJO_1`, `CARDINALIDAD_1`,
`NUMERO_2`, `LETRA_2`, `BIS_2`, `SUFIJO_2`, `PLACA`, `CARDINALIDAD_2`, `DIRECCION_COMPLETA`. `BIS_1` y `BIS_2` son `true`
o `false`; cualquier otro valor cuenta como `false`.

La lista de departamentos ya **no** es una variable: `IMPUESTO_DEPARTAMENTOS` se retiró (ADR-0080). Se encienden y apagan
en la tabla `consultas.vehicle_tax_departments`.

### 4.3 La API key no es una variable

La API key del impuesto es **por compañía**. No va en ningún `.env`, configmap ni sealed-secret.

1. El SuperAdmin la configura en **Admin › Compañías › Productos** (producto Diagnóstico). Se puede configurar,
   reemplazar y borrar; la UI nunca muestra el valor.
2. core-api la guarda cifrada con Data Protection (propósito `Flit.Platform.TenantProductCredential.v1` + empresa +
   código `impuesto_api_key`). El anillo de llaves vive en la base principal (`CONNECTION_STRING_CORE`): no hay variable
   de cifrado propia. core-api y core-identity comparten anillo porque comparten base y nombre de aplicación.
3. core-consultas la pide por gRPC a core-identity (`CredencialesProductoService.ObtenerCredencialImpuesto`). Solo atiende
   al cliente `svc-consultas` con el scope `platform.identidad.credenciales.impuesto`, y solo si Diagnóstico está
   encendido en la empresa y en sus ancestros. Caché: 300 s con llave, 60 s sin llave.

Lo que sí hay que configurar para que la entrega funcione:

| Qué | Dónde | Variable o clave |
|---|---|---|
| Perfil `identity` arriba con gRPC | core-identity | `COMPOSE_PROFILES=…,identity` y `CORE_IDENTITY_GRPC_PORT=8083` (`Identidad__GrpcPort`) |
| Dirección del gRPC de Identidad | core-consultas | `CONSULTAS_IDENTIDAD_ADDRESS=http://core-identity:8083` (`Consultas__Identidad__Address`) |
| Cliente de servicio `svc-consultas` | core-api y core-identity (registro) + core-consultas (uso) | `SVC_CONSULTAS_CLIENT_SECRET` → `Suite__Oidc__ServiceClients__svc-consultas__Secret` y `Platform__ServiceClient__ClientSecret`. Mismo valor en los dos lados |
| Scope del cliente | `appsettings.json` de core-api/core-identity | `svc-consultas` ya trae `platform.identidad.credenciales.impuesto`. No es variable |
| Producto Diagnóstico encendido | Admin › Compañías › Productos | Dato de la base, no variable |

### 4.4 Lado Diagnóstico

| Variable | Default | Uso |
|---|---|---|
| `DIAGNOSTICO_IMPUESTO_DISPONIBLE` → `Diagnostico__Fuentes__ImpuestoDisponible` | `false` | Ofrece la fuente de impuesto. ⚠ No está en ningún `.example` |
| `DIAGNOSTICO_CONSULTAS_ADDRESS` → `Diagnostico__Consultas__Address` | vacío | gRPC de core-consultas. Enciende juntas todas las fuentes reales |

---

## 5. Desfases encontrados (⚠)

Corrida de `python scripts/dev/variables-entorno.py --desfases` sobre `ded2112ef`, más revisión manual.

### 5.1 En el compose y en ningún `.example`

| Servicio(s) | Variables sin `.example` |
|---|---|
| core-api | `JWT_PERSIST_SIGNING_KEY`, `TRAMITES_DERIVACION_CONCURRENCIA_MAXIMA`, `TRAMITES_DERIVACION_HABILITADA`, `TRAMITE_VALIDATION_BODY_TYPE_MODE`, `TRAMITE_VALIDATION_DUPLICATE_MODE`, `TRAMITE_VALIDATION_MANDATARIO_MODE`, `TRAMITE_VALIDATION_VEHICLE_STATE_MODE` |
| core-api, core-comparendos, core-diagnostico, core-ict | `FILE_MANAGER_AUTH_TOKEN` |
| core-api, core-comparendos, core-diagnostico, core-ict, core-personanatural | `CORE_API_GRPC_PORT` |
| core-api, core-ict | `ICT_SERVICE_TOKEN_SECRET` |
| core-comparendos | `COMPARENDOS_TRAMITES_ADDRESS`, `CORE_COMPARENDOS_CPUS`, `CORE_COMPARENDOS_MEM_LIMIT`, `CORE_COMPARENDOS_TAG` |
| core-comparendos, core-diagnostico | `FILE_MANAGER_FILES_PATH`, `FILE_MANAGER_TIMEOUT_SECONDS` |
| core-consultas | `INTEMPO_MODE`, `RUES_AUTH_SCHEME`, `RUES_TIMEOUT_SECONDS`, `VERIFIK_CONDUCTOR_MODE`, `VERIFIK_RNMC_MODE`, `VERIFIK_RUES_MODE`, `VERIFIK_SIMIT_MODE`, `VERIFIK_VEHICLE_MODE` |
| core-diagnostico | `CORE_DIAGNOSTICO_TAG`, `DIAGNOSTICO_AMBIENTE`, `DIAGNOSTICO_CONSULTAS_ADDRESS`, `DIAGNOSTICO_DERIVACION_CONCURRENCIA`, `DIAGNOSTICO_DERIVACION_CREDITO_POR_COMPANIA`, `DIAGNOSTICO_DERIVACION_HABILITADA`, `DIAGNOSTICO_DERIVACION_PREFETCH`, `DIAGNOSTICO_DERIVACION_TASA_POR_SEGUNDO`, `DIAGNOSTICO_DERIVACION_URL_TRAMITES`, `DIAGNOSTICO_IMPUESTO_DISPONIBLE` |
| core-ict | `ICT_JWT_JWKS_URI`, `ICT_RELAX_REGISTER_COMMIT_DURABILITY` |
| core-notificaciones | `CORE_NOTIFICACIONES_TAG`, `SMTP_CHECK_CERT_REVOCATION` |
| core-personanatural | `PERSONANATURAL_CORE_API_ADDRESS` |
| frontend-comparendos | `FRONTEND_COMPARENDOS_CPUS`, `FRONTEND_COMPARENDOS_MEM_LIMIT`, `FRONTEND_COMPARENDOS_TAG` |
| frontend-diagnostico | `FRONTEND_DIAGNOSTICO_TAG` |
| gateway | `CORS_ORIGIN_ALT`, `CORS_ORIGIN_MARCA_BLANCA`, `FLIT_IDENTITY_CLUSTER_ENABLED` |
| migrador/migracion-api | `FLITMIG_ATTACHMENTS_MODE`, `FLITMIG_SOURCE_FM_FILES_PATH`, `FLITMIG_SYSTEM_USER_EMAIL`, `FLITMIG_TARGET_FM_FILES_PATH`, `FLITMIG_V1_SNAPSHOT_CONSOLIDATED`, `FLITMIG_V1_SNAPSHOT_INCLUDE` |
| observabilidad | `OBSERVABILIDAD_CPUS` |

Total: 53, todas con default en el compose: ninguna impide arrancar. El riesgo es que nadie
las replique en `flit-gitops` ni sepa que existen. Las que más importan para operar: `ICT_SERVICE_TOKEN_SECRET`
(secreta, fail-closed), `FILE_MANAGER_AUTH_TOKEN` (secreta, 401 en PDN), `CORS_ORIGIN_MARCA_BLANCA` y `CORS_ORIGIN_ALT`,
`FLIT_IDENTITY_CLUSTER_ENABLED`, la matriz `TRAMITE_VALIDATION_*`, `DIAGNOSTICO_IMPUESTO_DISPONIBLE`,
`DIAGNOSTICO_CONSULTAS_ADDRESS` y `DIAGNOSTICO_DERIVACION_*`, y los modos `VERIFIK_*_MODE`.

Corregido en esta entrega: `CONSULTAS_IDENTIDAD_ADDRESS` (ola del impuesto) se agregó vacía a `.env.impuesto.example`.

### 5.2 En un `.example` y no en el compose

| Variable | `.example` | Lectura |
|---|---|---|
| `ANTHROPIC_DRFLIT_MAX_TOKENS` | `.env.anthropic.example` | ⚠ Comentada en el `.example`; el compose no la pasa a core-api. Vale el default del código |
| `ANTHROPIC_DRFLIT_TIMEOUT_SECONDS` | `.env.anthropic.example` | ⚠ Igual que la anterior |
| `AZURE_DEVOPS_PAT` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `AZURE_ORG` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `AZURE_ORG_URL` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `AZURE_PAT` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `AZURE_PROJECT` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `AZURE_PROJECT_NAME` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `AZURE_TEAM` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `BRANDING_INTERNAL_API_URL` | `frontend/.env.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | Fronts en local. En el compose es clave fija (`http://gateway:${GATEWAY_PORT}`). Esperado |
| `COMPOSE_PROJECT_NAME` | `.env.prod.example` | Nativa de Compose, no se interpola. Esperado |
| `CORE_API_ORIGIN` | `frontend/.env.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | Fronts en local. En el compose es clave fija. Esperado |
| `GITHUB_TOKEN` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `KYVERUM_WEBHOOK_CALLBACK_URL` | `.env.kyverum.example` | ⚠ El compose la llena desde `CONSULTAS_KYVERUM_WEBHOOK_CALLBACK_URL` (ver §5.3) |
| `MESSAGING_IDENTITY_VALIDATION` | `.env.kyverum.example` | ⚠ Comentada (opcional, fase 2); el compose no la pasa |
| `NEXT_PUBLIC_API_BASE_URL` | `frontend/.env.example` | Se hornea en el build (`cd.yml`, build-args), no en runtime. Esperado |
| `NEXT_PUBLIC_CANAL_OT_ORIGEN` | `frontend/.env.example` | Se hornea en el build (`cd.yml`, build-args), no en runtime. Esperado |
| `NEXT_PUBLIC_DR_FLIT_SUPPORT_EMAIL` | `frontend/.env.example` | Se hornea en el build (`cd.yml`, build-args), no en runtime. Esperado |
| `NEXT_PUBLIC_DR_FLIT_SUPPORT_PHONE` | `frontend/.env.example` | Se hornea en el build (`cd.yml`, build-args), no en runtime. Esperado |
| `NEXT_PUBLIC_FLIT_HOSTS` | `frontend/.env.example` | Se hornea en el build (`cd.yml`, build-args), no en runtime. Esperado |
| `NEXT_PUBLIC_MANUAL_REVIEW_MOCK` | `frontend/.env.example` | Se hornea en el build (`cd.yml`, build-args), no en runtime. Esperado |
| `QA_LEAD_EMAIL` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `QA_LEAD_NAME` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED` | `.env.renting.example` | Derogada (ADR-0044). Bien que no esté: con el canal encendido tumba el arranque |
| `SONAR_ORGANIZATION` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `SONAR_TOKEN` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `TRAMITES_URL` | `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | Fronts en local. En el compose sale de `FLIT_TRAMITES_URL`. Esperado |
| `USER_REAL_EMAIL` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `USER_REAL_NAME` | `.env.example` | Herramientas locales (ADO, GitHub, Sonar). No es de despliegue. Esperado |
| `VERIFIK_AUTH_SCHEME` | `.env.verifik.example` | ⚠ Comentada; el compose no la pasa a core-consultas. Vale el default del código |

### 5.3 Desfases de valor o de documentación

| Tema | Detalle |
|---|---|
| `CORS_ORIGIN` de PDN | `.env.prod.example` y `handoff-vps-suite.md` dicen `https://flitsas.online`; `cd.yml` fija `front_domain=app.flitsas.com` para PDN (decisión 2026-10-19). Confirmar el valor vigente en `flit-gitops/services/flit2-pdn/configmaps.yaml` antes de tocarlo |
| «Deploy via SSH» | `.env.prod.example` (bloque «Lo que exporta el CD») y la cabecera del compose hablan de un CD que exporta variables por SSH. Ese job ya no existe (`cd.yml:1421-1423`): en GitOps nadie las exporta |
| Cabecera del compose | Menciona un perfil «bus» para rabbitmq; el servicio no declara `profiles`: arranca siempre |
| `KYVERUM_WEBHOOK_CALLBACK_URL` | `.env.kyverum.example` la define con la ruta vieja de core-api. El compose la pasa a core-consultas desde `CONSULTAS_KYVERUM_WEBHOOK_CALLBACK_URL` (`.env.prod.example`, ruta `/api/v1/consultas/avisos/kyverum-verify`) |
| `Consultas__Simulacion__*`, `Consultas__Ambiente` | Claves que core-consultas lee y que el compose no declara. Correcto para QA/PDN; en DEV GitOps se ponen en el configmap si se quiere simular |

---

## 6. Conteo

| Servicio | Total | Propias (tabla) | Transversales |
|---|---|---|---|
| core-api | 75 | 66 | 9 |
| core-identity | 7 propias + 67 heredadas | 2 | 5 |
| core-consultas | 93 | 86 | 7 |
| core-notificaciones | 43 | 36 | 7 |
| core-diagnostico | 35 | 28 | 7 |
| core-comparendos | 25 | 18 | 7 |
| core-personanatural | 21 | 14 | 7 |
| core-ict | 20 | 13 | 7 |
| migracion | 23 | 19 | 4 |
| gateway | 21 | 6 | 15 |
| rabbitmq | 4 | 2 | 2 |
| python-ml | 4 | 0 | 4 |
| frontend | 11 | 6 | 5 |
| frontend-hub | 12 | 7 | 5 |
| frontend-comparendos | 10 | 5 | 5 |
| frontend-diagnostico | 10 | 5 | 5 |
| frontend-personanatural | 5 | 0 | 5 |
| observabilidad | 3 | 0 | 3 |
| **Únicas en el compose** | **324** | | |
