#!/usr/bin/env bash
# Postgres y RabbitMQ de las pruebas de core-api, con puerto dinámico en 127.0.0.1. Reemplaza a `services:`.
#   - En el pool propio (177) el 5432 y el 5672 son del host.
#   - Con docker rootless, el puerto al azar puede chocar con una conexión saliente del host
#     («bind: address already in use»): se reintenta con otro.
# Exporta ConnectionStrings__Core y FLIT_TEST_RABBITMQ a $GITHUB_ENV.
# Uso: ci-servicios.sh <prefijo>      |  ci-servicios.sh --apagar <prefijo>
set -euo pipefail
if [ "${1:-}" = --apagar ]; then docker rm -f "$2-pg" "$2-mq" >/dev/null 2>&1 || true; exit 0; fi
p=${1:?prefijo de los contenedores}

levantar() { # $1 nombre, $2 puerto interno, resto: argumentos de docker run
  local n=$1 puerto=$2; shift 2
  for i in 1 2 3 4 5; do
    docker rm -f "$n" >/dev/null 2>&1 || true
    if docker run -d --name "$n" -p "127.0.0.1::$puerto" "$@" >/dev/null 2>"/tmp/$n.err"; then return 0; fi
    echo "Intento $i de $n falló: $(tail -1 "/tmp/$n.err")"; sleep 2
  done
  echo "::error::No se pudo levantar $n"; return 1
}
puerto() { docker port "$1" "$2/tcp" | head -1 | sed 's/.*://'; }

# Postgres sin durabilidad (fsync off): el reset de Integration reescribe ~150 tablas por prueba.
levantar "$p-pg" 5432 -e POSTGRES_DB=flit_dev -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres \
  postgres:16-alpine -c fsync=off -c synchronous_commit=off -c full_page_writes=off -c max_connections=300
# Usuario propio: guest solo entra desde loopback dentro del contenedor.
levantar "$p-mq" 5672 -e RABBITMQ_DEFAULT_USER=flit -e RABBITMQ_DEFAULT_PASS=flit-prueba rabbitmq:4.1-alpine

# rabbitmq-diagnostics como el usuario rabbitmq: si corre como root antes de que arranque el servidor, crea
# /var/lib/rabbitmq/.erlang.cookie con dueño root y RabbitMQ muere con «eacces» al leerlo.
for i in $(seq 1 60); do docker exec "$p-pg" pg_isready -U postgres -d flit_dev >/dev/null 2>&1 && break; sleep 1; done
for i in $(seq 1 90); do docker exec -u rabbitmq "$p-mq" rabbitmq-diagnostics -q ping >/dev/null 2>&1 && break; sleep 1; done
docker exec "$p-pg" pg_isready -U postgres -d flit_dev >/dev/null || { echo "::error::Postgres no quedó listo"; exit 1; }
docker exec -u rabbitmq "$p-mq" rabbitmq-diagnostics -q ping >/dev/null || { echo "::error::RabbitMQ no quedó listo"; exit 1; }

pg=$(puerto "$p-pg" 5432); mq=$(puerto "$p-mq" 5672)
echo "ConnectionStrings__Core=Host=127.0.0.1;Port=$pg;Database=flit_dev;Username=postgres;Password=postgres" >> "$GITHUB_ENV"
echo "FLIT_TEST_RABBITMQ=amqp://flit:flit-prueba@127.0.0.1:$mq/" >> "$GITHUB_ENV"
echo "Postgres en 127.0.0.1:$pg, RabbitMQ en 127.0.0.1:$mq"
