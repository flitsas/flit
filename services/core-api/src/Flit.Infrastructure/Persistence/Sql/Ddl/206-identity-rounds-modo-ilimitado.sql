-- ───────────────────────────────────────────────────────────────────────────
-- Modo sin límite de rondas de identidad y umbral de alerta a soporte.
-- Migración EF: 20261126100000_IdentityRoundsModoIlimitado.
--
-- limite_activo = true (valor de fábrica): el límite de rondas aplica.
-- limite_activo = false: ninguna validación ni reenvío se bloquea por rondas, y soporte recibe alerta cada
-- umbral_alerta_soporte validaciones de la persona. IDEMPOTENTE.
-- ───────────────────────────────────────────────────────────────────────────

ALTER TABLE admin.identity_rounds_config
  ADD COLUMN IF NOT EXISTS limite_activo          boolean  NOT NULL DEFAULT true,
  ADD COLUMN IF NOT EXISTS umbral_alerta_soporte  integer  NOT NULL DEFAULT 3;

ALTER TABLE admin.identity_rounds_config DROP CONSTRAINT IF EXISTS ck_identity_rounds_config_umbral;
ALTER TABLE admin.identity_rounds_config
  ADD CONSTRAINT ck_identity_rounds_config_umbral CHECK (umbral_alerta_soporte BETWEEN 1 AND 20);
