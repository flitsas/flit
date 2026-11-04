#!/usr/bin/env bash
# Suite de core-api en UN solo job, para el pool propio de la 177 (workflow core-api.yml, job «pruebas-vps»).
# Se ejecuta en services/core-api, DESPUÉS de compilar una vez (Release) y de migrar flit_dev.
#
# Por qué no la matriz de 6 jobs en la 177 (2026-10-24, 3 mediciones fallidas, carga hasta 336 en 8 núcleos): cada
# job compilaba la solución y levantaba su Postgres y su RabbitMQ, y cada `dotnet test` creía tener los 8 núcleos.
# Aquí se compila una vez y los carriles (los mismos de ci-core-api-pruebas.sh) corren como procesos:
#   - como mucho $PARALELO a la vez (por defecto 3), del más largo al más corto;
#   - cada proceso de pruebas con a lo sumo $XUNIT_HILOS hilos de xUnit;
#   - con `nice`, para que el propio runner (Runner.Worker) no se quede sin CPU («lost communication»);
#   - cada carril con SU servidor Postgres (en disco con fsync off, con flit_dev ya migrada copiada) y SU vhost de RabbitMQ: en GitHub
#     cada carril tiene su máquina, y aquí no deben pisarse (pg_snapshot_xmin es de todo el servidor).
set -uo pipefail
PARALELO=${PARALELO:-3}
export XUNIT_HILOS=${XUNIT_HILOS:-2}
# Admin es el camino crítico (2026-11-03: 26,6 min con 2 hilos, corriendo solo con la CPU ociosa desde el minuto 18).
XUNIT_HILOS_ADMIN=${XUNIT_HILOS_ADMIN:-4}
export MAX_PARALELO=${MAX_PARALELO:-2}   # proyectos simultáneos dentro del carril «resto»
SHARDS=4
base=${ConnectionStrings__Core:?falta ConnectionStrings__Core (ci-servicios.sh)}
pg_cont=${CI_PG_CONTENEDOR:?falta CI_PG_CONTENEDOR}

# Orden: del carril más largo al más corto, para que el último en arrancar no alargue la corrida.
# ADMIN_SHARDS (var CI_VPS_ADMIN_SHARDS) parte admin por clase. Por defecto 1: con 2 (2026-11-04, run 41838341625) cada
# proceso de admin ocupó ~3,5 GB, el slice de runners (MemoryHigh 12G) se quedó sin memoria y la 177 entró en thrashing
# (carga 220, 79 % de CPU en el kernel). Subirlo solo con más memoria para el slice.
export ADMIN_SHARDS=${ADMIN_SHARDS:-1}
carriles=()
for ((i = 0; i < ADMIN_SHARDS; i++)); do carriles+=("admin:$i"); done
carriles+=(integration:0 integration:1 integration:2 integration:3 resto)

mq=${FLIT_TEST_RABBITMQ:?falta FLIT_TEST_RABBITMQ (ci-servicios.sh)}
mq_cont=${CI_MQ_CONTENEDOR:?falta CI_MQ_CONTENEDOR}
prefijo=${CI_PREFIJO:?falta CI_PREFIJO}
servicios=../../.github/scripts/ci-servicios.sh
contar_tablas() { docker exec "$1" psql -U postgres -d flit_dev -qAt -c \
  "SELECT count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema')"; }

# Cada carril con SU servidor Postgres (ver ci-servicios.sh --pg) y su vhost de RabbitMQ: en GitHub cada carril tiene
# su máquina. flit_dev ya migrada se copia con sus roles (pg_dumpall --roles-only + pg_dump), sin volver a migrar.
roles=$(docker exec "$pg_cont" pg_dumpall -U postgres --roles-only)
dump="${RUNNER_TEMP:-/tmp}/flit_dev-$prefijo.dump"
docker exec "$pg_cont" pg_dump -U postgres -Fc flit_dev > "$dump" || { echo "::error::pg_dump de flit_dev falló"; exit 1; }
tablas=$(contar_tablas "$pg_cont")
declare -A cadena_pg=() url_mq=()
for c in "${carriles[@]}"; do
  id=${c/:/-}; cont="$prefijo-pg-$id"
  puerto=$("$servicios" "$prefijo" --pg "$id") || exit 1
  echo "$roles" | docker exec -i "$cont" psql -U postgres -q >/dev/null 2>&1   # «postgres ya existe»: se ignora
  docker exec -i "$cont" pg_restore -U postgres -d flit_dev < "$dump" 2> "TestResults-restore-$id.log" || true
  t=$(contar_tablas "$cont")
  [ "$t" = "$tablas" ] || { echo "::error::$cont quedó con $t tablas y flit_dev tiene $tablas"; cat "TestResults-restore-$id.log"; exit 1; }
  cadena_pg[$c]=$(echo "$base" | sed -E "s/Port=[0-9]+/Port=$puerto/")
  docker exec -u rabbitmq "$mq_cont" rabbitmqctl -q add_vhost "$id" >/dev/null
  docker exec -u rabbitmq "$mq_cont" rabbitmqctl -q set_permissions -p "$id" flit '.*' '.*' '.*' >/dev/null
  url_mq[$c]="${mq%/}/$id"
done
rm -f "$dump"
echo "Postgres y vhost por carril listos ($tablas tablas). Paralelo=$PARALELO, hilos xUnit=$XUNIT_HILOS (admin $XUNIT_HILOS_ADMIN), resto=$MAX_PARALELO."

declare -A pid_de=() inicio=()
rc=0; hechos=0
correr() { # $1 = carril[:shard]
  local c=$1 carril=${1%%:*} shard=0 log="TestResults-carril-${1/:/-}.log" hilos=$XUNIT_HILOS
  [ "$carril" != "$c" ] && shard=${c#*:}
  [ "$carril" = admin ] && hilos=$XUNIT_HILOS_ADMIN
  (
    export ConnectionStrings__Core="${cadena_pg[$c]}" FLIT_TEST_RABBITMQ="${url_mq[$c]}" SHARD=$shard SHARDS=$SHARDS XUNIT_HILOS=$hilos
    exec nice -n 10 ../../.github/scripts/ci-core-api-pruebas.sh "$carril"
  ) > "$log" 2>&1 &
  pid_de[$c]=$!; inicio[$c]=$SECONDS
  echo "▶ $c (pid ${pid_de[$c]})"
}
terminar() { # $1 = carril que terminó, $2 = código
  local c=$1 r=$2 log="TestResults-carril-${1/:/-}.log" t=$(( (SECONDS - inicio[$1]) / 60 ))
  if [ "$r" = 0 ]; then echo "✔ $c en ${t} min"
  else echo "::error::Falló el carril $c (exit $r, ${t} min)"; echo "::group::Log de $c"; tail -150 "$log"; echo "::endgroup::"; rc=1; fi
  grep -h -E '^(Passed!|Failed!|Correcto!|Con error!)' "$log" | sed "s/^/   $c: /" || true
  unset "pid_de[$c]"; hechos=$((hechos + 1))
}

pendientes=("${carriles[@]}")
while [ ${#pendientes[@]} -gt 0 ] || [ ${#pid_de[@]} -gt 0 ]; do
  while [ ${#pendientes[@]} -gt 0 ] && [ ${#pid_de[@]} -lt "$PARALELO" ]; do
    correr "${pendientes[0]}"; pendientes=("${pendientes[@]:1}")
  done
  wait -n -p fin; r=$?
  for c in "${!pid_de[@]}"; do [ "${pid_de[$c]}" = "$fin" ] && terminar "$c" "$r" && break; done
done

# Defensa: si el bucle terminara antes de tiempo (un error de expansión lo corta sin abortar), no dar verde.
if [ "$hechos" -ne ${#carriles[@]} ]; then echo "::error::Solo terminaron $hechos de ${#carriles[@]} carriles"; rc=1; fi

exit $rc   # los contenedores por carril los borra «ci-servicios.sh --apagar» (etiqueta del prefijo)
