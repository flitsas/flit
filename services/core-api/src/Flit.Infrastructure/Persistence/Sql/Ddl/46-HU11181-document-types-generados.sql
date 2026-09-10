-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11181 (Feature #11174) — Los documentos que GENERA el sistema entran al catálogo.
--
-- Hoy el orden del expediente sale de listas hardcodeadas (TraspasoConsolidadoOrdering /
-- MatriculaConsolidadoOrdering) porque los documentos generados —FUR, certificados, escrituras—
-- no existen en tramites.document_types y, al ser FK de admin.ot_document_precedence, no se
-- pueden reordenar. Este DDL los da de alta y los marca.
--
-- SEMÁNTICA ADITIVA — `is_system_generated` NO significa «excluido del checklist»:
--   • Checklist del gestor  = tramites.procedure_document_requirements (SIN TOCAR aquí).
--   • Lista ordenable del OT = matriz base ∪ tipos con is_system_generated, deduplicada.
-- `compraventa` e `impronta` son a la vez generados y documentos del checklist (compraventa es
-- OBLIGATORIA en la matriz base de traspaso, 25-HU10522), así que leerlo como exclusión los
-- borraría del checklist y cambiaría la obligatoriedad. No se inserta ninguna fila en
-- procedure_document_requirements: la obligatoriedad queda exactamente como estaba.
--
-- `generated_sort_order` es el orden por defecto de los generados cuando el OT todavía no ha
-- configurado nada. Reproduce el orden vigente de TraspasoConsolidadoOrdering (la lista más
-- completa) para que la pantalla de prelación arranque mostrando el expediente tal como sale hoy.
--
-- DDL IDEMPOTENTE (ADD COLUMN IF NOT EXISTS + ON CONFLICT DO NOTHING + UPDATE por code).
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE tramites.document_types
  ADD COLUMN IF NOT EXISTS is_system_generated boolean NOT NULL DEFAULT false;

ALTER TABLE tramites.document_types
  ADD COLUMN IF NOT EXISTS generated_sort_order smallint;

COMMENT ON COLUMN tramites.document_types.is_system_generated
  IS 'HU #11181 — el documento lo produce FLIT (FUR, certificados, mandato, escrituras). Entra en la lista ordenable del OT; NO implica exclusión del checklist del gestor.';
COMMENT ON COLUMN tramites.document_types.generated_sort_order
  IS 'HU #11181 — orden por defecto del documento generado en el expediente cuando el OT no ha configurado prelación. NULL en los documentos que solo se adjuntan.';

-- 1) Tipos generados que no existían en el catálogo. Los códigos son los mismos que usa
--    ProcedureInstanceAttachment.Tipo, para que el mapeo catálogo ↔ adjunto sea directo.
--    `licencia_transito`, `fur`, `mandato`, `tramite_virtual`, `compraventa` e `impronta` YA
--    existían (23-HU10520) y solo se marcan más abajo.
INSERT INTO tramites.document_types (code, name, description, mime_types_allowed, max_size_bytes, is_active)
VALUES
    ('certificado_identidad',
     'Certificado de validación de identidad',
     'Certificado de validación biométrica de identidad del comprador/propietario (generado por FLIT)',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('certificado_identidad_vendedor',
     'Certificado de identidad (vendedor)',
     'Certificado de validación biométrica de identidad del vendedor (generado por FLIT)',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('certificado_rues',
     'Certificado RUES',
     'Certificado RUES de la persona jurídica del trámite (generado por FLIT, HU #10589)',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('certificado_rnmc',
     'Certificado RNMC',
     'Certificado de medidas correctivas RNMC (generado por FLIT, HU #10762)',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('escritura',
     'Escrituras del vendedor',
     'Escritura de la compañía del vendedor/propietario (generado por FLIT, HU #10926)',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('escritura_comprador',
     'Escrituras del comprador',
     'Escritura de la compañía del comprador (generado por FLIT, HU #10926)',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true)
ON CONFLICT (code) DO NOTHING;

-- 2) Marca + orden por defecto de TODOS los generados (los nuevos y los que ya existían).
UPDATE tramites.document_types dt
SET is_system_generated = true,
    generated_sort_order = v.orden,
    updated_at = now()
FROM (VALUES
    ('fur',                            1::smallint),
    ('licencia_transito',              2::smallint),
    ('mandato',                        3::smallint),
    ('tramite_virtual',                4::smallint),
    ('certificado_identidad',          5::smallint),
    ('certificado_identidad_vendedor', 6::smallint),
    ('certificado_rues',               7::smallint),
    ('certificado_rnmc',               8::smallint),
    ('compraventa',                    9::smallint),
    ('escritura',                     10::smallint),
    ('escritura_comprador',           11::smallint),
    ('impronta',                      12::smallint)
) AS v(code, orden)
WHERE dt.code = v.code
  AND (dt.is_system_generated IS DISTINCT FROM true
       OR dt.generated_sort_order IS DISTINCT FROM v.orden);
