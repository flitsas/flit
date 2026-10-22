-- =============================================================================
-- HU #13639 (Feature #13100 FA-3, Épica #12753) — backfill de los slugs OT a los roles OT que no son ot_admin.
-- Fuente: docs/design/EPICA-12753-anexo-a-permisos-ot.md (Anexo A, §1.3 y P6). Migración: 20261016170000_HU13639_SlugsOtRolesOt.
--
-- Qué hace: a todo rol activo con target_entity_type = 'TRANSIT_OFFICE' distinto de ot_admin (catálogo global Y roles
-- por tenant, p. ej. gestor_tramites_ot y personalizados; sin enumerar códigos) le concede los 4 slugs que hoy ve en
-- el dock: ot.tramites.manage, ot.improntas.manage, ot.reportes.manage, ot.mandatos.manage.
-- NO les concede ot.mandatarios.manage, ot.documentos.manage (la API ya los rechaza), ni ot.usuarios.manage /
-- ot.integracion.manage (decisión P1: solo ot_admin).
-- Depende del DDL 137 (permisos ya creados). Idempotente: ON CONFLICT DO NOTHING.
-- =============================================================================
INSERT INTO security.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM security.roles r
  JOIN security.permissions p ON p.slug IN ('ot.tramites.manage', 'ot.improntas.manage', 'ot.reportes.manage', 'ot.mandatos.manage')
  JOIN security.modules m ON m.id = p.module_id
 WHERE r.deleted_at IS NULL
   AND m.product_code = r.product_code  -- tr_role_permissions_same_product: un rol OT de otro producto no aborta la migración
   AND r.target_entity_type = 'TRANSIT_OFFICE'
   AND r.code <> 'ot_admin'
ON CONFLICT (role_id, permission_id) DO NOTHING;
