-- Solo vehículos propios por familia de trámite (MATRICULAS / TRASPASO / OTROS).
-- Conserva only_own_vehicles como flag de TRASPASO (compat).
-- Copia el valor histórico a las tres familias para no cambiar comportamiento al desplegar.

ALTER TABLE admin.tenant_operational_policies
  ADD COLUMN IF NOT EXISTS only_own_vehicles_matriculas boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS only_own_vehicles_otros boolean NOT NULL DEFAULT false;

UPDATE admin.tenant_operational_policies
   SET only_own_vehicles_matriculas = only_own_vehicles,
       only_own_vehicles_otros = only_own_vehicles
 WHERE only_own_vehicles_matriculas IS DISTINCT FROM only_own_vehicles
    OR only_own_vehicles_otros IS DISTINCT FROM only_own_vehicles;

COMMENT ON COLUMN admin.tenant_operational_policies.only_own_vehicles IS
  'Solo vehículos propios — familia TRASPASO (legado; espejo de onlyOwnVehiclesByFamily.TRASPASO).';
COMMENT ON COLUMN admin.tenant_operational_policies.only_own_vehicles_matriculas IS
  'Solo vehículos propios — familia MATRICULAS.';
COMMENT ON COLUMN admin.tenant_operational_policies.only_own_vehicles_otros IS
  'Solo vehículos propios — familia OTROS.';
