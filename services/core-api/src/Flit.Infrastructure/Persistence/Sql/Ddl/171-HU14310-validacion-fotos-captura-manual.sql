-- =============================================================================
-- HU #14310 (Feature #14307, Épica #14271) — resultado de la validación de cada foto de la captura manual.
-- Migración: 20261105120000_HU14310_ValidacionFotosCapturaManual.
--
-- La persona valida selfie, anverso y reverso en su dispositivo. El resultado es INFORMATIVO (filtra calidad, no es un
-- control de seguridad) y nunca bloquea la captura, la aprobación ni el rechazo. No se guardan imágenes adicionales.
-- Se siguen las columnas simples con CHECK de lista cerrada (como rejection_reason_code / approval_origin del DDL 130), no un
-- jsonb: son 3 fotos x (estado, motivo), valores de lista cerrada que el CHECK garantiza y que se pueden consultar.
-- ADITIVO: 6 columnas NULL sobre tramites.procedure_instance_biometric_validations. NULL = captura anterior a este cambio
-- (el detalle devuelve validacionFotos = null y no muestra aviso). La RLS y el trigger de auditoría ya cubren las columnas.
-- Idempotente (IF NOT EXISTS / DROP CONSTRAINT IF EXISTS) y reversible (Down en la migración).
-- =============================================================================

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD COLUMN IF NOT EXISTS face_check_status varchar(20) NULL,
    ADD COLUMN IF NOT EXISTS face_check_reason varchar(30) NULL,
    ADD COLUMN IF NOT EXISTS id_front_check_status varchar(20) NULL,
    ADD COLUMN IF NOT EXISTS id_front_check_reason varchar(30) NULL,
    ADD COLUMN IF NOT EXISTS id_back_check_status varchar(20) NULL,
    ADD COLUMN IF NOT EXISTS id_back_check_reason varchar(30) NULL;

ALTER TABLE tramites.procedure_instance_biometric_validations
    DROP CONSTRAINT IF EXISTS ck_biometric_validations_photo_checks;

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD CONSTRAINT ck_biometric_validations_photo_checks CHECK (
        (face_check_status IS NULL OR face_check_status IN ('verificada', 'no_verificada'))
        AND (id_front_check_status IS NULL OR id_front_check_status IN ('verificada', 'no_verificada'))
        AND (id_back_check_status IS NULL OR id_back_check_status IN ('verificada', 'no_verificada'))
        AND (face_check_reason IS NULL OR face_check_reason IN
             ('sin_rostro', 'varios_rostros', 'sin_documento', 'lado_incorrecto', 'sin_soporte', 'sin_resultado'))
        AND (id_front_check_reason IS NULL OR id_front_check_reason IN
             ('sin_rostro', 'varios_rostros', 'sin_documento', 'lado_incorrecto', 'sin_soporte', 'sin_resultado'))
        AND (id_back_check_reason IS NULL OR id_back_check_reason IN
             ('sin_rostro', 'varios_rostros', 'sin_documento', 'lado_incorrecto', 'sin_soporte', 'sin_resultado'))
        -- el motivo solo existe en una foto no verificada (COALESCE: un estado NULL no deja pasar un motivo)
        AND (face_check_reason IS NULL OR COALESCE(face_check_status, '') = 'no_verificada')
        AND (id_front_check_reason IS NULL OR COALESCE(id_front_check_status, '') = 'no_verificada')
        AND (id_back_check_reason IS NULL OR COALESCE(id_back_check_status, '') = 'no_verificada')
    );

COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.face_check_status IS
    'HU #14310: resultado informativo de la validación de la selfie en el dispositivo (verificada|no_verificada). NULL = captura anterior.';
