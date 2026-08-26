-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11183 (Feature #11174) — el certificado de vigencia SOAT/RTM también lo genera FLIT.
--
-- Lo produce SoatRtmCertificatePdfGenerator y se adjunta como `certificado_soat_rtm`, pero en el
-- catálogo está con el código `certificado_vigencia_soat_rtm` (23-HU10520). Al no estar marcado
-- como generado no aparecía en la lista ordenable del OT, y al no coincidir el código con el tipo
-- del adjunto tampoco lo alcanzaría el orden configurado: esa equivalencia la declara
-- ConsolidadoDocumentCodeMap.
--
-- Orden por defecto 13: cierra el bloque de generados, después de la impronta (12).
-- DDL IDEMPOTENTE: el UPDATE no hace nada si ya está marcado.
-- ─────────────────────────────────────────────────────────────────────────────

UPDATE tramites.document_types
SET is_system_generated = true,
    generated_sort_order = 13::smallint,
    updated_at = now()
WHERE code = 'certificado_vigencia_soat_rtm'
  AND (is_system_generated IS DISTINCT FROM true
       OR generated_sort_order IS DISTINCT FROM 13::smallint);
