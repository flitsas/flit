-- ─────────────────────────────────────────────────────────────────────────────
-- HU #10522 (RF17/RF22) — Matriz documental base de Traspaso.
--
-- Siembra tramites.procedure_document_requirements para el procedure_type
-- TRASPASO_STANDARD (procedure_type canónico del traspaso estándar — decisión LT)
-- con los MISMOS documentos, obligatoriedad y orden del catálogo vivo
-- (TramiteTipologiaCatalog.traspaso_standard): compraventa/soat/rtm/paz_salvo/cedulas
-- obligatorios; impronta y cert_tradicion opcionales. Así el gestor arranca mostrando
-- EXACTAMENTE el checklist actual de traspaso (paridad, cero regresión).
--
-- Nota: "cedulas" se oculta para persona natural vía reglas condicionales (RF35) — eso
-- se sigue aplicando sobre la matriz en el motor, no en el seed.
--
-- Referencia los códigos canónicos (minúscula) de 23-HU10520-document-types-seed.
-- Idempotente: ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_document_requirements
    (procedure_type_id, document_type_id, is_mandatory, default_sort_order)
SELECT pt.id, dt.id, v.is_mandatory, v.sort_order
FROM (VALUES
    ('compraventa',    true,  10::smallint),
    ('impronta',       false, 20::smallint),
    ('soat',           true,  30::smallint),
    ('rtm',            true,  40::smallint),
    ('paz_salvo',      true,  50::smallint),
    ('cedulas',        true,  60::smallint),
    ('cert_tradicion', false, 70::smallint)
) AS v(code, is_mandatory, sort_order)
JOIN tramites.document_types dt ON dt.code = v.code
JOIN tramites.procedure_types pt ON pt.code = 'TRASPASO_STANDARD'
ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING;
