-- HU #10151 (Revisión) — Motor parametrización: DDL incremental | Feature #10116
-- Migración: 20260617230250_HU10151_RevisionParametrizacion
-- Prerequisito: 04-HU10151-tramites-parametrizacion.sql ya aplicado
--
-- CHECKLIST A20: consultation_templates y external_data_sources son catálogos globales
-- (sin tenant_id) administrados exclusivamente por SuperAdmin. Excepción documentada en
-- ADR-0019-motor-parametrizacion-global-superadmin.md.

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Nueva tabla: tramites.consultation_templates (G1)
--    Catálogo global (sin tenant_id): excepción A20 documentada en ADR-0019.
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE tramites.consultation_templates (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_consultation_templates PRIMARY KEY (id),
    external_data_source_id uuid NOT NULL
        REFERENCES tramites.external_data_sources(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    code varchar(50) NOT NULL,
    name varchar(150) NOT NULL,
    entity_scope varchar(30) NOT NULL,          -- vehicle | actor
    person_type varchar(20),                    -- natural | juridical | null (vehículo)
    required_field_keys jsonb NOT NULL DEFAULT '[]',
    request_schema jsonb NOT NULL DEFAULT '{}',
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}',
    row_version bigint NOT NULL DEFAULT 0,
    -- A5: audit columns (catálogo global: sin soft-delete, A6 excepción ADR-0019)
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_consultation_templates_code UNIQUE (code),
    CONSTRAINT ck_consultation_templates_entity_scope
        CHECK (entity_scope IN ('vehicle', 'actor')),
    CONSTRAINT ck_consultation_templates_person_type
        CHECK (person_type IS NULL OR person_type IN ('natural', 'juridical'))
);
-- A9: índice FK
CREATE INDEX ix_consultation_templates_external_data_source_id
    ON tramites.consultation_templates(external_data_source_id);

-- A16: triggers row_version y audit en consultation_templates
DROP TRIGGER IF EXISTS tr_consultation_templates_row_version ON tramites.consultation_templates;
CREATE TRIGGER tr_consultation_templates_row_version
    BEFORE UPDATE ON tramites.consultation_templates
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_consultation_templates_audit ON tramites.consultation_templates;
CREATE TRIGGER tr_consultation_templates_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.consultation_templates
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. ALTER TABLE tramites.procedure_types (G2)
--    Ciclo de vida: publication_status, published_at, published_by, row_version
--    NO se agregan is_locked ni consultation_template_id (van en form_fields, G3)
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE tramites.procedure_types
    ADD COLUMN IF NOT EXISTS publication_status varchar(20) NOT NULL DEFAULT 'draft',
    ADD COLUMN IF NOT EXISTS published_at timestamptz,
    ADD COLUMN IF NOT EXISTS published_by uuid,
    ADD COLUMN IF NOT EXISTS row_version bigint NOT NULL DEFAULT 0;

-- A13: CHECK solo draft | published | archived (sin 'review')
ALTER TABLE tramites.procedure_types
    ADD CONSTRAINT ck_procedure_types_publication_status
        CHECK (publication_status IN ('draft', 'published', 'archived'));

-- A16: triggers row_version y audit en procedure_types
DROP TRIGGER IF EXISTS tr_procedure_types_row_version ON tramites.procedure_types;
CREATE TRIGGER tr_procedure_types_row_version
    BEFORE UPDATE ON tramites.procedure_types
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_procedure_types_audit ON tramites.procedure_types;
CREATE TRIGGER tr_procedure_types_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_types
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. ALTER TABLE tramites.form_fields (G3)
--    Campos bloqueados / sembrados por plantilla de consulta.
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE tramites.form_fields
    ADD COLUMN IF NOT EXISTS is_locked boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS lock_reason varchar(200),
    ADD COLUMN IF NOT EXISTS consultation_template_id uuid,
    ADD COLUMN IF NOT EXISTS row_version bigint NOT NULL DEFAULT 0;

-- FK explícita nombrada (necesaria para rollback idempotente)
ALTER TABLE tramites.form_fields
    ADD CONSTRAINT fk_form_fields_consultation_templates
        FOREIGN KEY (consultation_template_id)
        REFERENCES tramites.consultation_templates(id)
        ON DELETE SET NULL ON UPDATE CASCADE;

-- A9: índice parcial FK
CREATE INDEX ix_form_fields_consultation_template_id
    ON tramites.form_fields(consultation_template_id)
    WHERE consultation_template_id IS NOT NULL;

-- A16: triggers row_version y audit en form_fields
DROP TRIGGER IF EXISTS tr_form_fields_row_version ON tramites.form_fields;
CREATE TRIGGER tr_form_fields_row_version
    BEFORE UPDATE ON tramites.form_fields
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_form_fields_audit ON tramites.form_fields;
CREATE TRIGGER tr_form_fields_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.form_fields
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. A16: row_version + triggers en procedure_steps y procedure_sections
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE tramites.procedure_steps
    ADD COLUMN IF NOT EXISTS row_version bigint NOT NULL DEFAULT 0;

DROP TRIGGER IF EXISTS tr_procedure_steps_row_version ON tramites.procedure_steps;
CREATE TRIGGER tr_procedure_steps_row_version
    BEFORE UPDATE ON tramites.procedure_steps
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_procedure_steps_audit ON tramites.procedure_steps;
CREATE TRIGGER tr_procedure_steps_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_steps
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

ALTER TABLE tramites.procedure_sections
    ADD COLUMN IF NOT EXISTS row_version bigint NOT NULL DEFAULT 0;

DROP TRIGGER IF EXISTS tr_procedure_sections_row_version ON tramites.procedure_sections;
CREATE TRIGGER tr_procedure_sections_row_version
    BEFORE UPDATE ON tramites.procedure_sections
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_procedure_sections_audit ON tramites.procedure_sections;
CREATE TRIGGER tr_procedure_sections_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_sections
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. A16: trigger audit en external_data_sources (catálogo global existente)
-- ─────────────────────────────────────────────────────────────────────────────
DROP TRIGGER IF EXISTS tr_external_data_sources_audit ON tramites.external_data_sources;
CREATE TRIGGER tr_external_data_sources_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.external_data_sources
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
