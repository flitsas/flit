# ADR-0018: Modelo de datos fase-1 FLIT Evolution

**Fecha**: 2026-06-17  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT, equipo core-api  
**Tags**: arquitectura, backend, datos, fase-1-diseño

## Contexto

El feature pack fase-1 (#10113–#10139) requiere un modelo PostgreSQL multi-tenant con schemas por bounded context (`identity`, `security`, `tramites`, `admin`, `analytics`, `catalogs`, `audit`). Las HUs #10146–#10155 materializan el DDL en migraciones EF Core del monorepo `core-api`, con RLS por `tenant_id`, soft delete, `row_version` y trazabilidad PII (Ley 1581).

## Decisión

Adoptar **un único DbContext (`FlitDbContext`) en `Flit.Infrastructure`** con migraciones ordenadas por HU, DDL embebido en recursos (`Persistence/Sql/Ddl/`), convención **snake_case** vía `EFCore.NamingConventions`, y parche de conformidad `11-schema-conformance-patch.sql` para PK `pk_*`, RLS, triggers y comentarios `@pii:`.

## Alternativas consideradas

### Opción 1: Módulos EF separados por bounded context

**Pros:** aislamiento por contexto, equipos paralelos.  
**Cons:** orden de migraciones complejo, FKs cross-schema frágiles.  
**Esfuerzo:** L  
**Riesgos:** drift entre contextos.

### Opción 2: Solo scripts SQL sin EF (flyway/psql)

**Pros:** control total del DDL.  
**Cons:** desalineación modelo C# ↔ BD, sin snapshot EF.  
**Esfuerzo:** M  
**Riesgos:** deuda en capa aplicación.

### Opción 3: DbContext único + DDL embebido por HU (elegida)

**Pros:** trazabilidad HU→migración, `dotnet ef database update`, reversible por HU.  
**Cons:** snapshot EF parcial hasta mapear entidades de todas las tablas.  
**Esfuerzo:** M  
**Riesgos:** migraciones SQL-only hasta completar entidades.

## Tradeoff aceptado

Se prioriza **entrega incremental por HU** (DDL ejecutable y reversible) sobre mapeo EF completo de las ~48 tablas en una sola iteración.

## Consecuencias

### Lo que se gana
- Orden de despliegue explícito alineado con dependencias FK.
- Validación automatizable con `db-schema-validator`.
- RLS y auditoría centralizados desde bootstrap.

### Lo que se pierde
- Entidades EF y repositorios aún no cubren todo el modelo fase-1.

### Seguimiento
- HUs #10146–#10155 (schema/migración).
- Checklist: `.cursor/skills/db-schema-validator/checklist-validacion-schema.md`.
- Índice DDL: `services/core-api/docs/schema/README.md`.
