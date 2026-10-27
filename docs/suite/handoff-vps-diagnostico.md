# Handoff de VPS: Diagnóstico Vehicular

HU #13734 (F00.1-H8, Feature #13715). Para el líder técnico. Cada paso trae qué hacer, cómo comprobarlo y cómo volver
atrás. Se aplica **primero en DEV**; QA y PDN solo con aprobación del líder y después de que DEV esté estable. Mismo
modelo que [handoff-vps-comparendos.md](handoff-vps-comparendos.md).

**Qué llega.** Dos contenedores en el perfil `diagnostico` del compose:

- `core-diagnostico` (.NET 10): API del producto, detrás del gateway (`/api/v1/diagnostico/*`). Registra su manifiesto
  por el gateway y pregunta a Identidad (gRPC) si la empresa del token tiene Diagnóstico.
- `frontend-diagnostico` (Next.js): la app web en `<amb>.diagnostico.flitsas.online`, con el login del hub.

**Por qué es seguro fusionar antes de encender.** El perfil `diagnostico` no arranca hasta que el `.env` lo pida
(`COMPOSE_PROFILES=…,diagnostico`). Fusionar construye las imágenes, pero no levanta nada.

| Ambiente | Web (`DIAGNOSTICO_PORT`) | API interna (`CORE_DIAGNOSTICO_PORT`) | gRPC | Host |
|---|---|---|---|---|
| DEV | 4024 | 4029 | 8087 | `dev.diagnostico.flitsas.online` |
| QA | 5024 | 5029 | 8087 | `qa.diagnostico.flitsas.online` |
| PDN | 6024 | 6029 | 8087 | `diagnostico.flitsas.online` |

La API y el gRPC no se publican: viven en la red de Docker. Los puertos los exporta el CD por ambiente
([despliegue-y-puertos.md](../despliegue-y-puertos.md) §2.d).

**Los tres ambientes por GitOps.** DEV, QA (Argo CD en la 177) y PDN (Argo CD en la 169) se despliegan con
`deploy-gitops`; el antiguo despliegue por SSH (`deploy-to-vps`) ya no existe en `cd.yml`. La configuración vive en
`flit-gitops/services/flit2-<ambiente>/`, no en el compose ni en el `.env`. Ese job todavía **no** lleva las imágenes de
Diagnóstico: antes de sumarlas a su lista, la `kustomization.yaml` de cada ambiente tiene que declarar
`ghcr.io/flitsas/flitdev/core-diagnostico` y `ghcr.io/flitsas/flitdev/frontend-diagnostico` (si no, `yq -e` corta el
despliegue de todo el ambiente) y los manifiestos tienen que traer las variables de abajo.

## 1. Base de datos: usuario dueño del esquema `diagnostico`

```bash
CLAVE=$(openssl rand -base64 32 | tr -d '/+=')
psql "$ADMIN_URL" -v servicio=diagnostico -v conexiones=20 -v clave="$CLAVE" \
     -f deploy/postgres/servicio-con-esquema-propio.sql
psql "postgresql://flit_diagnostico:$CLAVE@<host>/<base>" -v servicio=diagnostico -v ajeno=tramites \
     -f deploy/postgres/verificar-aislamiento.sql
```

En el `.env`: `CONNECTION_STRING_DIAGNOSTICO=Host=postgres;Port=5432;Database=flit;Username=flit_diagnostico;Password=<CLAVE>;Maximum Pool Size=20`.
El servicio migra su esquema al arrancar.

**Comprobar:** `psql "$ADMIN_URL" -c "\du flit_diagnostico"`. **Volver atrás:** `DROP SCHEMA diagnostico CASCADE; DROP ROLE flit_diagnostico;`
(solo si nunca se usó).

## 2. RabbitMQ: colas, política de 7 días y usuario con destino

Desde [ADR-0075](../decisions/ADR-0075-diagnostico-consultas-por-rabbitmq.md), las consultas del diagnóstico van por el
bus: diagnóstico deja un trabajo en `flit.consultas`, core-consultas lo atiende y publica el resultado en su propio
exchange, y diagnóstico cierra la celda. Sin broker no hay consultas.

**Orden.** Este paso va **antes** de desplegar una imagen de core-diagnostico que publique trabajos de consulta. Sin el
destino `consultas`, el broker le rechaza cada publicación (`ACCESS_REFUSED`) y su outbox se queda reintentando.

### 2.1 Variable obligatoria

`RABBITMQ_URL_DIAGNOSTICO` es obligatoria (`:?`). `docker compose` valida todo el archivo, así que la exige en **todos**
los ambientes que usan `docker-compose.prod.yml`, también en los que no tienen el perfil `diagnostico`. Si falta,
`docker compose` no arranca nada. Hay que escribirla en el `.env` **antes de fusionar**, porque en DEV fusionar es
desplegar.

```
RABBITMQ_URL_DIAGNOSTICO=amqp://diagnostico:<clave>@rabbitmq:5672/flit
```

**Comprobar:** `docker compose -f docker-compose.prod.yml config > /dev/null` no nombra `RABBITMQ_URL_DIAGNOSTICO`.

En DEV y QA por GitOps, el equivalente es la misma cadena (`Platform__Messaging__ConnectionString` de
core-diagnostico) en `flit-gitops/services/flit2-<amb>/`, como sealed secret.

### 2.2 Colas y política de retención

`deploy/rabbitmq/definitions.json` declara por adelantado las tres colas del flujo. Un mensaje publicado en un exchange
sin cola atada se pierde, y estas colas no pueden esperar a que su consumidor arranque por primera vez:

| Cola | La consume | Atada a `flit.consultas` con |
|---|---|---|
| `consultas.diagnostico` | core-consultas (lotes) | `consultas.diagnostico.*` |
| `consultas.diagnostico-express` | core-consultas (Express) | `consultas.diagnostico-express.*` |
| `diagnostico.consultas-resultados` | core-diagnostico | `consultas.resultado.diagnostico` |

También declara la política `dlq-7d`, que borra a los **7 días** (`message-ttl` 604800000 ms) los mensajes de las `.dlq`
de esas tres colas. Llevan placa, VIN y documentos en claro (U1 de ADR-0075, Ley 1581). Las `.retry.<n>` y las `.dlq`
las crea cada consumidor al arrancar, y la política se les aplica sola cuando aparecen.

En un broker que ya corre, recargar las definiciones (agrega lo nuevo y no borra nada):

```bash
docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl import_definitions /etc/rabbitmq/definitions.json
```

**Comprobar:**

```bash
docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl list_queues -p flit name messages consumers policy | grep -e consultas.diagnostico -e consultas-resultados
docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl list_bindings -p flit source_name destination_name routing_key | grep flit.consultas
docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl list_policies -p flit
```

Deben aparecer las tres colas, sus tres bindings, la política `dlq-7d` y las tres `ttl-*` de §2.2.1, todas con
prioridad 10. Cuando los consumidores ya arrancaron, las tres `.dlq` muestran `dlq-7d` en la columna `policy` y cada
cola principal muestra su `ttl-*`. RabbitMQ aplica **una sola** política por cola, la de mayor prioridad. Si en el
ambiente hay otra política cuyo patrón alcance estas colas o sus `.dlq` (`list_policies`), tiene que repetir lo que
definen estas o tener una prioridad menor que 10.

**Volver atrás:** no hay vuelta atrás de `dlq-7d`. Es el control de retención de Ley 1581 sobre datos en claro (U1 de
ADR-0075) y un rollback no lo apaga: si la política estorba, se corrige con otro PR sobre `definitions.json`, revisado
por el security-agent, nunca con `clear_policy`. **No borrar las colas**: se perderían los trabajos pendientes. Para
volver a una versión de los servicios sin consumidores, ver §7.

#### 2.2.1 TTL de las colas principales

Las tres colas principales tienen un TTL de **24 h** (decisión 6 del usuario sobre el reconciliador). Un trabajo o un
resultado que nadie consumió en ese tiempo sale de la cola hacia su `.dlq`, donde `dlq-7d` lo purga a los 7 días. No
se pierde en silencio y la cola no crece sin límite si su consumidor no vuelve. Son tres políticas en
`definitions.json`, una por cola, con patrón exacto para no chocar con `dlq-7d`:

| Política | Cola | `message-ttl` | `dead-letter-exchange` → `dead-letter-routing-key` |
|---|---|---|---|
| `ttl-consultas-diagnostico` | `consultas.diagnostico` | 86400000 | `consultas.diagnostico.reintentos` → `consultas.diagnostico.dlq` |
| `ttl-consultas-diagnostico-express` | `consultas.diagnostico-express` | 86400000 | `consultas.diagnostico-express.reintentos` → `consultas.diagnostico-express.dlq` |
| `ttl-diagnostico-consultas-resultados` | `diagnostico.consultas-resultados` | 86400000 | `diagnostico.consultas-resultados.reintentos` → `diagnostico.consultas-resultados.dlq` |

El destino es el exchange directo de reintentos y DLQ que declara el SDK de cada consumidor al arrancar
(`PlatformConsumer`). La cola principal se declara sin argumentos, por eso la DLQ va en la política. Si el consumidor
nunca arrancó, ese exchange no existe y lo vencido se descarta. Por eso este paso va con los consumidores ya
desplegados (§2.3 y §4).

core-diagnostico necesita el mismo valor: con
`Diagnostico:Consultas:Reconciliador:TtlTrabajo` (por defecto `1.00:00:00`), el reconciliador cierra en ERROR
reprocesable, aunque core-consultas esté caído, toda celda cuyo trabajo se publicó hace más de TTL + `Margen` (30 min).
Ese mensaje ya no está en la cola y no puede duplicarse. El servicio no arranca si `TtlTrabajo <= Margen`.
**Política y opción deben coincidir.** Con un TTL del broker menor que la opción, una celda cuyo trabajo ya venció
esperaría hasta la opción. Con uno mayor, se cerraría en ERROR un trabajo que todavía está en la cola, y si después se
reprocesa, la consulta se duplica.

En la misma sección viven `ToleranciaOrden` (15 min) y la marca de agua. Una celda solo se republica si core-consultas
ya devolvió trabajos de su misma cola publicados más de 15 min después que el suyo. Un lote grande que espera su turno
no se toca.

**Cambiar el TTL en un ambiente** (ejemplo: 12 h = 43200000 ms):

1. En core-diagnostico, la variable `Diagnostico__Consultas__Reconciliador__TtlTrabajo=12:00:00` (compose o
   `flit-gitops/services/flit2-<amb>/`). Reiniciar el servicio.
2. En el broker, las tres políticas con el nuevo `message-ttl`, sin cambiar lo demás:

   ```bash
   MQ="docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl set_policy -p flit --apply-to queues --priority 10"
   $MQ ttl-consultas-diagnostico '^consultas\.diagnostico$' \
     '{"message-ttl":43200000,"dead-letter-exchange":"consultas.diagnostico.reintentos","dead-letter-routing-key":"consultas.diagnostico.dlq"}'
   $MQ ttl-consultas-diagnostico-express '^consultas\.diagnostico-express$' \
     '{"message-ttl":43200000,"dead-letter-exchange":"consultas.diagnostico-express.reintentos","dead-letter-routing-key":"consultas.diagnostico-express.dlq"}'
   $MQ ttl-diagnostico-consultas-resultados '^diagnostico\.consultas-resultados$' \
     '{"message-ttl":43200000,"dead-letter-exchange":"diagnostico.consultas-resultados.reintentos","dead-letter-routing-key":"diagnostico.consultas-resultados.dlq"}'
   ```

   `definitions.json` es uno solo para todos los ambientes. Un `import_definitions` posterior vuelve a dejar las 24 h
   del archivo. Si el cambio es para todos, va por PR al archivo **y** al valor por defecto de `TtlTrabajo`. Una
   prueba (`ReconciliadorTests.El_TTL_por_defecto_es_el_de_la_politica_del_broker_y_lo_vencido_va_a_la_DLQ`) falla si
   no coinciden.

**Comprobar:**

```bash
docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl list_policies -p flit | grep -e ttl- -e dlq-7d
docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl list_queues -p flit name policy effective_policy_definition | grep -e consultas.diagnostico -e consultas-resultados
```

Cada cola principal debe mostrar su `ttl-*` con `message-ttl` y su `dead-letter-exchange`. Cada `.dlq` debe mostrar
`dlq-7d`. En el log de arranque de core-diagnostico aparece `Reconciliador de consultas en marcha: … TTL <valor>`
(evento 137280), y ese valor tiene que coincidir. Los vencidos llegan a la `.dlq` con la cabecera
`x-first-death-reason: expired` y hacen sonar la alerta de DLQ. El reconciliador registra cada cierre por TTL con el
evento 137287.

**Volver atrás del TTL** (solo del TTL, nunca de `dlq-7d`):
`rabbitmqctl clear_policy -p flit ttl-consultas-diagnostico` (y las otras dos). Las colas vuelven a no vencer. Sin TTL
en el broker, un trabajo que espera más que `TtlTrabajo` + `Margen` se cerraría en ERROR aunque siga en la cola. Por
eso se sube también `Diagnostico__Consultas__Reconciliador__TtlTrabajo` a un valor muy alto (por ejemplo,
`30.00:00:00`) hasta reponer las políticas.

### 2.3 Usuario `diagnostico` con destino `consultas`

El usuario necesita escribir en `flit.consultas` además de en `flit.diagnostico`. El script lo recrea con el tercer
argumento, igual que `tramites` → `notificaciones`. **Con la misma clave de hoy no hay que tocar el `.env`.** La clave
se lee del `.env` sin imprimirla:

```bash
CLAVE="$(sed -n 's|^RABBITMQ_URL_DIAGNOSTICO=amqp://diagnostico:\(.*\)@rabbitmq:5672/flit$|\1|p' .env)"
[ -n "$CLAVE" ] || echo "falta RABBITMQ_URL_DIAGNOSTICO en el .env (§2.1)"
deploy/rabbitmq/usuario-de-servicio.sh diagnostico "$CLAVE" consultas
unset CLAVE
```

Si el usuario no existía, generar una clave nueva (`openssl rand -hex 24`, como en el paso 1 de
[handoff-vps-consultas-notificaciones.md](handoff-vps-consultas-notificaciones.md)) y escribirla en la variable de §2.1.
El usuario `consultas` **no cambia**: devuelve los resultados en su propio exchange.

**Comprobar:**

```bash
docker compose -f docker-compose.prod.yml exec -T rabbitmq rabbitmqctl list_user_permissions -p flit diagnostico
```

La columna de escritura debe ser `^(flit\.diagnostico|flit\.consultas|diagnostico\..*)$`. Al arrancar
core-diagnostico, su log no tiene `ACCESS_REFUSED` y en `diagnostico.outbox` no crecen las filas con
`published_at IS NULL`.

**Volver atrás:** volver a correr el script **sin** el tercer argumento y con la misma clave. Desde ese momento,
diagnóstico no puede publicar trabajos de consulta.

### 2.4 Consumidor de resultados y pool de Postgres

core-diagnostico consume `diagnostico.consultas-resultados` con `Concurrencia` 4 y `Prefetch` 8
(`Diagnostico:Consultas:Bus` en su `appsettings.json`). Cada mensaje en curso ocupa **una** conexión de Postgres
mientras dura su transacción. Si se cambia por ambiente, se pasa como `Diagnostico__Consultas__Bus__Concurrencia` y
`Diagnostico__Consultas__Bus__Prefetch`. `Prefetch` tiene que ser mayor o igual que `Concurrencia`.

Con el tope de §1 (`conexiones=20`, `Maximum Pool Size=20`), el consumo cabe con holgura: 4 conexiones del consumidor,
más el reconciliador (1, cada 60 s), el publicador de la outbox (1) y las peticiones HTTP. El trabajador de Postgres
anterior (`Diagnostico:Procesador`) desaparece y libera sus conexiones. Si se sube `Concurrencia`, hay que subir
`conexiones` y `Maximum Pool Size` en la misma cantidad.

**Comprobar** con un lote en curso:

```bash
psql "$ADMIN_URL" -Atc "select count(*) from pg_stat_activity where usename = 'flit_diagnostico';"
```

Debe quedar por debajo de 20. Si llega al tope, el log dice `too many connections for role "flit_diagnostico"` o
`The connection pool has been exhausted`.

**Volver atrás:** bajar `Diagnostico__Consultas__Bus__Concurrencia` y recrear el servicio (`up -d core-diagnostico`).

### 2.5 Qué esperar en la operación

| Si se cae | Qué pasa | Al volver |
|---|---|---|
| core-consultas | Los trabajos esperan en `consultas.diagnostico*` y las celdas siguen PENDIENTE. El reconciliador se pausa: no duplica trabajos ni pasa celdas a ERROR | Las consultas siguen solas |
| core-diagnostico | Los resultados esperan en `diagnostico.consultas-resultados` | Se aplican solos |
| RabbitMQ | Las outbox de los dos servicios acumulan | Publican al volver |
| Un trabajo agota los reintentos de infraestructura | Llega a `consultas.diagnostico*.dlq` y suena `alerta-dlq.sh`, que revisa todas las `.dlq`. El reconciliador re-publica la celda una vez y después la cierra en ERROR (reprocesable) | El mensaje de la `.dlq` se borra a los 7 días |

## 3. Cliente de servicio `svc-diagnostico`

Declarado en `services/core-api/src/Flit.Api/appsettings.json` con `platform.manifest` (manifiesto),
`platform.identidad.read` (suscripción por petición) y `platform.tramites.catalogos` (tipos de trámite publicados).
Falta el secreto, el mismo valor en los dos lados:

```
SVC_DIAGNOSTICO_CLIENT_SECRET=<openssl rand -base64 48>
```

El compose lo pasa a core-api/core-identity (`Suite__Oidc__ServiceClients__svc-diagnostico__Secret`) y a
core-diagnostico (`Platform__ServiceClient__ClientSecret`). **Reiniciar core-api** (y core-identity si está aparte).

El cliente OIDC público `diagnostico` (login de la app web, PKCE) se registra solo con el host del ambiente
(`Suite:Hosts`, §7.2 de [handoff-vps-suite.md](handoff-vps-suite.md)).

## 4. Resto del `.env`

```
COMPOSE_PROFILES=<los que ya tenga>,diagnostico
DIAGNOSTICO_SESSION_SECRET=<openssl rand -base64 48, distinto del del hub y del de Comparendos>
DIAGNOSTICO_IDENTIDAD_ADDRESS=http://core-identity:8083   # obligatoria: vacía = ninguna empresa habilitada (403)
```

La suscripción simulada es solo para pruebas en local y no se pone en ningún ambiente.

El compose ya fija, sin variable del `.env`: `Diagnostico__Tramites__Address=http://core-api:${CORE_API_GRPC_PORT}`
(sin ella `GET /tipos-tramite` responde 503) y `Diagnostico__Fuentes__ImpuestoDisponible=false`.

Archivos (evidencias y PDF del informe, HU #13846): el compose pasa las mismas `FILE_MANAGER_*` de core-api, con el mismo
defecto (`https://ny46zhyeti.execute-api.us-east-1.amazonaws.com/pdn/`, `api/v1/files`, timeout 30 s) y la categoría
`diagnostico` (`DIAGNOSTICO_FILE_MANAGER_CATEGORY`). Si el ambiente ya define `FILE_MANAGER_BASE_URL` o
`FILE_MANAGER_AUTH_TOKEN` para core-api, el diagnóstico las toma igual: no hay que repetirlas. Sin URL no arranca.

### 4.1 Certificado de Data Protection (HU #13751, obligatorio)

Los documentos de propietario y locatario se guardan cifrados. El anillo de llaves vive en
`diagnostico.data_protection_keys`, cifrado con un certificado `.pfx` RSA que **no** está en la base, ni en el repo, ni en
la imagen. Sin él (o con clave errada) `core-diagnostico` **no arranca**, en ningún ambiente (los tres corren como
Development: no hay excepción).

**Uno por ambiente, con respaldo.** Perder el certificado o su clave deja **ilegibles para siempre** los documentos ya
cifrados en ese ambiente. Antes de encender, guardar `.pfx` + clave en el gestor de secretos del equipo (fuera del VPS).
No copiar el de DEV a QA/PDN.

```bash
sudo install -d -m 700 /etc/flit/secrets
CLAVE=$(openssl rand -base64 32 | tr -d '/+=')
openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj "/CN=flit-diagnostico-<amb>"         -keyout /tmp/dp.key -out /tmp/dp.crt
sudo openssl pkcs12 -export -inkey /tmp/dp.key -in /tmp/dp.crt -out /etc/flit/secrets/diagnostico-dp.pfx -passout pass:"$CLAVE"
shred -u /tmp/dp.key /tmp/dp.crt
sudo chmod 400 /etc/flit/secrets/diagnostico-dp.pfx   # legible por el usuario con que corre el contenedor
```

En el `.env` (variables nuevas):

```
DIAGNOSTICO_DP_CERT_FILE=/etc/flit/secrets/diagnostico-dp.pfx   # se monta como secreto en /run/secrets/diagnostico_dp_pfx
DIAGNOSTICO_DP_CERT_PASSWORD=<CLAVE>
```

El compose pasa `Diagnostico__DataProtection__CertificadoRuta=/run/secrets/diagnostico_dp_pfx` y
`Diagnostico__DataProtection__CertificadoClave=${DIAGNOSTICO_DP_CERT_PASSWORD}`. En DEV/QA por GitOps el equivalente es
un sealed secret con el `.pfx` montado como archivo y la clave como variable en `flit-gitops/services/flit2-<amb>/`.

**Comprobar:** el log de arranque no dice «no puede arrancar: las llaves de Data Protection…» y `/health/ready` responde.
**Volver atrás:** detener el servicio (§7); nunca borrar el certificado ni las filas de `data_protection_keys`.

**Rotación (pendiente, sin procedimiento aún).** Hoy el servicio carga un solo certificado: reemplazarlo deja sin poder
descifrar el anillo existente. Hasta que exista soporte para certificados anteriores (solo descifrado) y un runbook de
rotación, **no rotar**; si el certificado se compromete, escalar al líder técnico antes de tocar nada.

## 5. Encender

```bash
docker compose pull core-diagnostico frontend-diagnostico
docker compose up -d core-diagnostico frontend-diagnostico
docker compose logs -f core-diagnostico     # debe decir que migró y registró el manifiesto
```

Si falta una variable, el servicio no arranca y el log dice cuál y de qué variable del `.env` sale.

**Comprobar:**

```bash
docker compose exec core-diagnostico wget -qO- http://localhost:${CORE_DIAGNOSTICO_PORT:-4029}/health/ready
curl -s http://127.0.0.1:${GATEWAY_PORT:-4002}/api/v1/diagnostico/ -o /dev/null -w '%{http_code}\n'   # distinto de 502/503 = el gateway llega al servicio
curl -s http://127.0.0.1:${DIAGNOSTICO_PORT:-4024}/healthz
```

## 6. nginx y hub

1. El server{} de `<amb>.diagnostico.flitsas.online` hoy redirige a `/proximamente/diagnostico` del hub. Cambiar su
   `location /` por `proxy_pass http://127.0.0.1:<DIAGNOSTICO_PORT>;` con las mismas reglas que Comparendos.
   `nginx -t && systemctl reload nginx`. **Volver atrás:** restaurar la redirección.
2. Por PR: quitar `diagnostico` de `Suite:Hosts:ComingSoon` para ese ambiente.
3. El SuperAdmin enciende Diagnóstico por empresa en Compañías → Productos.

## 7. Si algo sale mal

`docker compose stop core-diagnostico frontend-diagnostico` y quitar `diagnostico` de `COMPOSE_PROFILES`: el resto del
stack no depende de ellos. `RABBITMQ_URL_DIAGNOSTICO` se queda en el `.env`, porque el compose la sigue exigiendo (§2.1).

**Volver a una versión sin consultas por el bus** (imagen **y** compose anteriores de core-diagnostico y
core-consultas): el trabajador de Postgres vuelve a atender las celdas. Los mensajes que queden en
`consultas.diagnostico`, `consultas.diagnostico-express` y `diagnostico.consultas-resultados` ya no los consume nadie.
Si la versión anterior todavía **publica** trabajos (tanda A de ADR-0075, sin consumidores), esas colas crecen
durante 24 h, hasta su TTL (§2.2.1). Si la topología de sus consumidores no se declaró nunca, lo vencido se descarta;
si sí se declaró, pasa a la `.dlq`. En ese caso, con confirmación del líder técnico, vaciarlas con `rabbitmqctl purge_queue -p flit <cola>`: son
copias de trabajos que ya atiende el trabajador de Postgres. No se borran las colas ni la política, y el usuario puede
conservar su destino.
