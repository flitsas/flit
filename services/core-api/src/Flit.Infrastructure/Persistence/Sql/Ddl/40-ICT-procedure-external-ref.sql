-- =============================================================================
-- ICT — Correlación del borrador materializado con el pre-trámite ICT, para hacer
-- IDEMPOTENTE la materialización (gRPC CreateDraftFromIct). Columnas aditivas nullables:
--   origin        varchar(20)  -- sistema originador ('ict' | null = plataforma)
--   external_ref  varchar(64)  -- id del pre-trámite (ict.external_integration_master.id)
--
-- El índice único PARCIAL garantiza A-LO-SUMO-UN borrador por pre-trámite: si un reintento
-- del Job 4 (tras caerse antes de grabar la correlación en el master) intentara crear un
-- SEGUNDO borrador, la BD lo rechaza (23505) en vez de dejar un huérfano. El guard en
-- CreateDraftFromIct busca por (tenant_id, external_ref) ANTES de crear y devuelve el borrador
-- existente, de modo que el reintento reusa el mismo procedure_instance_id.
-- La tabla es ExcludeFromMigrations (DDL crudo embebido). Idempotente.
-- =============================================================================

ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS origin       varchar(20),
    ADD COLUMN IF NOT EXISTS external_ref varchar(64);

COMMENT ON COLUMN tramites.procedure_instances.origin
    IS 'Sistema originador del trámite: ''ict'' para los materializados por la integración; null = plataforma.';
COMMENT ON COLUMN tramites.procedure_instances.external_ref
    IS 'Referencia externa del origen (ICT: ict.external_integration_master.id) para correlación idempotente.';

CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instances_tenant_external_ref
    ON tramites.procedure_instances (tenant_id, external_ref)
    WHERE external_ref IS NOT NULL AND deleted_at IS NULL;
