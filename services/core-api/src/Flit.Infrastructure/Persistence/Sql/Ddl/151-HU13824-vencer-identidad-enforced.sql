-- =============================================================================
-- HU #13824 (Feature #13820, Épica #13817) — «Vencer» una identidad aprobada (solo Super Admin).
-- Migración: 20261022140000_HU13824_VencerIdentidad.
--
-- Qué hace:
--   1. Dos columnas en tramites.procedure_instance_biometric_validations:
--        expired_manually_at   instante en que un Super Admin venció la identidad a mano (NULL = nunca).
--        valid_until_previous  fin de vigencia que tenía antes de vencerla. Los trámites que ya salieron de borrador
--                              conservan su firma hasta ese fin (BiometricRules.EsAprobadaVigenteParaTramite).
--      La fila, el certificado y el estado 'aprobado' no se tocan: lo que termina es valid_until (= expired_manually_at).
--   2. Índice parcial por tenant sobre las filas vencidas a mano (el listado de trámites consulta solo esas).
--   3. Módulo RBAC 'validaciones-identidad-admin' (producto 'plataforma') y permiso 'identidad.validaciones.vencer'.
--      Se concede ÚNICAMENTE al rol SuperAdmin: ningún rol de compañía lo trae, así la regla «Solo SA» del módulo de
--      permisos por usuario (HU #13653) lo deja en «Solo SA». El endpoint exige además el rol SuperAdmin, de modo que un
--      rol personalizado al que se le asigne el slug a mano tampoco puede usarlo.
-- Idempotente: IF NOT EXISTS / NOT EXISTS / ON CONFLICT DO NOTHING.
-- =============================================================================

ALTER TABLE tramites.procedure_instance_biometric_validations
    ADD COLUMN IF NOT EXISTS expired_manually_at  timestamptz,
    ADD COLUMN IF NOT EXISTS valid_until_previous timestamptz;

CREATE INDEX IF NOT EXISTS ix_biometric_validations_expired_manually
    ON tramites.procedure_instance_biometric_validations (tenant_id)
    WHERE expired_manually_at IS NOT NULL;

INSERT INTO security.modules (code, name, description, sort_order, is_active, product_code)
SELECT 'validaciones-identidad-admin', 'Validaciones de identidad (administración)',
       'Acciones administrativas sobre validaciones de identidad, exclusivas del Super Admin',
       (SELECT COALESCE(MAX(sort_order), 0) FROM security.modules)::int + 1,
       true, 'plataforma'
 WHERE NOT EXISTS (SELECT 1 FROM security.modules m WHERE m.code = 'validaciones-identidad-admin');

INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
SELECT m.id, 'identidad.validaciones.vencer', 'Vencer una identidad aprobada', 'POST',
       '/api/v1/tramites/biometric-validations/{id}/vencer', true
  FROM security.modules m
 WHERE m.code = 'validaciones-identidad-admin'
   AND NOT EXISTS (SELECT 1 FROM security.permissions p WHERE p.slug = 'identidad.validaciones.vencer');

-- Marcador de enforcement (DDL 139): el endpoint lo exige, así que nace en 'true'.
UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES ('identidad.validaciones.vencer', 'true')) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;

-- Solo el rol SuperAdmin (mismo patrón del DDL 133 para banners.manage).
INSERT INTO security.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM security.roles r
  JOIN security.permissions p ON p.slug = 'identidad.validaciones.vencer'
 WHERE r.code = 'SuperAdmin'
   AND r.deleted_at IS NULL
ON CONFLICT (role_id, permission_id) DO NOTHING;
