#!/usr/bin/env bash
# Limpieza del runner propio (pool de la 177), al empezar y al terminar cada job de core-api.
# Un job cancelado (timeout o push nuevo) dejaba vivos el servidor de Roslyn (VBCSCompiler), nodos de MSBuild,
# testhosts y los contenedores ci-* (2026-10-24: carga 265 en la 177 después de cancelar).
#   - Procesos: solo los dotnet/VBCSCompiler/MSBuild/testhost cuya línea de comandos o cwd está bajo la carpeta de
#     ESTE runner (/home/runner/rN/). Los runners comparten usuario, pero cada uno corre un job a la vez: lo que
#     quede bajo su carpeta es de un job suyo ya terminado.
#   - Contenedores: los que ci-servicios.sh etiquetó con el nombre de este runner.
# Uso: ci-limpiar-runner.sh [--final]   (--final apaga además los servidores de compilación del SDK del job)
set -uo pipefail
[ "${RUNNER_ENVIRONMENT:-}" = self-hosted ] || exit 0
raiz=$(dirname "$(dirname "${RUNNER_WORKSPACE:?}")")   # /home/runner/rN

if [ "${1:-}" = --final ]; then dotnet build-server shutdown >/dev/null 2>&1 || true; fi

n=0
for d in /proc/[0-9]*; do
  pid=${d#/proc/}
  [ "$pid" = $$ ] && continue
  [ -O "$d" ] || continue
  comm=$(cat "$d/comm" 2>/dev/null) || continue
  case "$comm" in dotnet|VBCSCompiler|MSBuild|testhost*|Flit.*) ;; *) continue ;; esac
  cmd=$(tr '\0' ' ' < "$d/cmdline" 2>/dev/null) || continue
  cwd=$(readlink "$d/cwd" 2>/dev/null || true)
  case "$cmd $cwd/" in *"$raiz/"*) kill -9 "$pid" 2>/dev/null && n=$((n+1)) && echo "Proceso residual $pid ($comm) terminado." ;; esac
done

ids=$(docker ps -aq --filter "label=flit-ci.runner=${RUNNER_NAME:?}" 2>/dev/null || true)
[ -n "$ids" ] && docker rm -f $ids >/dev/null 2>&1 && echo "Contenedores residuales borrados: $(echo "$ids" | wc -l)."
echo "Limpieza de $RUNNER_NAME: $n procesos."
