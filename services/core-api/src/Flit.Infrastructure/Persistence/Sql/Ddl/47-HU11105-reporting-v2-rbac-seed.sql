-- HU #11105 / Feature #11076 — Seed RBAC Reporting V2 (ADR-0038 / diseño §9)
-- Idempotente: ON CONFLICT DO NOTHING / DO UPDATE. Re-ejecutable.
-- AC1: exactamente 15 slugs reporting.*
-- AC2: módulo code=reportes-v2, name=Reportería Transaccional V2, is_active=true
-- AC3: detailed-report.* inactivos
-- AC4: segunda ejecución sin duplicados

-- 1) Módulo reportes-v2
INSERT INTO security.modules (id, code, name, sort_order, is_active, created_at)
VALUES (uuidv7(), 'reportes-v2', 'Reportería Transaccional V2', 3, true, now())
ON CONFLICT (code) DO UPDATE
SET name = EXCLUDED.name,
    is_active = true,
    updated_at = now();

-- 2) 15 permisos reporting.*
WITH mod AS (
    SELECT id FROM security.modules WHERE code = 'reportes-v2' LIMIT 1
),
seed(slug, name, http_method, route_pattern, scope) AS (
    VALUES
        ('reporting.read', 'Ver reportes V2', 'GET', '/api/v1/reporting/procedures*', 'tenant'),
        ('reporting.detail', 'Ver detalle de trámite en reportes', 'GET', '/api/v1/reporting/procedures/{id}', 'tenant'),
        ('reporting.export', 'Solicitar/listar exportaciones', 'POST', '/api/v1/reporting/exports*', 'tenant'),
        ('reporting.export.download', 'Descargar exportación', 'GET', '/api/v1/reporting/exports/{id}/download-url', 'tenant'),
        ('reporting.saved-queries.read', 'Ver consultas guardadas', 'GET', '/api/v1/reporting/saved-queries*', 'tenant'),
        ('reporting.saved-queries.write', 'Gestionar consultas guardadas', 'POST', '/api/v1/reporting/saved-queries*', 'tenant'),
        ('reporting.schedules.read', 'Ver informes programados V2', 'GET', '/api/v1/reporting/schedules*', 'tenant'),
        ('reporting.schedules.write', 'Gestionar informes programados V2', 'POST', '/api/v1/reporting/schedules*', 'tenant'),
        ('reporting.alerts.read', 'Ver alertas V2', 'GET', '/api/v1/reporting/alerts*', 'tenant'),
        ('reporting.alerts.write', 'Gestionar alertas V2', 'POST', '/api/v1/reporting/alerts*', 'tenant'),
        ('reporting.dashboard.preferences', 'Preferencias de dashboard', 'GET', '/api/v1/reporting/preferences*', 'tenant'),
        ('reporting.audit', 'Auditoría operacional de trámites', 'GET', '/api/v1/reporting/procedures/{id}/audit*', 'tenant'),
        ('reporting.consolidado', 'Reporte consolidado/volumetría', 'GET', '/api/v1/reporting/consolidado*', 'tenant'),
        ('reporting.productivity', 'Reporte de productividad V2', 'GET', '/api/v1/reporting/productivity*', 'tenant'),
        ('reporting.global', 'Vista global multi-tenant', 'GET', '/api/v1/reporting/*', 'global')
)
INSERT INTO security.permissions (id, module_id, slug, name, http_method, route_pattern, scope, is_active, created_at)
SELECT uuidv7(), mod.id, seed.slug, seed.name, seed.http_method, seed.route_pattern, seed.scope, true, now()
FROM seed CROSS JOIN mod
ON CONFLICT (slug) DO UPDATE
SET module_id = EXCLUDED.module_id,
    name = EXCLUDED.name,
    http_method = EXCLUDED.http_method,
    route_pattern = EXCLUDED.route_pattern,
    scope = EXCLUDED.scope,
    is_active = true,
    updated_at = now();

-- 3) Legado detailed-report.* inactivo (módulo reportes-detallados o reportes-v2 como contenedor)
INSERT INTO security.modules (id, code, name, sort_order, is_active, created_at)
VALUES (uuidv7(), 'reportes-detallados', 'Reportes Detallados (legado)', 8, false, now())
ON CONFLICT (code) DO UPDATE
SET is_active = false,
    updated_at = now();

WITH legacy_mod AS (
    SELECT id FROM security.modules WHERE code = 'reportes-detallados' LIMIT 1
),
legacy(slug, name, http_method, route_pattern) AS (
    VALUES
        ('detailed-report.read', 'Ver reportes detallados (legado)', 'GET', '/api/v1/detailed-report/procedures'),
        ('detailed-report.export', 'Exportar reportes detallados (legado)', 'GET', '/api/v1/detailed-report/procedures/export')
)
INSERT INTO security.permissions (id, module_id, slug, name, http_method, route_pattern, scope, is_active, created_at)
SELECT uuidv7(), legacy_mod.id, legacy.slug, legacy.name, legacy.http_method, legacy.route_pattern, 'tenant', false, now()
FROM legacy CROSS JOIN legacy_mod
ON CONFLICT (slug) DO UPDATE
SET is_active = false,
    module_id = EXCLUDED.module_id,
    updated_at = now();

-- 4) Desactivar también slugs reportes.detallados.* si existen de seeds previos
UPDATE security.permissions
SET is_active = false,
    updated_at = now()
WHERE slug LIKE 'detailed-report.%'
   OR slug LIKE 'reportes.detallados.%';

UPDATE security.modules
SET is_active = false,
    updated_at = now()
WHERE code = 'reportes-detallados';

-- 5) Grants SuperAdmin: todos reporting.*; AdminCompany: todos excepto reporting.global
INSERT INTO security.role_permissions (id, role_id, permission_id, created_at)
SELECT uuidv7(), r.id, p.id, now()
FROM security.roles r
CROSS JOIN security.permissions p
WHERE r.code = 'SuperAdmin'
  AND r.deleted_at IS NULL
  AND p.slug LIKE 'reporting.%'
  AND p.is_active = true
  AND NOT EXISTS (
      SELECT 1 FROM security.role_permissions rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

INSERT INTO security.role_permissions (id, role_id, permission_id, created_at)
SELECT uuidv7(), r.id, p.id, now()
FROM security.roles r
CROSS JOIN security.permissions p
WHERE r.code = 'AdminCompany'
  AND r.deleted_at IS NULL
  AND p.slug LIKE 'reporting.%'
  AND p.slug <> 'reporting.global'
  AND p.is_active = true
  AND NOT EXISTS (
      SELECT 1 FROM security.role_permissions rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );
