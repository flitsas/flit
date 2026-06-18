# ADR-0019: Motor de parametrización — Catálogos globales sin tenant_id (SuperAdmin)

**Fecha**: 2026-06-18
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, Samuel Cardenas (Feature #10116)
**Tags**: arquitectura, backend, datos, tramites, parametrizacion, fase-1-diseño

## Contexto

El motor dinámico de trámites (Feature #10116, HU #10151) define tablas de catálogo en el schema `tramites`:
- `procedure_entities` — aristas del trámite (VEHICLE, OWNER, BUYER, LESSEE)
- `external_data_sources` — fuentes externas (SIMIT, RUNT, RNMC, RUES, FASECOLDA, RESOLUCIONES)
- `consultation_templates` — plantillas de consulta (RUNT_VEHICLE, RUNT_ACTOR_NATURAL, etc.)
- `procedure_types` — tipos de trámite (MATRICULA_NUEVA, TRASPASO_SIMPLE, etc.)

El checklist A4 de `db-schema-validator` requiere `tenant_id NOT NULL` en tablas de negocio. Sin embargo, estos catálogos son de naturaleza **global**: son configurados una sola vez por el equipo SuperAdmin de FLIT y aplican a **todos** los tenants sin diferenciación.

## Decisión

Las tablas `procedure_entities`, `external_data_sources`, `consultation_templates` y `procedure_types` **no llevan `tenant_id`** y son administradas exclusivamente por el rol SuperAdmin de FLIT.

Excepción formal al checklist A4 y A20 documentada en este ADR.

## Alternativas consideradas

### Opción A: tenant_id nullable con NULL = global

**Pros:** compatible con el checklist A4 sin excepción formal.
**Cons:** consultas complejas (`WHERE tenant_id IS NULL OR tenant_id = :tid`), riesgo de filtros incorrectos en repositorios futuros.
**Esfuerzo:** M
**Riesgos:** drift entre tenants si se mezclan registros globales y por-tenant.

### Opción B: Duplicar catálogos por tenant (copia en onboarding)

**Pros:** máximo aislamiento de datos por tenant.
**Cons:** explosión de filas, mantenimiento sincronizado imposible a escala, incoherencia en actualizaciones de catálogos regulatorios (RUNT/SIMIT cambian).
**Esfuerzo:** L
**Riesgos:** deriva silenciosa entre tenants.

### Opción C: Catálogos globales sin tenant_id + RBAC SuperAdmin (elegida)

**Pros:** modelo simple, coherencia regulatoria garantizada, sin RLS necesario en catálogos.
**Cons:** excepción al checklist A4; se pierde la inferencia automática de `tenant_id` en queries EF.
**Esfuerzo:** S
**Riesgos:** SuperAdmin debe ser el único con escritura; endpoints de lectura deben ser públicos a tenants.

## Tradeoff aceptado

Se acepta la excepción a checklist A4 para estos catálogos globales. La protección se realiza mediante:
1. **RBAC**: solo rol `superadmin` tiene permiso `tramites:catalogs:write`.
2. **Sin RLS** en estas tablas (no tienen `tenant_id`); acceso de lectura global.
3. **Datos sin secretos**: `base_url` en `external_data_sources` usa stubs en seeds; credenciales API gestionadas fuera de BD (variables de entorno / secrets manager).

## Consecuencias

### Lo que se gana
- Modelo de datos limpio y predecible para todos los tenants.
- Actualizaciones regulatorias (nuevas fuentes RUNT, cambios SIMIT) aplicadas una vez.
- Queries de lectura simples sin filtro de tenant.

### Lo que se pierde
- No hay personalización de catálogos por tenant en fase-1 (puede agregarse en fase-2 con override por tenant si se requiere).

### Seguimiento
- HU #10183 (HU-2): entidades EF Core de tramites — deben reflejar esta excepción en las configuraciones `IEntityTypeConfiguration` (sin `HasQueryFilter` de tenant en catálogos).
- Checklist `db-schema-validator` A4/A20: marcar PASS con referencia a este ADR en PRs de tramites.
- Seeds mínimos: `04-HU10151-seeds-minimos.sql`.

## Referencias
- ADR-0018: Modelo de datos fase-1 FLIT Evolution
- Checklist: `.cursor/skills/db-schema-validator/checklist-validacion-schema.md` (§A4, §A20)
- DDL: `services/core-api/docs/schema/ddl/04-HU10151-revision-parametrizacion.sql`
