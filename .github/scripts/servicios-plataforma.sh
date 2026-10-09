#!/usr/bin/env bash
# Servicios de plataforma (Epic #13316, HU #13341): los que nacieron de la plantilla services/core-plantilla. Se
# reconocen por su src/Flit.*.Api/ServicioSettings.cs; no hay que registrarlos en ningún workflow.
#
#   servicios-plataforma.sh listar            -> servicios=["core-plantilla", ...]   (para $GITHUB_OUTPUT)
#   servicios-plataforma.sh contrato <dir>    -> falla si el servicio no tiene su .proto en contracts/proto
#   servicios-plataforma.sh solucion <dir>    -> ruta de su .slnx
set -euo pipefail

raiz="$(cd "$(dirname "$0")/../.." && pwd)"

codigo() {
  # El código del servicio (minúsculas) es la constante Codigo de su ServicioSettings.cs.
  local settings
  settings=$(ls "$raiz/services/$1"/src/Flit.*.Api/ServicioSettings.cs 2>/dev/null | head -n1)
  [ -n "$settings" ] || { echo "::error::services/$1 no tiene src/Flit.*.Api/ServicioSettings.cs" >&2; return 1; }
  sed -n 's/.*public const string Codigo = "\([a-z][a-z0-9-]*\)";.*/\1/p' "$settings" | head -n1
}

case "${1:-}" in
  listar)
    lista=""
    for settings in "$raiz"/services/core-*/src/Flit.*.Api/ServicioSettings.cs; do
      [ -f "$settings" ] || continue
      dir=$(basename "$(dirname "$(dirname "$(dirname "$settings")")")")
      lista="${lista:+$lista,}\"$dir\""
    done
    echo "servicios=[${lista}]"
    ;;
  contrato)
    dir="${2:?falta el directorio del servicio (p. ej. core-consultas)}"
    cod=$(codigo "$dir")
    if [ "$cod" = "plantilla" ]; then
      echo "core-plantilla es la plantilla: no tiene contrato propio."
      exit 0
    fi
    protos=$(ls "$raiz/contracts/proto/flit/$cod/v1/"*.proto 2>/dev/null || true)
    if [ -z "$protos" ]; then
      echo "::error file=services/$dir::Falta el contrato gRPC de $dir: crea contracts/proto/flit/$cod/v1/$cod.proto (paquete flit.$cod.v1). Ver docs/suite/servicio-nuevo.md, paso 2."
      exit 1
    fi
    echo "Contrato de $dir: $(echo "$protos" | sed "s|$raiz/||" | tr '\n' ' ')"
    ;;
  solucion)
    dir="${2:?falta el directorio del servicio}"
    ls "$raiz/services/$dir"/*.slnx | head -n1 | sed "s|$raiz/||"
    ;;
  *)
    echo "uso: $0 listar | contrato <dir> | solucion <dir>" >&2
    exit 2
    ;;
esac
