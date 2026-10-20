-- HU #12967 (Feature #12888, FLIT Suite, tarea B-07) — los booleans de módulos pasan a la habilitación de productos.
-- Migración: 20260925200000_HU12967_BooleansAHabilitacion · contrato de plataforma v1, §4.
--
-- admin.tenant_operational_policies.tramites_module_enabled y comparendos_module_enabled dejan de leerse: la
-- configuración de empresa y el dashboard leen platform.tenant_products (ITenantProductFlags). Aquí se copian sus
-- valores a filas de platform.tenant_products, respetando lo que un SuperAdmin ya haya cambiado a mano
-- (updated_by no nulo). resoluciones_module_enabled no se toca: Resoluciones está fuera de la suite v1.
-- Las columnas se retiran un sprint después (reglas R6), en su propia migración.
--
-- Una empresa sin fila de política usaba los valores por defecto (Trámites sí, Comparendos no), que ya coinciden
-- con lo que dejó la migración de B-03. Idempotente.

-- Comparendos encendido en la política ⇒ fila encendida.
INSERT INTO platform.tenant_products (tenant_id, product_code, enabled, notes)
SELECT p.tenant_id, 'comparendos', true, 'Migrado desde comparendos_module_enabled (HU #12967)'
  FROM admin.tenant_operational_policies p
 WHERE p.comparendos_module_enabled
ON CONFLICT (tenant_id, product_code) DO NOTHING;

-- Trámites apagado en la política ⇒ fila apagada, salvo que un SuperAdmin ya la haya tocado.
UPDATE platform.tenant_products tp
   SET enabled = false, notes = 'Migrado desde tramites_module_enabled (HU #12967)', updated_at = now()
  FROM admin.tenant_operational_policies p
 WHERE tp.tenant_id = p.tenant_id AND tp.product_code = 'tramites'
   AND NOT p.tramites_module_enabled AND tp.enabled AND tp.updated_by IS NULL;

DO $$
DECLARE
    v_off integer;
    v_comparendos integer;
BEGIN
    SELECT count(*) INTO v_off FROM admin.tenant_operational_policies WHERE NOT tramites_module_enabled;
    SELECT count(*) INTO v_comparendos FROM admin.tenant_operational_policies WHERE comparendos_module_enabled;
    RAISE NOTICE 'HU #12967: % empresas con Trámites apagado y % con Comparendos encendido en la política', v_off, v_comparendos;
END $$;

COMMENT ON COLUMN admin.tenant_operational_policies.tramites_module_enabled IS
    'OBSOLETA desde HU #12967 (B-07): no se lee. La habilitación vive en platform.tenant_products. Se retira un sprint después.';
COMMENT ON COLUMN admin.tenant_operational_policies.comparendos_module_enabled IS
    'OBSOLETA desde HU #12967 (B-07): no se lee. La habilitación vive en platform.tenant_products. Se retira un sprint después.';
