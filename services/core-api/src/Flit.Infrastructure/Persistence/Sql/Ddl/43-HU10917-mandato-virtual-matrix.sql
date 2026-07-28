-- ─────────────────────────────────────────────────────────────────────────────
-- HU #10917 (ADR-0036) — 'mandato' y 'tramite_virtual' ORDENABLES por el OT.
--
-- Los documentos GENERADOS 'mandato' (condicional) y 'tramite_virtual' (siempre) se añaden a la matriz
-- documental de Matrícula (MATRICULA_NUEVA) y Traspaso (TRASPASO_STANDARD) como NO obligatorios (se
-- autogeneran, no se cargan), con orden inicial ANTES de los documentos subidos. Con esto:
--   · aparecen en la Prelación del OT (procedure_document_requirements + ot_document_precedence) y el OT
--     puede REORDENARLOS como cualquier otro documento;
--   · el Expediente Consolidado MAESTRO los ubica según la matriz resuelta del OT en vez de dejarlos al
--     final como "Anexos".
-- No los vuelve obligatorios (is_mandatory=false) ni de carga manual: si el trámite no exige mandato, su
-- adjunto simplemente no existe y el orden no lo incluye (SelectByPrecedence solo ordena adjuntos reales).
-- Idempotente: ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING — no pisa reordenamientos.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_document_requirements
    (procedure_type_id, document_type_id, is_mandatory, default_sort_order)
SELECT pt.id, dt.id, false, v.sort_order
FROM (VALUES
    ('MATRICULA_NUEVA',   'mandato',         5::smallint),
    ('MATRICULA_NUEVA',   'tramite_virtual', 6::smallint),
    ('TRASPASO_STANDARD', 'mandato',         5::smallint),
    ('TRASPASO_STANDARD', 'tramite_virtual', 6::smallint)
) AS v(pt_code, dt_code, sort_order)
JOIN tramites.document_types dt ON dt.code = v.dt_code
JOIN tramites.procedure_types pt ON pt.code = v.pt_code
ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING;
