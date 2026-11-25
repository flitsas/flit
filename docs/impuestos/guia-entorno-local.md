# Impuestos en local: levantar, simular el API y probar

> Generado: 2026-11-25. Rama `feature/AB-14694-producto-impuestos`, módulo M20 (antes de M07 y M09–M14: el alta, la
> liquidación, los avisos y los recibos todavía no están en la rama). Las anclas derivan rápido: reverifícalas.
> Complementa [../suite/local.md](../suite/local.md), [../diagnosticos/guia-entorno-local-suite.md](../diagnosticos/guia-entorno-local-suite.md)
> (la suite completa) y el [plan](plan-implementacion.md) §7 (stub) y §8 (infraestructura).

**Regla de oro:** todo por `http://127.0.0.1:<puerto>`, nunca `localhost` (las cookies de sesión no distinguen puertos,
[../suite/local.md](../suite/local.md)).

## 1. Qué corre y dónde

| Pieza | Local | Qué hace |
|---|---|---|
| Stub del API de liquidación (`scripts/dev/stub-impuestos/`) | `8898` | API sintético: alta de empresa, `/fuentes`, consultas, avisos firmados y PDFs, con escenarios |
| `core-impuestos` | `4033` · gRPC `8089` | API del producto (`/api/v1/impuestos/*`) |
| `frontend-impuestos` | `4034` | App web |
| gateway | `4002` | Enruta `/api/v1/impuestos/*` a 4033 (`impuestos-route`) |
| core-api | `4003` · gRPC `8082` | Login OIDC, tokens de servicio, organismos y usuarios del producto |
| core-identity | `4025` · gRPC `8083` | Suscripción por petición y datos del alta |
| hub | `4022` | Login y menú de productos |
| RabbitMQ | `5672` · consola `15672` | Bus (`flit.impuestos`) |
| PostgreSQL de pruebas | `5433` | Base `flit_suite` (nunca la 5432: ahí están `flit_local`, QA y PDN) |

`scripts/dev/` está **fuera de git** (`.gitignore`): el stub, el script de la suite y el del certificado viven solo en tu
copia local. Si no los tienes, pídelos o sigue §3.2.

## 2. Requisitos

- Los de la suite ([guia-entorno-local-suite.md](../diagnosticos/guia-entorno-local-suite.md) §1): Git Bash, .NET 10, pnpm,
  Python 3, RabbitMQ local y la BD de pruebas 5433 arriba (`.claude/rules/pruebas-locales.md` §1).
- `~/.flit/local.env` con `SVC_IMPUESTOS` y `MQ_IMPUESTOS` (secretos aleatorios locales; el script de la suite los genera
  la primera vez si faltan): `python -I -c 'import secrets; print(secrets.token_urlsafe(32))'`.
- **Certificado de Data Protection** de core-impuestos en `infra/secrets/impuestos-dp.pfx` + `.clave` (gitignored), distinto
  del de Diagnóstico. Sin él core-impuestos no arranca. Con el script local (desde la raíz, en PowerShell):

  ```powershell
  . .\scripts\dev\nuevo-certificado-impuestos.ps1     # crea el .pfx la primera vez y carga Impuestos__DataProtection__*
  ```

  No lo regeneres con `-Forzar` si ya tienes datos: las credenciales cifradas de tu base dejarían de leerse.
- `services/core-impuestos/src/Flit.Impuestos.Api/appsettings.Development.json`, copiado de su `.example` (gitignored). Las
  variables de entorno mandan sobre él.

## 3. Levantar

### 3.1 Con el script de la suite

```bash
scripts/dev/levantar-diagnostico-local.sh            # simulado; --sin-build para no recompilar
scripts/dev/levantar-diagnostico-local.sh parar
```

Cuando tu copia del script trae el bloque de Impuestos, levanta además el stub (8898), core-impuestos (4033/8089) y
frontend-impuestos (4034), crea el usuario `impuestos` del broker y registra `svc-impuestos` y el host
`http://127.0.0.1:4034` del cliente OIDC `impuestos` en core-api y core-identity. Los servicios .NET corren desde
`E:/flit/bin/<servicio>`, así `dotnet build`/`dotnet test` del repo no chocan con sus DLL.

### 3.2 Equivalente manual

Con core-api, core-identity, gateway, hub y RabbitMQ ya arriba (guía de la suite §3.2), en este orden:

1. **core-api y core-identity** tienen que haber arrancado con
   `Suite__Oidc__ServiceClients__svc-impuestos__Secret=$SVC_IMPUESTOS` y `Suite__Hosts__Overrides__impuestos=http://127.0.0.1:4034`
   (los dos con los mismos valores: el último que arranca reescribe los clientes de login).
2. **RabbitMQ:** usuario `impuestos` con clave `$MQ_IMPUESTOS`; configura `^(flit\.impuestos|impuestos\..*)$`, escribe
   `^(flit\.impuestos|flit\.notificaciones|impuestos\..*)$`, lee `^(flit\..*|impuestos\..*)$`.
3. **Stub:**

   ```bash
   python -I scripts/dev/stub-impuestos/stub_impuestos.py      # 127.0.0.1:8898; imprime el INTEGRATION_TOKEN del stub
   ```

4. **core-impuestos** (desde `services/core-impuestos/src/Flit.Impuestos.Api`):

   ```bash
   env ASPNETCORE_ENVIRONMENT=Development \
     "ConnectionStrings__Servicio=Host=127.0.0.1;Port=5433;Database=flit_suite;Username=postgres;Password=postgres" \
     Urls=http://127.0.0.1:4033 Servicio__GrpcPort=8089 Servicio__MigrateOnStartup=true \
     Platform__Auth__JwksUri=http://127.0.0.1:4003/.well-known/jwks.json Platform__Auth__Issuers__0=http://127.0.0.1:4022/ \
     Platform__ServiceClient__TokenEndpoint=http://127.0.0.1:4003/connect/token \
     "Platform__ServiceClient__ClientSecret=$SVC_IMPUESTOS" \
     "Platform__Messaging__ConnectionString=amqp://impuestos:$MQ_IMPUESTOS@127.0.0.1:5672/flit" \
     Impuestos__Api__BaseUrl=http://127.0.0.1:8898 \
     Impuestos__Api__IntegrationToken=<el que imprime el stub> \
     Impuestos__Tramites__Address=http://127.0.0.1:8082 \
     Impuestos__Identidad__Address=http://127.0.0.1:8083 \
     Impuestos__Plataforma__GatewayUrl=http://127.0.0.1:4002 \
     dotnet run --no-launch-profile
   ```

   Más `Impuestos__DataProtection__CertificadoRuta` y `__CertificadoClave` (los carga el script del certificado). Con
   `Impuestos__Identidad__SimularSuscripcionActiva=true` (solo local) toda empresa queda suscrita sin preguntarle a
   Identidad; útil si core-identity no está arriba.

5. **Gateway:** `ReverseProxy__Clusters__core-impuestos-cluster__Destinations__core-impuestos-1__Address=http://127.0.0.1:4033/`.
6. **frontend-impuestos:** copia `frontend-impuestos/.env.example` a `.env.local` y
   `pnpm --filter @flit/frontend-impuestos dev` (`http://127.0.0.1:4034`).

`Impuestos__Api__BaseUrl` acepta http **solo** en loopback (el stub); fuera de local el API exige https.

## 4. El stub

Panel de escenarios en `http://127.0.0.1:8898/__panel`; estado sin secretos en `/__estado`. Un escenario se elige por
cabecera `X-Stub-Escenario`, por placa o `tax_id` mágico, o con el defecto del panel. La tabla completa (alta, consultas,
avisos desordenados, firma inválida, reintentos, sin callback, archivos) está en `scripts/dev/stub-impuestos/README.md` y en
el plan §7. Todos los datos son sintéticos: no uses placas ni NIT reales.

Vector de firma para comprobar un receptor: secreto `whsec_prueba`, ts `1700000000`, cuerpo `{"a":1}` ⇒
`v1=0ca0bb9c916989fb6c1d1eb729a180da30cb078b7cbe551c8f8da0ec452ac9db`.

## 5. Verificación rápida

```bash
curl -s http://127.0.0.1:8898/__estado | head -c 300; echo
curl -s http://127.0.0.1:4033/health/ready; echo
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:4002/api/v1/impuestos/sesion   # 401 sin token: el gateway llega
```

Luego, en `http://127.0.0.1:4022`, entra como SuperAdmin, enciende Impuestos para una empresa de prueba (Compañías →
Productos) y abre Impuestos desde el menú de productos.

## 6. Pruebas

Siempre por `scripts/dev/t.sh`, solo las clases de los archivos tocados y con la BD 5433 (`.claude/rules/pruebas-locales.md`):

```bash
export ConnectionStrings__Core="Host=localhost;Port=5433;Database=<bd>;Username=postgres;Password=postgres"
scripts/dev/t.sh dotnet services/core-impuestos/tests/Flit.Impuestos.Tests/Flit.Impuestos.Tests.csproj --build --filter "FullyQualifiedName~<Clase>"
scripts/dev/t.sh vitest frontend-impuestos/__tests__/<archivo>.test.tsx
python -I -m unittest discover -s scripts/dev/stub-impuestos -p test_stub.py
```

La ruta del gateway tiene su prueba en `services/core-api/tests/Flit.Gateway.Tests/ImpuestosRoutingTests.cs`.

## 7. Problemas frecuentes

| Síntoma | Causa | Qué hacer |
|---|---|---|
| core-impuestos no arranca: «falta configuración: …» | Falta una variable obligatoria | El mensaje dice cuál y de qué variable del `.env` sale |
| No arranca por el certificado | Falta `infra/secrets/impuestos-dp.*` o la clave no coincide | Corre el script del certificado (sin `-Forzar` si ya hay datos) |
| `GET /sesion` responde 403 `PRODUCTO_NO_HABILITADO` | La empresa no tiene Impuestos o falta `Impuestos__Identidad__Address` | Enciéndelo en Compañías → Productos o revisa core-identity 8083 |
| 502 en `/api/v1/impuestos/*` | El gateway no tiene el destino 4033 | Variable del clúster en el gateway (§3.2, paso 5) |
| El login de Impuestos vuelve al hub con error de `redirect_uri` | core-api y core-identity arrancaron con hosts distintos | Reinícialos con el mismo `Suite__Hosts__Overrides__impuestos` |
| `ACCESS_REFUSED` en el log | Faltan permisos del usuario `impuestos` | §3.2, paso 2 |
