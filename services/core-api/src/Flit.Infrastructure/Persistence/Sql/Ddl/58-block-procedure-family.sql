-- Bloqueo de creación de trámites por familia (activo = no permitir crear).
-- Matrículas reutiliza allow_initial_registration (bloqueado ⇔ NOT allow).
-- TRASPASO y OTROS: columnas nuevas (default false = no bloqueados, comportamiento actual).

ALTER TABLE admin.tenant_operational_policies
  ADD COLUMN IF NOT EXISTS block_procedure_family_traspaso boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS block_procedure_family_otros boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN admin.tenant_operational_policies.block_procedure_family_traspaso IS
  'Si true, la compañía no puede crear trámites de familia TRASPASO.';
COMMENT ON COLUMN admin.tenant_operational_policies.block_procedure_family_otros IS
  'Si true, la compañía no puede crear trámites de familia OTROS.';
