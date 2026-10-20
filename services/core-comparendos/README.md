# core-comparendos — Gestión de Comparendos

API de **Comparendos** (GDC V2) en la FLIT Suite, producto `comparendos` del contrato de plataforma v1. Nació de la
plantilla de servicio (`services/core-plantilla`) y sigue [servicio-nuevo.md](../../docs/suite/servicio-nuevo.md).
Seguimiento en ADO: Epics #12878 a #12984 y #13029 (proyecto FLIT - GESTION COMPARENDOS).

| | Local / DEV | QA | PDN |
|---|---|---|---|
| REST (interno, detrás del gateway en `/api/v1/comparendos/*`) | 4028 | 5028 | 6028 |
| gRPC (interno) | 8086 | 8086 | 8086 |
| Su app web (`frontend-comparendos`) | 4023 | 5023 | 6023 |

## Estructura

| Proyecto | Qué tiene |
|---|---|
| `Flit.Comparendos.Domain` | Reglas puras, sin base ni red: festivos y días hábiles, motor de plazos y descuentos, normalización de NIT, placa y número de comparendo, enumeraciones del modelo. |
| `Flit.Comparendos.Api` | Endpoints, persistencia (esquema `comparendos`, EF con nombres en snake_case), módulos, trabajos programados e integraciones. |
| `Flit.Comparendos.Grpc.Contracts` | Contrato `flit.comparendos.v1` (`contracts/proto/flit/comparendos/v1`). |

## Cómo decide

La fuente de verdad funcional es la especificación GDC V2. Ante una contradicción entre documentos manda lo que
respondió producto en el Excel «Preguntas pendientes GDC»; si una pregunta quedó sin respuesta, su propuesta de
desarrollo. Las reglas en el código citan su origen (por ejemplo `[Excel C9]` o `RN-074`).

## Autenticación y permisos

- Usuarios: token del producto (`aud=comparendos`) que emite el hub. La empresa sale del claim `tenant_id`; nunca de
  una cabecera. Los permisos `comparendos.*` los registra el servicio al arrancar con su manifiesto (cliente de
  servicio `svc-comparendos`, scope `platform.manifest`).
- El SuperAdmin ve todas las empresas y configura lo global, pero no opera comparendos de una empresa [Excel S2].
- Las capacidades contratadas por empresa (DP, contraventor, OCR, reportes) se validan en cada endpoint [Excel S1].

## Correr en local

```bash
cd services/core-comparendos
ConnectionStrings__Servicio="Host=127.0.0.1;Port=5432;Database=flit_comparendos;Username=$USER" \
Platform__Auth__JwksUri=http://127.0.0.1:4003/.well-known/jwks.json \
Platform__Auth__Issuers__0=http://127.0.0.1:4022/ \
Platform__ServiceClient__TokenEndpoint=http://127.0.0.1:4003/connect/token \
Platform__ServiceClient__ClientSecret=<secreto de svc-comparendos> \
Platform__Messaging__ConnectionString=amqp://comparendos:<clave>@127.0.0.1:5672/flit \
Comparendos__Plataforma__GatewayUrl=http://127.0.0.1:4002 \
Servicio__GrpcPort=8086 ASPNETCORE_URLS=http://127.0.0.1:4028 \
dotnet run --project src/Flit.Comparendos.Api
```

core-api necesita `Suite__Hosts__Overrides__comparendos=http://127.0.0.1:4023` y el secreto
`Suite__Oidc__ServiceClients__svc-comparendos__Secret`. Sin file-manager (`FILE_MANAGER_BASE_URL` vacío) los archivos
quedan en la tabla `archivo_local`.

## Pruebas

`dotnet test Flit.Comparendos.slnx`. Las de API usan el Postgres de `ConnectionStrings__Core` con una base efímera por
clase y tokens firmados con una llave propia; sin Postgres se omiten en local y fallan en CI.
