# services/core-api — .NET 10 / C#

> **Estado:** scaffold mínimo (boilerplate FLIT post-consolidación stack 2026-05-27).
> **ADR base:** ADR-0002 (arquitectura microservicios) y ADR-0014 (consolidación stack) — consultar en ADO o en el repositorio (`**/ADR-*.md`) cuando estén versionados.
> **Agente:** [`.cursor/agents/backend-agent.md`](../../.cursor/agents/backend-agent.md) · convenciones en [`.cursor/rules/00-flit-conventions.mdc`](../../.cursor/rules/00-flit-conventions.mdc).

## Lo que está en este scaffold

- `global.json` — SDK .NET 10 con `rollForward: latestPatch`.
- `Directory.Build.props` / `Directory.Packages.props` — Central Package Management.
- `Flit.slnx` — solución con `Flit.Api`, `Flit.Gateway`, `Flit.Infrastructure`.
- `src/Flit.Api/` — host ASP.NET Core (composición DI, EF Core vía Infrastructure).
- `src/Flit.Gateway/` — API Gateway YARP (proxy, JWT, rate limit, correlation id). Ver [README del Gateway](src/Flit.Gateway/README.md).
- `src/Flit.Infrastructure/` — `FlitDbContext`, factory de diseño, extensiones PostgreSQL.
- `Dockerfile` — imagen multi-stage para el servicio.

## Contratos API

OpenAPI versionado en el monorepo:

- `contracts/openapi/core-api.v1.yaml` (cuando exista en el branch)
- CI: `.github/workflows/contracts.yml`

## Convenciones de datos

- Checklist schema y repositorio: [`.cursor/skills/db-schema-validator/checklist-validacion-schema.md`](../../.cursor/skills/db-schema-validator/checklist-validacion-schema.md)
- Migraciones: `services/core-api/src/Flit.Infrastructure/**/Migrations/` vía `pnpm migrate:core-api` desde la raíz.

## Comandos típicos

Desde la raíz del monorepo:

```bash
pnpm run install:dotnet
pnpm run build:core-api
pnpm run test:core-api
pnpm run dev:core-api      # Flit.Api → http://localhost:4003
pnpm run dev:gateway       # Flit.Gateway → http://localhost:4002
pnpm run migrate:core-api
```

Desde este directorio:

```bash
dotnet restore
dotnet build
dotnet run --project src/Flit.Api
dotnet run --project src/Flit.Gateway
```

## Notas

- El gateway en DEV requiere `appsettings.Development.json` (copiar desde `.example` en `Flit.Gateway/`).
- Sin `ConnectionStrings:Core` en configuración, `Flit.Api` falla al arrancar — ver `.env.example` en la raíz.
