-- ─────────────────────────────────────────────────────────────────────────────
-- HU #10881 (CF-06) — Asocia el documento de prenda (inscripcion_prenda) a Traspaso
-- Estándar como candidato de override por Organismo de Tránsito.
--
-- Inserta la fila en tramites.procedure_document_requirements con is_mandatory=false
-- (NO se exige por defecto; paridad, sin regresión) y condition_group='prenda' (documenta
-- la intención: el requisito solo se activa cuando la condición del grupo está activa). El
-- mecanismo runtime que ACTIVA el requisito para esta HU es el override de obligatoriedad
-- por OT existente (tramites.document_requirement_overrides, HU #10198) evaluado directamente
-- por IPrendaDocumentRequirementPolicy — NO el resolutor de la matriz viva (HU #10522), que
-- queda sin cambios. Esta fila es la asociación trámite↔documento que exige
-- SetDocumentRequirementOverrideHandler (HU #10198, AC de integridad) antes de admitir un
-- override sobre este documento para un OT: sin ella, el endpoint Admin no puede activar el
-- toggle "Documento de prenda obligatorio" (404/422).
--
-- Idempotente: ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_document_requirements
    (procedure_type_id, document_type_id, is_mandatory, default_sort_order, condition_group)
SELECT pt.id, dt.id, false, 80::smallint, 'prenda'
FROM tramites.procedure_types pt
JOIN tramites.document_types dt ON dt.code = 'inscripcion_prenda'
WHERE pt.code = 'TRASPASO_STANDARD'
ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING;
