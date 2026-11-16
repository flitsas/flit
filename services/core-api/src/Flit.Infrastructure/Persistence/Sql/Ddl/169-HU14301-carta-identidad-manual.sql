-- =============================================================================
-- HU #14301 (Feature #14300, Épica #14271) — carta de identidad que FLIT emite al aprobar una validación MANUAL.
-- Migración: 20261105120000_HU14301_CartaIdentidadManual.
--
-- ADITIVO sobre tramites.procedure_instance_biometric_validations; sin tablas nuevas. Cinco columnas NULL:
--   letter_series        serie del «Código de integridad» (derivada de forma estable de la evidencia).
--   letter_sha256        hash SHA-256 de la evidencia sellada (el que se imprime en la carta).
--   letter_pdf_sha256    SHA-256 de los bytes del PDF guardado.
--   letter_path          ruta opaca del PDF en el storage (la carta pertenece a la VALIDACIÓN: también hay carta en las
--                        prevalidaciones sin trámite, procedure_instance_id NULL).
--   letter_generated_at  instante de generación (el «Sellado»).
-- NO toca certificate_hash (la serie firmaSerie de Kyverum que lee el sello del FUR): las validaciones de Kyverum quedan
-- exactamente como estaban y sus cinco columnas nuevas siempre NULL. La RLS tenant_isolation y el trigger de auditoría de
-- la tabla ya cubren las columnas nuevas. Sin backfill: solo las aprobaciones manuales posteriores generan carta.
-- Idempotente (IF NOT EXISTS) y reversible (Down en la migración).
-- =============================================================================

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD COLUMN IF NOT EXISTS letter_series varchar(60),
    ADD COLUMN IF NOT EXISTS letter_sha256 varchar(64),
    ADD COLUMN IF NOT EXISTS letter_pdf_sha256 varchar(64),
    ADD COLUMN IF NOT EXISTS letter_path text,
    ADD COLUMN IF NOT EXISTS letter_generated_at timestamptz;

COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.letter_series IS
    'Serie del código de integridad de la carta de identidad de FLIT (solo aprobaciones manuales). NO es certificate_hash (Kyverum).';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.letter_sha256 IS
    'SHA-256 (hex) de la evidencia sellada en la carta de identidad de FLIT.';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.letter_pdf_sha256 IS
    'SHA-256 (hex) de los bytes del PDF de la carta guardado en storage.';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.letter_path IS
    'Ruta opaca del PDF de la carta en storage. La carta pertenece a la validación (también sin trámite).';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.letter_generated_at IS
    'Instante en que FLIT generó la carta (una sola vez por validación).';
