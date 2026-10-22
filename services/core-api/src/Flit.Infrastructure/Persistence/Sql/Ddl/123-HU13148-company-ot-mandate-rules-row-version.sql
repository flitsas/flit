-- HU #13148 (Feature #13117 F5, Épica #13090 Mandatarios) — control de concurrencia optimista por RowVersion en
-- admin.company_ot_mandate_rules (tipo de mandato de una compañía en un organismo). Migración:
-- 20260930200000_HU13148_CompanyOtMandateRulesRowVersion.
--
-- Qué hace: agrega row_version bigint NOT NULL DEFAULT 0 y el trigger de la plataforma (public.trg_row_version, que
-- suma 1 en cada UPDATE), igual que admin.transit_office_mandate_config (DDL 41). Las filas anteriores a la migración
-- quedan con row_version = 0 (valor inicial válido; el DEFAULT rellena la columna nueva). Sin RLS (la tabla no la lleva).
-- Idempotente (ADD COLUMN IF NOT EXISTS + DROP/CREATE TRIGGER). Reversible: ver Down en la migración.

ALTER TABLE admin.company_ot_mandate_rules
    ADD COLUMN IF NOT EXISTS row_version bigint NOT NULL DEFAULT 0;

DROP TRIGGER IF EXISTS tr_company_ot_mandate_rules_row_version ON admin.company_ot_mandate_rules;
CREATE TRIGGER tr_company_ot_mandate_rules_row_version BEFORE UPDATE ON admin.company_ot_mandate_rules
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

COMMENT ON COLUMN admin.company_ot_mandate_rules.row_version IS 'HU13148 · Token de concurrencia optimista; lo incrementa tr_company_ot_mandate_rules_row_version en cada UPDATE.';
