-- Mandatario persona por defecto (compañía gestora × OT) para tipo signer / Persona-RL.
-- Solo aplica cuando assignment_mode = 'signer'; en institucional/abierto queda NULL.
-- DDL IDEMPOTENTE.

ALTER TABLE admin.company_ot_mandate_rules
  ADD COLUMN IF NOT EXISTS default_mandate_signer_id uuid NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname = 'fk_comr_default_mandate_signer'
  ) THEN
    ALTER TABLE admin.company_ot_mandate_rules
      ADD CONSTRAINT fk_comr_default_mandate_signer
      FOREIGN KEY (default_mandate_signer_id)
      REFERENCES admin.mandate_signers(id)
      ON DELETE SET NULL
      ON UPDATE CASCADE;
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_company_ot_mandate_rules_default_signer
  ON admin.company_ot_mandate_rules(default_mandate_signer_id)
  WHERE default_mandate_signer_id IS NOT NULL;

COMMENT ON COLUMN admin.company_ot_mandate_rules.default_mandate_signer_id IS
  'Mandatario persona preferido (signer). Preselección en wizard FUR; NULL = sin default.';
