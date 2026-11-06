-- =============================================================================
-- HU #14305 (Feature #14304, Épica #14271) — token de la captura manual CIFRADO.
-- Migración: 20261105120000_HU14305_TokenCapturaManualCifrado.
--
-- Qué hace (ADITIVO sobre tramites.procedure_instance_biometric_validations):
--   Agrega manual_capture_token_encrypted (varchar(2000), NULL): el token del enlace de captura manual cifrado con Data Protection
--   (propósito propio), para que el Super Admin copie el enlace sin regenerarlo. Cambia a propósito la regla de la #13202 de
--   guardar solo el hash: el hash (token_hash) sigue siendo la llave de búsqueda del público; el cifrado solo sirve a «Copiar enlace».
--   Se llena al activar, regenerar o rechazar; se borra (NULL) al enviar la captura y al aprobar.
-- Sin backfill: los enlaces emitidos antes de este cambio quedan en NULL (responden 'enlace_no_disponible' y se regeneran).
-- La RLS tenant_isolation y el trigger de auditoría de la tabla ya cubren la columna nueva.
-- Idempotente (IF NOT EXISTS) y reversible (Down en la migración).
-- =============================================================================

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD COLUMN IF NOT EXISTS manual_capture_token_encrypted varchar(2000);

COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.manual_capture_token_encrypted IS
    'Token de la captura manual cifrado (Data Protection). Solo mientras espera la captura del cliente; NULL al enviar la captura o aprobar. Nunca en DTOs, logs ni auditoría. HU #14305.';
