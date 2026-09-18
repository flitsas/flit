# Certificados por dominio de cliente — emisión y renovación automáticas

> HU #12426 · Feature #12370 (padre #12237 Marca Blanca) · ADR-0060
> Depende de #12417 (sello `X-Flit-Domain` + CORS dinámico), #12421 (catch-all de
> nginx, `deploy/edge/README.md`) y #12425 (verificación de dominio → `verified`,
> en curso en paralelo). Este directorio **NO** opera el borde: es la
> configuración versionable y el runbook. La instalación real vive en el VPS,
> fuera de este repositorio.

## Qué resuelve

Hasta esta HU, `deploy/edge/README.md §Certificados` documenta un certificado
**manual** por dominio de prueba — el único camino disponible para servir TLS a
un dominio de cliente real. Esta HU cierra ese hueco: cuando un dominio de red
pasa a `verified` (HU #12425), su certificado se emite solo, se renueva antes
de vencer y, si algo falla, alguien se entera antes de que el dominio deje de
servir HTTPS (AC1-AC3). Nada de esto puede tocar los dominios que ya sirven hoy
— el dominio de FLIT y el del portal de organismos de tránsito siguen
exactamente igual (AC4).

## Restricción dura del contrato de API

`Flit.Gateway` (YARP) **no publica** `/api/v1/internal/*` — devuelve 404 antes
de `MapReverseProxy()` (`delta-hechos-post-adr.md` hecho 21). El endpoint que
consume esta HU, `PUT /api/v1/internal/domains/{host}/certificate`, es de
`Flit.Api` (`core-api`), no del Gateway. Como `core-api` publica
`127.0.0.1:${CORE_API_PORT}` en el mismo host que nginx
(`docker-compose.prod.yml:308-309`), cualquier hook que corra **en el VPS**
(nginx/acme.sh/certbot, mismo host que el compose) llega a `core-api`
directamente por loopback — **nunca** por el Gateway público ni por el dominio
externo. Esto es intencional: un hook de emisión de certificados no necesita
pasar por CORS, YARP ni el sello de dominio, y hacerlo por el Gateway
simplemente fallaría con 404.

```
notify-certificate.sh  ──PUT──▶  http://127.0.0.1:${CORE_API_PORT}/api/v1/internal/domains/{host}/certificate
                                  (X-Internal-Key: $FLIT_INTERNAL_API_KEY)
```

## Dos opciones evaluadas

### Opción A — Caddy delante, terminador TLS con `on_demand_tls`

Caddy soporta emisión **a demanda** por host: en el primer handshake TLS para
un `Host` desconocido, llama a un endpoint `ask` propio; si responde `200`,
emite el certificado ACME en caliente y sirve la conexión; si no, la rechaza
sin gastar una emisión de Let's Encrypt.

```caddyfile
{
	on_demand_tls {
		ask      http://127.0.0.1:${CORE_API_PORT}/api/v1/internal/domains/ask
		interval 2m
		burst    5
	}
}
```

- **`ask` endpoint propuesto:** `GET /api/v1/internal/domains/ask?domain={host}`
  en `core-api`, auth `X-Internal-Key`, `200` solo si el host tiene un registro
  `admin.tenant_domains` con `status ∈ {verified, active}`; cualquier otro caso
  (desconocido, `pending`, `failed`, retirado) devuelve `4xx` y Caddy no emite.
  **Esto NO existe hoy en el contrato** (`contratos-api.md` solo define `PUT
  …/certificate` y `GET …/active`) — es una extensión que #12425/#12417
  tendrían que exponer si se elige esta opción. Se declara aquí como gap, no se
  implementa (fuera de `deploy/edge/**`).
- **Ventaja:** cero polling, verdaderamente instantáneo la primera vez que
  llega tráfico a un host nuevo; Caddy gestiona renovación (~30 días antes) y
  reload internos sin intervención.
- **Desventaja decisiva para esta HU:** Caddy **reemplazaría** el nginx externo
  que #12421 ya instaló y probó (catch-all con `/hubs`, `/ml`, `/api/v1/`,
  precedencia de `server_name`, timeouts alineados con
  `MIGRACION_API_URL`). Migrar ese enrutamiento — ya evidenciado en
  `evidencias-12421.html` — a la sintaxis de Caddy es un cambio de borde mucho
  más amplio que "añadir emisión de certificados", con riesgo real sobre AC4
  (el dominio de FLIT "sigue exactamente igual que hoy"): un reemplazo total
  del terminador TLS no es "igual", es otro proxy. Añade además un endpoint
  nuevo en `core-api` fuera del alcance de `deploy/edge/**`.

### Opción B — nginx (ya instalado) + `acme.sh`/certbot con hook, `nginx -s reload` — RECOMENDADA

Mantiene el terminador TLS que #12421 ya construyó y documentó, y añade
automatización de emisión/renovación **encima**, sin tocar el `.conf.example`
de enrutamiento:

1. Un poller (`poll-and-issue.sh.example`, cron/systemd timer cada 5 min)
   consulta al backend qué dominios están `verified` y aún sin certificado, y
   para cada uno ejecuta `acme.sh --issue --webroot /var/www/acme-challenge -d
   <host>` (HTTP-01 — el mismo `location /.well-known/acme-challenge/` que el
   `.conf.example` de #12421 ya reserva sin usar).
2. `acme.sh --install-cert` con `--reloadcmd` apunta a
   `notify-certificate.sh.example`, que además de avisar al backend hace
   `nginx -t && systemctl reload nginx` (reload, **no** restart — no corta
   conexiones TLS en curso de otros dominios, AC4).
3. `acme.sh` trae su propio cron de renovación (instalado por su propio
   instalador, fuera de este repo) que reintenta certificados a ≤30 días de
   vencer — cubre AC2 sin script adicional; el hook de renovación es el mismo
   `--reloadcmd`.
4. `alert-expiry.sh.example` corre aparte (no depende de que la renovación de
   `acme.sh` haya corrido) y avisa si algo quedó a ≤14 días sin resolver
   (AC3) — cinturón y tirantes: no confía en que el cron de `acme.sh` nunca
   falle.

**Contrato confirmado (#12425):** el poller necesita saber qué dominios están
`verified` sin certificado. El contrato actual solo expone
`GET /api/v1/internal/domains/active` (dominios ya **activos**, no los que
esperan certificado — sería circular: activarse exige tener certificado,
`ck_tenant_domains_active_requires_verified`, hecho 13). #12425 añade
`GET /api/v1/internal/domains/pending-certificate` → `200 { "hosts": [...] }`
(auth `X-Internal-Key`, mismo patrón que `…/active`) con exactamente los
dominios `verified` sin certificado. `poll-and-issue.sh.example` se escribe
contra ese contrato, con la URL en una variable
(`FLIT_ACME_DOMAIN_LIST_URL`) para no acoplarse a la ruta literal si en algún
ambiente cambia.

**Por qué se recomienda B:**

- **AC1 (sin intervención por cliente):** el poller cubre el alta sin que
  nadie ejecute nada a mano; la latencia (≤5 min por el intervalo del timer)
  es aceptable frente al beneficio de no reemplazar el borde ya probado.
- **AC2 (renovación ≤30 días, sin corte):** `acme.sh` renueva solo; el
  `--reloadcmd` hace `reload`, no `restart`.
- **AC4 (dominios existentes intactos, FLIT igual que hoy):** el `.conf.example`
  de #12421 **no se toca**; `nginx -s reload` re-lee configuración sin cerrar
  conexiones establecidas de otros `server{}`; emitir el certificado de un
  dominio nuevo es un archivo nuevo bajo `/etc/letsencrypt/.../<host>/`, no una
  edición del catch-all (que ya sirve cualquier host por `server_name _` —
  solo necesita que exista el par de archivos de certificado para ESE host, lo
  que exige, eso sí, que el bloque `443 ssl` referencie el certificado
  correcto por host: ver nota de **límite conocido** más abajo).
- **AC6 (sin secretos):** ni `acme.sh` ni `notify-certificate.sh.example`
  llevan claves en el repo; `FLIT_INTERNAL_API_KEY` sale del `.env` del VPS
  (gestor de secretos), igual que hoy en `gateway`/`core-api`.

**Límite de nginx clásico — resuelto de forma nativa (sin OpenResty):** un
único bloque `server { listen 443 ssl; server_name _; }` con `ssl_certificate`
**fijo** solo puede presentar UN certificado por defecto para SNI desconocido.
Desde `nginx` **≥ 1.15.9**, `ssl_certificate`/`ssl_certificate_key` aceptan
**variables** (no requieren `ssl_preread`, `map`, Lua ni OpenResty — soporte
estándar del propio `ngx_http_ssl_module`, resuelto por conexión con la
variable predefinida `$ssl_server_name`, el SNI que el cliente envía en el
`ClientHello`). El catch-all de #12421 puede servir un certificado distinto
por host así:

```nginx
# En el bloque `server { listen 443 ssl; server_name _; }` del catch-all,
# EN VEZ de una ruta fija de ssl_certificate/ssl_certificate_key:
ssl_certificate     /etc/acme/live/$ssl_server_name/fullchain.pem;
ssl_certificate_key /etc/acme/live/$ssl_server_name/key.pem;
```

Las rutas cuelgan de la raíz que use `acme.sh --install-cert --cert-home
/etc/acme` (o la que se fije en `poll-and-issue.sh.example`/
`notify-certificate.sh.example`), una carpeta por host — exactamente lo que
`acme.sh` ya deja tras cada emisión, sin generar ni reescribir ningún bloque
`server{}` nuevo. El bloque del dominio FLIT (`server_name dev.flitsas.online`
explícito, en el nginx real del VPS, fuera de este `.conf.example`) **no se
toca**: sigue con su `ssl_certificate` fijo, por precedencia de `server_name`
nunca cae en el catch-all (AC4).

**Requisito:** `nginx -V 2>&1 | grep -o 'nginx/[0-9.]*'` ≥ `1.15.9` en el VPS
real — verificar antes de instalar esta variante (paso añadido en
`RUNBOOK.md §(i)` paso 5). Si el VPS trae una versión anterior, la variante de
respaldo (regenerar un bloque `server{}` por host o usar OpenResty) sigue
documentada como alternativa, pero deja de ser la recomendada por defecto.

**Handshake de un host `verified` sin certificado todavía (ventana entre
"verificado" y "primera emisión exitosa", AC1):** con `ssl_certificate` por
variable, si `/etc/acme/live/<host>/` no existe aún, nginx no tiene qué servir
para ese SNI. Dos mecanismos, uno **recomendado**:

- **Recomendado — certificado de respaldo (`ssl_certificate` con ruta fija
  como último `server{}` sin `server_name` calzando, o `ssl_certificate` con
  una ruta de *fallback* fuera del `map`):** dejar un certificado comodín o
  autofirmado de respaldo en `/etc/acme/live/_default/` y resolver la ruta con
  un pequeño `map $ssl_server_name $ssl_cert_path { default
  /etc/acme/live/_default; ... }` **solo si** se necesita distinguir "existe"
  de "no existe" — en la práctica, con `ssl_certificate
  /etc/acme/live/$ssl_server_name/fullchain.pem;` directo (sin `map`), un host
  sin carpeta simplemente falla el handshake con un error de certificado (no
  interrumpe nada de los demás dominios, y el cliente reintenta cuando el
  poller termine de emitir minutos después) — comportamiento aceptable porque
  la ventana es corta (≤5 min, intervalo del poller) y no degrada ningún
  dominio ya activo. Se prefiere esto sobre `ssl_reject_handshake` porque un
  error de certificado siempre deja un mensaje diagnosticable en el cliente
  (`ERR_CERT_...`), mientras que un *reject* temprano (TCP RST/FIN silencioso)
  es más difícil de distinguir de una caída real del borde al depurar (§(iv)
  del runbook usa `openssl s_client` que sí distingue ambos casos, pero el
  primero dejó más rastro en los logs de nginx).
- **Alternativa — `ssl_reject_handshake on;` (nginx ≥ 1.19.4)** dentro de un
  `server{}` por defecto explícito para SNI no reconocidos: rechaza el
  *handshake* TLS de forma temprana y explícita en vez de fallar por "archivo
  no encontrado". Más limpio operacionalmente si el VPS ya es ≥ 1.19.4 y se
  quiere una señal de "host no listo" indistinguible de "host inexistente"
  (mismo tratamiento, sin filtrar si el dominio existe o no — endurecimiento
  de superficie). Documentado como alternativa aceptable, no la default,
  porque exige una versión más nueva que el requisito mínimo de la variable
  (`ssl_certificate` por variable ya funciona desde 1.15.9).

Verificado que ninguna de las dos variantes toca el bloque `server{}` del
dominio de FLIT: ambas se aplican solo dentro del `server { server_name _; }`
del catch-all.

## Rate limits de Let's Encrypt y protección contra abuso

- Let's Encrypt limita 50 certificados/semana por dominio registrable y 5
  duplicados idénticos/semana — el poller **no** reintenta un host que ya
  falló hace menos de 1 h (`poll-and-issue.sh.example` guarda un
  `last-attempt` por host bajo `/var/lib/flit-acme/`) para no agotar el
  límite con reintentos en bucle.
- **Solo hosts `verified` (o `active`, ya certificados) pueden disparar una
  emisión** — el poller confía exclusivamente en la respuesta del endpoint
  interno autenticado con `X-Internal-Key`; un tercero no puede forzar una
  emisión inventando un `Host` porque nunca llega a `acme.sh` sin pasar antes
  por `verified` en `admin.tenant_domains` (que exige la comprobación DNS de
  #12425). Esto es lo mismo que exige la Opción A del endpoint `ask` — en
  ambas opciones, la fuente de verdad de "quién puede emitir" es el backend,
  nunca el script de borde.
- HTTP-01 exige que el DNS del cliente ya apunte al borde (documentado en
  `deploy/edge/README.md §(i)` paso 2) — si el poller intenta emitir antes de
  que el DNS propague, `acme.sh` falla con `dns/http mismatch` sin gastar el
  límite de forma agresiva (falla el *challenge*, no cuenta contra el límite
  de certificados igual que un éxito, pero si se repite mucho sí cuenta contra
  el límite de "fallos por cuenta/hora" de Let's Encrypt — de ahí el cooldown
  de 1 h de arriba).

## Archivos de este directorio

| Archivo | Qué hace |
|---|---|
| `README.md` | Este documento — diseño, opciones, decisión, rate limits |
| `RUNBOOK.md` | Alta, baja, reversión, verificación post-cambio, costo, AC7 |
| `poll-and-issue.sh.example` | Poller: consulta dominios `verified` sin certificado, emite con `acme.sh`, dispara reload |
| `notify-certificate.sh.example` | Hook post-emisión/renovación: informa a `core-api` y recarga nginx |
| `alert-expiry.sh.example` | Alerta al canal de operación cuando un certificado de cliente está a ≤14 días de vencer |
| `systemd/flit-acme-poll.service.example` + `.timer.example` | Unidad systemd para `poll-and-issue.sh.example` (cada 5 min) |
| `systemd/flit-acme-expiry.service.example` + `.timer.example` | Unidad systemd para `alert-expiry.sh.example` (diario) |

## Variables de entorno nuevas (para el `.env` del VPS — no en este repo)

| Variable | Ejemplo | Dónde se usa | Nota |
|---|---|---|---|
| `FLIT_INTERNAL_API_KEY` | _(ya existe, `.env.prod.example`)_ | `notify-certificate.sh.example`, `poll-and-issue.sh.example` | Misma clave que ya usan `gateway`/`core-api` para `Internal__ApiKey` — se reutiliza, no se crea una nueva. |
| `CORE_API_INTERNAL_URL` | `http://127.0.0.1:4003` (DEV) | ambos scripts | Nueva. `http://127.0.0.1:${CORE_API_PORT}` del ambiente — **no** el Gateway (ver §Restricción dura). Este repo no la declara en `docker-compose.prod.yml` porque los scripts de `acme/` corren en el HOST del VPS, fuera de los contenedores; se fija en el `.env`/entorno del cron o de la unidad systemd, no en el compose. |
| `OPS_ALERT_WEBHOOK_URL` | `https://hooks.slack.com/services/...` | `alert-expiry.sh.example` | Nueva. Webhook genérico compatible con Slack/Teams (payload `{"text": "..."}`). Vacío ⇒ el script registra en journal y sale con error, no falla en silencio. |
| `FLIT_ACME_DOMAIN_LIST_URL` | `http://127.0.0.1:4003/api/v1/internal/domains/pending-certificate` | `poll-and-issue.sh.example` | Nueva. Contrato confirmado en #12425 (ver arriba) — ruta configurable a propósito. |

**No se edita `.env.prod.example` ni `docker-compose.prod.yml` desde esta HU**
(fuera de alcance de `infra-agent` en este encargo) — quedan declaradas aquí
para que quien mantenga esos archivos las incorpore.

## Ver también

- `deploy/edge/README.md` — enrutamiento base (#12421), instalación del
  catch-all, riesgos de `X-Flit-Domain`/NAT.
- `deploy/edge/nginx/flit-network-domains.conf.example` — el `.conf` que este
  directorio complementa (no lo duplica).
- `.claude/state/marca-blanca/diseno/contratos-api.md` — contrato
  `PUT /api/v1/internal/domains/{host}/certificate`.
