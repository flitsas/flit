-- =============================================================================
-- HU #13438 (Feature #13436, Épica #12750) — la gestión de banners es exclusiva del Super Admin FLIT.
--
-- El seeder (HU #12239) concedió `banners.manage` a SuperAdmin y AdminCompany. La API ahora exige el ROL
-- SuperAdmin (AdminBannersEndpoints), pero las bases ya sembradas conservan el grant sobre AdminCompany y
-- sobre cualquier rol personalizado al que se le haya asignado. Este DDL lo retira: deja `banners.manage`
-- únicamente en los roles con code 'SuperAdmin'. Idempotente (un segundo run no encuentra filas que borrar).
-- =============================================================================

DELETE FROM security.role_permissions rp
USING security.permissions p, security.roles r
WHERE rp.permission_id = p.id
  AND rp.role_id = r.id
  AND p.slug = 'banners.manage'
  AND r.code <> 'SuperAdmin';
