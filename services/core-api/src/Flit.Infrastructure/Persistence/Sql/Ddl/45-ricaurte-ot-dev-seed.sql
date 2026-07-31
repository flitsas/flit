-- =============================================================================
-- 45-ricaurte-ot-dev-seed.sql (DEV-ONLY) — Habilita "STRIA TTEyMOV CUND/RICAURTE"
-- (code DANE 25612000) como ORGANISMO DE TRÁNSITO OPERABLE.
--
-- Motivo: la compuerta IOtOperabilityGate de core-api exige, al materializar un
-- borrador, que el OT destino sea operable = oficina activa + perfil OT + tenant OT
-- activo. Un traspaso ICT cuyo vehículo esté matriculado en Ricaurte resolvía el OT
-- desde el RUNT pero fallaba con OT_NOT_AUTHORIZED_FOR_TYPE porque a Ricaurte le
-- faltaban el tenant OT y su perfil (la oficina ya existe en el catálogo). Esto los
-- crea, replicando el patrón del OT de Bogotá (16-HU10133-ot-admin-dev-seed.sql).
--
-- Idempotente (ON CONFLICT / NOT EXISTS). La oficina se referencia por CODE, no por
-- id, para no depender de un uuid concreto entre entornos.
-- =============================================================================
SET LOCAL row_security = off;

-- 1) Tenant OT dedicado para Ricaurte (tenant_type RENTING + activo, como el OT de Bogotá).
INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, created_at)
VALUES (
    'bbbbbbbb-2561-4000-8000-000000000001',
    'OT-RICAURTE',
    'STRIA TTEyMOV CUND/RICAURTE OT (DEV)',
    '900000025-6',
    'RENTING',
    true,
    now()
)
ON CONFLICT (id) DO NOTHING;

-- 2) Perfil OT: vincula la oficina Ricaurte (por code) con el tenant OT. Se inserta solo si
--    la oficina existe y aún no tiene perfil (unique por transit_office_id).
INSERT INTO admin.transit_office_profiles
    (id, tenant_id, transit_office_id, operation_mode, quipux_read_only, created_at)
SELECT
    'cccccccc-2561-4000-8000-000000000001',
    'bbbbbbbb-2561-4000-8000-000000000001',
    o.id,
    'dashboard',
    false,
    now()
FROM catalogs.transit_offices o
WHERE o.code = '25612000'
  AND NOT EXISTS (
      SELECT 1 FROM admin.transit_office_profiles p WHERE p.transit_office_id = o.id
  );
