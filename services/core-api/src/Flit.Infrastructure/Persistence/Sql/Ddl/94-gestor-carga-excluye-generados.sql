-- Documentos que el sistema genera o apalanca: se asocian en Documental (consolidado / prelación OT)
-- pero no se piden ni se exigen en Requisitos ni en la radicación.
--
-- Amplía la marca is_system_generated a SOAT, RTM y cédulas (carga del gestor). Mandato,
-- trámite virtual, compraventa, impronta y certificados ya estaban marcados (46 / 47).

COMMENT ON COLUMN tramites.document_types.is_system_generated
  IS 'El documento lo produce o apalanca FLIT. Entra en consolidado y prelación OT; no se pide ni se exige en el checklist de carga del gestor.';

UPDATE tramites.document_types dt
SET is_system_generated = true,
    generated_sort_order = COALESCE(dt.generated_sort_order, v.orden),
    updated_at = now()
FROM (VALUES
    ('soat', 13::smallint),
    ('rtm', 14::smallint),
    ('cedulas', 15::smallint)
) AS v(code, orden)
WHERE dt.code = v.code
  AND (dt.is_system_generated IS DISTINCT FROM true
       OR dt.generated_sort_order IS NULL);
