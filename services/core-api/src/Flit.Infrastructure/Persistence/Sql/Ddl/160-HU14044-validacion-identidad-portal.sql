-- =============================================================================
-- HU #14044 (Feature #14023, Épica #14021 Portal Persona Natural, ADR-0075) — validación de identidad por solicitud.
-- Migración: 20261028120000_HU14044_ValidacionIdentidadPortal.
--
-- Por qué: decisión del PO (2026-10-27): en el portal una validación vale SOLO para la solicitud en que se hizo; no se
-- reutiliza entre trámites ni tiene vigencia en días. La prevalidación standalone (persona a nivel de empresa) y la del
-- trámite reutilizan por documento, así que el portal necesita su propia ancla: la solicitud (external_ref) y el rol.
--
-- Qué hace (ADITIVO: dos columnas nullable + CHECKs + índices; sin tablas nuevas; la RLS tenant_isolation ya cubre las
-- filas porque nacen en la empresa «Persona Natural»):
--   1. tramites.procedure_instance_biometric_validations.portal_solicitud_ref / portal_parte (NULL en todo lo existente).
--   2. ck_biometric_validations_portal_ref: las dos van juntas o ninguna.
--   3. ck_biometric_validation_anchor acepta portal_solicitud_ref como ancla (la validación del portal no tiene trámite
--      ni persona del módulo Identidad).
--   4. ix_biometric_validations_portal_solicitud: lectura por solicitud y rol.
--   5. uq_biometric_validations_inflight_doc_norm se recrea EXCLUYENDO las filas del portal (una validación en vuelo de un
--      trámite del portal no debe bloquear a la misma cédula en otra solicitud) y nace
--      uq_biometric_validations_inflight_portal: a lo sumo UNA validación en vuelo por (solicitud, rol, documento).
-- Sin backfill. Idempotente (IF NOT EXISTS / guardas en pg_constraint) y reversible (Down en la migración).
-- =============================================================================

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD COLUMN IF NOT EXISTS portal_solicitud_ref varchar(100),
    ADD COLUMN IF NOT EXISTS portal_parte         varchar(40);

DO $mig$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conname = 'ck_biometric_validations_portal_ref'
                      AND conrelid = 'tramites.procedure_instance_biometric_validations'::regclass) THEN
        ALTER TABLE tramites.procedure_instance_biometric_validations
            ADD CONSTRAINT ck_biometric_validations_portal_ref
            CHECK ((portal_solicitud_ref IS NULL) = (portal_parte IS NULL));
    END IF;

    -- Ancla: persona, trámite, ficha del mandatario o solicitud del portal. NOT VALID + VALIDATE: el ADD no recorre la
    -- tabla (solo toma el bloqueo un instante) y la validación posterior usa un bloqueo que no frena lecturas ni
    -- escrituras; toda fila existente cumple la versión anterior y por tanto esta (que solo agrega una alternativa).
    ALTER TABLE tramites.procedure_instance_biometric_validations
        DROP CONSTRAINT IF EXISTS ck_biometric_validation_anchor;
    ALTER TABLE tramites.procedure_instance_biometric_validations
        ADD CONSTRAINT ck_biometric_validation_anchor
        CHECK (person_id IS NOT NULL OR procedure_instance_id IS NOT NULL OR mandate_signer_id IS NOT NULL
               OR portal_solicitud_ref IS NOT NULL) NOT VALID;
    ALTER TABLE tramites.procedure_instance_biometric_validations
        VALIDATE CONSTRAINT ck_biometric_validation_anchor;
END
$mig$;

CREATE INDEX IF NOT EXISTS ix_biometric_validations_portal_solicitud
    ON tramites.procedure_instance_biometric_validations (portal_solicitud_ref, portal_parte, created_at)
    WHERE portal_solicitud_ref IS NOT NULL;

-- Unicidad en vuelo por (tenant, documento) SOLO para lo que no es del mandatario ni del portal.
DROP INDEX IF EXISTS tramites.uq_biometric_validations_inflight_doc_norm;
CREATE UNIQUE INDEX IF NOT EXISTS uq_biometric_validations_inflight_doc_norm
    ON tramites.procedure_instance_biometric_validations (
        tenant_id,
        upper(btrim(document_type)),
        upper(btrim(document_number))
    )
    WHERE status IN ('pendiente_envio', 'enviado', 'en_proceso')
      AND deleted_at IS NULL
      AND mandate_signer_id IS NULL
      AND portal_solicitud_ref IS NULL;

-- Con tenant_id primero (misma regla que el resto de índices por empresa). DROP previo: si el índice ya existía con la
-- forma anterior (sin tenant_id), IF NOT EXISTS lo dejaría como estaba.
DROP INDEX IF EXISTS tramites.uq_biometric_validations_inflight_portal;
CREATE UNIQUE INDEX IF NOT EXISTS uq_biometric_validations_inflight_portal
    ON tramites.procedure_instance_biometric_validations (
        tenant_id,
        portal_solicitud_ref,
        portal_parte,
        upper(btrim(document_number))
    )
    WHERE status IN ('pendiente_envio', 'enviado', 'en_proceso')
      AND deleted_at IS NULL
      AND portal_solicitud_ref IS NOT NULL;

COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.portal_solicitud_ref IS
    'HU14044 · external_ref de la solicitud del portal de Persona Natural dueña de la validación. Solo vale para esa solicitud: no se reutiliza ni tiene vigencia.';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.portal_parte IS
    'HU14044 · Rol del actor en la solicitud del portal (vendedor, comprador, titular…); va junto con portal_solicitud_ref.';
COMMENT ON INDEX tramites.uq_biometric_validations_inflight_portal IS
    'HU14044 · A lo sumo una validación en vuelo por empresa, solicitud, rol y documento del portal (idempotencia de la fachada).';
