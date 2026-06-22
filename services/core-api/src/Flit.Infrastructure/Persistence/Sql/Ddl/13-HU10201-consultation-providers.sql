-- HU #10201 — Cableado de proveedores de consulta (data-only) | Feature #10116
-- Migración: HU10201_ConsultationProviders
-- Catálogos globales (sin tenant_id): excepción A20 documentada en ADR-0019.
-- Sin secretos: base_url usa stub seguro. Idempotente.

-- ─────────────────────────────────────────────────────────────────────────────
-- AC1/AC2 — Cablear RUNT_VEHICLE al proveedor Verifik (config real, todos los entornos).
--   El seed previo (04-HU10151) lo insertó con external_refs = '{}' usando
--   ON CONFLICT DO NOTHING, por lo que re-insertar NO actualiza external_refs.
--   Se usa UPDATE para poblar la configuración del proveedor.
-- ─────────────────────────────────────────────────────────────────────────────
UPDATE tramites.consultation_templates
SET external_refs = '{"provider":"verifik","endpointKey":"runt_vehicle"}'::jsonb,
    updated_at = now()
WHERE code = 'RUNT_VEHICLE';

-- ─────────────────────────────────────────────────────────────────────────────
-- AC3 — Template stub para el gateway de integraciones Flit.
--   Primero la fuente externa (FK destino). base_url es un stub seguro (sin secretos).
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.external_data_sources (id, code, name, base_url, auth_type, timeout_ms, is_active, external_refs)
VALUES
    (uuidv7(), 'FLIT_INTEGRATIONS', 'Flit Integrations — Gateway interno', 'https://gateway.flit.local', 'none', 5000, true, '{}')
ON CONFLICT (code) DO NOTHING;

INSERT INTO tramites.consultation_templates (
    id, code, name,
    external_data_source_id,
    entity_scope, person_type,
    required_field_keys, request_schema,
    is_active, external_refs
)
SELECT
    uuidv7(),
    'FLIT_GATEWAY_DEMO',
    'Gateway Flit Integrations (stub HU#10201)',
    ds.id,
    'vehicle', NULL,
    '[]'::jsonb,
    '{}'::jsonb,
    true,
    '{"provider":"flit_integrations"}'::jsonb
FROM tramites.external_data_sources ds WHERE ds.code = 'FLIT_INTEGRATIONS'
ON CONFLICT (code) DO NOTHING;
