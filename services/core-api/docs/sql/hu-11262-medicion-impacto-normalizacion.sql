-- HU #11262 / D9 — Medición previa de impacto de normalización canónica (solo lectura)
-- Cuenta pares (actor de trámite, validación biométrica) del mismo tenant que:
--   * NO empatan con igualdad exacta de (tipo_doc, numero_doc)
--   * SÍ empatan tras Trim + Upper (regla canónica DocumentCanonicalNormalization)
-- Ejecutar en DEV antes de activar HU #11263. Pegar el resultado en Discussion de #11262.

SELECT
  a.tenant_id,
  COUNT(*) AS pares_impacto_normalizacion
FROM tramites.procedure_instance_actors a
INNER JOIN tramites.procedure_instance_biometric_validations v
  ON v.tenant_id = a.tenant_id
 AND v.status = 'aprobado'
WHERE
  -- No empatan exacto (regla histórica SQL / igualdad ordinal)
  NOT (
    COALESCE(a.document_type, '') = COALESCE(v.document_type, '')
    AND COALESCE(a.document_number, '') = COALESCE(v.document_number, '')
  )
  -- Sí empatan canónico (Trim + Upper)
  AND UPPER(TRIM(COALESCE(a.document_type, ''))) = UPPER(TRIM(COALESCE(v.document_type, '')))
  AND UPPER(TRIM(COALESCE(a.document_number, ''))) = UPPER(TRIM(COALESCE(v.document_number, '')))
GROUP BY a.tenant_id
ORDER BY pares_impacto_normalizacion DESC;
