#!/usr/bin/env bash
# Epic #13316 (HU #13349, ADR-0064) — usuario de un servicio en el broker, con permisos mínimos:
#   - escribe SOLO en su exchange (flit.<servicio>) y en sus colas y exchanges (<servicio>.*);
#   - configura (declara) solo eso mismo;
#   - lee cualquier exchange de productor (flit.*, para atar sus colas a los eventos que consume) y SOLO sus colas.
# Así un servicio no puede publicar en el exchange de otro ni leer sus colas (AC2).
# Excepción explícita (HU #13354): un tercer argumento opcional con los servicios a los que este puede dejarles TRABAJOS
# (p. ej. «notificaciones»), que suma escritura en flit.<destino>. Sigue sin poder declarar nada ajeno ni leer sus colas.
#
# Uso, en la VPS, con el broker del ambiente arriba (idempotente: correrlo otra vez cambia la clave):
#   CLAVE="$(openssl rand -base64 32)"   # va al .env del ambiente (RABBITMQ_URL_<SERVICIO>), nunca al repo
#   deploy/rabbitmq/usuario-de-servicio.sh consultas "$CLAVE"
#   deploy/rabbitmq/usuario-de-servicio.sh tramites "$CLAVE" notificaciones   # deja trabajos de correo
#
# Variables opcionales: COMPOSE (por defecto "docker compose -f docker-compose.prod.yml"), VHOST (por defecto flit).
set -euo pipefail

servicio="${1:?uso: $0 <servicio> <clave>}"
clave="${2:?uso: $0 <servicio> <clave>}"
trabajos="${3:-}"
compose="${COMPOSE:-docker compose -f docker-compose.prod.yml}"
vhost="${VHOST:-flit}"

if ! [[ "$servicio" =~ ^[a-z][a-z0-9-]{1,40}$ ]]; then
  echo "Nombre de servicio inválido: $servicio (minúsculas, p. ej. consultas)" >&2
  exit 2
fi

ctl() { $compose exec -T rabbitmq rabbitmqctl "$@"; }

if ctl list_users -q | awk '{print $1}' | grep -qx "$servicio"; then
  ctl change_password "$servicio" "$clave" >/dev/null
else
  ctl add_user "$servicio" "$clave" >/dev/null
fi

propio="^(flit\\.${servicio}|${servicio}\\..*)$"
escritura="$propio"
if [[ -n "$trabajos" ]]; then
  destinos=""
  for destino in ${trabajos//,/ }; do
    [[ "$destino" =~ ^[a-z][a-z0-9-]{1,40}$ ]] || { echo "Servicio destino inválido: $destino" >&2; exit 2; }
    destinos="${destinos}|flit\\.${destino}"
  done
  escritura="^(flit\\.${servicio}${destinos}|${servicio}\\..*)$"
fi
lectura="^(flit\\..*|${servicio}\\..*)$"
ctl set_permissions -p "$vhost" "$servicio" "$propio" "$escritura" "$lectura" >/dev/null

echo "Listo: usuario '$servicio' en el vhost '$vhost' (escribe en flit.$servicio${trabajos:+, trabajos a $trabajos} y $servicio.*; lee flit.* y sus colas)."
echo "Cadena para el servicio: amqp://$servicio:<clave>@rabbitmq:5672/$vhost"
