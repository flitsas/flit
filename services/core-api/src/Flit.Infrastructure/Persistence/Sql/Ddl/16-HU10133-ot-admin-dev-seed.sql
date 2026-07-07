-- HU #10133 (DEV-ONLY) — Seed de desarrollo para consola /admin/transit-offices
-- ⚠️  NO usar en producción. GUIDs fijos para validación E2E del módulo OT:
--       OT_TENANT_ID      = bbbbbbbb-0001-4000-8000-000000000001
--       BOGOTA_OFFICE_ID  = aaaaaaaa-0001-4000-8000-000000000001
--       OT_ACTOR_USER_ID  = ec4dddb9-ade5-43e8-b33b-c6036eba49d0 (otadmin@flit.local)
-- Idempotente: ON CONFLICT DO NOTHING / WHERE NOT EXISTS.

BEGIN;

-- RLS en tablas admin/tramites: el seed corre con el usuario de la app, no bypass.
SET LOCAL row_security = off;

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Catálogo FK (mirror del catálogo estático en memoria — StaticTransitOfficeCatalog)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES
    ('aaaaaaaa-0001-4000-8000-000000000001', '11001', 'Secretaría de Movilidad Bogotá', '11', '11001', true),
    ('aaaaaaaa-0001-4000-8000-000000000002', '05001', 'Medellín — Secretaría de Movilidad', '05', '05001', true),
    ('aaaaaaaa-0001-4000-8000-000000000003', '76001', 'Cali — STTMP', '76', '76001', true),
    ('aaaaaaaa-0001-4000-8000-000000000004', '08001', 'Barranquilla — Tránsito Distrital', '08', '08001', true),
    ('aaaaaaaa-0001-4000-8000-000000000005', '68001', 'Bucaramanga — Dirección de Tránsito', '68', '68001', true),
    ('aaaaaaaa-0001-4000-8000-000000000006', '13001', 'Cartagena — DATT', '13', '13001', true)
ON CONFLICT (id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Tenant OT de desarrollo (ya puede existir como OT-BOGOTA)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, created_at)
VALUES (
    'bbbbbbbb-0001-4000-8000-000000000001',
    'OT-BOGOTA',
    'Secretaría de Movilidad Bogotá OT (DEV)',
    '900000009-9',
    'RENTING',
    true,
    now()
)
ON CONFLICT (id) DO NOTHING;

-- Usuario OT actor (FK changed_by en procedure_instances / status_history)
INSERT INTO identity.users (id, email, display_name, status, created_at)
VALUES (
    'ec4dddb9-ade5-43e8-b33b-c6036eba49d0',
    'otadmin@flit.local',
    'Administrador OT Demo',
    'active',
    now()
)
ON CONFLICT (id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Perfil OT — vincula el tenant JWT con la oficina Bogotá del catálogo UI
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO admin.transit_office_profiles
    (id, tenant_id, transit_office_id, operation_mode, quipux_read_only, created_at)
VALUES (
    'b9ec839d-7b78-4165-8860-cf29b104c76c',
    'bbbbbbbb-0001-4000-8000-000000000001',
    'aaaaaaaa-0001-4000-8000-000000000001',
    'dashboard',
    false,
    now()
)
ON CONFLICT (tenant_id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. Grants — clientes mock habilitados para el OT Bogotá (tenant_id = cliente)
--    Limpia grants erróneos donde el tenant OT fue usado como cliente.
-- ─────────────────────────────────────────────────────────────────────────────
DELETE FROM admin.tenant_transit_office_grants
WHERE tenant_id = 'bbbbbbbb-0001-4000-8000-000000000001';

INSERT INTO admin.tenant_transit_office_grants (id, tenant_id, transit_office_id, is_enabled, created_at)
VALUES
    ('bbbbbbbb-0001-4000-8000-000000000001', '0ad1c0de-0000-4000-8000-000000000001', 'aaaaaaaa-0001-4000-8000-000000000001', true, now()),
    ('bbbbbbbb-0001-4000-8000-000000000002', '0ad1c0de-0000-4000-8000-000000000002', 'aaaaaaaa-0001-4000-8000-000000000001', true, now()),
    -- FLITDEV (tenant que usa el operador en dev): habilitado en 3 de 6 OTs para
    -- demostrar el filtrado por empresa en el wizard (Bogotá, Medellín, Cali).
    ('bbbbbbbb-0002-4000-8000-000000000001', '11111111-1111-1111-1111-111111111111', 'aaaaaaaa-0001-4000-8000-000000000001', true, now()),
    ('bbbbbbbb-0002-4000-8000-000000000002', '11111111-1111-1111-1111-111111111111', 'aaaaaaaa-0001-4000-8000-000000000002', true, now()),
    ('bbbbbbbb-0002-4000-8000-000000000003', '11111111-1111-1111-1111-111111111111', 'aaaaaaaa-0001-4000-8000-000000000003', true, now())
ON CONFLICT (tenant_id, transit_office_id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. Tipos documentales mínimos (prelación OT — FK a tramites.document_types)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.document_types (id, code, name, is_active)
VALUES
    ('d1111111-1111-4111-8111-111111111111', 'SOAT', 'SOAT', true),
    ('d2222222-2222-4222-8222-222222222222', 'CEDULA', 'Cédula de ciudadanía', true),
    ('d3333333-3333-4333-8333-333333333333', 'TARJETA_PROPIEDAD', 'Tarjeta de propiedad', true)
ON CONFLICT (code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 6. Trámites entregados de clientes en cola de decisión OT (tab client-procedures).
--    N 03 (ADR-0022): 'entregado' reemplaza a 'pending_ot' — el approve/reject OT opera
--    sobre entregado → aprobado | rechazado. Este seed también lo re-ejecuta
--    DevelopmentAuthSeeder en cada arranque dev (post-migraciones), por eso debe usar
--    el vocabulario nuevo.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_instances
    (id, tenant_id, procedure_type_id, reference_number, status, transit_office_id,
     submitted_at, created_by_user_id, created_at)
SELECT
    v.id, v.tenant_id, v.procedure_type_id, v.reference_number, 'entregado',
    'aaaaaaaa-0001-4000-8000-000000000001', now(),
    'ec4dddb9-ade5-43e8-b33b-c6036eba49d0', now()
FROM (VALUES
    ('bbbbbbbb-0001-4000-8000-000000000010'::uuid, '0ad1c0de-0000-4000-8000-000000000001'::uuid,
     '66666666-6666-6666-6666-666666666666'::uuid, 'OT-DEV-0001'),
    ('bbbbbbbb-0001-4000-8000-000000000011'::uuid, '0ad1c0de-0000-4000-8000-000000000002'::uuid,
     '66666666-6666-6666-6666-666666666666'::uuid, 'OT-DEV-0002'),
    ('bbbbbbbb-0001-4000-8000-000000000012'::uuid, '0ad1c0de-0000-4000-8000-000000000001'::uuid,
     '44444444-4444-4444-4444-444444444444'::uuid, 'OT-DEV-0003')
) AS v(id, tenant_id, procedure_type_id, reference_number)
WHERE EXISTS (
    SELECT 1 FROM identity.users u WHERE u.id = 'ec4dddb9-ade5-43e8-b33b-c6036eba49d0'
)
ON CONFLICT (tenant_id, reference_number) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 7. Feature flag demo (perfil OT)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO admin.ot_feature_flags (id, tenant_id, flag_key, is_enabled, config, created_at)
VALUES (
    'cccccccc-0001-4000-8000-000000000001',
    'bbbbbbbb-0001-4000-8000-000000000001',
    'enable_special_queue',
    false,
    '{}',
    now()
)
ON CONFLICT (tenant_id, flag_key) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 8. Regla OT de ejemplo (motor de reglas — flag_key rule:{id})
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO admin.ot_feature_flags (id, tenant_id, flag_key, is_enabled, config, created_at)
VALUES (
    'cccccccc-0002-4000-8000-000000000002',
    'bbbbbbbb-0001-4000-8000-000000000001',
    'rule:cccccccc-0002-4000-8000-000000000002',
    true,
    '{"name":"Bloqueo por deuda pendiente","conditions":[{"field":"deuda_pendiente","op":"eq","value":true}],"logic":"and","action":{"type":"block"}}',
    now()
)
ON CONFLICT (tenant_id, flag_key) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 9. Webhook + bitácora API (tabs webhooks / logs)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO admin.ot_webhook_subscriptions
    (id, tenant_id, event_type, target_url, secret_hash, is_active, created_at)
VALUES (
    'dddddddd-0001-4000-8000-000000000001',
    'bbbbbbbb-0001-4000-8000-000000000001',
    'vehicle_state_changed',
    'https://webhook.site/flit-ot-dev',
    'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
    true,
    now()
)
ON CONFLICT (id) DO NOTHING;

INSERT INTO admin.ot_api_call_logs
    (id, tenant_id, direction, endpoint, http_method, payload_hash, response_code, duration_ms, called_at, correlation_id)
VALUES
    ('eeeeeeee-0001-4000-8000-000000000001', 'bbbbbbbb-0001-4000-8000-000000000001',
     'outbound', '/api/v1/quipux/vehicles/state', 'POST',
     'a665a45920422f9d417e4867efdc4fb8a04a1f3fff1fa07e998e86f7f7a27ae3', 200, 142, now() - interval '2 hours',
     'f1111111-1111-4111-8111-111111111111'),
    ('eeeeeeee-0001-4000-8000-000000000002', 'bbbbbbbb-0001-4000-8000-000000000001',
     'inbound', '/api/v1/admin/ot/webhooks/callback', 'POST',
     'b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9', 202, 38, now() - interval '1 hour',
     'f2222222-2222-4222-8222-222222222222'),
    ('eeeeeeee-0001-4000-8000-000000000003', 'bbbbbbbb-0001-4000-8000-000000000001',
     'outbound', '/api/v1/quipux/procedures/submit', 'POST',
     'c3d4e5f6789012345678901234567890abcdef1234567890abcdef1234567890', 504, 30012, now() - interval '30 minutes',
     'f3333333-3333-4333-8333-333333333333')
ON CONFLICT (id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 10. Etiquetas documentales
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO admin.ot_document_tags (id, tenant_id, code, name, color, created_at)
VALUES
    ('e1111111-1111-4111-8111-111111111111', 'bbbbbbbb-0001-4000-8000-000000000001', 'URGENTE', 'Urgente', '#DC2626', now()),
    ('e2222222-2222-4222-8222-222222222222', 'bbbbbbbb-0001-4000-8000-000000000001', 'REVISAR', 'Revisar', '#F59E0B', now())
ON CONFLICT (tenant_id, code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 11. Prelación documental (procedureTypeId usado en la UI de documentos OT)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO admin.ot_document_precedence
    (id, tenant_id, procedure_type_id, document_type_id, sort_order, created_at)
SELECT
    v.id,
    'bbbbbbbb-0001-4000-8000-000000000001',
    '66666666-6666-6666-6666-666666666666',
    dt.id,
    v.sort_order,
    now()
FROM (VALUES
    ('f1111111-1111-4111-8111-111111111111'::uuid, 'SOAT', 1::smallint),
    ('f2222222-2222-4222-8222-222222222222'::uuid, 'CEDULA', 2::smallint),
    ('f3333333-3333-4333-8333-333333333333'::uuid, 'TARJETA_PROPIEDAD', 3::smallint)
) AS v(id, doc_code, sort_order)
JOIN tramites.document_types dt ON dt.code = v.doc_code
ON CONFLICT (tenant_id, procedure_type_id, document_type_id) DO NOTHING;

COMMIT;
