-- Recorte de la rúbrica del certificado Kyverum (ADR-0054).
-- Path + hash en la validación biométrica; el PNG vive en storage (como el baúl).
-- Idempotente: ADD COLUMN IF NOT EXISTS.

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD COLUMN IF NOT EXISTS signature_image_path varchar(1000),
    ADD COLUMN IF NOT EXISTS signature_image_sha256 varchar(64);

ALTER TABLE tramites.procedure_instance_biometric_validations
    DROP CONSTRAINT IF EXISTS ck_biometric_validations_signature_image;

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD CONSTRAINT ck_biometric_validations_signature_image
    CHECK (
        (signature_image_path IS NULL AND signature_image_sha256 IS NULL)
        OR (signature_image_path IS NOT NULL AND signature_image_sha256 IS NOT NULL)
    );

COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.signature_image_path IS
    '@pii:high Recorte PNG de la firma manuscrita del certificado Kyverum (ADR-0054). Path opaco de storage; el binario no se guarda en BD.';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.signature_image_sha256 IS
    'SHA-256 hex del PNG de signature_image_path. Ambos nulos o ambos presentes.';
