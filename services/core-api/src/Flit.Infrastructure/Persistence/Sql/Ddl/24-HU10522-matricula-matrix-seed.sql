-- ─────────────────────────────────────────────────────────────────────────────
-- HU #10522 (RF17/RF22) — Matriz documental base de Matrícula Inicial.
--
-- Siembra tramites.procedure_document_requirements para el procedure_type
-- MATRICULA_NUEVA (procedure_type canónico de la matrícula inicial — decisión LT)
-- con los MISMOS 9 documentos, obligatoriedad y orden del catálogo vivo
-- (TramiteTipologiaCatalog: factura+aduana obligatorios, resto opcional). Así el
-- gestor arranca mostrando EXACTAMENTE el checklist actual (paridad, cero regresión),
-- y desde la consola el admin ya puede reordenar / cambiar obligatoriedad.
--
-- Referencia los códigos canónicos (minúscula) de 23-HU10520-document-types-seed.
-- Idempotente: ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING — no pisa
-- la parametrización que el admin haya ajustado luego.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_document_requirements
    (procedure_type_id, document_type_id, is_mandatory, default_sort_order)
SELECT pt.id, dt.id, v.is_mandatory, v.sort_order
FROM (VALUES
    ('factura',                true,  10::smallint),
    ('aduana',                 true,  20::smallint),
    ('impronta',               false, 30::smallint),
    ('soat',                   false, 40::smallint),
    ('certificado_ambiental',  false, 50::smallint),
    ('declaracion_aduana',     false, 60::smallint),
    ('acta_remate',            false, 70::smallint),
    ('oficio_judicial',        false, 80::smallint),
    ('otro',                   false, 90::smallint)
) AS v(code, is_mandatory, sort_order)
JOIN tramites.document_types dt ON dt.code = v.code
JOIN tramites.procedure_types pt ON pt.code = 'MATRICULA_NUEVA'
ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING;
