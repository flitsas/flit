-- FEATURE-08 / Fase 2b — Parte B1: CREATE tramites.procedure_type_sources
-- Migración: 20260721100100_F08_ProcedureTypeSources
-- CFD-04: fuentes de datos externas habilitadas por tipo de trámite.
-- Catálogo global sin tenant_id: excepción A4/A20 documentada en ADR-0019.
-- PK compuesta: excepción A3 (tabla de asociación pura, unicidad del par garantizada).
-- Sin row_version en PK compuesta; sin soft-delete (is_active como flag de activación).

CREATE TABLE IF NOT EXISTS tramites.procedure_type_sources (
    procedure_type_id uuid NOT NULL
        REFERENCES tramites.procedure_types(id) ON DELETE CASCADE ON UPDATE CASCADE,
    external_data_source_id uuid NOT NULL
        REFERENCES tramites.external_data_sources(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    is_active boolean NOT NULL DEFAULT true,
    execution_order smallint NOT NULL DEFAULT 0,
    config jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT pk_procedure_type_sources
        PRIMARY KEY (procedure_type_id, external_data_source_id)
);

CREATE INDEX IF NOT EXISTS ix_procedure_type_sources_source_id
    ON tramites.procedure_type_sources(external_data_source_id);

COMMENT ON TABLE tramites.procedure_type_sources IS
'Fuentes de datos externas habilitadas por tipo de trámite (CFD-04). Catálogo global sin tenant_id — excepción A4/A20 documentada en ADR-0019. Solo SuperAdmin puede escribir (RBAC: tramites:catalogs:write).';
COMMENT ON COLUMN tramites.procedure_type_sources.config IS
'Configuración especial de la fuente para el tipo. Esquema: { "simitMode": "INTERNAL" | "ONLINE", "optimizeDailyCache": bool }';

-- Sin trigger trg_audit_log: la tabla tiene PK compuesta (sin columna id) y
-- public.trg_audit_log() asigna NEW.id → falla en INSERT/UPDATE/DELETE.
-- Auditoría de fuentes queda cubierta por RBAC + logs de aplicación.
