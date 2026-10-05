# Handoff de VPS: encender core-identity

HU #13228 (Feature #13220, Epic #13217). Para el líder técnico, con su sesión de Claude conectada a la VPS. Cada paso
trae qué hacer, cómo comprobarlo y cómo volver atrás. Se aplica **primero en DEV**; QA y PDN solo con aprobación del
líder y después de que DEV esté estable.

Contexto: [identidad-frontera.md](identidad-frontera.md). `core-identity` es el servicio de identidad, con su propia
imagen (`ghcr.io/flitsas/flitdev/core-identity`): atiende solo el login y sigue en pie cuando `core-api` se cae o se
despliega.

## Requisitos

- El PR de la suite fusionado en la rama del ambiente y desplegado (trae el servicio `core-identity` en
  `docker-compose.prod.yml`, la bandera del gateway y el CD por servicio).
- Acceso a la carpeta del despliegue (`$HOSTINGER_DEPLOY_PATH`) y a su `.env`.

| Ambiente | Rama | Puerto `core-identity` | GitHub Environment |
|---|---|---|---|
| DEV | `develop` | 4004 | `develop` |
| QA | `staging` | 5004 | `staging` |
| PDN | `release` | 6004 | `production` |

## Paso 0 — Foto del estado actual

```bash
cd "$DEPLOY_PATH"
cp .env ".env.antes-identidad.$(date +%Y%m%d%H%M)"
docker compose -f docker-compose.prod.yml ps
docker stats --no-stream --format '{{.Name}} {{.MemUsage}}'
free -m
# Postgres corre en el host (host.docker.internal desde los contenedores):
psql -U <usuario> -d <base> -Atc "show max_connections; select count(*) from pg_stat_activity;"
```

**Comprobar:** todo `healthy`; anotar la memoria que usa `core-api` y las conexiones actuales (se usan en el paso 3).

## Paso 1 — Levantar core-identity sin tráfico

En el `.env`, agregar `identity` a los perfiles (respetando los que ya haya, por ejemplo `suite`):

```bash
COMPOSE_PROFILES=identity            # o: COMPOSE_PROFILES=suite,identity
```

```bash
docker compose -f docker-compose.prod.yml up -d --no-deps core-identity
```

**Comprobar:**

```bash
docker compose -f docker-compose.prod.yml ps core-identity          # healthy
curl -fsS http://127.0.0.1:<puerto>/health/ready                    # {"status":"ready"}
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:<puerto>/api/v1/public/banners/active   # 404 (no atiende negocio)
docker compose -f docker-compose.prod.yml logs --tail=50 core-identity | grep -i -E "hosted|migrat"   # no migra
```

Nada cambia para los usuarios: el gateway todavía no le manda tráfico.

**Volver atrás:** quitar `identity` de `COMPOSE_PROFILES` y `docker compose -f docker-compose.prod.yml stop core-identity`.

## Paso 2 — Mandar el login a core-identity

En el `.env`:

```bash
FLIT_IDENTITY_CLUSTER_ENABLED=true
```

```bash
docker compose -f docker-compose.prod.yml up -d --no-deps gateway
```

**Comprobar** (por el gateway, con el puerto del gateway del ambiente):

```bash
docker compose -f docker-compose.prod.yml logs --tail=50 gateway | grep "Rutas de identidad"
curl -fsS http://127.0.0.1:<gateway>/.well-known/openid-configuration | head -c 200
docker compose -f docker-compose.prod.yml logs --tail=20 core-identity     # aparecen las peticiones
```

Después, en el navegador: iniciar sesión en el ambiente, abrir Trámites y cerrar sesión.

**La prueba que importa** (solo en DEV):

```bash
docker compose -f docker-compose.prod.yml stop core-api
# en el navegador: iniciar sesión → entra; Trámites abre pero sin datos
docker compose -f docker-compose.prod.yml start core-api
# recargar: los datos aparecen sin volver a iniciar sesión
```

Con la bandera encendida, el gateway revisa `/health/ready` de `core-identity` y de `core-api` cada 2 s: si identidad
se cae o no está lista, el login pasa solo a `core-api` en unos 3 segundos (y vuelve cuando identidad se recupera). Para verlo:
`docker compose -f docker-compose.prod.yml stop core-identity`, iniciar sesión (funciona) y `start core-identity`.

**Volver atrás:** `FLIT_IDENTITY_CLUSTER_ENABLED=false` y `up -d --no-deps gateway`. Todo vuelve a `core-api` al
instante; las sesiones abiertas siguen sirviendo (llaves y sesiones viven en la base).

## Paso 3 — Límites de recursos

Con lo anotado en el paso 0. Estos valores van en `docker-compose.prod.yml` por PR, no a mano en la VPS: el CD copia
el compose en cada despliegue y borraría el cambio. Pasar los números medidos al equipo para el PR.

| Qué | Propuesta | Por qué |
|---|---|---|
| Memoria de `core-api` | `mem_limit` = uso normal × 2 | Que un pico de `core-api` no se lleve el resto |
| Prioridad ante falta de memoria | `oom_score_adj: 500` en `core-api`, `-500` en `core-identity` | Si el sistema tiene que matar algo, que no sea el login |
| Conexiones de `core-api` | `Maximum Pool Size` en su cadena de conexión, dejando margen en `max_connections` | Que `core-api` no agote las conexiones y deje a identidad sin base |

**Comprobar:** `docker inspect -f '{{.HostConfig.Memory}} {{.HostConfig.OomScoreAdj}}' <contenedor>`.

## Paso 4 — Despliegue por servicio

En GitHub, en el Environment del ambiente (Settings → Environments → `develop`), crear la variable:

```
FLIT_DEPLOY_ROLLING = true
```

**Comprobar:** en el siguiente despliegue, el log del paso «Deploy via SSH» no hace `down`; levanta `core-api`, espera
`healthy`, luego el resto y al final `core-identity`; termina con «core-identity listo.».

**Volver atrás:** borrar la variable. El siguiente despliegue vuelve al `down` y `up` de siempre.

## Paso 5 — Monitoreo

- Alerta si `core-identity` deja de estar `healthy` (el mismo mecanismo que se use para `core-api`).
- Retención de logs de `core-identity` igual a la de `core-api`.

## Resumen de variables

| Dónde | Variable | Valor |
|---|---|---|
| `.env` | `COMPOSE_PROFILES` | agregar `identity` |
| `.env` | `FLIT_IDENTITY_CLUSTER_ENABLED` | `true` |
| GitHub Environment | `FLIT_DEPLOY_ROLLING` | `true` |

`CORE_IDENTITY_PORT` y `CORE_IDENTITY_TAG` los exporta el CD; no hacen falta en el `.env`.

## Si algo sale mal

| Síntoma | Qué hacer |
|---|---|
| `core-identity` no queda `healthy` y `/health/ready` dice `pending_migrations` | `core-api` todavía no migró: esperar a que esté `healthy` y reiniciar `core-identity` |
| Nadie puede entrar después del paso 2 | Volver atrás del paso 2 (bandera en `false`) y revisar `logs core-identity` |
| Los tokens de un proceso no sirven en el otro | El bloque de variables de los dos debe ser el mismo (ancla en el compose); revisar que nadie lo haya cambiado a mano |
| Al salir desde el hub y volver a entrar, un producto muestra «Reconectando tu sesión…» cada vez | El cierre de sesión no le llega al producto: su `/auth/frontchannel-logout` se abre dentro de la página del hub (front-channel logout, H8 de `matriz-pruebas.md`). Revisar que nginx no bloquee esa ruta ni le quite la cabecera `Content-Security-Policy` que la deja cargarse ahí; `curl -I https://<host del producto>/auth/frontchannel-logout` debe responder 200 con `frame-ancestors *` |
