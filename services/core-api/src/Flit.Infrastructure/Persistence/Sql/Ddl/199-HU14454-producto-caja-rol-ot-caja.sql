-- =============================================================================
-- HU #14454 (Feature #14453, Épica #14210, ADR-0082) — producto OIDC «caja» y rol ot_caja.
-- Migración: 20261117140000_HU14454_RolOtCaja.
--
-- Qué hace:
--   1. Producto 'caja' (client_id y aud del cliente OIDC que registra OidcClientSync desde ProductCodes.All).
--   2. Módulo RBAC 'caja' y permiso 'caja.cobrar' (producto 'caja': el disparador tr_role_permissions_same_product
--      exige el mismo producto en el rol y en el módulo del permiso).
--   3. Rol global ot_caja (target_entity_type TRANSIT_OFFICE, producto 'caja', de sistema) con ese único permiso.
--
-- El rol NO concede ningún permiso de FLIT 2.0: quien solo tenga ot_caja queda fuera de los módulos del OT
-- (OtModuleAuthorizationHandler, SoloCajaGuardMiddleware e Identidad). Idempotente: un segundo run no duplica nada.
-- =============================================================================

INSERT INTO platform.products (code, name, icon, status, sort_order)
VALUES ('caja', 'Caja', 'banknote', 'active', 50)
ON CONFLICT (code) DO NOTHING;

INSERT INTO security.modules (code, name, description, sort_order, is_active, product_code)
SELECT 'caja', 'Caja del organismo', 'Cobro en la caja del portal del punto de atención',
       (SELECT COALESCE(MAX(sort_order), 0) FROM security.modules)::int + 1, true, 'caja'
 WHERE NOT EXISTS (SELECT 1 FROM security.modules WHERE code = 'caja');

INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
SELECT m.id, 'caja.cobrar', 'Cobrar en la caja del organismo', 'POST', '/api/v1/persona-natural/caja/tramites/{codigo}/confirmar', true
  FROM security.modules m
 WHERE m.code = 'caja'
   AND NOT EXISTS (SELECT 1 FROM security.permissions p WHERE p.slug = 'caja.cobrar');

INSERT INTO security.roles (code, name, description, is_system, is_active, target_entity_type, product_code)
SELECT 'ot_caja', 'Caja del organismo', 'Cobra en la caja del portal del punto. No entra a FLIT 2.0.', true, true, 'TRANSIT_OFFICE', 'caja'
 WHERE NOT EXISTS (
       SELECT 1 FROM security.roles
        WHERE code = 'ot_caja' AND target_entity_type = 'TRANSIT_OFFICE' AND deleted_at IS NULL);

INSERT INTO security.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM security.roles r
  JOIN security.permissions p ON p.slug = 'caja.cobrar'
 WHERE r.code = 'ot_caja' AND r.target_entity_type = 'TRANSIT_OFFICE' AND r.deleted_at IS NULL
ON CONFLICT (role_id, permission_id) DO NOTHING;
