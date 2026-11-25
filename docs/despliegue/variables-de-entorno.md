# Variables de entorno: despliegue de Diagnóstico y consumo de la API de impuesto

> Generado: 2026-11-24 · fuente: `docker-compose.prod.yml` @ `c69a08575`

Alcance: el perfil `diagnostico` (core-diagnostico + frontend-diagnostico) y la fuente de impuesto vehicular que
core-consultas consume. Del resto de la suite solo aparecen las variables de las que Diagnóstico o el impuesto dependen.
El detalle operativo sigue en [handoff-vps-diagnostico.md](../suite/handoff-vps-diagnostico.md) y
[despliegue-develop-diagnostico.md](../suite/despliegue-develop-diagnostico.md).

**Regenerar o verificar.** Desde la raíz del repo:

```bash
grep -oE '\$\{(DIAGNOSTICO|IMPUESTO|CONSULTAS_IDENTIDAD|CORE_DIAGNOSTICO|FRONTEND_DIAGNOSTICO)[A-Z0-9_]*' docker-compose.prod.yml | sort -u
```

Hoy devuelve 51 nombres. Si cambia, el compose cambió: actualizar este archivo en el mismo PR.

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

`docker-compose.prod.yml` no tiene `env_file`: Compose solo interpola desde el `.env` junto al compose (o los
`--env-file` que se pasen). El contenido de `.env.impuesto.example` se copia a ese `.env`.

| Archivo en el servidor | Sale de | Qué aporta | ¿Cuándo? |
|---|---|---|---|
| `.env` (junto al compose) | `.env.prod.example`, bloque «Diagnóstico» | `COMPOSE_PROFILES`, base, broker, cliente `svc-diagnostico`, IA, Data Protection | Siempre |
| bloque en el mismo `.env` | `.env.impuesto.example` | `IMPUESTO_*`, `CONSULTAS_IDENTIDAD_ADDRESS` | Solo con aval (ver §3) |
| `.pfx` de Data Protection | fuera del repo; ruta en `DIAGNOSTICO_DP_CERT_FILE` (default `/etc/flit/secrets/diagnostico-dp.pfx`) | Secreto de archivo `diagnostico_dp_pfx`, montado en `/run/secrets/diagnostico_dp_pfx` | Siempre con el perfil |

Encender el perfil: `COMPOSE_PROFILES=<los que ya tenga>,diagnostico`. La suscripción y la credencial de impuesto
necesitan además el perfil `identity` (core-identity con gRPC).

### 1.2 Obligatorias: si faltan, `docker compose` aborta

Compose valida **todo** el archivo, incluidos los perfiles apagados. Si falta una, no arranca ningún contenedor.

| Variable | Valor esperado | Por qué afecta a Diagnóstico |
|---|---|---|
| `RABBITMQ_URL_DIAGNOSTICO` | `amqp://diagnostico:<clave>@rabbitmq:5672/flit` | Las consultas del diagnóstico van por el bus (ADR-0075). Se exige aunque el perfil esté apagado |
| `FLIT_SUITE_ENV` | `dev`, `qa` o **vacía** en PDN | Registra el cliente OIDC `diagnostico` con el host del ambiente |
| `CORS_ORIGIN` | URL pública del front del ambiente, sin barra final | Sin ella aborta el stack entero |
| `RABBITMQ_URL_TRAMITES`, `RABBITMQ_URL_PLATAFORMA`, `SVC_TRAMITES_CLIENT_SECRET` | Ver `.env.prod.example` | Sin ellas aborta el stack entero |

Comprobar sin levantar nada: `docker compose -f docker-compose.prod.yml config > /dev/null`.

### 1.3 Obligatorias de Diagnóstico: sin ellas no arranca o no sirve

| Variable | Efecto si falta |
|---|---|
| `CONNECTION_STRING_DIAGNOSTICO` | No arranca (usuario dueño del esquema `diagnostico`) |
| `SVC_DIAGNOSTICO_CLIENT_SECRET` | Sin token de servicio. Mismo valor en core-api/core-identity (registro) y core-diagnostico (uso) |
| `DIAGNOSTICO_DP_CERT_PASSWORD` + `.pfx` | No arranca en ningún ambiente. Respaldo del certificado obligatorio |
| `DIAGNOSTICO_IDENTIDAD_ADDRESS` | 403 para todas las empresas (suscripción fail-closed). Valor: `http://core-identity:8083` |
| `FILE_MANAGER_BASE_URL` | Sin URL no arranca. El default del compose es el mismo de core-api |
| `DIAGNOSTICO_SESSION_SECRET` | Front sin sesión. ≥ 32 caracteres, distinta del hub y de Comparendos |

### 1.4 Degradadas: arranca, pero algo queda apagado

| Variable vacía | Efecto |
|---|---|
| `CORE_IDENTITY_GRPC_PORT` (core-identity) | Sin gRPC de Identidad: ninguna empresa habilitada y el impuesto sin credencial |
| `DIAGNOSTICO_CONSULTAS_ADDRESS` | Todas las fuentes «no conectadas». Al definirla se encienden **juntas** RUNT por Intempo, RUNT persona, RUES, SIMIT, avalúo e impuesto |
| `DIAGNOSTICO_IA_CLAVE_PRIVADA` / `DIAGNOSTICO_IA_KID` | Dictamen con la plantilla «sin análisis» |
| `AI_ENTREGA_DIAGNOSTICO_CLAVE_PUBLICA` / `AI_ENTREGA_DIAGNOSTICO_KID` (core-api) | core-api no entrega la llave de IA: mismo efecto |
| `FILE_MANAGER_AUTH_TOKEN` | Contabo (PDN) responde 401. Mismo valor que core-api y core-ict |
| `CONSULTAS_IDENTIDAD_ADDRESS` (core-consultas) | Impuesto real «no listo» |

### 1.5 Valores por ambiente

| Variable | DEV | QA | PDN |
|---|---|---|---|
| `DIAGNOSTICO_PORT` (frontend-diagnostico) | 4024 | 5024 | 6024 |
| `CORE_DIAGNOSTICO_PORT` (core-diagnostico, no publicado) | 4032 | 5032 | 6032 |
| `CORE_DIAGNOSTICO_GRPC_PORT` | 8088 | 8088 | 8088 |
| `GATEWAY_PORT` (token y JWKS) | 4002 | 5002 | 6002 |
| `CORE_DIAGNOSTICO_TAG`, `FRONTEND_DIAGNOSTICO_TAG` | `dev` | `qa` | `sha-<commit>` |
| `DIAGNOSTICO_REDACCION_HABILITADO` | `true` | `true` | `false` hasta decisión del PO |
| `DIAGNOSTICO_IA_KID` | `diag-dev-<aaaamm>` | `diag-qa-<aaaamm>` | `diag-pdn-<aaaamm>` |
| `DIAGNOSTICO_IMPUESTO_DISPONIBLE` | `false` salvo prueba simulada | `false` | `false` |
| `IMPUESTO_API_*`, `IMPUESTO_CONTACTO_*` | vacías | vacías | vacías hasta el aval de §3 |

Origen: `cd.yml:83-140` (puertos y tags) y los comentarios del compose (redacción IA, kid, impuesto).

### 1.6 Secretos de Diagnóstico

- Van a `sealed-secrets.yaml` en GitOps o al `.env` del servidor. Nunca al repo ni a un configmap.
- Uno **distinto por ambiente**: `SVC_DIAGNOSTICO_CLIENT_SECRET`, `DIAGNOSTICO_SESSION_SECRET`, la clave del usuario
  `diagnostico` del broker, `DIAGNOSTICO_DP_CERT_PASSWORD` y `DIAGNOSTICO_IA_CLAVE_PRIVADA`.
- Respaldo fuera del servidor: el `.pfx` de Data Protection. Perderlo deja ilegibles los documentos cifrados.
- `AI_ENTREGA_DIAGNOSTICO_CLAVE_PUBLICA` **no** es secreta; su privada sí.
- La API key del impuesto no es un secreto del ambiente: es por compañía (§3.3).

---

## 2. Variables por servicio

Columnas: **Obligatoria** = `sí (aborta)` (Compose no arranca), `sí` (el servicio no arranca o no sirve), `condicional`
(solo si se usa la función) o `no`. **Secreta** = `sí` (sealed-secret), `sensible` (dato de contacto: no versionar) o
`no`. **Default** = valor si falta (`—` = sin default; `vacío` = cadena vacía).

Los .NET usan además `FLIT_DOTNET_ENVIRONMENT` (default `Development`), `OTEL_EXPORTER_OTLP_ENDPOINT` (vacío) y los
límites `*_MEM_LIMIT` / `*_CPUS` (default `0` = sin límite).

### 2.1 core-diagnostico (perfil `diagnostico`)

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `CONNECTION_STRING_DIAGNOSTICO` | `ConnectionStrings__Servicio` | sí | sí | vacío | `.env.prod.example` | Base del usuario dueño del esquema `diagnostico`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `CORE_API_GRPC_PORT` | `Diagnostico__Tramites__Address` | no | no | `8082` | ⚠ ninguno | Puerto gRPC de core-api: tipos de trámite publicados y derivación. Sin él, `GET /tipos-tramite` da 503. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `CORE_DIAGNOSTICO_GRPC_PORT` | `Servicio__GrpcPort` | no | no | `8088` | `.env.prod.example` | Puerto gRPC interno de core-diagnostico. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `CORE_DIAGNOSTICO_PORT` | `ASPNETCORE_URLS` | no | no | `4032` | `.env.prod.example` | Puerto REST interno (el gateway enruta `/api/v1/diagnostico/*`). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `CORE_DIAGNOSTICO_TAG` | imagen | no | no | `latest` | ⚠ ninguno | Tag de la imagen en GHCR. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_AMBIENTE` | `Diagnostico__Ambiente` | no | no | vacío | ⚠ ninguno | LOCAL, DEV, QA o PDN. Solo se valida con simulación. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_CONSULTAS_ADDRESS` | `Diagnostico__Consultas__Address` | condicional | no | vacío | ⚠ ninguno | gRPC de core-consultas. Al definirla se encienden juntas todas las fuentes reales del diagnóstico. | [despliegue diag.](../suite/despliegue-develop-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_CONCURRENCIA` | `Diagnostico__Derivacion__Concurrencia` | no | no | `4` | ⚠ ninguno | Llamadas a la vez por réplica al crear trámites. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_CREDITO_POR_COMPANIA` | `Diagnostico__Derivacion__CreditoPorCompania` | no | no | `2` | ⚠ ninguno | Ítems en vuelo por compañía (≤ concurrencia). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_HABILITADA` | `Diagnostico__Derivacion__Habilitada` | no | no | `false` | ⚠ ninguno | «Crear trámite» individual y masivo. Junto con `TRAMITES_DERIVACION_HABILITADA`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_PREFETCH` | `Diagnostico__Derivacion__Prefetch` | no | no | `8` | ⚠ ninguno | Prefetch de la cola `diagnostico.derivaciones`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_TASA_POR_SEGUNDO` | `Diagnostico__Derivacion__TasaPorSegundo` | no | no | `10` | ⚠ ninguno | Llamadas por segundo al crear trámites. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DERIVACION_URL_TRAMITES` | `Diagnostico__Derivacion__UrlTramites` | no | no | vacío | ⚠ ninguno | Base del enlace «Ver trámite». Vacía = relativo. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DP_CERT_FILE` | secreto `diagnostico_dp_pfx` | sí | sí (archivo) | `/etc/flit/secrets/diagnostico-dp.pfx` | `.env.prod.example` | Ruta en el host del `.pfx` de Data Protection. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_DP_CERT_PASSWORD` | `Diagnostico__DataProtection__CertificadoClave` | sí | sí | vacío | `.env.prod.example` | Clave del `.pfx` de Data Protection. Sin certificado válido no arranca. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_FILE_MANAGER_CATEGORY` | `FILE_MANAGER_CATEGORY` | no | no | `diagnostico` | `.env.prod.example` | Categoría de Diagnóstico en el file-manager. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IA_CLAVE_PRIVADA` | `Diagnostico__Ia__ClavePrivada` | no | sí | vacío | `.env.prod.example` | Privada RSA (base64 DER PKCS#8) que descifra la llave de IA. Vacía = dictamen con plantilla. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IA_CLAVE_PRIVADA_ANTERIOR` | `Diagnostico__Ia__ClavePrivadaAnterior` | no | sí | vacío | `.env.prod.example` | Privada anterior, solo durante una rotación. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IA_KID` | `Diagnostico__Ia__Kid` | no | no | vacío | `.env.prod.example` | Kid actual (`diag-<amb>-<aaaamm>`). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IA_KID_ANTERIOR` | `Diagnostico__Ia__KidAnterior` | no | no | vacío | `.env.prod.example` | Kid anterior, solo durante una rotación. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IA_ORIGEN` | `Diagnostico__Ia__Origen` | no | no | `plataforma` | `.env.prod.example` | Origen de la configuración de IA. Default `plataforma`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IDENTIDAD_ADDRESS` | `Diagnostico__Identidad__Address` | sí | no | vacío | `.env.prod.example` | gRPC de Identidad (`http://core-identity:8083`). Vacío = 403 para todas las empresas. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_IMPUESTO_DISPONIBLE` | `Diagnostico__Fuentes__ImpuestoDisponible` | no | no | `false` | ⚠ ninguno | Ofrece la fuente de impuesto en Diagnóstico. | [§ Impuesto](#3-impuesto-vehicular-fuente-real-ola-hu-14681) |
| `DIAGNOSTICO_REDACCION_HABILITADO` | `Diagnostico__Redaccion__Habilitado` | no | no | `false` | `.env.prod.example` | Cola de redacción IA del dictamen. En PDN la decide el PO. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FILE_MANAGER_AUTH_TOKEN` | mismo nombre | condicional | sí | vacío | ⚠ ninguno | Bearer del file-manager. Mismo valor que core-api y core-ict. Obligatorio con Contabo (PDN). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FILE_MANAGER_BASE_URL` | mismo nombre | sí | no | `https://ny46zhyeti.execute-api.us-east-1.amazonaws.com/pdn/` | `.env.prod.example` | URL del file-manager de adjuntos. Misma que core-api. Sin URL no arranca. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FILE_MANAGER_FILES_PATH` | mismo nombre | no | no | `api/v1/files` | ⚠ ninguno | Ruta de archivos del file-manager. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FILE_MANAGER_TIMEOUT_SECONDS` | mismo nombre | no | no | `30` | ⚠ ninguno | Timeout del file-manager. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FLIT_HUB_URL` | `Platform__Auth__Issuers__0` | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `GATEWAY_PORT` | `Platform__ServiceClient__TokenEndpoint`, `Platform__Auth__JwksUri`, `Diagnostico__Plataforma__GatewayUrl` | no | no | `4002` | `.env.prod.example` | Token de servicio, JWKS y manifiesto por el gateway. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `RABBITMQ_URL_DIAGNOSTICO` | `Platform__Messaging__ConnectionString` | sí (aborta) | sí | — (aborta) | `.env.prod.example` | AMQP del usuario `diagnostico`. Sin ella el compose aborta, aunque el perfil esté apagado. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `SVC_DIAGNOSTICO_CLIENT_SECRET` | `Platform__ServiceClient__ClientSecret` | sí | sí | vacío | `.env.prod.example` | Secreto de `svc-diagnostico`. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |

El secreto `diagnostico_dp_pfx` se monta en `/run/secrets/diagnostico_dp_pfx`. `Diagnostico__Tramites__Address` es fija
(`http://core-api:${CORE_API_GRPC_PORT}`). La simulación ya no se declara en core-diagnostico: vive en core-consultas, y
QA y PDN nunca la encienden.

### 2.2 frontend-diagnostico (perfil `diagnostico`)

Cliente OIDC público `diagnostico` del hub (PKCE). Se registra solo con el host del ambiente (`FLIT_SUITE_ENV`).

| Variable | Clave en el contenedor | Obligatoria | Secreta | Default en el compose | `.example` | Para qué sirve | Doc |
|---|---|---|---|---|---|---|---|
| `DIAGNOSTICO_PORT` | `PORT` | no | no | `4024` | `.env.prod.example` | Puerto del front. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `DIAGNOSTICO_SESSION_SECRET` | `FLIT_SESSION_SECRET` | sí | sí | vacío | `.env.prod.example` | Cifra la cookie de sesión del front de Diagnóstico. ≥ 32 caracteres, distinta del hub. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FLIT_HOSTS` | mismo nombre | no | no | lista larga (ver compose L1318) | `.env.prod.example` | Hosts FLIT sin marca de red. Igual que `NEXT_PUBLIC_FLIT_HOSTS` del build. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FLIT_HUB_URL` | mismo nombre | no | no | `https://dev.flitsas.online` | `.env.prod.example`, `frontend-comparendos/.env.example`, `frontend-diagnostico/.env.example`, `frontend-hub/.env.example` | URL pública del hub (emisor de tokens). | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FLIT_INTERNAL_API_KEY` | mismo nombre | condicional | sí | vacío | `.env.prod.example`, `frontend/.env.example` | Clave interna con el gateway (marca por la red interna). Vacía = fail-closed. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FLIT_TRAMITES_URL` | `TRAMITES_URL` | no | no | `https://dev.tramites.flitsas.online` | `.env.prod.example` | URL pública de Trámites. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `FRONTEND_DIAGNOSTICO_TAG` | imagen | no | no | `latest` | ⚠ ninguno | Tag de la imagen en GHCR. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |
| `GATEWAY_PORT` | `CORE_API_ORIGIN`, `BRANDING_INTERNAL_API_URL`, `FLIT_OIDC_INTERNAL_URL` | no | no | `4002` | `.env.prod.example` | API, marca y canje OIDC por la red interna. | [diagnóstico](../suite/handoff-vps-diagnostico.md) |

### 2.3 core-consultas y core-identity: solo lo que usan Diagnóstico y el impuesto

| Variable | Servicio | Clave en el contenedor | Obligatoria | Secreta | Default | `.example` | Para qué sirve |
|---|---|---|---|---|---|---|---|
| `CORE_IDENTITY_GRPC_PORT` | core-identity | `Identidad__GrpcPort` | condicional | no | vacío | `.env.prod.example` (comentada) | gRPC de Identidad (8083). Lo usan la suscripción de Diagnóstico y la credencial de impuesto |
| `SVC_DIAGNOSTICO_CLIENT_SECRET` | core-api / core-identity | `Suite__Oidc__ServiceClients__svc-diagnostico__Secret` | sí | sí | vacío | `.env.prod.example` | Registro de `svc-diagnostico`. Mismo valor que en core-diagnostico. Reiniciar core-api (y core-identity) al cambiarlo |
| `SVC_CONSULTAS_CLIENT_SECRET` | core-api / core-identity | `Suite__Oidc__ServiceClients__svc-consultas__Secret` | sí | sí | vacío | `.env.prod.example` | Registro de `svc-consultas`, con el scope `platform.identidad.credenciales.impuesto` |
| `SVC_CONSULTAS_CLIENT_SECRET` | core-consultas | `Platform__ServiceClient__ClientSecret` | sí | sí | vacío | `.env.prod.example` | Token de servicio de core-consultas. Mismo valor que el registro |
| `CORE_CONSULTAS_GRPC_PORT` | core-consultas | `Servicio__GrpcPort` | no | no | `8084` | `.env.prod.example` | Destino de `DIAGNOSTICO_CONSULTAS_ADDRESS` (`http://core-consultas:8084`) |
| `RABBITMQ_URL_CONSULTAS` | core-consultas | `Platform__Messaging__ConnectionString` | sí | sí | `amqp://consultas:sin-clave@rabbitmq:5672/flit` | `.env.prod.example` | Consume los trabajos que Diagnóstico deja en `flit.consultas`. El default no sirve: poner la clave real |
| `CONSULTAS_IDENTIDAD_ADDRESS` | core-consultas | `Consultas__Identidad__Address` | condicional | no | vacío | `.env.impuesto.example` | gRPC de Identidad para la API key de impuesto por compañía |
| `IMPUESTO_API_*`, `IMPUESTO_CONTACTO_*` | core-consultas | mismo nombre | condicional | ver §3.2 | vacío / `280` | `.env.impuesto.example` | Fuente de impuesto (detalle en §3) |

### 2.4 Lo mínimo de core-api

| Variable | Clave en el contenedor | Default | Para qué le sirve a Diagnóstico |
|---|---|---|---|
| `CORE_API_GRPC_PORT` | `Ict__GrpcPort` | `8082` | gRPC de tipos de trámite, configuración de IA y derivación |
| `AI_ENTREGA_DIAGNOSTICO_KID`, `AI_ENTREGA_DIAGNOSTICO_CLAVE_PUBLICA` | `Ai__Entrega__Diagnostico__*` | vacío | Pública RSA-3072 de Diagnóstico (no secreta). core-api cifra con ella la llave de IA. Las `*_SIGUIENTE` solo en rotación |
| `ANTHROPIC_API_KEY` | mismo nombre | vacío | La llave que se entrega cifrada a Diagnóstico. Secreta (`.env.anthropic.example`) |
| `TRAMITES_DERIVACION_HABILITADA`, `TRAMITES_DERIVACION_CONCURRENCIA_MAXIMA` | `Tramites__Derivacion__*` | `false`, `8` | rpc «crear borrador desde Diagnóstico». Se enciende junto con `DIAGNOSTICO_DERIVACION_HABILITADA`. Concurrencia ≥ suma de `DIAGNOSTICO_DERIVACION_CONCURRENCIA` de las réplicas |

---

## 3. Impuesto vehicular (fuente real, ola HU #14681)

> ⚠ **Encender el impuesto real en DEV, QA o PDN exige aval de Seguridad y de Legal** (también Habeas Data; ver
> [despliegue-develop-diagnostico.md §9, punto 5](../suite/despliegue-develop-diagnostico.md)). Sin esos avales, las
> variables `IMPUESTO_API_*` e `IMPUESTO_CONTACTO_*` quedan **vacías** en el servidor.

Verificado contra `services/core-consultas/src/Flit.Consultas.Api/Impuesto/OpcionesImpuesto.cs`,
`services/core-consultas/src/Flit.Consultas.Api/Program.cs:135-180` y `docker-compose.prod.yml:726-751`.

### 3.1 Qué decide simulado o real

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

### 3.2 Variables de core-consultas

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

### 3.3 La API key no es una variable

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

### 3.4 Lado Diagnóstico

| Variable | Default | Uso |
|---|---|---|
| `DIAGNOSTICO_IMPUESTO_DISPONIBLE` → `Diagnostico__Fuentes__ImpuestoDisponible` | `false` | Ofrece la fuente de impuesto. ⚠ No está en ningún `.example` |
| `DIAGNOSTICO_CONSULTAS_ADDRESS` → `Diagnostico__Consultas__Address` | vacío | gRPC de core-consultas. Enciende juntas todas las fuentes reales |

---

## 4. Desfases que afectan a Diagnóstico o al impuesto (⚠)

### 4.1 En el compose y en ningún `.example`

Todas tienen default en el compose: ninguna impide arrancar. El riesgo es que nadie las replique en `flit-gitops`.

| Variable | Servicio | Default |
|---|---|---|
| `DIAGNOSTICO_IMPUESTO_DISPONIBLE` | core-diagnostico | `false` |
| `DIAGNOSTICO_CONSULTAS_ADDRESS` | core-diagnostico | vacío |
| `DIAGNOSTICO_AMBIENTE` | core-diagnostico | vacío |
| `DIAGNOSTICO_DERIVACION_HABILITADA`, `_CONCURRENCIA`, `_PREFETCH`, `_CREDITO_POR_COMPANIA`, `_TASA_POR_SEGUNDO`, `_URL_TRAMITES` | core-diagnostico | `false`, `4`, `8`, `2`, `10`, vacío |
| `FILE_MANAGER_AUTH_TOKEN` | core-diagnostico (y core-api, core-ict) | vacío. Secreta |
| `FILE_MANAGER_FILES_PATH`, `FILE_MANAGER_TIMEOUT_SECONDS` | core-diagnostico | `api/v1/files`, `30` |
| `CORE_DIAGNOSTICO_TAG`, `FRONTEND_DIAGNOSTICO_TAG` | ambos | `latest` |
| `CORE_API_GRPC_PORT` | core-diagnostico (y otros) | `8082` |
| `TRAMITES_DERIVACION_HABILITADA`, `TRAMITES_DERIVACION_CONCURRENCIA_MAXIMA` | core-api | `false`, `8` |

Corregido en c69a08575: `CONSULTAS_IDENTIDAD_ADDRESS` se agregó vacía a `.env.impuesto.example`.

### 4.2 Claves que el código lee y el compose no declara

| Clave | Servicio | Lectura |
|---|---|---|
| `Consultas__Simulacion__ImpuestoPresupuestoSegundos` | core-consultas | Presupuesto del sondeo simulado; default `3` en código. No está en el compose ni en un `.example` |
| `Consultas__Simulacion__Habilitada`, `Consultas__Simulacion__Fuentes`, `Consultas__Ambiente` | core-consultas | Deciden simulado o real. Bien que no estén para QA/PDN. En DEV GitOps van al configmap si se quiere simular |

### 4.3 De documentación

| Tema | Detalle |
|---|---|
| `handoff-vps-diagnostico.md` §4 | Dice que el compose fija `Diagnostico__Fuentes__ImpuestoDisponible=false` sin variable. Hoy sale de `DIAGNOSTICO_IMPUESTO_DISPONIBLE` |
| `CORE_IDENTITY_GRPC_PORT` | En `.env.prod.example` está comentada (`# CORE_IDENTITY_GRPC_PORT=8083`). Con el perfil `diagnostico` hay que descomentarla, o Diagnóstico responde 403 |
