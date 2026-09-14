-- =============================================================================
-- Interruptor «documentos de red para Concesión» — HU #12410 (Feature #12257, Épica #12235).
-- Migración: 20260914130000_HU12410_NetworkDocumentsSwitch. Backend Agent.
--
-- Tercer interruptor global de identity.hierarchy_switches (108-HU12323): network_documents_concesion.
--   apagado (DEFAULT, pendiente 13 del PO / bloqueo D1 Ley 1581) ⇒ una cabeza de clase CONCESION recibe
--            403 { error: "network_documents_disabled" } en GET /api/v1/tramites/network/instances/{id}/attachments
--            y en …/attachments/{attachmentId}/download.
--   encendido ⇒ CONCESION se comporta igual que MARCA_BLANCA (listado + descarga proxeada, solo lectura).
-- MARCA_BLANCA nunca consulta este interruptor. group_read_scope sigue siendo el freno global: apagado,
-- toda cabeza degrada a Single y la policy de cabeza responde 403 antes de llegar aquí.
--
-- Solo AMPLÍA el CHECK de claves y siembra la fila apagada. Reaplicar no pisa un interruptor encendido a
-- mano (ON CONFLICT DO NOTHING). Los triggers de row_version y audit_log de la tabla (108-) cubren la
-- fila nueva sin cambios: el estado y sus conmutaciones quedan en public.audit_log (AC5).
-- Aditiva, idempotente y reaplicable. Reversible: ver Down de la migración.
-- =============================================================================

ALTER TABLE identity.hierarchy_switches
    DROP CONSTRAINT IF EXISTS ck_hierarchy_switches_switch_key;

ALTER TABLE identity.hierarchy_switches
    ADD CONSTRAINT ck_hierarchy_switches_switch_key
    CHECK (switch_key IN ('group_read_scope', 'inherited_configuration', 'network_documents_concesion'));

-- Seed idempotente: nace APAGADO (a diferencia de los dos de 108-, que nacen encendidos).
INSERT INTO identity.hierarchy_switches (switch_key, is_enabled)
VALUES ('network_documents_concesion', false)
ON CONFLICT (switch_key) DO NOTHING;

COMMENT ON COLUMN identity.hierarchy_switches.switch_key IS
    'Clave fija del interruptor: group_read_scope | inherited_configuration | network_documents_concesion (ck_hierarchy_switches_switch_key).';

COMMENT ON TABLE identity.hierarchy_switches IS
    'HU #12323 (ADR-0057) — interruptores GLOBALES de la jerarquía de clientes, conmutables sin '
    'despliegue. group_read_scope: apagado ⇒ toda cabeza de grupo resuelve alcance Single (deja de '
    'leer a sus hijos). inherited_configuration: apagado ⇒ los hijos dejan de heredar la '
    'configuración del padre (lista de OT; Feature #12256). network_documents_concesion (HU #12410): '
    'apagado (por defecto) ⇒ una cabeza CONCESION recibe 403 en las rutas de documentos de la red; '
    'MARCA_BLANCA no lo consulta. Ninguno reutiliza is_group_parent: el vínculo y el invariante de '
    'profundidad siguen intactos con los interruptores apagados. Sin tenant_id ni RLS por ser '
    'configuración de plataforma (excepción documentada checklist A4/A10).';
