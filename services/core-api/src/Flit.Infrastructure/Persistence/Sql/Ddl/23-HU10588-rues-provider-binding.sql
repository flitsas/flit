-- HU #10588 — Binding del proveedor RUES a la plantilla de consulta | Feature #10583 (2ª ola)
-- Migración: HU10588_RuesProviderBinding
-- Catálogo global (sin tenant_id): excepción A20 documentada en ADR-0019.
-- El seed previo (04-HU10151-seeds-minimos) insertó RUES_ACTOR_JURIDICAL con external_refs = '{}'
-- usando ON CONFLICT DO NOTHING, por lo que re-insertar NO actualiza external_refs. Se usa UPDATE
-- para poblar la configuración del proveedor (patrón HU #10201). Data-only, idempotente.

UPDATE tramites.consultation_templates
SET external_refs = '{"provider":"verifik_rues","endpointKey":"rues_juridical"}'::jsonb,
    updated_at = now()
WHERE code = 'RUES_ACTOR_JURIDICAL';
