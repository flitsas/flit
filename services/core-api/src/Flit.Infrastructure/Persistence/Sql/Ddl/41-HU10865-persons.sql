-- ─────────────────────────────────────────────────────────────────────────────
-- HU #10865 (Feature #10864, CF-00) — Entidad persona/sujeto a nivel tenant
-- para prevalidaciones de identidad. ADR-0030 (Propuesto).
--
-- 1) Crea tramites.persons (nueva tabla tenant-scoped con RLS, triggers, PII)
-- 2) Altera tramites.procedure_instance_biometric_validations:
--    - ADD COLUMN person_id uuid (nullable backcompat)
--    - ALTER COLUMN procedure_instance_id → nullable (standalone)
--    - DROP FK CASCADE → ADD FK SET NULL (protege validaciones standalone)
--    - ADD FK person_id → persons (RESTRICT)
--    - ADD CHECK anchor (al menos uno de los dos FK debe ser NOT NULL)
--    - ADD índice persona_id
--    - ADD índice de cobertura para FindVigenteApprovedByDocumentAsync (CF-02)
--
-- Idempotente: ADD COLUMN IF NOT EXISTS, DROP CONSTRAINT IF EXISTS,
-- CREATE INDEX IF NOT EXISTS, CREATE TABLE ... (sin IF NOT EXISTS porque es tabla nueva).
-- ─────────────────────────────────────────────────────────────────────────────

-- ============================================================================
-- 1) tramites.persons — entidad persona/sujeto a nivel tenant
-- ============================================================================
CREATE TABLE tramites.persons (
    id              uuid            NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persons PRIMARY KEY (id),

    tenant_id       uuid            NOT NULL
        CONSTRAINT fk_persons_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    -- Clave de negocio: unicidad por (tenant, tipo, número) en filas no-eliminadas.
    document_type   varchar(20)     NOT NULL,
    document_number varchar(40)     NOT NULL,

    -- Datos de la persona / empresa. @pii:* (Ley 1581)
    full_name       varchar(200)    NOT NULL,
    email           varchar(320)    NOT NULL,

    -- 'natural' | 'juridical'
    person_type     varchar(20)     NOT NULL DEFAULT 'natural',
    CONSTRAINT ck_persons_person_type CHECK (person_type IN ('natural', 'juridical')),

    -- Representante legal embebido (solo para juridical; NULL para natural).
    -- Mismo patrón que ProcedureInstanceActor — ADR-0030 §Tradeoff.
    legal_rep_document_type   varchar(20),
    legal_rep_document_number varchar(40),
    legal_rep_name            varchar(200),
    legal_rep_email           varchar(320),

    -- Columnas estándar FLIT (checklist §A5)
    created_at      timestamptz     NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    deleted_at      timestamptz,
    deleted_by      uuid,
    row_version     integer         NOT NULL DEFAULT 1
);

-- ── Índices (checklist §A9, §A11, §A12) ─────────────────────────────────────

-- FK index: tenant_id (§A9 + §A11)
CREATE INDEX ix_persons_tenant_id
    ON tramites.persons (tenant_id);

-- Clave de negocio única por tenant (parcial sobre filas no eliminadas — §A11 tenant_id primero)
CREATE UNIQUE INDEX uq_persons_tenant_document
    ON tramites.persons (tenant_id, document_type, document_number)
    WHERE deleted_at IS NULL;

-- ── Comentarios PII (checklist §A15, Ley 1581) ───────────────────────────────
COMMENT ON COLUMN tramites.persons.document_number IS
    '@pii:high Número de documento de identidad de la persona.';
COMMENT ON COLUMN tramites.persons.full_name IS
    '@pii:medium Nombre completo de la persona (o razón social si es jurídica).';
COMMENT ON COLUMN tramites.persons.email IS
    '@pii:high Correo electrónico principal de la persona.';
COMMENT ON COLUMN tramites.persons.legal_rep_document_number IS
    '@pii:high Número de documento del representante legal (solo persona jurídica).';
COMMENT ON COLUMN tramites.persons.legal_rep_name IS
    '@pii:medium Nombre del representante legal (solo persona jurídica).';
COMMENT ON COLUMN tramites.persons.legal_rep_email IS
    '@pii:high Correo del representante legal (solo persona jurídica).';

-- ── RLS — aislamiento por tenant (checklist §A10) ────────────────────────────
ALTER TABLE tramites.persons ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.persons
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- ── Triggers (checklist §A16) ────────────────────────────────────────────────
DROP TRIGGER IF EXISTS tr_persons_row_version ON tramites.persons;
CREATE TRIGGER tr_persons_row_version
    BEFORE UPDATE ON tramites.persons
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_persons_audit ON tramites.persons;
CREATE TRIGGER tr_persons_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.persons
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ============================================================================
-- 2) ALTER tramites.procedure_instance_biometric_validations (HU #10865)
-- ============================================================================

-- 2a) Agregar person_id (nullable: NULL para registros históricos sin Person)
ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD COLUMN IF NOT EXISTS person_id uuid
        CONSTRAINT fk_procedure_instance_biometric_validations_persons
        REFERENCES tramites.persons(id) ON DELETE RESTRICT ON UPDATE CASCADE;

-- 2b) Hacer procedure_instance_id nullable (soporta prevalidaciones standalone)
ALTER TABLE tramites.procedure_instance_biometric_validations
    ALTER COLUMN procedure_instance_id DROP NOT NULL;

-- 2c) Cambiar ON DELETE CASCADE → SET NULL en FK hacia procedure_instances.
--     CASCADE borraba filas de validación al borrar el trámite; con nullable,
--     SET NULL protege las standalone (ADR-0030 §Consecuencias).
ALTER TABLE tramites.procedure_instance_biometric_validations
    DROP CONSTRAINT IF EXISTS fk_procedure_instance_biometric_validations_procedure_instances;

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD CONSTRAINT fk_procedure_instance_biometric_validations_procedure_instances
        FOREIGN KEY (procedure_instance_id)
        REFERENCES tramites.procedure_instances(id)
        ON DELETE SET NULL
        ON UPDATE CASCADE;

-- 2d) Check de ancla: al menos uno de los dos FK debe estar seteado.
--     Las filas históricas tienen procedure_instance_id NOT NULL → satisfacen el check.
ALTER TABLE tramites.procedure_instance_biometric_validations
    DROP CONSTRAINT IF EXISTS ck_biometric_validation_anchor;

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD CONSTRAINT ck_biometric_validation_anchor
        CHECK (person_id IS NOT NULL OR procedure_instance_id IS NOT NULL);

-- 2e) Índice para FK person_id (checklist §A9; parcial sobre no-nulos — §A12)
CREATE INDEX IF NOT EXISTS ix_procedure_instance_biometric_validations_person_id
    ON tramites.procedure_instance_biometric_validations (person_id)
    WHERE person_id IS NOT NULL;

-- 2f) Índice de cobertura para FindVigenteApprovedByDocumentAsync (CF-02).
--     Cubre también prevalidaciones standalone (procedure_instance_id IS NULL).
CREATE INDEX IF NOT EXISTS ix_biometric_validations_vigente_approved
    ON tramites.procedure_instance_biometric_validations
        (tenant_id, document_type, document_number, status, valid_until DESC)
    WHERE status = 'aprobado'
      AND deleted_at IS NULL;

-- ── Comentario en person_id ──────────────────────────────────────────────────
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.person_id IS
    'HU #10865 — FK a tramites.persons. NULL en registros históricos (backcompat); NOT NULL en filas nuevas.';
