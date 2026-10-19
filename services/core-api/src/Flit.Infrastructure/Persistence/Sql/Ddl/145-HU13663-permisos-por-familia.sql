-- =============================================================================
-- HU #13663 (Feature #13636 F-F, Épica #12753) — permisos Ver y Crear por familia de trámite, sin regresión.
-- Migración: 20261019150000_HU13663_PermisosPorFamilia.
--
-- Familias = eje ProcedureFamily (ADR-0050): MATRICULAS (toda la familia: inicial, leasing, cancelación, rematrícula),
-- TRASPASO y OTROS (todo lo demás: prendas, duplicados, blindaje, cambios de color/carrocería, novedades).
--
-- 1) Catálogo: un módulo RBAC por familia (tramites-matriculas, tramites-traspasos, tramites-otros), todos de producto
--    'tramites', con su par <familia>.read / <familia>.create. Un módulo por familia hace que la pantalla de roles muestre
--    un par Ver/Crear por familia (mismo patrón que los ot-* de la HU #13638).
-- 2) Backfill sin regresión: todo rol activo (catálogo global y por tenant) que hoy tiene tramites.read recibe los 3 .read; con
--    tramites.create recibe los 3 .create. Misma regla del trigger tr_role_permissions_same_product: son slugs de producto
--    'tramites' y solo se otorgan a roles que ya tenían un slug de ese producto.
-- 3) NO se marcan enforced: nacen con el default 'false' porque este DDL todavía no los exige la API. La HU #13664 (DDL 146)
--    los pasa a 'true' al exigirlos, de modo que el marcador nunca promete una protección inexistente.
-- Idempotente: NOT EXISTS + ON CONFLICT DO NOTHING.
-- =============================================================================

-- 1a. Módulos (sort_order consecutivo; solo se asigna al crear).
INSERT INTO security.modules (code, name, description, sort_order, is_active, product_code)
SELECT v.code, v.name, v.description,
       (SELECT COALESCE(MAX(sort_order), 0) FROM security.modules)::int + v.ord,
       true, 'tramites'
  FROM (VALUES
        (1, 'tramites-matriculas', 'Trámites: Matrículas', 'Ver y crear trámites de la familia Matrículas (HU #13663)'),
        (2, 'tramites-traspasos',  'Trámites: Traspasos',  'Ver y crear trámites de la familia Traspasos (HU #13663)'),
        (3, 'tramites-otros',      'Trámites: Otros trámites', 'Ver y crear trámites de las demás familias (HU #13663)')
       ) AS v(ord, code, name, description)
 WHERE NOT EXISTS (SELECT 1 FROM security.modules m WHERE m.code = v.code);

-- 1b. Permisos
INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
SELECT m.id, v.slug, v.name, v.http_method, v.route_pattern, true
  FROM (VALUES
        ('tramites-matriculas', 'matriculas.read',       'Ver trámites de Matrículas',      'GET',  '/api/v1/tramites?family=MATRICULAS'),
        ('tramites-matriculas', 'matriculas.create',     'Crear trámites de Matrículas',    'POST', '/api/v1/tramites?family=MATRICULAS'),
        ('tramites-traspasos',  'traspasos.read',        'Ver trámites de Traspasos',       'GET',  '/api/v1/tramites?family=TRASPASO'),
        ('tramites-traspasos',  'traspasos.create',      'Crear trámites de Traspasos',     'POST', '/api/v1/tramites?family=TRASPASO'),
        ('tramites-otros',      'otros-tramites.read',   'Ver otros trámites',              'GET',  '/api/v1/tramites?family=OTROS'),
        ('tramites-otros',      'otros-tramites.create', 'Crear otros trámites',            'POST', '/api/v1/tramites?family=OTROS')
       ) AS v(module_code, slug, name, http_method, route_pattern)
  JOIN security.modules m ON m.code = v.module_code
 WHERE NOT EXISTS (SELECT 1 FROM security.permissions p WHERE p.slug = v.slug);

-- 2. Backfill: tramites.read -> los 3 .read ; tramites.create -> los 3 .create
INSERT INTO security.role_permissions (role_id, permission_id)
SELECT DISTINCT rp.role_id, np.id
  FROM security.role_permissions rp
  JOIN security.permissions op ON op.id = rp.permission_id
  JOIN security.roles r ON r.id = rp.role_id
  JOIN security.permissions np
    ON (op.slug = 'tramites.read'   AND np.slug IN ('matriculas.read', 'traspasos.read', 'otros-tramites.read'))
    OR (op.slug = 'tramites.create' AND np.slug IN ('matriculas.create', 'traspasos.create', 'otros-tramites.create'))
  JOIN security.modules nm ON nm.id = np.module_id
 WHERE r.deleted_at IS NULL
   AND nm.product_code = r.product_code  -- tr_role_permissions_same_product: solo roles del producto del módulo
   AND r.is_active
ON CONFLICT (role_id, permission_id) DO NOTHING;
