---
name: db-schema-validator
description: Valida migraciones PostgreSQL y capa de repositorio EF Core contra `.cursor/skills/db-schema-validator/checklist-validacion-schema.md`. Solo lectura; no modifica código. Usar antes de mergear PRs con DDL, migraciones EF Core o cambios en repositorios. Invocada por database-agent (Modo C) o code-review-agent cuando el PR toca persistencia. Triggers db-schema-validator, validar migración, validar schema, DDL, RLS, tenant_id, EF Core repositorio, validación de datos.
---

Solo lectura; no modifica código ni aplica migraciones. Checklist detallado en `./checklist-validacion-schema.md`. Formato de salida en `./plantilla-salida-validacion.md`.

## Pre-flight

1. `.cursor/skills/db-schema-validator/checklist-validacion-schema.md` — schema (§A) y repositorio (§B)
2. ADRs vigentes en el repositorio (`**/ADR-*.md`) vinculados a la entidad/migración
4. Migraciones existentes en `services/core-api/**/Migrations/` (evitar duplicados semánticos)
5. Diff del PR o archivos de migración indicados por el invocador

## Alcance

| Tipo de cambio | Qué validar |
|---|---|
| Migración nueva (CREATE/ALTER) | Checklist schema §A completo |
| Seed de catálogos | Estructura §A20 + catálogos |
| EF Core `IEntityTypeConfiguration` | Alineación schema↔código + checklist §A |
| Repositorios / interceptores | Checklist §B |
| Solo lectura sin DDL | §B parcial (repositorios tocados) |

Si el PR **no** incluye cambios de persistencia → veredicto **NA**.

## Procedimiento

1. Identificar archivos de migración y/o persistencia en el diff:
   ```bash
   git diff --name-only origin/develop...HEAD
   ```
2. Por cada `CREATE TABLE` / migración EF Core equivalente, ejecutar el checklist §A (ver `./checklist-validacion-schema.md`).
3. Si hay cambios en `Infrastructure/Persistence/` o repositorios, ejecutar checklist §B.
4. Buscar anti-patterns del checklist §A y §B.
5. Verificar que entidades de negocio nuevas tengan ADR `Propuesto` referenciado en el PR.
6. Emitir reporte con `./plantilla-salida-validacion.md`.

## Comandos útiles

```bash
git diff origin/develop...HEAD -- "**/Migrations/**"
git diff origin/develop...HEAD -- "**/Persistence/**" "**/Repositories/**"
rg -n "CREATE TABLE|migrationBuilder\.Sql|IgnoreQueryFilters|FromSqlRaw\(\$" --glob "*.{cs,sql}"
rg -n "float|real|timestamp[^z]|serial|is_deleted" --glob "*.{cs,sql}"
rg -n "@pii:" --glob "*.sql"
```

## Veredicto de datos

| Resultado | Condición |
|---|---|
| **OK_TO_MERGE_DB** | Todos los ítems aplicables PASS |
| **MISSING_N** | Fallos corregibles (nomenclatura, índice faltante, comment PII, etc.) |
| **BLOCKED** | Violación grave: sin `tenant_id`/RLS, SQL concatenado, `IgnoreQueryFilters` sin ADR, tabla en `public`, migración irreversible |
| **NA** | Sin cambios de persistencia en el PR |

## Prohibido

- Modificar migraciones o código
- Aplicar migraciones a DEV/QA/PDN (eso es `infra-agent`)
- Aprobar desviaciones de convención sin ADR referenciado
- Omitir ítems del checklist por PR pequeño
- Sustituir el review formal del PR (`code-review-agent`); esta skill valida solo la dimensión **datos**

## Invocación típica

```
Usa db-schema-validator para validar la migración del PR !88
Usa db-schema-validator sobre los archivos en services/core-api/Modules/Procedures/Migrations/
```
