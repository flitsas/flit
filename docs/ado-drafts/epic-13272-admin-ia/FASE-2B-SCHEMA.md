# Fase 2b — Schema Feature #13508

**Veredicto db-schema-validator:** `OK_TO_MERGE_DB` (con NA documentados)

## Artefactos

| Archivo | Rol |
|---------|-----|
| `services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/130-Feature13508-ai-tool-provider-settings.sql` | DDL Up idempotente |
| `services/core-api/src/Flit.Infrastructure/Migrations/20261012200000_Feature13508_AiToolProviderSettings.cs` | EF Up/Down |
| `services/core-api/tests/Flit.Infrastructure.Tests/Persistence/AiToolProviderSettingsSchemaTests.cs` | Tests estáticos del DDL |

## Checklist §A (resumen)

| Criterio | Resultado | Nota |
|----------|-----------|------|
| A1–A3, A7–A9, A12–A14, A18–A19 | PASS | schema `admin`, uuidv7, FK+índice vía UNIQUE, ADR-0070 |
| A4, A10, A11 | **NA** | Global plataforma sin `tenant_id`/RLS (ADR-0070 / ADR-0058) |
| A5 parcial | PASS | settings: created/updated + row_version; sin soft delete |
| A6 | **NA** | Sin soft delete (ADR); transitions append-only; alerts ack |
| A15 | NA | Sin columnas PII (keys solo env) |
| A16 | PASS | row_version + audit_log en settings; immutable en transitions |
| A17 | PASS | Down en migración EF |

## Notas backend-agent

- Mapear entidades EF en `Persistence/Entities/Admin/` + `IEntityTypeConfiguration` (excluidas de snapshot si se sigue patrón mandate/notification).
- `providers_json` → `JsonDocument` / VO tipado; nunca serializar API keys.
- Repos: lecturas `AsNoTracking`; updates settings con `row_version` concurrency token.
- No aplicar esta migración a DEV/QA/PDN desde database-agent — infra/deploy pipeline.
