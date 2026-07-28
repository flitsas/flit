-- HU #10916 (ADR-0036 §D9) — mandatario que firma el mandato del trámite, resuelto al APROBAR.
-- Columna sobre tramites.procedure_instances (tabla ExcludeFromMigrations: su esquema lo lleva el DDL
-- crudo, no EF). FK a admin.mandate_signers con ON DELETE SET NULL: al borrar el mandatario el trámite
-- queda sin firmante persistido (se comporta como "sin resolver" en la próxima regeneración). Índice
-- parcial para la lectura por mandatario. DDL IDEMPOTENTE (IF NOT EXISTS + guarda del constraint).

ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS mandate_signer_id uuid;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_procedure_instances_mandate_signer'
    ) THEN
        ALTER TABLE tramites.procedure_instances
            ADD CONSTRAINT fk_procedure_instances_mandate_signer
            FOREIGN KEY (mandate_signer_id) REFERENCES admin.mandate_signers(id)
            ON DELETE SET NULL ON UPDATE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_procedure_instances_mandate_signer_id
    ON tramites.procedure_instances (mandate_signer_id)
    WHERE mandate_signer_id IS NOT NULL;
