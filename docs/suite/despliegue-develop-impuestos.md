# Desplegar Impuestos en develop (ambiente DEV)

> Generado: 2026-11-25
> Rama de referencia: `feature/AB-14694-producto-impuestos`, módulo M20 (antes de M07 y M09–M14). Las anclas a código son
> de esa rama; reverificarlas antes de ejecutar.
> Los datos del entorno DEV (espacio de nombres, roles, proxy, sellado) se toman de
> [despliegue-develop-diagnostico.md](despliegue-develop-diagnostico.md), medidos el 2026-11-04: vuelva a medirlos.

**Para quién es.** Para quien opera el servidor de DEV y no conoce el producto. **Qué es Impuestos:** un producto de la
FLIT Suite que liquida el impuesto vehicular por placa (individual o masivo) contra el API de liquidación del proveedor,
recibe el resultado por un aviso firmado (o lo consulta si el aviso no llega) y guarda los recibos PDF. Integra el API
**directo**, sin core-consultas para la liquidación ([ADR-0093](../decisions/ADR-0093-impuestos-integracion-directa-api-liquidacion.md)).

**Lea esto primero:**

1. **`develop` sí es DEV.** Un push a `develop` despliega en DEV (`cd.yml`, job `setup`, rama `develop`). Fusionar es
   desplegar.
2. **DEV no usa `docker-compose.prod.yml` ni un `.env`.** Corre en k3s con Argo CD; la configuración vive en
   `flit-gitops/services/flit2-dev/` (`configmaps.yaml` y `sealed-secrets.yaml`). Una variable nueva en el compose **no
   llega sola**. Aquí el compose es la referencia de nombres y valores; los pasos con `docker compose` de
   [handoff-vps-impuestos.md](handoff-vps-impuestos.md) hay que traducirlos.
3. **El CD ya construye las dos imágenes** (`build-core-impuestos`, `build-frontend-impuestos`) y `deploy-gitops` escribe sus
   digests **solo si** `services/flit2-dev/kustomization.yaml` las declara; si no, las omite con un aviso y sigue.
4. **Gates del líder técnico** que bloquean este despliegue (plan §11): G-2 (base), G-4 (puertos), G-5 (clientes OIDC),
   G-6 (host del webhook), G-7 (flit-gitops), G-8 (broker), G-9 (secretos del API), G-10 (certificado) y G-11 (file-manager,
   para los recibos). Ver §8.

---

## 1. Alcance

| Pieza | Situación en DEV |
|---|---|
| `core-impuestos` (API, .NET 10) | Nuevo |
| `frontend-impuestos` (web, Next.js) | Nuevo |
| core-api y core-identity | Ya corren. Suman el cliente `svc-impuestos` (secreto) y publican trabajos en `flit.impuestos` |
| gateway | Ya corre. Suma la ruta `/api/v1/impuestos/*` (llega con la imagen de core-api) |
| RabbitMQ | Ya corre. Suma el usuario `impuestos` y escritura de `tramites` y `plataforma` en `flit.impuestos` |
| core-consultas | No cambia por este despliegue |

## 2. Mapa de servicios

| Servicio | Imagen (`ghcr.io/flitsas/flitdev/…`) · Dockerfile | Puerto DEV | gRPC | Depende de | Health check |
|---|---|---|---|---|---|
| `core-impuestos` | `core-impuestos` · `services/core-impuestos/Dockerfile` | 4033 | 8089 | Postgres, RabbitMQ, gateway (tokens y manifiesto), core-identity, core-api, API de liquidación | Vida `GET /health` · Listo `GET /health/ready` |
| `frontend-impuestos` | `frontend-impuestos` · `frontend-impuestos/Dockerfile` | 4034 | No | gateway | `GET /healthz` |

**Trampa.** El `HEALTHCHECK` del Dockerfile de core-impuestos apunta al 8080. En k3s defina las sondas con el puerto real
(4033), como en Diagnóstico.

## 3. Variables de entorno

La columna **Variable** es el nombre que recibe el contenedor (lo que va en `flit-gitops`); entre paréntesis, el nombre del
`.env` del compose. `I/` = `services/core-impuestos/src/Flit.Impuestos.Api/`. Los `<secreto>` se generan por ambiente y van
a un sealed secret, nunca a un archivo del repositorio.

### 3.1 core-impuestos: secretos

| Variable | Oblig. | Para qué | Dónde se lee |
|---|---|---|---|
| `ConnectionStrings__Servicio` (`CONNECTION_STRING_IMPUESTOS`) | Sí, no arranca sin ella | Rol dueño del esquema `impuestos` | `I/ServicioSettings.cs` |
| `Platform__ServiceClient__ClientSecret` (`SVC_IMPUESTOS_CLIENT_SECRET`) | Sí | Tokens como `svc-impuestos`; el mismo valor en core-api/core-identity | `I/ServicioSettings.cs` |
| `Platform__Messaging__ConnectionString` (`RABBITMQ_URL_IMPUESTOS`) | Sí | Usuario `impuestos` del broker | `I/ServicioSettings.cs` |
| `Impuestos__DataProtection__CertificadoClave` (`IMPUESTOS_SVC_DP_CERTIFICADO_CLAVE`) | Sí, no arranca sin ella | Clave del `.pfx` | `I/Plataforma/ProteccionDatos.cs` |
| Archivo `.pfx` (`IMPUESTOS_SVC_DP_CERTIFICADO_RUTA` en el compose: ruta del host) | Sí | Certificado RSA, montado como archivo | compose, `secrets: impuestos_dp_pfx` |
| `Impuestos__Api__IntegrationToken` (`IMPUESTOS_SVC_INTEGRATION_TOKEN`) | Para el alta | Token de integración del ambiente (G-9) | `I/Modulos/ApiLiquidacion/OpcionesApiLiquidacion.cs` |

### 3.2 core-impuestos: configuración

| Variable | Oblig. | Ejemplo DEV | Para qué | Dónde se lee |
|---|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` (`FLIT_DOTNET_ENVIRONMENT`) | Opcional | `Development` | Los tres ambientes corren como Development | compose |
| `ASPNETCORE_URLS` | Sí | `http://+:4033` | Puerto REST | `I/Program.cs` |
| `Servicio__GrpcPort` (`CORE_IMPUESTOS_GRPC_PORT`) | Sí | `8089` | gRPC interno, solo HTTP/2 | `I/ServicioSettings.cs` |
| `Platform__ServiceClient__TokenEndpoint` | Sí | `http://gateway:4002/connect/token` | Dónde pedir tokens | `I/ServicioSettings.cs` |
| `Platform__Auth__JwksUri` | Sí | `http://gateway:4002/.well-known/jwks.json` | Llaves para validar tokens | `I/ServicioSettings.cs` |
| `Platform__Auth__Issuers__0` (`FLIT_HUB_URL` + `/`) | Sí | `https://dev.flitsas.online/` | Emisor aceptado, con barra final | `I/ServicioSettings.cs` |
| `Impuestos__DataProtection__CertificadoRuta` | Sí | `/run/secrets/impuestos_dp_pfx` | Ruta del `.pfx` dentro del contenedor | `I/Plataforma/ProteccionDatos.cs` |
| `Impuestos__Plataforma__GatewayUrl` | Necesaria en la práctica | `http://gateway:4002` | Registrar el manifiesto; sin ella solo hay un aviso en el log | `I/Plataforma/RegistroManifiesto.cs` |
| `Impuestos__Identidad__Address` (`IMPUESTOS_SVC_IDENTIDAD_ADDRESS`) | Necesaria en la práctica | `http://core-identity:8083` | Suscripción por petición. **Vacía = 403 en todo el negocio** | `I/Plataforma/GuardSuscripcion.cs` |
| `Impuestos__Tramites__Address` | Necesaria en la práctica | `http://core-api:8082` | Réplica de organismos (homologar la secretaría). Vacía = sin réplica | `I/Modulos/Catalogos/CatalogoOrganismosGrpc.cs` |
| `Impuestos__Api__BaseUrl` (`IMPUESTOS_SVC_API_BASE_URL`) | Para el alta y las consultas | `https://<base del API de DEV>` | Base del API, https | `I/Modulos/ApiLiquidacion/OpcionesApiLiquidacion.cs` |
| `Impuestos__Api__TimeoutSegundos` (`IMPUESTOS_SVC_HTTP_TIMEOUT_SECONDS`) | Opcional | `30` | Timeout por llamada | ídem |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Opcional | El de los demás | Trazas | compose |

**No poner en ningún ambiente:** `Impuestos__Identidad__SimularSuscripcionActiva` (solo local). **Afinación, no tocar salvo necesidad:** `Servicio__MigrateOnStartup` (`true`),
`Impuestos__Organismos__IntervaloSincronizacion` (1 día), `Impuestos__Fuentes__VigenciaCache`,
`Impuestos__LimitesSolicitudes__*` (límite por IP del receptor de avisos; menor que 1 impide arrancar).

**Llegan con su módulo** (todavía sin código que las lea): `IMPUESTOS_SVC_AVISOS_URL_BASE` (M09),
`IMPUESTOS_SVC_ARCHIVOS_URL` (M11), `IMPUESTOS_SVC_CONCURRENCIA_EMPRESA` y `IMPUESTOS_SVC_CONCURRENCIA_GLOBAL` (M12). Ver el
handoff §4.3.

### 3.3 frontend-impuestos

| Variable | Ejemplo DEV | Para qué |
|---|---|---|
| `PORT` (`IMPUESTOS_PORT`) | `4034` | Puerto de Next.js |
| `HOSTNAME` | `0.0.0.0` | Escuchar en el pod |
| `CORE_API_ORIGIN` | `http://gateway:4002` | BFF `/api/v1/*` → gateway |
| `BRANDING_INTERNAL_API_URL` | `http://gateway:4002` | Marca por la red interna |
| `FLIT_INTERNAL_API_KEY` | `<secreto>` (el mismo de los demás fronts) | Sello del dominio |
| `FLIT_HUB_URL` | `https://dev.flitsas.online` | Hub y servidor OIDC |
| `FLIT_OIDC_INTERNAL_URL` | `http://gateway:4002` | OIDC por la red interna |
| `FLIT_SESSION_SECRET` (`IMPUESTOS_SESSION_SECRET`) | `<secreto>`, ≥ 32 caracteres | Cookie de sesión; distinto del hub y de los otros productos |
| `TRAMITES_URL` (`FLIT_TRAMITES_URL`) | `https://dev.tramites.flitsas.online` | Ayuda y Cambiar contraseña |
| `FLIT_HOSTS` | El del compose | Hosts aceptados |

### 3.4 Servicios que ya corren: lo que Impuestos les pide

| Servicio | Qué cambia | Variable |
|---|---|---|
| core-api, core-identity | Registrar `svc-impuestos` | `Suite__Oidc__ServiceClients__svc-impuestos__Secret` (`SVC_IMPUESTOS_CLIENT_SECRET`) |
| core-identity | gRPC de Identidad encendido | `Identidad__GrpcPort` (`CORE_IDENTITY_GRPC_PORT`, 8083) |
| gateway | Destino del clúster | `ReverseProxy__Clusters__core-impuestos-cluster__Destinations__core-impuestos-1__Address` = `http://core-impuestos:4033/` |

## 4. Infraestructura

### 4.1 Base de datos (G-2)

Esquema `impuestos`, rol por ambiente. En DEV, como Diagnóstico (`flit_diagnostico_dev` en la base `flitdev`), el nombre
probable es `flit_impuestos_dev`: `servicio-con-esquema-propio.sql` no admite el sufijo, así que se crea a mano con los
mismos permisos y se suma a la línea de la red del clúster en `pg_hba.conf`. Comandos de referencia: handoff §1.
Aislamiento: `deploy/postgres/verificar-aislamiento.sql`. **El esquema migra al arrancar**; no hay otro mecanismo.

### 4.2 RabbitMQ (G-8)

En k3s el script `usuario-de-servicio.sh` no sirve tal cual (arma `docker compose exec`); use `rabbitmqctl` directo:

```bash
# Solo si list_users no lo muestra. La clave sale de una variable, nunca del historial.
kubectl -n <ns> exec <pod-rabbitmq> -- rabbitmqctl add_user impuestos "$CLAVE_MQ_IMPUESTOS"
kubectl -n <ns> exec <pod-rabbitmq> -- rabbitmqctl set_permissions -p flit impuestos \
  '^(flit\.impuestos|impuestos\..*)$' \
  '^(flit\.impuestos|flit\.notificaciones|impuestos\..*)$' \
  '^(flit\..*|impuestos\..*)$'
```

Y sumar `|flit\.impuestos` a la escritura de `tramites` y `plataforma`, leyendo antes sus permisos actuales
(`list_user_permissions`). Sin eso, core-api y core-identity no pueden avisarle a core-impuestos que se encendió el producto
o que hay que reintentar el aprovisionamiento: su outbox reintenta con `ACCESS_REFUSED`.

### 4.3 Certificado de Data Protection (G-10)

Sin él core-impuestos **no arranca**. Uno por ambiente, RSA, distinto del de Diagnóstico, con respaldo en el gestor de
secretos **antes** de encender. En k3s: sealed secret con el `.pfx` montado como archivo; la ruta tiene que coincidir con
`Impuestos__DataProtection__CertificadoRuta`. Procedimiento: handoff §5. No rotar.

### 4.4 gRPC entre servicios

h2c en la red interna, nada se publica. Cada llamada lleva un token de `svc-impuestos`.

| Origen → destino | Dirección | Scope | Para qué | Si falta |
|---|---|---|---|---|
| core-impuestos → core-identity | `http://core-identity:8083` | `platform.identidad.read`, `platform.identidad.empresas`, `platform.identidad.empresas.alta`, `platform.identidad.credenciales.impuesto.copia`, `platform.aprovisionamiento.reportar` | Suscripción, datos del alta, copia de la llave de Diagnóstico, estado del aprovisionamiento | 403 en todo el negocio |
| core-impuestos → core-api | `http://core-api:8082` | `platform.tramites.catalogos`, `platform.usuarios.impuestos`, `platform.usuarios.impuestos.conteos` | Organismos y usuarios del producto | Sin réplica de organismos |

El canal h2c en claro es el mismo riesgo aceptado del `credenciales_producto.proto` (gate G-12, aval de seguridad).

### 4.5 Identidad y OIDC (G-5)

| Cliente | Tipo | Quién lo registra |
|---|---|---|
| `svc-impuestos` | De servicio, client credentials | Scopes en core-api `appsettings.json`; falta su secreto (§3.4) |
| `impuestos` | Público, PKCE, login de la web | Solo, al arrancar core-api o core-identity, con `Suite__Hosts__Environment` |

Esperado en DEV: `redirect_uri` `https://dev.impuestos.flitsas.online/auth/callback`. Si core-api y core-identity reciben
valores distintos de `Suite__Hosts__Environment`, se pisan ([handoff-vps-suite.md](handoff-vps-suite.md) §7.2).

### 4.6 Dominio, proxy y host del webhook (G-6)

| Tema | Qué hacer |
|---|---|
| Dominio | `dev.impuestos.flitsas.online`: DNS y certificado antes de encender |
| Proxy de la web | nginx del host (o ingress) hacia `frontend-impuestos:4034`. Plantilla en `deploy/edge/nginx/flit-suite-hosts.conf.example` |
| API desde el navegador | Sin ingress propio: el navegador llama a `/api/v1/*` del mismo host, el BFF lo pasa al gateway y este a core-impuestos |
| Host del webhook | https válido, **sin redirecciones**, `/api/v1/impuestos/avisos/` directo al gateway (no por el BFF). `api.dev.flitsas.online` ya cumple; el host de la app web necesita la `location` dedicada de la plantilla |
| CORS | No hace falta tocar nada |
| «Próximamente» | Quitar `impuestos` de `Suite:Hosts:ComingSoon` por PR al encender |

## 5. Orden de despliegue, paso a paso

**Fusionar a `develop` despliega** las imágenes nuevas de core-api (con la ruta del gateway) y, si la kustomization las
lista, las de Impuestos. Por eso los pasos 1 a 4 van antes del merge. Cada paso requiere confirmación del líder técnico.

1. **flit-gitops (G-7, antes del merge).** En `services/flit2-dev/`, manifiestos de `core-impuestos` y `frontend-impuestos`
   con las variables de §3, las dos imágenes en `kustomization.yaml` y `replicas: 0`. Modelo:
   [despliegue-dev-k3s-comparendos.md](despliegue-dev-k3s-comparendos.md). **Comprobar:** Argo CD `Synced`, sin pods.
2. **Base de datos (G-2).** §4.1. **Comprobar:** `\du` muestra el rol; `verificar-aislamiento.sql` pasa.
3. **Secretos (G-5, G-9, G-10).** Sellar los de §3.1, §3.3 y §3.4; certificado respaldado. Reiniciar core-api y
   core-identity con el secreto de `svc-impuestos`. **Comprobar:** los dos reinician sanos y el login del hub funciona.
4. **Broker (G-8).** §4.2. **Comprobar:** `list_user_permissions -p flit impuestos`, `tramites` y `plataforma`.
5. **Fusionar a `develop`.** Con las condiciones del flujo (work item, CI, review humano, confirmación). El CD construye y
   escribe los digests.
6. **core-impuestos:** `replicas: 1`. **Comprobar:** el log dice que migró y registró el manifiesto; `/health/ready` responde.
7. **frontend-impuestos y proxy:** `replicas: 1`, vhost o ingress hacia 4034. **Comprobar:**
   `curl -s https://dev.impuestos.flitsas.online/healthz`.
8. **Host del webhook (G-6).** El elegido en §4.6; `IMPUESTOS_SVC_AVISOS_URL_BASE` cuando M09 la lea.
9. **Encendido.** Sacar `impuestos` de «Próximamente» (PR), y el SuperAdmin enciende Impuestos para la empresa de prueba en
   Compañías → Productos. **Comprobar:** el chip de aprovisionamiento llega a «Listo» (o al motivo que corresponda) y la
   empresa aparece en el backoffice de Impuestos.

## 6. Verificación tras desplegar

```bash
kubectl -n <ns> exec deploy/core-impuestos -- wget -qO- http://localhost:4033/health/ready
kubectl -n <ns> exec deploy/gateway -- wget -qS -O /dev/null http://localhost:4002/api/v1/impuestos/sesion 2>&1 | head -1   # 401 sin token
kubectl -n <ns> exec <pod-rabbitmq> -- rabbitmqctl list_queues -p flit name messages consumers | grep impuestos
```

En el log de core-impuestos, buscar el registro del manifiesto, la sincronización de organismos y ningún
`ACCESS_REFUSED`. Nunca pegar en un ticket un log con placas, documentos o nombres de propietarios (Ley 1581).

## 7. Reversión

Primero apagar, luego dejar drenar, y solo entonces desplegar a la inversa. En DEV, el rollback de una imagen es **revertir
el commit de digests en `flit-gitops`**. Paso a paso con confirmación del líder técnico; al terminar, verificar salud (§6)
y avisar a QA y al líder con fecha, hora y commit restaurado (skill `flit-rollback-procedure`).

- **Apagar solo Impuestos (lo normal):** desactivar el producto por empresa, `replicas: 0` en los dos Deployments. El
  esquema, las credenciales cifradas y las colas quedan.
- **Migraciones:** solo hacia adelante; no `DROP SCHEMA` ni `dotnet ef database update` hacia atrás. **Nunca** borrar el
  certificado ni `impuestos.data_protection_keys`.
- **Broker:** no borrar colas, exchanges ni políticas; no `purge_queue` sin confirmación del líder.
- Si un fallo es de código y no de infraestructura, se reporta y se escala al agente implementador.

## 8. Gates del líder técnico que siguen abiertos

| Gate | Qué falta | Bloquea |
|---|---|---|
| G-2 | Rol y esquema `impuestos` en DEV | Arranque en DEV |
| G-4 | Confirmar 4033/5033/6033, 4034/5034/6034 y 8089 (registrados como propuestos en `despliegue-y-puertos.md` §2.d) | Despliegue |
| G-5 | `SVC_IMPUESTOS_CLIENT_SECRET` por ambiente y revisión de los scopes (contrato §3) | Arranque y manifiesto |
| G-6 | Host público https del webhook y su URL base | Aceptación de avisos en DEV |
| G-7 | Manifiestos en `flit-gitops/services/flit2-dev/` con `replicas: 0` | Despliegue DEV |
| G-8 | Usuario `impuestos` y escritura de `tramites` y `plataforma` en `flit.impuestos` | Aprovisionamiento en DEV |
| G-9 | `IMPUESTOS_SVC_API_BASE_URL` e `IMPUESTOS_SVC_INTEGRATION_TOKEN`, integración habilitada en el proveedor | Prueba real |
| G-10 | Certificado `.pfx` de DEV con respaldo | Arranque en DEV |
| G-11 | Categoría `impuestos` del file-manager | Recibos PDF (M11) |
| G-12 | Aval de seguridad (PII, copia de la llave de Diagnóstico, h2c) | Merge |

## 9. Documentos relacionados

- [handoff-vps-impuestos.md](handoff-vps-impuestos.md) — pasos con `docker compose`.
- [docs/impuestos/guia-entorno-local.md](../impuestos/guia-entorno-local.md) — levantar el producto en local con el stub.
- [despliegue-y-puertos.md](../despliegue-y-puertos.md) §2.d — puertos.
- [contrato-plataforma-v1.md](contrato-plataforma-v1.md) §3 — scopes de `svc-impuestos`.
- [plan-implementacion.md](../impuestos/plan-implementacion.md) §8 y §11 — infraestructura y gates.
