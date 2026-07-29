-- ─────────────────────────────────────────────────────────────────────────────
-- HU #10943 (Feature #10864, CF-03) — Editar prevalidación y reenviar validación.
--
-- Agrega a tramites.procedure_instance_biometric_validations:
--   - resend_count   int          NOT NULL DEFAULT 0     (D10: tope de 3 reenvíos)
--   - last_resent_at timestamptz  NULL                   (D10: cooldown de 5 minutos)
--
-- Idempotente: ADD COLUMN IF NOT EXISTS.
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD COLUMN IF NOT EXISTS resend_count integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS last_resent_at timestamptz;

COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.resend_count IS
    'HU #10943 — cantidad de reenvíos (manual o por cambio de correo). Tope 3 (BiometricRules.MaxReenvios).';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.last_resent_at IS
    'HU #10943 — momento del último reenvío. Cooldown de 5 minutos (BiometricRules.ReenvioCooldownMinutos).';
