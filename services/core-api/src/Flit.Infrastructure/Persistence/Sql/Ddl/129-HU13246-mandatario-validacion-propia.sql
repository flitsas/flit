-- HU #13246 (Feature #13245 F9, Épica #13090 Mandatarios) — validación de identidad PROPIA del mandatario (ADR-0050 y
-- ADR-0061, enmiendas). Migración: 20261001211736_HU13246_MandatarioValidacionPropia.
--
-- Por qué: la firma biométrica del mandatario solo cuenta una validación lanzada PARA ese mandatario. Hasta ahora se
-- resolvía por documento y la aprobación de un comprador, un vendedor o una prevalidación con la misma cédula la habilitaba.
--
-- Qué hace (ADITIVO: columna nullable + CHECKs + índices; sin tablas nuevas; la RLS tenant_isolation de la tabla ya cubre
-- las filas nuevas porque la validación se registra en el tenant de la COMPAÑÍA del mandatario):
--   1. tramites.procedure_instance_biometric_validations.mandate_signer_id uuid NULL — ficha dueña de la validación.
--      FK a admin.mandate_signers(id) ON DELETE RESTRICT (el mandatario se da de baja de forma lógica, nunca se borra).
--   2. party_role admite 'mandatario' (la columna es varchar libre, sin CHECK previo). ck_biometric_validations_mandatario_ref
--      liga los dos campos: party_role = 'mandatario' <=> mandate_signer_id NOT NULL.
--   3. ck_biometric_validation_anchor ahora acepta mandate_signer_id como ancla (la validación del mandatario no tiene
--      trámite ni persona del módulo Identidad).
--   4. ix_biometric_validations_mandate_signer (mandate_signer_id, created_at) parcial: lectura por ficha (A9).
--   5. uq_biometric_validations_inflight_doc_norm se recrea EXCLUYENDO las filas del mandatario (la validación en vuelo de
--      un mandatario no debe bloquear a un comprador con la misma cédula ni al revés) y nace
--      uq_biometric_validations_inflight_mandate_signer: a lo sumo UNA validación en vuelo por mandatario (dos reenvíos
--      simultáneos dejan una sola activa).
-- Sin backfill: los mandatarios existentes no tienen validación propia y pierden la firma biométrica hasta validarse
-- (decisión 6 del Líder Técnico, 01-oct-2026; reporte de afectados en HU #13247).
-- Idempotente (IF NOT EXISTS / guardas en pg_constraint) y reversible (Down en la migración).

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD COLUMN IF NOT EXISTS mandate_signer_id uuid;

DO $mig$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conname = 'fk_procedure_instance_biometric_validations_mandate_signers'
                      AND conrelid = 'tramites.procedure_instance_biometric_validations'::regclass) THEN
        ALTER TABLE tramites.procedure_instance_biometric_validations
            ADD CONSTRAINT fk_procedure_instance_biometric_validations_mandate_signers
            FOREIGN KEY (mandate_signer_id) REFERENCES admin.mandate_signers (id)
            ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conname = 'ck_biometric_validations_mandatario_ref'
                      AND conrelid = 'tramites.procedure_instance_biometric_validations'::regclass) THEN
        ALTER TABLE tramites.procedure_instance_biometric_validations
            ADD CONSTRAINT ck_biometric_validations_mandatario_ref
            CHECK ((party_role IS NOT DISTINCT FROM 'mandatario') = (mandate_signer_id IS NOT NULL));
    END IF;

    -- Ancla: persona, trámite o ficha del mandatario.
    ALTER TABLE tramites.procedure_instance_biometric_validations
        DROP CONSTRAINT IF EXISTS ck_biometric_validation_anchor;
    ALTER TABLE tramites.procedure_instance_biometric_validations
        ADD CONSTRAINT ck_biometric_validation_anchor
        CHECK (person_id IS NOT NULL OR procedure_instance_id IS NOT NULL OR mandate_signer_id IS NOT NULL);
END
$mig$;

CREATE INDEX IF NOT EXISTS ix_biometric_validations_mandate_signer
    ON tramites.procedure_instance_biometric_validations (mandate_signer_id, created_at)
    WHERE mandate_signer_id IS NOT NULL;

-- Unicidad en vuelo por (tenant, documento) SOLO para las validaciones que no son del mandatario.
DROP INDEX IF EXISTS tramites.uq_biometric_validations_inflight_doc_norm;
CREATE UNIQUE INDEX IF NOT EXISTS uq_biometric_validations_inflight_doc_norm
    ON tramites.procedure_instance_biometric_validations (
        tenant_id,
        upper(btrim(document_type)),
        upper(btrim(document_number))
    )
    WHERE status IN ('pendiente_envio', 'enviado', 'en_proceso')
      AND deleted_at IS NULL
      AND mandate_signer_id IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_biometric_validations_inflight_mandate_signer
    ON tramites.procedure_instance_biometric_validations (mandate_signer_id)
    WHERE status IN ('pendiente_envio', 'enviado', 'en_proceso')
      AND deleted_at IS NULL
      AND mandate_signer_id IS NOT NULL;

COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.mandate_signer_id IS
    'HU13246 · Ficha del mandatario (admin.mandate_signers) dueña de la validación; solo con party_role = mandatario. La validación del mandatario es exclusiva: no entra en las consultas por documento del trámite ni de la prevalidación.';
COMMENT ON INDEX tramites.uq_biometric_validations_inflight_mandate_signer IS
    'HU13246 · A lo sumo una validación en vuelo por mandatario (anti-carrera de reenvíos).';
