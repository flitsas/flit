-- HU #13283 (Feature #13280 A1, Épica #13202 Identidad manual) — schema del proveedor 'manual'.
-- Migración: 20261005205449_HU13283_IdentidadManual.
--
-- Qué hace (ADITIVO sobre tramites.procedure_instance_biometric_validations; sin tablas nuevas):
--   1. status pasa de varchar(20) a varchar(40): 'pendiente_revision_manual' mide 25 caracteres y no cabía.
--   2. ck_biometric_validations_status: lista CERRADA de estados (los 7 que ya usa el código + manual_activo y
--      pendiente_revision_manual). Hasta hoy la columna no tenía CHECK; el rechazo de un estado inválido vive aquí.
--   3. ck_biometric_validations_provider: lista cerrada de proveedores (mock, kyverum, migracion_v1 + manual).
--   4. Columnas de la identidad manual, todas NULL: approval_origin ('automatica'|'manual'), manual_activated_by/at,
--      consent_at/ip/text_version, reviewed_by/at, rejection_reason_code. Backfill: approval_origin='automatica' en las
--      filas ya aprobadas; el resto queda NULL.
--   5. La firma trazada REUTILIZA signature_image_path / signature_image_sha256 (ADR-0054): sin columna nueva.
--   6. ix_biometric_validations_manual_tab (tenant_id, status, manual_activated_at) parcial WHERE provider = 'manual':
--      sirve la pestaña de manuales.
-- La RLS tenant_isolation y el trigger de auditoría de la tabla ya cubren las columnas nuevas.
-- Los estados manuales NO entran en las listas de "en vuelo" de Kyverum (índices únicos, alertas, reintentos).
-- Idempotente (IF NOT EXISTS / DROP CONSTRAINT IF EXISTS) y reversible (Down en la migración).
-- Riesgo Habeas Data: las imágenes del flujo manual se conservan sin purga por decisión del PO (pendiente validación legal).

ALTER TABLE tramites.procedure_instance_biometric_validations
    ALTER COLUMN status TYPE varchar(40);

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD COLUMN IF NOT EXISTS approval_origin text,
    ADD COLUMN IF NOT EXISTS manual_activated_by uuid,
    ADD COLUMN IF NOT EXISTS manual_activated_at timestamptz,
    ADD COLUMN IF NOT EXISTS consent_at timestamptz,
    ADD COLUMN IF NOT EXISTS consent_ip text,
    ADD COLUMN IF NOT EXISTS consent_text_version text,
    ADD COLUMN IF NOT EXISTS reviewed_by uuid,
    ADD COLUMN IF NOT EXISTS reviewed_at timestamptz,
    ADD COLUMN IF NOT EXISTS rejection_reason_code text;

-- Backfill: lo ya aprobado lo aprobó el flujo automático (mock/Kyverum/migración V1). Solo toca filas sin origen.
UPDATE tramites.procedure_instance_biometric_validations
   SET approval_origin = 'automatica'
 WHERE status = 'aprobado' AND approval_origin IS NULL;

ALTER TABLE tramites.procedure_instance_biometric_validations
    DROP CONSTRAINT IF EXISTS ck_biometric_validations_status;
ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD CONSTRAINT ck_biometric_validations_status
    CHECK (status IN ('enviado', 'en_proceso', 'aprobado', 'rechazado', 'expirado',
                      'pendiente_envio', 'error_envio', 'manual_activo', 'pendiente_revision_manual'));

ALTER TABLE tramites.procedure_instance_biometric_validations
    DROP CONSTRAINT IF EXISTS ck_biometric_validations_provider;
ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD CONSTRAINT ck_biometric_validations_provider
    CHECK (provider IN ('mock', 'kyverum', 'migracion_v1', 'manual'));

ALTER TABLE tramites.procedure_instance_biometric_validations
    DROP CONSTRAINT IF EXISTS ck_biometric_validations_approval_origin;
ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD CONSTRAINT ck_biometric_validations_approval_origin
    CHECK (approval_origin IS NULL OR approval_origin IN ('automatica', 'manual'));

CREATE INDEX IF NOT EXISTS ix_biometric_validations_manual_tab
    ON tramites.procedure_instance_biometric_validations (tenant_id, status, manual_activated_at)
    WHERE provider = 'manual';

COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.status IS
    'enviado|en_proceso|aprobado|rechazado|expirado|pendiente_envio|error_envio|manual_activo|pendiente_revision_manual';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.approval_origin IS
    'automatica|manual. NULL mientras no esté aprobada. Backfill automatica en las aprobadas previas a la HU #13283.';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.manual_activated_by IS
    '@pii:low Usuario (identity.users.id) que activó el flujo manual. Sin FK, igual que created_by.';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.manual_activated_at IS
    'Momento de activación del flujo manual (cancela Kyverum).';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.consent_at IS
    'Momento en que la persona aceptó el consentimiento de tratamiento de datos en el flujo manual.';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.consent_ip IS
    '@pii:medium IP desde la que se aceptó el consentimiento (Habeas Data, Ley 1581 de 2012).';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.consent_text_version IS
    'Versión del texto de consentimiento aceptado.';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.reviewed_by IS
    '@pii:low Usuario (identity.users.id) que revisó la validación manual. Sin FK, igual que created_by.';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.reviewed_at IS
    'Momento de la revisión humana de la validación manual.';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.rejection_reason_code IS
    'Código de la lista cerrada de motivos de rechazo (constante en código). Nunca texto libre.';
