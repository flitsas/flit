#!/usr/bin/env bash
# Epic #13316 (HU #13352, ADR-0064) — alerta de mensajes muertos. Revisa las colas <cola>.dlq del vhost del broker y,
# si alguna tiene más mensajes que en la revisión anterior, avisa al webhook de operaciones con la cola y la cantidad
# (mismo canal que la alerta de certificados: deploy/edge/acme). Se corre con el timer de systemd/ cada 5 minutos.
# Una cola que sigue con los mismos mensajes no vuelve a avisar; si se vacía y vuelve a llenarse, sí.
#
# Variables requeridas (del entorno de la unidad systemd, nunca del repo):
#   OPS_ALERT_WEBHOOK_URL   webhook compatible Slack/Teams (payload {"text": "..."})
# Opcionales:
#   FLIT_AMBIENTE           etiqueta del mensaje (DEV, QA, PDN)
#   COMPOSE                 por defecto "docker compose -f docker-compose.prod.yml" (correr desde la raíz del repo)
#   VHOST                   por defecto flit
#   ESTADO                  archivo con los conteos de la revisión anterior; por defecto /var/lib/flit-rabbitmq/dlq.estado
set -euo pipefail

: "${OPS_ALERT_WEBHOOK_URL:?OPS_ALERT_WEBHOOK_URL no definida: sin ella la alerta no tiene a dónde ir}"
compose="${COMPOSE:-docker compose -f docker-compose.prod.yml}"
vhost="${VHOST:-flit}"
estado="${ESTADO:-/var/lib/flit-rabbitmq/dlq.estado}"
ambiente="${FLIT_AMBIENTE:-}"

log() {
  local nivel="$1"; shift
  command -v logger >/dev/null 2>&1 && logger -t flit-rabbitmq-dlq -p "user.${nivel}" -- "$*"
  printf '[%s] %s: %s\n' "$(date -u +%FT%TZ)" "$nivel" "$*" >&2
}

colas="$($compose exec -T rabbitmq rabbitmqctl list_queues -p "$vhost" -q --no-table-headers name messages)" \
  || { log err "no se pudo leer las colas del broker"; exit 1; }

mkdir -p "$(dirname "$estado")"
touch "$estado"
nuevo="$(mktemp)"
trap 'rm -f "$nuevo"' EXIT

avisos=()
while read -r cola mensajes; do
  [[ "$cola" == *.dlq ]] || continue
  [[ "$mensajes" =~ ^[0-9]+$ ]] || continue
  printf '%s %s\n' "$cola" "$mensajes" >>"$nuevo"
  antes="$(awk -v c="$cola" '$1 == c { print $2 }' "$estado")"
  antes="${antes:-0}"
  if (( mensajes > antes )); then
    avisos+=("• ${cola}: ${mensajes} mensaje(s) muerto(s) (antes ${antes})")
  fi
done <<<"$colas"

if (( ${#avisos[@]} > 0 )); then
  titulo="FLIT${ambiente:+ ${ambiente}}: mensajes muertos en RabbitMQ (vhost ${vhost})"
  texto="$(printf '%s\n' "$titulo" "${avisos[@]}" "Revisar: rabbitmqctl list_queues -p ${vhost} name messages | grep dlq")"
  payload="$(python3 -c 'import json,sys; print(json.dumps({"text": sys.stdin.read()}))' <<<"$texto")"
  if curl -sS -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' --max-time 10 -d "$payload" "$OPS_ALERT_WEBHOOK_URL" | grep -qE '^2'; then
    log info "alerta enviada (${#avisos[@]} cola(s))"
  else
    log err "no se pudo enviar la alerta al webhook; se reintenta en la próxima revisión"
    exit 1 # sin actualizar el estado: la próxima revisión vuelve a avisar
  fi
fi

mv "$nuevo" "$estado"
trap - EXIT
