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

### gRPC: borradores desde Diagnóstico (ADR-0084)

`contracts/proto/flit/tramites/v1/derivacion.proto` — `DerivacionTramitesService`, servidor a servidor, sin wizard.

- **Quién**: solo `svc-diagnostico`, `aud=tramites`, scope `platform.tramites.derivacion`; la empresa en `x-flit-tenant-id`.
- **Encendido**: `Tramites:Derivacion:Habilitada` (apagado por defecto: el servicio no se mapea y responde `UNIMPLEMENTED`).
- **Techo**: `Tramites:Derivacion:ConcurrenciaMaxima` (8). Lleno → `RESOURCE_EXHAUSTED` inmediato; debe ser ≥ la suma de
  `Diagnostico:Derivacion:Concurrencia` de las réplicas de Diagnóstico.
- **`CrearBorradorDesdeDiagnostico`**: idempotente por `(empresa, external_ref = vehiculo_id)`. Mismo trámite con
  `ya_existia=true` si ya es de `origin=diagnostico`; `REFERENCIA_DE_OTRO_ORIGEN` si es de otro canal. Autor = el usuario
  real (`usuario_id`): activo, miembro de la empresa, sin suspensión, con `tramites.create` + Crear de la familia (permisos
  efectivos calculados de la base, `Flit.Api/Derivacion/AutorDerivacion.cs`). OT de la lista efectiva por nombre RUNT y,
  si no casa, por código; nunca se crea sin OT. Canal `tramites`. Fallos de siembra van a `advertencias`.
- **Rechazos** (`rechazo.codigo`, no reintentables): `TIPO_NO_DISPONIBLE`, `FAMILIA_BLOQUEADA`, `USUARIO_SIN_PERMISO`,
  `OT_NO_HABILITADO`, `REGLA_COMPANIA`, `TRAMITE_ACTIVO_DUPLICADO`, `EMPRESA_INACTIVA`, `REFERENCIA_DE_OTRO_ORIGEN`,
  `DATOS_INVALIDOS`. Técnicos como status: `RESOURCE_EXHAUSTED`, `UNAVAILABLE`, `INTERNAL`.
- **`ObtenerTramitesPorReferencia`**: hasta 500 referencias (`INVALID_ARGUMENT` si hay más); solo los de `origin=diagnostico`.
- **Logs**: ids, código y duración; nunca placa, VIN ni documentos.

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
