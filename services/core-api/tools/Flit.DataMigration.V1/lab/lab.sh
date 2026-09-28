#!/usr/bin/env bash
# Laboratorio local del migrador V1 → V2 (HU #13052 del Epic #13046).
#
#   ./lab.sh preparar [--recrear]   clona la base de dev en la de laboratorio y aplica las migraciones
#   ./lab.sh ids                    escribe out/ids-transfer.txt y out/ids-registration.txt (Entregado)
#   ./lab.sh pdf                    levanta el servicio de PDF de V1 (BackSrvPdfService) en local
#   ./lab.sh v1                     levanta V1 (BackCrudTransfer) contra la copia, sin tocar su .env
#   ./lab.sh migrar <args>          corre el migrador contra el laboratorio (args del CLI: --tipo, --ids…)
#   ./lab.sh estado                 resume lo migrado en el laboratorio
#
# La configuración vive en lab.env (ignorado por git; plantilla en lab.env.example).
# Regla del laboratorio: toda base de datos es local. Si algo apunta fuera, el script se detiene.
set -euo pipefail

LAB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CORE_API="$(cd "$LAB_DIR/../../.." && pwd)"
OUT="$LAB_DIR/out"

if [[ ! -f "$LAB_DIR/lab.env" ]]; then
  echo "Falta $LAB_DIR/lab.env. Cópialo de lab.env.example y ajústalo." >&2
  exit 1
fi
set -a; source "$LAB_DIR/lab.env"; set +a

falla() { echo "lab: $*" >&2; exit 1; }

es_local() {
  case "$1" in localhost|127.0.0.1|::1) return 0 ;; *) return 1 ;; esac
}

exigir_local() { # nombre-de-variable valor
  es_local "$2" || falla "$1=$2 no es local. El laboratorio solo trabaja contra bases locales."
}

exigir_local LAB_PG_HOST "$LAB_PG_HOST"

pg() { PGPASSWORD="$LAB_PG_PASSWORD" psql -h "$LAB_PG_HOST" -p "$LAB_PG_PORT" -U "$LAB_PG_USER" -v ON_ERROR_STOP=1 "$@"; }
cadena() { echo "Host=$LAB_PG_HOST;Port=$LAB_PG_PORT;Database=$1;Username=$LAB_PG_USER;Password=$LAB_PG_PASSWORD;Include Error Detail=true"; }
existe_db() { [[ "$(pg -d postgres -Atc "select 1 from pg_database where datname = '$1'")" == "1" ]]; }

preparar() {
  [[ "$LAB_V2_DB" != "$LAB_V2_TEMPLATE_DB" ]] || falla "LAB_V2_DB no puede ser la base de dev: se clonaría sobre sí misma."
  if [[ "${1:-}" == "--recrear" ]] && existe_db "$LAB_V2_DB"; then
    echo "Borrando $LAB_V2_DB…"
    pg -d postgres -c "DROP DATABASE \"$LAB_V2_DB\" WITH (FORCE);" >/dev/null
  fi
  if ! existe_db "$LAB_V2_DB"; then
    echo "Clonando $LAB_V2_TEMPLATE_DB en $LAB_V2_DB (la de dev no se modifica)…"
    pg -d postgres -c "CREATE DATABASE \"$LAB_V2_DB\" TEMPLATE \"$LAB_V2_TEMPLATE_DB\";" >/dev/null
  fi
  echo "Aplicando migraciones de la rama a $LAB_V2_DB…"
  (cd "$CORE_API" && ConnectionStrings__Core="$(cadena "$LAB_V2_DB")" dotnet ef database update \
      --project src/Flit.Infrastructure --startup-project src/Flit.Api) | grep -E "Applying|Done|rror" || true
}

ids() {
  mkdir -p "$OUT"
  for tipo in transfer registration; do
    pg -d "$LAB_V1_DB" -At -v tipo="$tipo" -f "$LAB_DIR/entregados.sql" > "$OUT/ids-$tipo.txt"
    echo "$tipo: $(wc -l < "$OUT/ids-$tipo.txt" | tr -d ' ') ids en $OUT/ids-$tipo.txt"
  done
}

pdf() {
  # V1 arma sus PDF (FUR, portada, cartas selfie, mandato…) con un servicio gRPC que en el clúster
  # resuelve como back-svc-pdfservice-grpc-pdn. Desde local ese nombre no existe: sin este servicio,
  # la instancia 3 reporta todas esas piezas como faltantes.
  [[ -d "$LAB_PDF_REPO" ]] || falla "No encuentro el servicio de PDF en $LAB_PDF_REPO."
  [[ -x "$LAB_CHROME_PATH" ]] || falla "No encuentro Chrome/Chromium en $LAB_CHROME_PATH."
  mkdir -p "${TMPDIR:-/tmp}/pdf-service"
  echo "Servicio de PDF en localhost:$LAB_PDF_PORT con $LAB_CHROME_PATH."
  cd "$LAB_PDF_REPO"
  # El proyecto apunta a .NET 9; DOTNET_ROLL_FORWARD lo deja correr sobre un SDK más nuevo.
  DOTNET_ROLL_FORWARD=Major \
  PdfService__GrpcPort="$LAB_PDF_PORT" \
  PdfService__ChromiumPath="$LAB_CHROME_PATH" \
  PdfService__TempDirectory="${TMPDIR:-/tmp}/pdf-service" \
  PdfService__PoolMinSize=1 PdfService__PoolMaxSize=3 PdfService__MaxConcurrency=3 \
    exec dotnet run
}

v1() {
  [[ -d "$LAB_V1_REPO" ]] || falla "No encuentro el repositorio de V1 en $LAB_V1_REPO."
  mkdir -p "$OUT"
  echo "V1 en http://localhost:$LAB_V1_PORT contra $LAB_V1_DB (su .env queda intacto; estas variables lo pisan)."
  cd "$LAB_V1_REPO"
  # dotenv no pisa variables que ya existen: lo que se exporta aquí gana sobre el .env de V1.
  TRANSFER_DB_HOST="$LAB_PG_HOST" \
  TRANSFER_DB_PORT="$LAB_PG_PORT" \
  TRANSFER_DB_NAME="$LAB_V1_DB" \
  TRANSFER_DB_USER="$LAB_PG_USER" \
  TRANSFER_DB_PASSWORD="$LAB_PG_PASSWORD" \
  TRANSFER_DB_SYNCHRONIZE=false \
  APP_PORT="$LAB_V1_PORT" \
  FILE_MANAGER_API_BASE_URL="$LAB_SOURCE_FM_URL" \
  PDF_SERVICE_URL="dns:///localhost:$LAB_PDF_PORT" \
    exec npm run dev
}

migrar() {
  local v1src v2dst
  v1src="$(cadena "$LAB_V1_DB")"; v2dst="$(cadena "$LAB_V2_DB")"
  existe_db "$LAB_V2_DB" || falla "No existe $LAB_V2_DB. Corre primero: ./lab.sh preparar"
  cd "$CORE_API"
  FLITMIG_ConnectionStrings__V1Source="$v1src" \
  FLITMIG_ConnectionStrings__V2Target="$v2dst" \
  FLITMIG_Migration__BatchId="$LAB_BATCH_ID" \
  FLITMIG_Migration__CreateTenantIfMissing=true \
  FLITMIG_Attachments__Mode=Copy \
  FLITMIG_SourceFileManager__BaseUrl="$LAB_SOURCE_FM_URL" \
  FLITMIG_SourceFileManager__AuthToken= \
  FLITMIG_TargetFileManager__BaseUrl="$LAB_TARGET_FM_URL" \
  FLITMIG_TargetFileManager__AuthToken= \
  FLITMIG_V1Snapshot__BaseUrl="http://localhost:$LAB_V1_PORT/" \
  FLITMIG_V1Snapshot__AuthToken= \
    dotnet run --project tools/Flit.DataMigration.V1 -- "$@"
}

estado() {
  pg -d "$LAB_V2_DB" -c "
    SELECT m.v1_table, m.final_status, count(*) AS tramites,
           count(*) FILTER (WHERE p.id IS NULL) AS huerfanos
    FROM   migration.migration_map m
    LEFT   JOIN tramites.procedure_instances p ON p.id = m.v2_id
    WHERE  m.batch_id = '$LAB_BATCH_ID'
    GROUP  BY 1, 2 ORDER BY 1, 2;"
}

case "${1:-}" in
  preparar) shift; preparar "$@" ;;
  ids)      ids ;;
  pdf)      pdf ;;
  v1)       v1 ;;
  migrar)   shift; migrar "$@" ;;
  estado)   estado ;;
  *) sed -n '2,12p' "$0"; exit 1 ;;
esac
