# Handoff de VPS: Consultas y Notificaciones como servicios

Epic #13316. Para el líder técnico, con su sesión de Claude conectada a la VPS. Cada paso trae qué hacer, cómo
comprobarlo y cómo volver atrás. Se aplica **primero en DEV**; QA y PDN solo con aprobación del líder y después de que
DEV esté estable.

**Qué cambia.** core-api deja de hablar con los proveedores (RUNT, Verifik, Kyverum, Fasecolda, RUES) y deja de enviar
correos. Lo hacen dos servicios nuevos:

- `core-consultas`: consultas, avalúos, impronta, certificado RUES y validación de identidad de Kyverum.
- `core-notificaciones`: correos (SMTP y API de Renting), webhooks del OT y mensajes muertos.

Los servicios se hablan por gRPC en la red interna y se avisan por RabbitMQ.

**Por qué este despliegue es distinto.** El PR llega con los cortes hechos: core-consultas, core-notificaciones y
RabbitMQ son **obligatorios** y no hay banderas para apagarlos. Toda la preparación (pasos 0 a 4) va **antes** de
fusionar, porque en DEV fusionar es desplegar. Si algo sale mal, se vuelve atrás desplegando la versión anterior de las
imágenes (al final).

Qué se probó y qué no: [pruebas-consultas-notificaciones.md](pruebas-consultas-notificaciones.md). Cómo se configura en
local: [local.md](local.md).

## Bloqueante: la imagen de core-api no compila desde el 2026-10-07

El 2026-10-07 se publicaron cinco avisos de seguridad sobre `SixLabors.ImageSharp` (GHSA-j3p4-wp97-rph4,
GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w, GHSA-gwg2-r3hj-4w44, GHSA-wmxv-xphr-5c9g). Solo se corrigen en la 4.1.2, y la
versión 2.x es la única que funciona con PdfSharpCore, que dibuja la firma del FUR. Como el proyecto trata los avisos de
NuGet como errores, **la imagen de core-api no se construye** en el CD, ni desde esta rama ni desde `develop`. Hay que
decidir qué hacer antes de desplegar (por ejemplo, suprimir esos cinco avisos con justificación mientras se reemplaza
PdfSharpCore). Es una decisión de seguridad que no toma este despliegue.

## Requisitos

- La suite encendida en el ambiente (fases 0 a 3 de [handoff-vps-suite.md](handoff-vps-suite.md)), con el bloque «Lo
  que exporta el CD» en el `.env`.
- El PR de la Epic aprobado. Trae también el contrato de plataforma v1.3 (`docs/suite/contrato-plataforma-v1.md`), que
  espera tu aprobación en esa misma revisión.
- Acceso a la carpeta del despliegue (`$HOSTINGER_DEPLOY_PATH`), a su `.env` y a una conexión de administrador de
  Postgres (`$ADMIN_URL`).

| Ambiente | Rama | core-consultas (REST · gRPC) | core-notificaciones (REST · gRPC) | Consola RabbitMQ |
|---|---|---|---|---|
| DEV | `develop` | 4026 · 8084 | 4027 · 8085 | `127.0.0.1:4672` |
| QA | `staging` | 4026 · 8084 | 4027 · 8085 | `127.0.0.1:4672` |
| PDN | `release` | 4026 · 8084 | 4027 · 8085 | `127.0.0.1:4672` |

Los puertos de core-consultas y core-notificaciones no se publican: viven solo en la red de Docker, así que pueden ser
los mismos en los tres ambientes. **Están propuestos; confírmalos** (variables `CORE_CONSULTAS_*` y
`CORE_NOTIFICACIONES_*`). El gRPC de core-identity (`CORE_IDENTITY_GRPC_PORT`, propuesto 8083) sigue apagado si la
variable va vacía.

## Antes de empezar: credenciales que hay que rotar

Durante el desarrollo quedaron expuestas estas credenciales. No van en este despliegue, pero conviene rotarlas antes de
PDN y poner los valores nuevos en los `.env`:

- La contraseña del buzón SMTP que estaba en texto plano en `docker-compose.yml` (desarrollo). Se quitó del archivo,
  pero sigue en el historial de git.
- La llave `kv_live_…` de Kyverum Verify.
- Las que se compartieron por chat durante las pruebas: la contraseña de `info@flitsas.com` y llaves de proveedores de
  un `appsettings.Development.json` local.

## Paso 0 — Foto del estado actual y respaldo

```bash
cd "$DEPLOY_PATH"
cp .env ".env.antes-13316.$(date +%Y%m%d%H%M)"
docker compose -f docker-compose.prod.yml ps
pg_dump "$ADMIN_URL" -Fc -f "respaldo-antes-13316.$(date +%Y%m%d%H%M).dump"
```

Y anotar lo que se usa hoy, para los pasos 3 y 4:

```bash
free -m
docker stats --no-stream --format '{{.Name}} {{.MemUsage}}'
psql "$ADMIN_URL" -Atc "show max_connections; select count(*) from pg_stat_activity;"
```

**Comprobar:** todo `healthy` y el respaldo creado. Hay margen para unas 40 conexiones más: cada servicio nuevo abre
hasta 20 (paso 3).

## Paso 1 — RabbitMQ

`docker compose` valida **todas** las variables obligatorias del archivo, aunque se levante un solo servicio. Por eso,
antes de levantar el broker, se generan las claves de sus usuarios (paso 2) y se escriben ya en el `.env`:

```bash
for s in tramites consultas notificaciones plataforma; do echo "$s: $(openssl rand -hex 24)"; done   # guardar las cuatro claves
```

| Variable | Valor |
|---|---|
| `RABBITMQ_URL_TRAMITES` | `amqp://tramites:<clave de tramites>@rabbitmq:5672/flit` |
| `RABBITMQ_URL_CONSULTAS` | `amqp://consultas:<clave de consultas>@rabbitmq:5672/flit` |
| `RABBITMQ_URL_NOTIFICACIONES` | `amqp://notificaciones:<clave de notificaciones>@rabbitmq:5672/flit` |
| `RABBITMQ_URL_PLATAFORMA` | `amqp://plataforma:<clave de plataforma>@rabbitmq:5672/flit` |
| `SVC_TRAMITES_CLIENT_SECRET` | cualquier cadena larga (también es obligatoria para validar el compose) |

Luego, en el mismo `.env`, `RABBITMQ_ADMIN_USER` y `RABBITMQ_ADMIN_PASSWORD`, y levantar solo el broker:

```bash
docker compose -f docker-compose.prod.yml up -d rabbitmq
until [ "$(docker inspect -f '{{.State.Health.Status}}' "$(docker compose -f docker-compose.prod.yml ps -q rabbitmq)")" = healthy ]; do sleep 3; done
docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl list_exchanges -p flit name
```

**Comprobar:** aparecen `flit.tramites`, `flit.consultas`, `flit.notificaciones` y `flit.plataforma`. Salen de
`deploy/rabbitmq/definitions.json` al arrancar.

**Esperar a `healthy` antes del paso 2.** Si se corre `rabbitmqctl` mientras el broker arranca, la cookie de Erlang
queda con dueño root y el broker se cae con `.erlang.cookie: eacces`. Arreglo: borrar el contenedor y el volumen
`rabbitmq-datos`, y volver a levantarlo.

**La consola web** (`127.0.0.1:4672`) no tiene usuario: al cargar las definiciones RabbitMQ no crea el de
`RABBITMQ_ADMIN_USER`. Si se necesita:
`docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl add_user <usuario> <clave>`, luego
`set_user_tags <usuario> administrator` y `set_permissions -p flit <usuario> ".*" ".*" ".*"`.

**Volver atrás:** `docker compose -f docker-compose.prod.yml stop rabbitmq`. Todavía nadie lo usa.

## Paso 2 — Usuarios del broker

Un usuario por servicio, **con las mismas claves del paso 1**. Solo puede escribir en su exchange;
los que dejan trabajos de correo pueden escribir también en `flit.notificaciones`.

```bash
deploy/rabbitmq/usuario-de-servicio.sh tramites       "$CLAVE_TRAMITES"       notificaciones
deploy/rabbitmq/usuario-de-servicio.sh consultas      "$CLAVE_CONSULTAS"
deploy/rabbitmq/usuario-de-servicio.sh notificaciones "$CLAVE_NOTIFICACIONES"
deploy/rabbitmq/usuario-de-servicio.sh plataforma     "$CLAVE_PLATAFORMA"     notificaciones
```

| Usuario | Lo usa | Variable del `.env` |
|---|---|---|
| `tramites` | core-api | `RABBITMQ_URL_TRAMITES` (obligatoria) |
| `consultas` | core-consultas | `RABBITMQ_URL_CONSULTAS` |
| `notificaciones` | core-notificaciones | `RABBITMQ_URL_NOTIFICACIONES` |
| `plataforma` | core-identity | `RABBITMQ_URL_PLATAFORMA` (obligatoria) |

Correr el script otra vez cambia la clave: si se cambia, actualizar también su `RABBITMQ_URL_*`.

**Comprobar:** `docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl list_users` muestra los cuatro.

## Paso 3 — Bases propias

Cada servicio nuevo tiene su usuario de Postgres, dueño solo de su esquema (`deploy/postgres/README.md`):

```bash
psql "$ADMIN_URL" -v servicio=consultas      -v conexiones=20 -v clave="$CLAVE_PG_CONSULTAS"      -f deploy/postgres/servicio-con-esquema-propio.sql
psql "$ADMIN_URL" -v servicio=notificaciones -v conexiones=20 -v clave="$CLAVE_PG_NOTIFICACIONES" -f deploy/postgres/servicio-con-esquema-propio.sql
```

Si el esquema ya existía (por ejemplo, porque Consultas corrió antes con el usuario principal), el script también le
pasa al usuario nuevo la propiedad de sus tablas. Sin eso el servicio no arranca: `permission denied for table
__EFMigrationsHistory`.

Con eso, `CONNECTION_STRING_CONSULTAS` (usuario `flit_consultas`) y `CONNECTION_STRING_NOTIFICACIONES` (usuario
`flit_notificaciones`) en el `.env`, con la misma base y host que `CONNECTION_STRING_CORE`.

**Comprobar:** `psql` con cada cadena entra. Si `max_connections` no tiene margen para 40 más (paso 0), bajar
`conexiones` en los dos comandos (y `Maximum Pool Size` en sus cadenas) antes de seguir.

## Paso 4 — Variables

| Qué | Variables | Nota |
|---|---|---|
| Clientes de servicio, un secreto distinto por ambiente | `SVC_TRAMITES_CLIENT_SECRET` (obligatoria), `SVC_CONSULTAS_CLIENT_SECRET`, `SVC_NOTIFICACIONES_CLIENT_SECRET`, `SVC_ICT_CLIENT_SECRET` | Los tokens entre servicios. Sin `SVC_ICT_CLIENT_SECRET`, core-ict no arranca |
| Proveedores | `VERIFIK_*`, `KYVERUM_*`, `KYVERUM_RUNT_*`, `FASECOLDA_*`, `RUES_*` (opcional) | **Mismos valores de hoy.** Ahora solo los lee core-consultas. Si a uno en modo real le falta su secreto, core-consultas no arranca y lo dice en el log |
| Correo | `SMTP_*`, `RENTING_API_*` y el certificado `.pfx` | **Mismos valores de hoy.** Ahora solo los lee core-notificaciones; el `.pfx` se monta solo ahí |
| Certificado de Renting | `RENTING_API_PFX_CERTIFICATE_PATH` | core-notificaciones **siempre** monta el `.pfx` (por defecto `/opt/certs/email/renting.rc.prod.pfx`), aunque Renting esté apagado. Si la ruta no existe en la VPS, el contenedor no arranca |
| Correo: modo consola | `SMTP_USE_CONSOLE_WHEN_NO_HOST` | **Revisar.** Por defecto (`true`), si falta `SMTP_HOST` los correos no salen: se escriben en el log y cuentan como enviados. En QA y PDN ponerla en `false`, para que un `SMTP_HOST` vacío falle a la vista |
| Aviso de Kyverum Verify | `CONSULTAS_KYVERUM_WEBHOOK_CALLBACK_URL` | **Variable nueva.** Ver abajo |
| Límites de recursos (opcional) | `CORE_CONSULTAS_MEM_LIMIT`, `CORE_CONSULTAS_CPUS`, `CORE_NOTIFICACIONES_MEM_LIMIT`, `CORE_NOTIFICACIONES_CPUS`, `RABBITMQ_MEM_LIMIT` | Sin la variable no hay límite. Fijarlos después de medir en DEV con `docker stats` (paso 5), igual que con core-identity |
| ICT | `ICT_SERVICE_TOKEN_SECRET` | **No quitarlo.** El token de ICT ya sale de Identidad (fijo en el compose), pero el aviso de estado core-api → core-ict sigue firmado con este secreto |

**El aviso de Kyverum cambia de ruta, no solo de host.**

- Hoy, en DEV: `KYVERUM_WEBHOOK_CALLBACK_URL=https://api.dev.flitsas.online/api/v1/webhooks/kyverum-verify`. Es la ruta
  de core-api.
- Agregar: `CONSULTAS_KYVERUM_WEBHOOK_CALLBACK_URL=https://api.dev.flitsas.online/api/v1/consultas/avisos/kyverum-verify`.
  Es la ruta de core-consultas. En QA y PDN, el mismo host que use hoy cada ambiente.
- **No copiar el valor viejo en la variable nueva.** Las validaciones nuevas se quedarían sin resultado: Kyverum
  avisaría a core-api, que ya no las crea. Las recupera la conciliación, pero tarda minutos.
- **No borrar la variable vieja en este despliegue.** Las validaciones creadas antes del corte siguen avisando ahí, y
  core-api las atiende con el secreto que guardó al crearlas.
- El gateway ya enruta `/api/v1/consultas/avisos/*` a core-consultas. Si nginx filtra rutas, abrir esa también.

**Comprobar:** `docker compose -f docker-compose.prod.yml config > /dev/null`. Si falta una variable obligatoria
(`RABBITMQ_URL_TRAMITES`, `RABBITMQ_URL_PLATAFORMA`, `SVC_TRAMITES_CLIENT_SECRET`), falla aquí y no al desplegar.

## Paso 5 — Desplegar

Si se cambia el `.env` después de que un contenedor existe, hay que recrearlo con `docker compose ... up -d <servicio>`:
`restart` deja los valores viejos.

Fusionar el PR (DEV) o pasar de rama (QA, PDN). El CD construye y levanta core-consultas y core-notificaciones junto
con core-api. Cada uno aplica sus migraciones al arrancar: core-api las suyas (incluida la `133`, que solo amplía un
CHECK) y los servicios nuevos, solo su esquema.

**Comprobar:**

```bash
docker compose -f docker-compose.prod.yml ps        # core-api, core-consultas, core-notificaciones, core-ict, rabbitmq: healthy
docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl list_queues -p flit name consumers | grep -v -e retry -e dlq
```

Las colas `notificaciones.email.send`, `notificaciones.webhooks.salientes` y `tramites.avisos-kyverum` deben tener un
consumidor cada una. Si core-api no arranca y nombra `Consultas:Remoto:Address`, `Notificaciones:Remoto:Address` o
`Platform:ServiceClient`, falta una variable del paso 4.

## Paso 6 — Datos

Apenas termine el despliegue:

```bash
psql "$ADMIN_URL" -v ON_ERROR_STOP=1 -f deploy/postgres/migrar-configuracion-consultas.sql
# Solo DEV y QA (valores de avalúo de prueba):
psql "$ADMIN_URL" -v ON_ERROR_STOP=1 -f deploy/postgres/migrar-valores-mock-avaluo.sql
```

Hasta correr el primero, las consultas usan la cadena global de proveedores y no la de cada empresa.

## Paso 7 — Verificar

Con un trámite de prueba y un usuario de prueba. Es la misma lista que se recorrió en local:

| Qué | Cómo se ve que funciona |
|---|---|
| Consulta de vehículo y persona | Responde en el asistente y deja fila en `consultas.consumo`; el SuperAdmin la ve en `GET /api/v1/superadmin/consultas/consumo?tenantId=…&desde=…&hasta=…` |
| Avalúo, impronta, Confirmación RUNT | El valor sugerido carga, la impronta se genera, «Consultar ahora» deja su intento |
| Correo de cambio de estado | Llega. En `notificaciones.entregas` queda `enviado`; en core-api el aviso queda `encolado`: core-api solo lo deja en la cola |
| Invitación y recuperación de contraseña | Llegan |
| Canales y buzón de pruebas (SuperAdmin → Notificaciones) | Los canales muestran su remitente y el buzón envía |
| Mensajes muertos (SuperAdmin → Plataforma → Mensajes muertos) | Carga. Un correo rechazado por el proveedor aparece con su causa y el código SMTP |
| Validación de identidad real | Queda en `consultas.validaciones_kyverum`, su aviso en `consultas.avisos` con `resultado = publicado`, y el trámite se aprueba solo |
| ICT | Los trámites siguen entrando; el log de core-ict no tiene `Unauthenticated` ni errores de Consultas |
| Bus | En `tramites.outbox`, `published_at IS NULL` no crece |

## Paso 8 — Alerta de mensajes muertos

`deploy/rabbitmq/alerta-dlq.sh` avisa al webhook de operaciones cuando llega algo a una cola de mensajes muertos.
Instalar el timer de `deploy/rabbitmq/systemd/` (`*.example`) como dice `deploy/rabbitmq/README.md`, con
`OPS_ALERT_WEBHOOK_URL` y `FLIT_AMBIENTE` en `/etc/flit-rabbitmq/env`. `OPS_ALERT_WEBHOOK_URL` es el mismo webhook de
operaciones que ya usa la alerta de certificados (`deploy/edge/acme`). Revisa cada 5 minutos.

**Comprobar:** `systemctl list-timers | grep flit-rabbitmq-dlq`.

## Después, con el despliegue estable

1. **Cortar el token viejo de ICT:** `ICT_SERVICE_TOKEN_ACCEPT_LEGACY=false` y recrear core-api. Desde ahí se rechaza
   el token firmado con el secreto compartido. Para volver atrás, `true`.
2. **Retirar la ruta vieja del aviso de Kyverum:** cuando ya no queden validaciones de antes del corte en proceso
   (`select count(*) from tramites.procedure_instance_biometric_validations where status = 'en_proceso' and provider =
   'kyverum' and created_at < '<fecha del despliegue>'` en 0), se puede quitar `KYVERUM_WEBHOOK_CALLBACK_URL`.

## Qué esperar en la operación

| Si se cae | Qué pasa | Al volver |
|---|---|---|
| core-consultas | El asistente dice «Consulta no disponible». Una validación de identidad creada en ese momento queda en «Error de envío» tras ~30 s | Las consultas vuelven solas (hasta ~15 s más de «no disponible»). Las validaciones en «Error de envío» se reenvían desde Identidad → Acciones → «Reenviar» |
| core-notificaciones | Los correos esperan en `notificaciones.email.send` | Salen solos |
| RabbitMQ | Los eventos y correos esperan en el outbox de cada servicio, que reintenta cada vez más espaciado (hasta 1 min) | Salen en orden |
| El aviso de Kyverum no llega | La validación queda «en proceso» | La conciliación de core-api le pregunta a Kyverum y la aprueba o rechaza en unos minutos |
| Un proveedor de correo rechaza un mensaje | Va directo a mensajes muertos, sin reintentos automáticos | Se corrige la causa (por ejemplo, buzón lleno) y se reintenta desde la pantalla |

## Volver atrás

Volver a desplegar la versión anterior (imagen **y** compose) de **core-api, core-identity y core-ict**, con el `.env`
del paso 0: las variables viejas siguen ahí y el compose anterior se las vuelve a pasar a core-api. core-consultas, core-notificaciones y RabbitMQ pueden seguir corriendo; la versión anterior no los usa.

- **La migración 133 no hay que revertirla.** Solo agrega el estado `encolado` a un CHECK, y el código anterior no lo
  necesita ni le estorba.
- **Validaciones de identidad creadas con la versión nueva:** la versión anterior las resuelve consultando a Kyverum
  directamente.
- **Correos que queden en la cola:** core-notificaciones los envía si sigue arriba.

## Si algo sale mal

| Síntoma | Qué hacer |
|---|---|
| `docker compose config` falla nombrando una variable | Falta en el `.env` (paso 4) |
| core-api no arranca: `Consultas:Remoto:Address`, `Notificaciones:Remoto:Address`, `Tramites:Bus:Habilitado` o `Platform:ServiceClient` | Variables del paso 4, o el compose no es el de esta rama |
| core-consultas no arranca y nombra un proveedor | Ese proveedor está en modo real sin su secreto |
| core-consultas o core-notificaciones se reinician con `permission denied for table __EFMigrationsHistory` | Su esquema ya existía con otro dueño: volver a correr el paso 3 (el script traspasa las tablas) y `up -d` del servicio |
| `docker compose up` falla con `mounts denied` o «no such file» sobre un `.pfx` | `RENTING_API_PFX_CERTIFICATE_PATH` apunta a una ruta que no existe en la VPS (paso 4) |
| Un servicio sigue con un valor viejo después de cambiar el `.env` | Se reinició con `restart`: recrearlo con `up -d <servicio>` |
| El broker se cae con `.erlang.cookie: eacces` | Paso 1: borrar contenedor y volumen, y esperar `healthy` antes de crear usuarios |
| Un servicio no conecta al broker (`ACCESS_REFUSED`) | La clave de su `RABBITMQ_URL_*` no coincide con la del paso 2: volver a correr el script con la misma clave |
| Las consultas fallan con `Unauthenticated` o `PermissionDenied` | El `SVC_*_CLIENT_SECRET` del servicio no coincide en los dos lados, o el reloj de la VPS está desfasado |
| Las validaciones de identidad no se aprueban solas | Revisar `CONSULTAS_KYVERUM_WEBHOOK_CALLBACK_URL` y que nginx deje pasar `/api/v1/consultas/avisos/*`. Mientras tanto la conciliación las resuelve en minutos |
| Un correo no llegó y no está en mensajes muertos | `SMTP_HOST` vacío con el modo consola encendido: el correo quedó en el log de core-notificaciones (paso 4) |
| Un correo no llegó | SuperAdmin → Plataforma → Mensajes muertos: dice la causa del proveedor |
| ICT con `Unauthenticated` | Falta `SVC_ICT_CLIENT_SECRET`, o `ICT_SERVICE_TOKEN_SECRET` se borró (el aviso de estado a ICT lo usa) |

## QA y PDN

Los mismos pasos, en el mismo orden, antes de cada paso de rama. En PDN no se corre `migrar-valores-mock-avaluo.sql`, y
la URL del aviso de Kyverum lleva el host de PDN.
