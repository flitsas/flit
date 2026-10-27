#!/usr/bin/env bash
# Corre un carril de las pruebas de core-api (workflow core-api.yml). Se ejecuta en services/core-api.
#   admin            Flit.Admin.Tests
#   integration      un shard de Flit.Integration.Tests ($SHARD de $SHARDS, por clase)
#   resto            los demás proyectos de prueba de Flit.slnx, en paralelo
#
# Reparto de Integration: las clases se reparten por turno entre los N shards; los 0..N-2 filtran por SUS
# clases y el ULTIMO corre «todo lo que no tomaron los demás». Así una prueba que no se pueda clasificar (DisplayName propio, clase anidada)
# cae en el último shard y nunca se pierde. Cada proceso crea su propia base flit_it_* (PostgresDatabaseFixture).
# Con LISTAR=1 solo imprime cuántas pruebas tomaría el shard (para comprobar que la suma da el total).
set -uo pipefail
carril=${1:?carril: admin | integration | resto}
SHARD=${SHARD:-0}; SHARDS=${SHARDS:-1}
cob=(); [ -n "${COBERTURA:-}" ] && cob=(--collect "XPlat Code Coverage")
contar() { sed -n 's/^    //p' | grep -c . ; }

case "$carril" in
  admin)
    exec dotnet test tests/Flit.Admin.Tests --no-build -c Release --logger "trx;LogFileName=admin.trx" "${cob[@]}" ;;

  integration)
    p=tests/Flit.Integration.Tests
    lista=$(dotnet test $p --no-build -c Release --list-tests) || { echo "::error::--list-tests falló"; echo "$lista" | tail -30; exit 1; }
    total=$(echo "$lista" | contar)
    # Nombre de la prueba -> clase: se quitan los argumentos de las Theory y el último segmento (el método).
    clases=$(echo "$lista" | sed -n 's/^    //p' | sed 's/(.*//; s/\.[^.]*$//' | sort -u)
    n=$(echo "$clases" | grep -c .)
    [ "$total" -gt 0 ] && [ "$n" -gt 0 ] || { echo "::error::Integration sin pruebas listadas (total=$total clases=$n)"; exit 1; }
    # Los caracteres especiales del filtro de VSTest se escapan con \.
    esc() { sed 's/[()&|=!~\\]/\\&/g'; }
    if [ "$SHARD" -lt $((SHARDS-1)) ]; then
      f=$(echo "$clases" | esc | awk -v s="$SHARD" -v n="$SHARDS" '(NR-1)%n==s {printf "%sFullyQualifiedName~%s.", (c++?"|":""), $0}')
    else
      f=$(echo "$clases" | esc | awk -v n="$SHARDS" '(NR-1)%n!=n-1 {printf "%sFullyQualifiedName!~%s.", (c++?"&":""), $0}')
    fi
    echo "Integration: $total pruebas en $n clases; shard $SHARD de $SHARDS"
    if [ -n "${LISTAR:-}" ]; then
      if [ -n "$f" ]; then dotnet test $p --no-build -c Release --list-tests --filter "$f" | contar; else echo "$total"; fi
      exit 0
    fi
    args=(); [ -n "$f" ] && args=(--filter "$f")
    exec dotnet test $p --no-build -c Release "${args[@]}" --logger "trx;LogFileName=integration-$SHARD.trx" "${cob[@]}" ;;

  resto)
    # Proyectos con pruebas de TIEMPO (LoginTimingTests compara la latencia de dos caminos del login con una
    # tolerancia de 2.5x): corren solos y primero, sin competir por CPU con los demás.
    solos="Flit.Modules.Security.Application.Tests"
    pids=(); nombres=(); rc=0
    proyectos=$(grep -o 'Path="[^"]*Tests\.csproj"' Flit.slnx | sed 's/Path="//; s/"$//')
    for csproj in $proyectos; do
      n=$(basename "$csproj" .csproj)
      case " $solos " in *" $n "*)
        if dotnet test "$csproj" --no-build -c Release --logger "trx;LogFileName=$n.trx" "${cob[@]}" > "TestResults-$n.log" 2>&1
        then echo "OK    $n (solo)"; else echo "FALLA $n (solo)"; tail -80 "TestResults-$n.log"; rc=1; fi ;;
      esac
    done
    for csproj in $proyectos; do
      n=$(basename "$csproj" .csproj)
      case $csproj in *Admin.Tests*|*Integration.Tests*) continue ;; esac
      case " $solos " in *" $n "*) continue ;; esac
      dotnet test "$csproj" --no-build -c Release --logger "trx;LogFileName=$n.trx" "${cob[@]}" > "TestResults-$n.log" 2>&1 &
      pids+=($!); nombres+=("$n")
    done
    for i in "${!pids[@]}"; do
      if wait "${pids[$i]}"; then echo "OK    ${nombres[$i]}"; else echo "FALLA ${nombres[$i]}"; tail -80 "TestResults-${nombres[$i]}.log"; rc=1; fi
    done
    grep -h -E '^(Passed!|Failed!|Correcto!|Con error!)' TestResults-*.log || true
    exit $rc ;;

  *) echo "carril desconocido: $carril"; exit 2 ;;
esac
