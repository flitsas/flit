# Flit.Gateway

API Gateway de FLIT basado en YARP (Microsoft.ReverseProxy). Reemplaza al eliminado `services/go-gateway/` por consolidación a stack .NET (**ADR-0017** — API Gateway YARP; consultar ADO o `**/ADR-*.md` en el repositorio cuando esté versionado).

## Responsabilidades

| Capa | Responsabilidad |
|---|---|
| Routing | `/api/*` → core-api (`Flit.Api`), `/hubs/*` → core-api SignalR, `/ml/*` → python-ml |
| Auth | Validación JWT RS256 al borde (llave pública distribuida desde core-api) |
| Rate limiting | Por IP (600/min default), por endpoint sensible (login: 10/min) |
| CORS | Por entorno (allowed origins en appsettings) |
| Observabilidad | OpenTelemetry (traces + spans propagated via `traceparent`) |
| Correlation | `X-Correlation-Id` (genera UUIDv7 si no viene del cliente) |
| Health | `/health` (liveness) y `/ready` (readiness — verifica core-api upstream) |

## Configuración

`appsettings.json` define Routes/Clusters de YARP. En prod, los destinos son los hostnames internos del Docker network (`core-api:8081`, `python-ml:8000`).

Variables de entorno para overrides en prod:
- `JWT__PUBLICKEYPEM` — llave pública RS256 (PEM). Inyectado desde sops.
- `RateLimit__PerIpPermitsPerMinute` — override del default 600.

## Puertos

| Ambiente | Gateway (público) | Flit.Api (interno) |
|----------|-------------------|---------------------|
| DEV | 4002 | 4003 |
| QA | 5002 | 5003 |
| PDN | 6002 (vía Caddy) | 6003 (Docker interno 8081) |

## Build & run

```bash
# Copiar config DEV local (gitignored) — obligatorio: sin esto el gateway apunta a Docker y devuelve 502
cp appsettings.Development.json.example appsettings.Development.json

cd services/core-api/src/Flit.Gateway
dotnet run
# DEV: localhost:4002 → proxea a localhost:4003 (Flit.Api) y localhost:4012 (python-ml)
```

## Notas AOT

YARP 2.2+ es AOT-compatible. Si surge warning de trim, evaluar source-gen o documentar JIT escape en `Flit.Gateway.csproj` (ver patrón en `Flit.Modules.Receipts.csproj`).
