# Checklist de validación — db-schema-validator

Referencia: `.cursor/skills/db-schema-validator/checklist-validacion-schema.md` (§A schema, §B repositorio).

---

## A. Schema y migración

Por cada tabla nueva o alteración significativa:

- [ ] **A1** Schema de bounded context (no `public`)
- [ ] **A2** Nombre tabla: `snake_case`, plural, inglés
- [ ] **A3** PK: `id uuid PRIMARY KEY DEFAULT uuidv7()`
- [ ] **A4** Tabla de negocio: `tenant_id NOT NULL` + FK a `identity.tenants`
- [ ] **A5** Columnas estándar: `created_at/by`, `updated_at/by`, `deleted_at/by`, `row_version`
- [ ] **A6** Soft delete (`deleted_at`, `deleted_by`) salvo excepción documentada en ADR
- [ ] **A7** FKs: patrón `fk_<table>_<referenced>[_<role>]`
- [ ] **A8** FKs: `ON DELETE` y `ON UPDATE` explícitos
- [ ] **A9** FKs: índice cubriendo la columna FK
- [ ] **A10** RLS habilitado + política `tenant_isolation` en tablas con `tenant_id`
- [ ] **A11** Índices: `tenant_id` como primera columna (salvo PK por `id`)
- [ ] **A12** Nomenclatura constraints/índices: `pk_`, `uq_`, `ck_`, `ix_`, `tr_`
- [ ] **A13** Tipos prohibidos ausentes: `float`/`real` (montos), `timestamp` sin tz, `serial`, `json` sin b
- [ ] **A14** Montos: `numeric(15,2)`; fechas-hora: `timestamptz`
- [ ] **A15** PII: `COMMENT` con `@pii:high|medium|low` en columnas aplicables
- [ ] **A16** Triggers: `row_version` (BEFORE UPDATE) y `audit_log` (AFTER I/U/D) en tablas de negocio
- [ ] **A17** Migración reversible: `Up` y `Down` (o script SQL con rollback)
- [ ] **A18** No duplica tabla existente (validación semántica contra migraciones previas)
- [ ] **A19** Entidad de negocio nueva: ADR `Propuesto` referenciado en PR/descripción
- [ ] **A20** Catálogos: estructura §9.1 (`is_active`, sin `tenant_id`, `external_refs jsonb`)

---

## B. Capa de acceso a datos

Si el PR toca repositorios, configuraciones EF Core o interceptores:

- [ ] **B1** `Domain`/`Application` no importan EF Core ni `Infrastructure`
- [ ] **B2** Interfaz de repositorio en `domain`; implementación en `infrastructure`
- [ ] **B3** No expone `IQueryable`/`DbSet` fuera de `infrastructure`
- [ ] **B4** Filtro global tenant + soft delete en entidades de negocio
- [ ] **B5** Sin filtrado manual redundante de `tenant_id` en repositorios
- [ ] **B6** Lecturas puras usan `AsNoTracking()`
- [ ] **B7** Listados paginados (sin `ToListAsync()` ilimitado en tablas de negocio)
- [ ] **B8** `row_version` mapeado como `IsConcurrencyToken()` + manejo de concurrencia
- [ ] **B9** Mapeo en `IEntityTypeConfiguration`, no Data Annotations en dominio
- [ ] **B10** Sin SQL por concatenación (`FromSqlRaw` con `$"..."` = BLOCKED)
- [ ] **B11** Sin `IgnoreQueryFilters()` salvo ADR
- [ ] **B12** Errores de BD traducidos a errores de dominio (no `PostgresException` al cliente)
- [ ] **B13** Soft delete vía `SoftDelete()`, no `Remove()` físico
- [ ] **B14** Interceptor de tenant (`set_config('app.current_tenant_id', ...)`) registrado

---

## C. Anti-patterns rápidos (grep)

Marcar **BLOCKED** si se encuentra:

| Patrón | Regla violada |
|---|---|
| Tabla en schema `public` (negocio) | A1 |
| `is_deleted boolean` | checklist §B |
| `float`/`real` en columnas de monto | A13 |
| `IgnoreQueryFilters()` sin comentario/ADR | B11 |
| `FromSqlRaw($"..." )` o concatenación SQL | B10 |
| `DbContext` inyectado en handler/controller | B1/B2 |
| `[Table]` en entidad de `Domain/` | B9 |
