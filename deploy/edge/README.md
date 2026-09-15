# Borde de la plataforma — dominios de red (Marca Blanca)

> HU #12421 · Feature #12368 · ADR-0060 D2 · Estado de esta HU: enrutamiento + configuración
> versionable + este runbook. **El nginx real del VPS y sus certificados TLS NO viven en este
> repositorio** — este directorio es la plantilla y el procedimiento, no la instalación.
> Certificados automáticos por dominio: HU #12426 (pendiente, NEW-28).

## Qué resuelve

Dar de alta el dominio de una red nueva (marca blanca) **no** requiere:
- editar `deploy/edge/nginx/flit-network-domains.conf.example` ni el nginx real,
- editar `docker-compose.prod.yml`,
- reiniciar ningún contenedor ni el nginx.

Solo requiere: registrar el dominio (SuperAdmin), apuntar el DNS del cliente, verificarlo
(#12425, pendiente) y activarlo. El catch-all de nginx (`server_name _`/`default_server`) y el
CORS dinámico del Gateway (caché 60 s sobre `GET /internal/domains/active`) hacen el resto.

## Componentes involucrados

| Componente | Dónde vive | Qué hace |
|---|---|---|
| nginx externo (borde) | VPS, fuera de este repo | Termina TLS, preserva `Host`, enruta `/api/v1/*` al Gateway y todo lo demás al frontend. Plantilla: `deploy/edge/nginx/flit-network-domains.conf.example`. |
| `Flit.Gateway` (YARP) | `docker-compose.prod.yml` (servicio `gateway`) | Sella `X-Flit-Domain` con el host real de la conexión (`DomainSealTransform`, HU #12417) y arma el CORS dinámico (lista fija ∪ dominios activos). |
| `Flit.Api` | `docker-compose.prod.yml` (servicio `core-api`) | `DomainContextMiddleware` lee el sello y resuelve la marca/tenant por dominio. Expone `GET /internal/domains/active` que consume el Gateway. |
| Frontend (Next.js) | `docker-compose.prod.yml` (servicio `frontend`) | Resuelve la marca en el servidor (`layout.tsx`) llamando al Gateway por la red interna Docker (`BRANDING_INTERNAL_API_URL`), con `X-Flit-Domain` explícito — HU #12419. |

## (i) Alta de una red — procedimiento

1. **Registrar el dominio** — SuperAdmin, `PUT /api/v1/admin/companies/{tenantId}/domain`
   (HU #12416). El tenant debe ser cabeza de red (`tenant_type = MARCA_BLANCA`, `is_group_parent`).
   El host no puede estar en la lista de reservados (`Domains:Reserved` = `*.flitsas.online`,
   `*.flitsas.com`; pendiente añadir el dominio del portal de organismos de tránsito cuando se
   confirme — ver contratos-api.md hecho 18) ni repetirse entre redes.
2. **DNS del cliente** — el cliente crea un `CNAME` (o `A`, según su proveedor) desde su dominio
   hacia el `EdgeTarget` de la plataforma (`Domains:EdgeTarget = edge.flitsas.online`,
   configuración de `Flit.Api`, no de este repo). Sin esto, aunque el dominio esté registrado y
   verificado, ninguna petición llega al borde.
3. **Verificación** — HU #12425 (pendiente): comprobación por TXT DNS, ciclo
   `pending → verified → active`. Hasta que #12425 esté desplegada, la activación es manual
   (Líder Técnico/SuperAdmin, fuera de este runbook).
4. **Certificado** — HU #12426 (pendiente): emisión automática por dominio. **Hasta entonces**,
   el ÚNICO dominio servible con TLS válido es el dominio de PRUEBA controlado del equipo, con un
   certificado emitido A MANO (ver §Certificados). Un dominio de cliente real sin certificado NO
   puede activarse con TLS — el CHECK de base de datos `ck_tenant_domains_active_requires_verified`
   exige `certificate_issued_at` para pasar a `active` (delta-hechos-post-adr.md hecho 13);
   relajarlo es un cambio de diseño que vuelve al architecture-agent, no un ajuste de este runbook.
5. **Activación ⇒ servido sin reiniciar (AC2)** — por qué funciona sin tocar nada:
   - nginx ya tiene el catch-all instalado (una sola vez, ver §Instalación) — el dominio nuevo
     cae ahí porque no calza en ningún `server_name` explícito (AC3, ver comentario del
     `.conf.example`).
   - El CORS dinámico del Gateway cachea `GET /internal/domains/active` 60 s — a los 60 s como
     máximo, el origen del dominio nuevo empieza a aceptarse sin reiniciar el Gateway.
   - El resolutor de dominio (`CachedTenantDomainResolver`) cachea 60 s por host, negativo
     incluido — a los 60 s como máximo, `DomainContext` empieza a resolver `Network` para ese host.
   - **Conclusión:** hasta 60 s de propagación tras activar, cero despliegue, cero reinicio.

## (ii) Baja / reversión de una red

1. Desactivar el dominio (SuperAdmin) — el estado deja de ser `active`; el resolutor lo cachea
   como negativo a los 60 s y el CORS dinámico deja de incluir su origen a los 60 s.
2. Confirmar que `/public/branding` para ese host vuelve a la identidad FLIT (`BrandIdentity.Flit`)
   antes de comunicar la baja al cliente.
3. No hace falta tocar nginx ni el compose — el catch-all sigue existiendo, simplemente ya no hay
   ninguna red activa detrás de ese host (el backend responde FLIT para todo host desconocido).
4. Si el dominio se va a re-registrar más adelante, el índice único
   (`uq_tenant_domains_host`, parcial `WHERE deleted_at IS NULL`) permite el re-registro tras un
   retiro lógico (soft delete) — no es necesario un borrado físico.

## (iii) Parametrización por ambiente

| Variable | DEV | QA | PDN | Dónde se usa |
|---|---|---|---|---|
| `FRONTEND_PORT` | 4001 | 5001 | 6001 | nginx (`.conf.example`), compose |
| `GATEWAY_PORT` | 4002 | 5002 | 6002 | nginx (`.conf.example`), compose |
| `CORE_API_PORT` | 4003 | 5003 | 6003 | compose (interno) |
| `FLIT_INTERNAL_SUBNET` | `172.28.40.0/24` (sugerido) | `172.28.50.0/24` (sugerido) | `172.28.60.0/24` (sugerido) | `docker-compose.prod.yml` (bloque `networks`), `DomainSeal__InternalAllowedNetworks__0` del gateway |
| `FLIT_INTERNAL_API_KEY` | por ambiente, generar | por ambiente, generar | por ambiente, generar | `Internal__ApiKey` de `gateway` y `core-api` |

Los tres ambientes corren con `ASPNETCORE_ENVIRONMENT=Development` (decisión ya vigente en este
repo, ver cabecera de `docker-compose.prod.yml`); la diferencia entre DEV/QA/PDN es el `.env` del
VPS de cada uno, nunca `appsettings.{Environment}.json`. Si DEV/QA/PDN comparten el mismo VPS
físico, `FLIT_INTERNAL_SUBNET` **debe** ser distinto entre ellos — Docker rechaza crear dos redes
con subredes solapadas en el mismo host (`docker compose up` falla con
`Pool overlaps with other one on this address space`). Si cada ambiente vive en un VPS distinto,
el default sugerido (`172.28.40.0/24`) es seguro de reutilizar en los tres.

## (iv) Prueba de humo — AC3 (dominios existentes intactos)

Verificar, tras instalar el catch-all, que el enrutamiento de HOY no cambió:

```bash
# Dominio de FLIT (frontend) — debe seguir sirviendo el frontend, marca FLIT
curl -sSI https://dev.flitsas.online/ | head -5

# Dominio de API de FLIT — debe seguir sirviendo el Gateway
curl -sS https://api.dev.flitsas.online/health

# Portal de organismos de tránsito (ict.*) — debe seguir sirviendo core-ict
curl -sS https://ict.dev.flitsas.com/health   # o el host ict.* vigente en ese ambiente

# Con X-Flit-Domain falso: la API debe recibir el Host REAL de la conexión,
# nunca el valor inventado por el cliente (AC1/AC5 de HU #12417, reforzado por #12421:
# nginx no toca esta cabecera, así que cualquier valor que un cliente externo mande
# llega intacto al Gateway, que lo DESCARTA y usa Request.Host.Host).
curl -sS -H "X-Flit-Domain: otra-red-cualquiera.com" \
  https://dev.flitsas.online/api/v1/public/branding
# Esperado: la respuesta corresponde al dominio REAL (dev.flitsas.online → identidad FLIT),
# NO a "otra-red-cualquiera.com".
```

Resultado esperado en los cuatro casos: **idéntico al comportamiento anterior a esta HU** — el
catch-all no debe interceptar ninguno de estos hosts (todos tienen `server_name` explícito con
mayor precedencia).

## (v) Prueba en DEV con dominio controlado — AC5

**Nota:** `GET /api/v1/public/branding` lo entrega la HU #12418, en curso al escribir este
runbook. Los pasos quedan documentados para ejecutarse **cuando #12418 esté desplegada en DEV** —
márcalo explícitamente al ejecutar, no antes.

1. Elegir un dominio de prueba controlado por el equipo (ej. un subdominio propio en un DNS que
   el equipo administre, no un dominio de un cliente real).
2. Registrar una red MARCA_BLANCA de prueba (SuperAdmin) con una marca configurada
   (HU #12412/#12413/#12414) y publicada.
3. Registrar el dominio de prueba para esa red (HU #12416) y activarlo (manual, hasta #12425).
4. Emitir un certificado MANUAL para ese dominio de prueba (`certbot certonly` o equivalente,
   fuera de este repo) y colocar sus rutas en la copia real de
   `flit-network-domains.conf.example` instalada en el VPS de DEV.
5. Apuntar el DNS del dominio de prueba al borde de DEV.
6. `curl -sSI https://<dominio-de-prueba>/` — **verificar cuando #12418 esté desplegado**:
   - `200` (o redirect esperado) servido por el frontend de DEV.
   - La pantalla de acceso (`/`) responde con la marca de prueba (nombre/color/logo del
     configurador), no con la identidad FLIT.
   - `GET https://<dominio-de-prueba>/api/v1/public/branding` responde `200` con
     `platformName`/`colors`/`logoUrl` de la red de prueba, **no** la constante `BrandIdentity.Flit`.
7. Documentar aquí (añadir una fila) el resultado real: fecha, dominio usado, resultado
   (Pass/Fail), quién lo ejecutó. **Esta HU (#12421) no marca AC5 como Pass** — queda declarado
   pendiente de ejecución humana con este procedimiento ya escrito.

| Fecha | Dominio de prueba | Resultado | Ejecutado por |
|---|---|---|---|
| _(pendiente)_ | _(pendiente — depende de #12418 desplegado en DEV)_ | _(pendiente)_ | _(pendiente)_ |

## Instalación (una sola vez por VPS)

1. Copiar `deploy/edge/nginx/flit-network-domains.conf.example` al VPS, p. ej.
   `/etc/nginx/sites-available/flit-network-domains.conf`.
2. Sustituir `${GATEWAY_PORT}` / `${FRONTEND_PORT}` por los valores del ambiente (ver tabla de
   §Parametrización). Con `envsubst`:
   ```bash
   GATEWAY_PORT=4002 FRONTEND_PORT=4001 \
     envsubst '${GATEWAY_PORT} ${FRONTEND_PORT}' \
     < flit-network-domains.conf.example > flit-network-domains.conf
   ```
3. Sustituir la ruta del certificado (`ssl_certificate`/`ssl_certificate_key`) por la del dominio
   de prueba (§Certificados) — hasta #12426 no hay wildcard ni on-demand TLS.
4. **Verificar que ningún otro `server{}` del puerto 80/443 en ese VPS ya declare
   `default_server`** (`grep -r default_server /etc/nginx/`). Si lo hay, quitar `default_server`
   de este archivo y dejarlo donde ya estaba — nginx no arranca con dos `default_server` para la
   misma combinación IP:puerto.
5. `ln -s /etc/nginx/sites-available/flit-network-domains.conf /etc/nginx/sites-enabled/`
   (o el mecanismo equivalente de esa instalación).
6. `nginx -t` (prueba de sintaxis) y `systemctl reload nginx` (reload, no restart — no corta
   conexiones en curso de los dominios ya servidos).
7. Añadir a `.env` del VPS: `FLIT_INTERNAL_API_KEY` (generar, `openssl rand -base64 36`) y
   `FLIT_INTERNAL_SUBNET` (ver tabla §Parametrización).
8. `docker compose -f docker-compose.prod.yml up -d --remove-orphans` — recrea la red interna con
   el CIDR explícito y los servicios `gateway`/`core-api`/`frontend` con las variables nuevas.

## Certificados (hasta #12426)

- Certificado MANUAL por dominio, emitido con la herramienta que ya use el VPS
  (`certbot certonly --webroot -w /var/www/acme-challenge -d <dominio>` o equivalente).
- **Sin secretos en el repo**: las claves privadas del certificado viven únicamente en el VPS
  (`/etc/letsencrypt/...` o la ruta que use esa instalación), nunca en este repositorio.
- Renovación: manual hasta #12426 (que además añade alerta de expiración ≤ 14 días).
- Un dominio de cliente real, sin este paso, no puede activarse con TLS (ver §Alta, paso 4).

## Variables nuevas — resumen

| Variable | Default | Servicio(s) | Notas |
|---|---|---|---|
| `FLIT_INTERNAL_API_KEY` | _(vacía)_ | `gateway`, `core-api` | Generar por ambiente, nunca commitear. Vacía = fail-closed. |
| `FLIT_INTERNAL_SUBNET` | `172.28.40.0/24` | red `default` del compose, `gateway` (`DomainSeal__InternalAllowedNetworks__0`) | Distinto por ambiente si comparten VPS. |
| `FLIT_HOSTS` | _(comentada, sin default activo)_ | ninguno todavía (documentado para #12418/#12419) | No alimenta ningún servicio de este compose; `NEXT_PUBLIC_FLIT_HOSTS` se hornea en build (`cd.yml`), no aquí. |

## Riesgos y límites conocidos

- **Docker NAT y `X-Flit-Domain` (mitigado):** las conexiones publicadas por puerto
  (`127.0.0.1:<puerto>`, el camino de nginx hacia `gateway`/`frontend`) pueden llegar al contenedor
  con la IP de origen NAT'eada por `docker-proxy`, que en varios escenarios es la IP *gateway* de
  la red interna (el `.1` de `FLIT_INTERNAL_SUBNET`) — una dirección que cae DENTRO del CIDR
  configurado en `DomainSeal__InternalAllowedNetworks__0`. Confiar solo en el CIDR permitiría, en
  teoría, que un cliente público forjara `X-Flit-Domain` si su conexión se viera originada en esa
  IP. **Mitigación aplicada:** `DomainSealTransform.ResolveSealedHost` (`src/Flit.Gateway/Transforms/DomainSealTransform.cs`)
  ahora exige **CIDR interno Y `X-Internal-Key`** (comparación en tiempo constante contra
  `Internal__ApiKey`, con guard de longitud; clave vacía ⇒ nunca se confía) para conservar el sello
  entrante — cualquier otro caso se descarta y se sella con `Request.Host.Host`. El frontend
  server-side (`resolve-brand.server.ts`, #12419) manda esa cabecera junto con `X-Flit-Domain` en
  la llamada por `BRANDING_INTERNAL_API_URL`; el compose (`docker-compose.prod.yml`, servicio
  `frontend`) declara `FLIT_INTERNAL_API_KEY` con el mismo valor que `Internal__ApiKey` de
  `gateway`/`core-api`. **Verificación en el VPS:** `docker network inspect
  <nombre-del-stack>_default` para confirmar el CIDR real de la red `default`, y revisar el log del
  Gateway (nivel `Information`/`Warning` de `DomainSealTransform`/`Program.cs`) ante una petición de
  prueba con `X-Flit-Domain` falso y sin `X-Internal-Key` vía nginx (paso incluido en §Prueba de
  humo AC3): debe reflejar siempre el host real, nunca el forjado.
- **`default_server` duplicado:** si el nginx real del VPS ya tiene otro `default_server` para
  80/443 (por ejemplo, uno genérico preexistente), instalar este archivo tal cual rompe el
  arranque de nginx. Ver paso 4 de §Instalación.
- **TLS real pendiente de #12426:** hasta entonces, un dominio de cliente real solo puede
  activarse con certificado manual — no es un flujo de autoservicio completo todavía.
- **`FLIT_HOSTS`/`NEXT_PUBLIC_FLIT_HOSTS` sin cablear:** el nombre está reservado y documentado
  (`.env.prod.example`), pero ningún servicio lo consume todavía — corresponde a #12418/#12419.
