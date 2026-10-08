# Runbook — descarga en streaming de las partes del lote de consolidados (nginx del VPS)

> Generado: 2026-10-07 · Épica #13216 · HU #13379 (nginx) y HU #13378 (disco temporal de core-api).
> El nginx real de cada VPS **no vive en este repositorio**: aquí están las plantillas
> (`deploy/edge/nginx/*.conf.example`) y este procedimiento. Nada de esto lo aplica el CD.

## Qué cambia y por qué

Las partes de un lote (`GET /api/v1/consolidados/lotes/{id}/partes/{n}`, ZIP de hasta ~250 MB con
`Content-Length` fijo) salen de core-api en streaming. Con la configuración por defecto de nginx
(`proxy_buffering on`) el borde volcaría cada parte a su archivo temporal del VPS
(`/var/lib/nginx/proxy` o equivalente, hasta 1 GB por respuesta) antes de entregarla: disco del host
consumido por descarga concurrente y primer byte retrasado. El bloque nuevo apaga el buffering solo
para esa ruta.

## Por dónde pasa la descarga (decide en qué `server{}` va el bloque)

El frontend descarga con `downloadFile` (`frontend/lib/api/download.ts`) y la base depende del modo de sesión:

| Modo de sesión de Trámites | URL que pide el navegador | Camino | Dónde va el bloque | Destino del bloque |
|---|---|---|---|---|
| `legacy` (por defecto) con `NEXT_PUBLIC_API_BASE_URL` | `https://api.<env>.flitsas.online/api/v1/consolidados/lotes/…` | nginx → gateway | `server{}` de `api.<env>.flitsas.online` (solo existe en el VPS) | gateway (`GATEWAY_PORT`) |
| `legacy` en dominio de red con base vacía (mismo origen) | `https://<dominio-red>/api/v1/…` | nginx catch-all → gateway | `flit-network-domains.conf` (plantilla actualizada) | gateway (`GATEWAY_PORT`) |
| `oidc` (FLIT Suite, `dev.tramites.flitsas.online`) | `https://<env>.tramites…/api/v1/…` (mismo origen) | nginx → Next (middleware → BFF `/bff/api/v1/*`) → gateway | `flit-suite-hosts.conf`, `server{}` de Trámites (plantilla actualizada) | **frontend** (`FRONTEND_PORT`), nunca el gateway |

En modo `oidc` el token vive en la sesión del servidor de Next y el BFF (`packages/auth/src/proxy.ts`)
lo añade al reenviar; el navegador no lo tiene. Mandar la ruta directo al gateway devolvería 401. El
BFF ya entrega el cuerpo en streaming (`new Response(upstream.body)`), así que basta con que nginx no
bufferice el salto hacia Next.

## Pasos manuales en cada VPS (DEV, QA, PDN)

Puertos: `GATEWAY_PORT` 4002 / 5002 / 6002 · `FRONTEND_PORT` 4001 / 5001 / 6001.

0. **Respaldo** del archivo que vas a tocar: `cp <archivo>.conf <archivo>.conf.bak-$(date +%F)`.
1. Localiza los `server{}` afectados:
   ```bash
   grep -rn "server_name" /etc/nginx/sites-enabled/ /etc/nginx/conf.d/ 2>/dev/null
   ```
2. **`server{}` de `api.<env>.flitsas.online`** (siempre, mientras Trámites siga en modo `legacy`):
   pega dentro del `server{}`, antes de su `location` general, este bloque con el puerto del ambiente.
   No le agregues `proxy_set_header`: si la location declara uno solo, deja de heredar los del
   `server{}` (Host, X-Forwarded-*). Si ese `server{}` define los `proxy_set_header` dentro de su
   `location`, y no a nivel de `server{}`, cópialos también aquí.
   ```nginx
   # HU #13379 — partes del lote de consolidados en streaming (hasta ~250 MB)
   location ~ ^/api/v1/consolidados/lotes/[^/]+/partes/[0-9]+$ {
       proxy_pass http://127.0.0.1:4002;   # GATEWAY_PORT del ambiente; sin URI (location regex)
       proxy_http_version 1.1;
       proxy_buffering off;
       proxy_request_buffering off;
       proxy_max_temp_file_size 0;
       proxy_read_timeout 300s;
   }
   ```
3. **Catch-all de dominios de red** (si está instalado — `deploy/edge/README.md` §Instalación): el
   mismo bloque, en el `server{}` de 443, junto a `location /api/v1/` (ver la plantilla
   `deploy/edge/nginx/flit-network-domains.conf.example`).
4. **Host de Trámites de la Suite** (solo si `dev.tramites.flitsas.online` o su equivalente ya está
   encendido): el bloque de `deploy/edge/nginx/flit-suite-hosts.conf.example`, con
   `proxy_pass http://127.0.0.1:<FRONTEND_PORT>;` — al frontend, no al gateway.
5. Prueba y recarga (reload, no restart: no corta las conexiones en curso):
   ```bash
   sudo nginx -t && sudo nginx -s reload
   ```
   Si `nginx -t` falla, no recargues: restaura el `.bak` y revisa.
6. Verificación (con un lote terminado de un usuario de prueba, sin datos de terceros):
   - En el navegador, la descarga muestra progreso desde el primer segundo y el tamaño total
     (modo `legacy`; en `oidc` el BFF quita `Content-Length` y el navegador no conoce el total).
   - En el VPS, durante la descarga, el temporal de nginx no crece:
     `sudo du -sh /var/lib/nginx/proxy` (o la ruta de `proxy_temp_path` de esa instalación).

### Vuelta atrás

Borra el bloque (o restaura el `.bak`) y `sudo nginx -t && sudo nginx -s reload`. La descarga sigue
funcionando con buffering: es una degradación de disco y latencia, no un corte.

## Previo de core-api (HU #13378) — disco temporal del empaquetado

`docker-compose.prod.yml` monta el volumen con nombre `core-api-lotes-tmp` en `/var/tmp/flit-lotes`
(`ConsolidadoLotes__DirectorioTemporal`). Lo crea el propio `docker compose up` del CD; no hay paso
manual, salvo comprobar el espacio libre del disco de Docker en cada VPS antes del primer despliegue:

```bash
df -h "$(docker info -f '{{.DockerRootDir}}')"
```

Necesita **≥ 600 MB libres** con `max_mb_per_part` por defecto (250 MB) y **≥ 4,5 GB** si se sube a
2048 MB, por ambiente (DEV, QA y PDN comparten VPS: cada proyecto compose tiene su propio volumen).
El CD hace `down` sin `-v`: el volumen sobrevive a los despliegues y core-api borra los huérfanos al
arrancar.

## Límites conocidos

- Más allá de nginx, la descarga atraviesa YARP (`core-api-cluster`, `ActivityTimeout` 30 s, que
  cuenta inactividad, no duración total) y, en modo `oidc`, el `fetch` del BFF (undici: 300 s entre
  bytes). Ninguno corta una descarga que fluye; sí una respuesta que tarde más que eso en empezar.
- En modo `legacy` con base vacía en un host cuyo nginx mande `/api/v1/*` al frontend, el proxy de
  rewrites de Next (`proxyTimeout` 120 s en `frontend/next.config.ts`) también es por inactividad y
  reenvía en streaming. Las plantillas del repo no usan esa combinación; el nginx real no está
  versionado, así que confírmalo en cada VPS.
