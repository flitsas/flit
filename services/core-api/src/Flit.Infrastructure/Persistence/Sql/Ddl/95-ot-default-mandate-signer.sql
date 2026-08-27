-- HU-L8 — Mandatario persona por defecto a nivel OT (global).
-- Manda sobre el default de la compañía aunque el firmante no esté vinculado a esa gestora.
-- Nacimiento: NULL. DDL IDEMPOTENTE.

ALTER TABLE admin.transit_office_mandate_config
  ADD COLUMN IF NOT EXISTS default_mandate_signer_id uuid NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname = 'fk_tomc_default_mandate_signer'
  ) THEN
    ALTER TABLE admin.transit_office_mandate_config
      ADD CONSTRAINT fk_tomc_default_mandate_signer
      FOREIGN KEY (default_mandate_signer_id)
      REFERENCES admin.mandate_signers(id)
      ON DELETE SET NULL
      ON UPDATE CASCADE;
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_transit_office_mandate_config_default_signer
  ON admin.transit_office_mandate_config(default_mandate_signer_id)
  WHERE default_mandate_signer_id IS NOT NULL;

COMMENT ON COLUMN admin.transit_office_mandate_config.default_mandate_signer_id IS
  'Mandatario global del OT. Preselección en trámite; gana al default de compañía. NULL al nacer.';
