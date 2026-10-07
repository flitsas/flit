# RabbitMQ: el bus de eventos de FLIT

[ADR-0064](../../docs/decisions/ADR-0064-datos-separados-eventos-y-reportes.md) y contrato de plataforma §7: un
contenedor por ambiente (hasta k3s), virtual host `flit`, un usuario por servicio. HU #13349.

| Pieza | Dónde |
|---|---|
| Contenedor `rabbitmq` (perfil `bus`), volumen `rabbitmq-datos` | `docker-compose.prod.yml` |
| Virtual host y exchanges de los productores (`flit.<productor>`, topic, durables) | `definitions.json`, se carga al arrancar |
| Usuario por servicio con permisos mínimos | `usuario-de-servicio.sh` |
| Mensajes que viajan | `contracts/asyncapi/` |

## Encenderlo (una vez por ambiente)

1. En el `.env`: `RABBITMQ_ADMIN_USER` y `RABBITMQ_ADMIN_PASSWORD` (administrador del broker, solo para operar).
2. Sumar `bus` a `COMPOSE_PROFILES` y desplegar. La consola de administración queda en
   `127.0.0.1:${RABBITMQ_MGMT_PORT}` (por túnel SSH; no se publica).
3. Un usuario por servicio que use el bus, con su clave en el `.env`:

   ```bash
   CLAVE="$(openssl rand -base64 32)"
   deploy/rabbitmq/usuario-de-servicio.sh consultas "$CLAVE"
   # RABBITMQ_URL_CONSULTAS=amqp://consultas:$CLAVE@rabbitmq:5672/flit
   ```

   Servicios y su exchange: `tramites` (core-api, `flit.tramites`), `plataforma` (core-identity, `flit.plataforma`),
   `consultas` (core-consultas), `notificaciones` (core-notificaciones).

## Permisos

Cada usuario escribe y declara solo en `flit.<servicio>` y en lo suyo (`<servicio>.*`: sus colas, sus colas de
reintento y su exchange `<cola>.reintentos`), y lee cualquier `flit.*` (para atar sus colas a los eventos que consume)
y solo sus colas. Publicar en el exchange de otro servicio o leer sus colas lo rechaza el broker (`ACCESS_REFUSED`).
Ningún servicio necesita escribir en el exchange por defecto: los reintentos van por `<cola>.reintentos`.

## Un exchange nuevo

Un productor nuevo se agrega a `definitions.json` (los consumidores no pueden crear el exchange de otro) y se recarga:
`docker compose -f docker-compose.prod.yml exec rabbitmq rabbitmqctl import_definitions /etc/rabbitmq/definitions.json`.
