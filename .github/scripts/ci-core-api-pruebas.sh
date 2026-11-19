#!/usr/bin/env bash
# Corre un carril de las pruebas de core-api (workflow core-api.yml). Se ejecuta en services/core-api.
#   admin            Flit.Admin.Tests (entero; o un shard $SHARD de $ADMIN_SHARDS si ADMIN_SHARDS > 1)
#   integration      un shard de Flit.Integration.Tests ($SHARD de $SHARDS, por clase)
#   resto            los demás proyectos de prueba de Flit.slnx, en paralelo
#
# ADMIN_SHARDS es aparte de SHARDS porque la matriz de GitHub pone SHARDS=4 a todos los carriles y allí admin va entero;
# solo el pool propio (ci-core-api-vps.sh) parte admin.
# Reparto por clase (Integration y admin partido): las clases se reparten entre los N shards (por TIEMPO si hay
# .github/ci/tiempos-<carril>.tsv, si no por turno; ver por_clase); los 0..N-2 filtran por SUS
# clases y el ULTIMO corre «todo lo que no tomaron los demás». Así una prueba que no se pueda clasificar (DisplayName propio, clase anidada)
# cae en el último shard y nunca se pierde. Cada proceso crea su propia base flit_it_* (PostgresDatabaseFixture).
# Con LISTAR=1 solo imprime cuántas pruebas tomaría el shard (para comprobar que la suma da el total).
set -uo pipefail
carril=${1:?carril: admin | integration | resto}
SHARD=${SHARD:-0}; SHARDS=${SHARDS:-1}
cob=(); [ -n "${COBERTURA:-}" ] && cob=(--collect "XPlat Code Coverage")
# Pool propio (ci-core-api-vps.sh): XUNIT_HILOS limita los hilos de xUnit de cada proceso y MAX_PARALELO, cuántos
# proyectos de «resto» corren a la vez. Sin ellas (GitHub), xUnit usa todos los núcleos y «resto» lanza todo junto.
hilos=(); [ -n "${XUNIT_HILOS:-}" ] && hilos=(-- "xUnit.MaxParallelThreads=$XUNIT_HILOS")
contar() { sed -n 's/^    //p' | grep -c . ; }

# Admin nombra la prueba que se cuelga (HU14397): --blame-hang la corta a los 8 min y deja su nombre en el log.
colgada=(--blame-hang --blame-hang-timeout 8min --blame-hang-dump-type none)

# Corre el shard $SHARD de $2 del proyecto $1 repartiendo sus clases por turno (ver arriba). $3 = nombre del .trx.
# $4.. = argumentos extra de dotnet test.
por_clase() {
    local p=$1 n_shards=$2 trx=$3 lista total clases n f
    shift 3
    lista=$(dotnet test "$p" --no-build -c Release --list-tests) || { echo "::error::--list-tests falló"; echo "$lista" | tail -30; exit 1; }
    total=$(echo "$lista" | contar)
    # Nombre de la prueba -> clase: se quitan los argumentos de las Theory y el último segmento (el método).
    clases=$(echo "$lista" | sed -n 's/^    //p' | sed 's/(.*//; s/\.[^.]*$//' | sort -u)
    n=$(echo "$clases" | grep -c .)
    [ "$total" -gt 0 ] && [ "$n" -gt 0 ] || { echo "::error::$p sin pruebas listadas (total=$total clases=$n)"; exit 1; }
    # Los caracteres especiales del filtro de VSTest se escapan con \.
    esc() { sed 's/[()&|=!~\\]/\\&/g'; }
    # Reparto: «shard<TAB>clase» por cada clase. Con $TIEMPOS (TSV «clase<TAB>segundos» de ci-tiempos-por-clase.py) se
    # reparte por tiempo: de la clase más lenta a la más rápida, cada una al shard con menos carga (LPT). Una clase que no
    # está en el archivo (nueva) pesa $TIEMPO_NUEVA s. Sin archivo, por turno como antes. Mismo resultado en todos los
    # shards: la lista de clases y el archivo son los mismos, y el orden se desempata por nombre.
    local reparto modo
    if [ -n "${TIEMPOS:-}" ] && [ -s "$TIEMPOS" ]; then
      modo="por tiempo ($(basename "$TIEMPOS"))"
      reparto=$(echo "$clases" | awk -F'\t' -v nueva="${TIEMPO_NUEVA:-10}" 'NR==FNR {t[$1]=$2; next} {print (($0 in t) ? t[$0] : nueva) "\t" $0}' "$TIEMPOS" - \
        | LC_ALL=C sort -t$'\t' -k1,1gr -k2,2 \
        | awk -F'\t' -v n="$n_shards" '{m=0; for (i=1;i<n;i++) if (carga[i] < carga[m]) m=i; carga[m]+=$1; print m "\t" $2}
                                       END {for (i=0;i<n;i++) printf "  shard %d: %.0f s estimados\n", i, carga[i] > "/dev/stderr"}')
    else
      modo="por turno"
      reparto=$(echo "$clases" | awk -v n="$n_shards" '{print (NR-1)%n "\t" $0}')
    fi
    if [ "$SHARD" -lt $((n_shards-1)) ]; then
      f=$(echo "$reparto" | awk -F'\t' -v s="$SHARD" '$1==s {print $2}' | esc | awk '{printf "%sFullyQualifiedName~%s.", (c++?"|":""), $0}')
    else
      f=$(echo "$reparto" | awk -F'\t' -v s="$SHARD" '$1!=s {print $2}' | esc | awk '{printf "%sFullyQualifiedName!~%s.", (c++?"&":""), $0}')
    fi
    echo "$(basename "$p"): $total pruebas en $n clases; shard $SHARD de $n_shards, reparto $modo"
    if [ -n "${LISTAR:-}" ]; then
      if [ -n "$f" ]; then dotnet test "$p" --no-build -c Release --list-tests --filter "$f" | contar; else echo "$total"; fi
      exit 0
    fi
    local args=(); [ -n "$f" ] && args=(--filter "$f")
    # "${hilos[@]}" va al final: empieza con «--» y lo que sigue son runsettings.
    exec dotnet test "$p" --no-build -c Release "${args[@]}" --logger "trx;LogFileName=$trx.trx" "$@" "${cob[@]}" "${hilos[@]}"
}

case "$carril" in
  admin)
    if [ "${ADMIN_SHARDS:-1}" -gt 1 ]; then
      TIEMPOS=${TIEMPOS-../../.github/ci/tiempos-admin.tsv} por_clase tests/Flit.Admin.Tests "$ADMIN_SHARDS" "admin-$SHARD" "${colgada[@]}"
    fi
    exec dotnet test tests/Flit.Admin.Tests --no-build -c Release --logger "trx;LogFileName=admin.trx" "${colgada[@]}" "${cob[@]}" "${hilos[@]}" ;;

  integration)
    TIEMPOS=${TIEMPOS-../../.github/ci/tiempos-integration.tsv} por_clase tests/Flit.Integration.Tests "$SHARDS" "integration-$SHARD" ;;

  resto)
    # Proyectos con pruebas de TIEMPO (LoginTimingTests compara la latencia de dos caminos del login con una
    # tolerancia de 2.5x): corren solos y primero, sin competir por CPU con los demás.
    solos="Flit.Modules.Security.Application.Tests"
    pids=(); nombres=(); rc=0
    proyectos=$(grep -o 'Path="[^"]*Tests\.csproj"' Flit.slnx | sed 's/Path="//; s/"$//')
    for csproj in $proyectos; do
      n=$(basename "$csproj" .csproj)
      case " $solos " in *" $n "*)
        if dotnet test "$csproj" --no-build -c Release --logger "trx;LogFileName=$n.trx" "${cob[@]}" "${hilos[@]}" > "TestResults-$n.log" 2>&1
        then echo "OK    $n (solo)"; else echo "FALLA $n (solo)"; tail -80 "TestResults-$n.log"; rc=1; fi ;;
      esac
    done
    for csproj in $proyectos; do
      n=$(basename "$csproj" .csproj)
      case $csproj in *Admin.Tests*|*Integration.Tests*) continue ;; esac
      case " $solos " in *" $n "*) continue ;; esac
      while [ -n "${MAX_PARALELO:-}" ] && [ "$(jobs -rp | wc -l)" -ge "$MAX_PARALELO" ]; do sleep 2; done   # sin `wait -n`: reaparía un pid que se espera abajo
      dotnet test "$csproj" --no-build -c Release --logger "trx;LogFileName=$n.trx" "${cob[@]}" "${hilos[@]}" > "TestResults-$n.log" 2>&1 &
      pids+=($!); nombres+=("$n")
    done
    for i in "${!pids[@]}"; do
      if wait "${pids[$i]}"; then echo "OK    ${nombres[$i]}"; else echo "FALLA ${nombres[$i]}"; tail -80 "TestResults-${nombres[$i]}.log"; rc=1; fi
    done
    grep -h -E '^(Passed!|Failed!|Correcto!|Con error!)' TestResults-*.log || true
    exit $rc ;;

  *) echo "carril desconocido: $carril"; exit 2 ;;
esac
