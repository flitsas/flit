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
#   - cada carril con SU copia de flit_dev (CREATE DATABASE … TEMPLATE), en el mismo Postgres: en GitHub cada carril
#     tenía su máquina y su base, y aquí no deben pisarse. Las bases efímeras flit_it_* de Integration ya son por proceso.
set -uo pipefail
PARALELO=${PARALELO:-3}
export XUNIT_HILOS=${XUNIT_HILOS:-2}
export MAX_PARALELO=${MAX_PARALELO:-2}   # proyectos simultáneos dentro del carril «resto»
SHARDS=4
base=${ConnectionStrings__Core:?falta ConnectionStrings__Core (ci-servicios.sh)}
pg_cont=${CI_PG_CONTENEDOR:?falta CI_PG_CONTENEDOR}

# Orden: del carril más largo al más corto, para que el último en arrancar no alargue la corrida.
carriles=(admin integration:0 integration:1 integration:2 integration:3 resto)

psql_pg() { docker exec "$pg_cont" psql -U postgres -d postgres -v ON_ERROR_STOP=1 -qAt -c "$1"; }

for c in "${carriles[@]}"; do
  db="flit_ci_${c/:/_}"
  psql_pg "DROP DATABASE IF EXISTS \"$db\" WITH (FORCE)" >/dev/null
  psql_pg "CREATE DATABASE \"$db\" TEMPLATE flit_dev" >/dev/null || { echo "::error::No se pudo clonar flit_dev en $db"; exit 1; }
done
echo "Bases por carril creadas desde flit_dev. Paralelo=$PARALELO, hilos xUnit=$XUNIT_HILOS, resto=$MAX_PARALELO."

declare -A pid_de=() inicio=()
rc=0; hechos=0
correr() { # $1 = carril[:shard]
  local c=$1 carril=${1%%:*} shard=0 db="flit_ci_${1/:/_}" log="TestResults-carril-${1/:/-}.log"
  [ "$carril" != "$c" ] && shard=${c#*:}
  (
    export ConnectionStrings__Core="${base/Database=flit_dev/Database=$db}" SHARD=$shard SHARDS=$SHARDS
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

for c in "${carriles[@]}"; do psql_pg "DROP DATABASE IF EXISTS \"flit_ci_${c/:/_}\" WITH (FORCE)" >/dev/null 2>&1 || true; done
exit $rc
