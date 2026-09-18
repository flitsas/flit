# Runbook — certificados por dominio de cliente (HU #12426)

> Complementa `deploy/edge/README.md` (enrutamiento, #12421) y
> `deploy/edge/acme/README.md` (diseño de esta HU). Este runbook cubre
> operación recurrente: alta, baja, reversión, verificación post-cambio y
> costo. Ejecutarlo requiere acceso al VPS — nada de esto corre desde este
> repositorio.

## (i) Alta de dominio de cliente — con certificado automático

Continúa donde termina `deploy/edge/README.md §(i)` (pasos 1-3: registro,
DNS, verificación). Desde que `#12425` marca el dominio `verified`:

1. **Nada que hacer a mano** (AC1) — `flit-acme-poll.timer` corre cada 5 min,
   detecta el host `verified` sin certificado (`GET
   .../internal/domains/pending-certificate`, contrato confirmado por #12425)
   y ejecuta `acme.sh --issue --webroot ... -d <host>`. Requisito: el DNS del
   cliente ya debe resolver hacia el borde (paso 2 del alta base) — si el
   poller corre antes de que el DNS propague, el intento falla y se reintenta
   tras el cooldown de 1 h (`FLIT_ACME_COOLDOWN_SECS`).
2. `acme.sh --install-cert` deja el certificado en
   `${ACME_CERT_HOME}/live/<host>/{fullchain.pem,key.pem}` (por defecto
   `/etc/acme/live/<host>/`) — la ruta que el catch-all de nginx resuelve por
   SNI con `ssl_certificate ${ACME_CERT_HOME}/live/$ssl_server_name/fullchain.pem;`
   (nginx ≥ 1.15.9, ver `acme/README.md §Límite de nginx clásico`). **No hace
   falta editar ningún `map` ni bloque `server{}` por host** — con la variable
   nativa, un dominio nuevo empieza a servirse en cuanto el archivo existe en
   esa ruta.
3. `--install-cert` dispara `--reloadcmd` → `notify-certificate.sh`, que hace
   `PUT /api/v1/internal/domains/{host}/certificate` (el backend pasa el
   dominio de `verified` a `active`, contrato en `contratos-api.md`) y luego
   `nginx -t && systemctl reload nginx` — reload, no restart (AC4).
4. **Verificar** (ver §(iv) más abajo).
5. **Requisito de versión de nginx (confirmar en el VPS antes de la primera
   instalación, una sola vez):** `nginx -V 2>&1 | grep -o 'nginx/[0-9.]*'` debe
   ser ≥ `1.15.9` para que `ssl_certificate`/`ssl_certificate_key` acepten la
   variable `$ssl_server_name`. Si el VPS trae una versión anterior, no usar
   este flujo tal cual — ver la variante de respaldo (regenerar `server{}` por
   host, o migrar a OpenResty) en `acme/README.md`, y documentar aquí cuál
   aplica a este VPS una vez confirmado.

## (ii) Baja de dominio de cliente

1. Ejecutar primero la baja funcional de `deploy/edge/README.md §(ii)`
   (desactivar el dominio, confirmar que `/public/branding` vuelve a FLIT).
2. Revocar el certificado (opcional pero recomendado si el cliente deja la
   plataforma de forma definitiva, no solo una pausa):
   ```bash
   ~/.acme.sh/acme.sh --revoke -d <host>
   ~/.acme.sh/acme.sh --remove -d <host>
   rm -rf ~/.acme.sh/<host> "${ACME_CERT_HOME:-/etc/acme}/live/<host>"
   ```
3. Con `ssl_certificate` por variable (`$ssl_server_name`) no hace falta
   quitar ninguna entrada de `map` ni bloque `server{}` — al borrar
   `${ACME_CERT_HOME}/live/<host>/`, ese SNI simplemente deja de tener
   certificado que servir (mismo comportamiento que un host aún no emitido,
   ver `acme/README.md §Handshake de un host verified sin certificado`); no
   requiere `nginx -s reload` para que el efecto aplique en la siguiente
   conexión.
4. Añadir el host a `FLIT_ACME_EXCLUDE_HOSTS` en `/etc/flit-acme/env` si va a
   quedar retirado por un tiempo pero no se quiere revocar el certificado
   todavía (evita alertas de vencimiento sobre un dominio ya inactivo).
5. Si el dominio se re-registra más adelante, repetir el alta completa desde
   cero (nuevo `verified`, nueva emisión) — el `uq_tenant_domains_host`
   parcial ya permite el re-registro (hecho 13).

## (iii) Reversión — volver al dominio FLIT sin downtime

El dominio de FLIT **nunca depende de esta HU**: su certificado y su
`server{}` explícito en el nginx real siguen exactamente igual (AC4,
`ck_tenant_domains_active_requires_verified` no aplica al tráfico servido por
`server_name` explícito, solo al catch-all). Revertir un dominio de cliente
a "solo FLIT" es la baja de §(ii) — no hay un "modo especial de reversión"
adicional: basta con que el backend deje de resolver esa red como `active`
(desactivación) y, opcionalmente, retirar el certificado. En ningún momento
hace falta tocar el `server{}` de FLIT ni reiniciar nginx para lograrlo — el
catch-all simplemente deja de tener un dominio activo detrás.

Si el incidente es en el **poller o el hook** (no en el dominio en sí):

1. `systemctl stop flit-acme-poll.timer` — detiene nuevas emisiones sin
   afectar los certificados ya instalados ni el tráfico servido.
2. Diagnosticar con `journalctl -u flit-acme-poll.service` /
   `journalctl -t flit-acme-notify`.
3. Corregir y `systemctl start flit-acme-poll.timer` — no requiere reload de
   nginx ni afecta dominios ya activos.

## (iv) Verificación post-cambio

```bash
# TLS del dominio de cliente recien activado
curl -sv https://<host-del-cliente>/ 2>&1 | grep -E 'SSL connection|subject:|expire date'

# SNI explicito (confirma que el certificado devuelto es el del host correcto,
# no el del dominio de prueba/default_server)
openssl s_client -connect <host-del-cliente>:443 -servername <host-del-cliente> </dev/null 2>/dev/null \
  | openssl x509 -noout -subject -dates

# Confirmar que el dominio de FLIT sigue con SU certificado, sin cambios
openssl s_client -connect dev.flitsas.online:443 -servername dev.flitsas.online </dev/null 2>/dev/null \
  | openssl x509 -noout -subject -dates

# Confirmar en el backend que el dominio quedo `active`
curl -sS -H "X-Internal-Key: $FLIT_INTERNAL_API_KEY" \
  http://127.0.0.1:${CORE_API_PORT}/api/v1/admin/companies/<tenantId>/domain
```

Esperado: el `subject`/SAN del primer `openssl s_client` es `<host-del-
cliente>`, no el dominio de prueba; el segundo (`dev.flitsas.online`) es
idéntico a antes de instalar esta HU (mismo emisor, misma fecha de emisión si
no le tocaba renovar todavía).

## (v) Costo operativo recurrente y responsable — AC5

| Concepto | Costo | Quién lo asume |
|---|---|---|
| Certificado Let's Encrypt (emisión y renovación) | **$0** — Let's Encrypt es gratuito | N/A |
| Dominio del cliente (registro/renovación anual del dominio en sí) | Variable según el registrador del cliente | **El cliente** — es su dominio, lo compra y renueva él |
| DNS del cliente (mantener el CNAME/A apuntando al borde) | Incluido en el plan de hosting/DNS del cliente | **El cliente** |
| Tiempo de operación (monitoreo de alertas, resolver una renovación fallida, alta/baja de dominio en consola) | Tiempo humano, no facturable directo | **FLIT** (equipo de infraestructura) |
| Cómputo del poller/timer (systemd, ya corre en el VPS existente) | Marginal — mismo VPS, sin recursos nuevos | **FLIT** (costo hundido del VPS) |
| Canal de alerta (webhook Slack/Teams) | Depende de si ya existe una integración; si es nueva, costo de configurarla una vez | **FLIT** |

**Conclusión:** el único costo recurrente con dinero real es el dominio del
cliente, que el cliente ya paga por definición (es su marca). FLIT asume el
costo operativo de mantener el poller, atender alertas y el procedimiento de
alta/baja — sin costo de licencia adicional (Let's Encrypt gratuito, `acme.sh`
open source).

## (vi) Prueba en DEV — AC7

**Prerrequisito:** dominio de prueba de NEW-22 (mismo dominio usado en
`deploy/edge/README.md §(v)` para AC5 de #12421) ya registrado, `verified` y
con el catch-all de #12421 instalado en el VPS de DEV. Esta prueba se apoya
en esa base — si `deploy/edge/README.md §(v)` sigue pendiente, esta tampoco
se puede ejecutar todavía.

Pasos:

1. Confirmar que `flit-acme-poll.timer` y `flit-acme-expiry.timer` están
   activos en el VPS de DEV: `systemctl list-timers | grep flit-acme`.
2. Registrar/verificar el dominio de prueba (si no lo está ya desde #12421).
3. Esperar hasta 5 min (intervalo del poller) o forzar una corrida manual:
   `sudo -u flit-acme /opt/flit/deploy/edge/acme/poll-and-issue.sh`.
4. Verificar en el journal que la emisión y la notificación a `core-api`
   fueron exitosas: `journalctl -t flit-acme-poll -t flit-acme-notify -n 50`.
5. Ejecutar los `curl`/`openssl s_client` de §(iv) contra el dominio de
   prueba.
6. Confirmar en la base de datos o vía
   `GET /api/v1/admin/companies/{tenantId}/domain` que `status = active` y
   `certificate.issuedAt`/`certificate.expiresAt` están poblados.
7. Documentar aquí el resultado real — **esta HU no marca AC7 como Pass**,
   queda pendiente de ejecución humana con este procedimiento ya escrito.

| Fecha | Dominio de prueba | Resultado | Ejecutado por | Notas |
|---|---|---|---|---|
| _(pendiente)_ | _(pendiente — depende de §(v) de #12421 y del endpoint `pending-certificate` confirmado)_ | _(pendiente)_ | _(pendiente)_ | _(pendiente)_ |
