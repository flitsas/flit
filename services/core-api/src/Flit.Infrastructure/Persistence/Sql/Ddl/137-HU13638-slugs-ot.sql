-- =============================================================================
-- HU #13638 (Feature #13100 FA-2, Épica #12753) — catálogo de permisos del módulo OT.
-- Fuente: docs/design/EPICA-12753-anexo-a-permisos-ot.md (Anexo A, Parte 1, aprobado el 2026-10-16).
-- Migración: 20261016150000_HU13638_SlugsOt.
--
-- Qué hace:
--   1. Crea los 7 módulos RBAC del organismo (ot-tramites, ot-improntas, ot-reportes, ot-mandatos, ot-documentos,
--      ot-usuarios, ot-integracion), todos de producto 'tramites' (ot_admin es de tramites: el disparador
--      tr_role_permissions_same_product rechazaría un módulo 'plataforma').
--   2. Crea los 8 permisos (slugs) ot.<modulo>.manage; ot-mandatos aloja dos (mandatos y mandatarios).
--   3. Concede los 8 al rol global ot_admin (target_entity_type TRANSIT_OFFICE, tenant_id NULL) y a SuperAdmin
--      (que ya tiene bypass por rol; se mantiene la coherencia con el resto del catálogo).
--
-- NO exige los slugs en ningún endpoint (FA-4, #13639) ni los concede a otros roles OT (FA-3, #13639).
-- El seeder de desarrollo no siembra estos slugs: llegan solo por esta migración. Si ot_admin todavía no existe
-- al migrar (base nueva), el catálogo queda creado y la concesión se completa al re-ejecutar este DDL.
-- Idempotente: un segundo run no duplica módulos, permisos ni grants.
-- =============================================================================

-- 1. Módulos (sort_order consecutivo a partir del mayor existente; solo se asigna al crear).
INSERT INTO security.modules (code, name, description, sort_order, is_active, product_code)
SELECT v.code, v.name, v.description,
       (SELECT COALESCE(MAX(sort_order), 0) FROM security.modules)::int + v.ord,
       true, 'tramites'
  FROM (VALUES
        (1, 'ot-tramites',    'OT: Trámites',         'Bandeja del organismo de tránsito'),
        (2, 'ot-improntas',   'OT: Validar impronta', 'Validación de improntas del organismo'),
        (3, 'ot-reportes',    'OT: Reportes',         'Reportes, consultas y alertas del organismo'),
        (4, 'ot-mandatos',    'OT: Mandatos',         'Mandatos y mandatarios del organismo'),
        (5, 'ot-documentos',  'OT: Documentos',       'Prelación documental del organismo'),
        (6, 'ot-usuarios',    'OT: Usuarios',         'Usuarios e invitaciones del organismo'),
        (7, 'ot-integracion', 'OT: Integración',      'Webhooks y bitácora API del organismo')
       ) AS v(ord, code, name, description)
 WHERE NOT EXISTS (SELECT 1 FROM security.modules m WHERE m.code = v.code);

-- 2. Permisos
INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
SELECT m.id, v.slug, v.name, v.http_method, v.route_pattern, true
  FROM (VALUES
        ('ot-tramites',    'ot.tramites.manage',    'Gestionar trámites del organismo',    'GET',  '/api/v1/admin/ot/client-procedures'),
        ('ot-improntas',   'ot.improntas.manage',   'Gestionar validación de improntas',   'GET',  '/api/v1/admin/ot/imprint-signatures'),
        ('ot-reportes',    'ot.reportes.manage',    'Gestionar reportes del organismo',    'GET',  '/api/v1/admin/ot/metrics/operational'),
        ('ot-mandatos',    'ot.mandatos.manage',    'Gestionar mandatos del organismo',    'GET',  '/api/v1/admin/ot/offices/{officeId}/mandatos'),
        ('ot-mandatos',    'ot.mandatarios.manage', 'Gestionar mandatarios del organismo', 'POST', '/api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers'),
        ('ot-documentos',  'ot.documentos.manage',  'Gestionar prelación documental',      'GET',  '/api/v1/admin/ot/document-precedence'),
        ('ot-usuarios',    'ot.usuarios.manage',    'Gestionar usuarios del organismo',    'GET',  '/api/v1/admin/ot/users'),
        ('ot-integracion', 'ot.integracion.manage', 'Gestionar webhooks y bitácora API',   'GET',  '/api/v1/admin/ot/webhooks')
       ) AS v(module_code, slug, name, http_method, route_pattern)
  JOIN security.modules m ON m.code = v.module_code
 WHERE NOT EXISTS (SELECT 1 FROM security.permissions p WHERE p.slug = v.slug);

-- 3. Grants: ot_admin (catálogo global TRANSIT_OFFICE) y SuperAdmin
INSERT INTO security.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM security.roles r
  JOIN security.permissions p ON p.slug IN (
       'ot.tramites.manage', 'ot.improntas.manage', 'ot.reportes.manage', 'ot.mandatos.manage',
       'ot.mandatarios.manage', 'ot.documentos.manage', 'ot.usuarios.manage', 'ot.integracion.manage')
 WHERE r.deleted_at IS NULL
   AND r.tenant_id IS NULL
   AND ((r.code = 'ot_admin' AND r.target_entity_type = 'TRANSIT_OFFICE')
     OR r.code = 'SuperAdmin')
ON CONFLICT (role_id, permission_id) DO NOTHING;
