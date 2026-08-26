-- =============================================================================
-- Cancelación de matrícula: documentos que acreditan cada causal.
-- Migración: 20260826100000_CancelacionCausales (DDL 92)
--
-- El trámite tiene UNA casilla en el numeral 3 del FUR (la 13) para cuatro situaciones que el
-- organismo trata distinto: decisión judicial, pérdida total por fuerza mayor, pérdida total por
-- accidente y decisión voluntaria. Hasta aquí FLIT no preguntaba cuál era, así que el checklist
-- pedía lo mismo para las cuatro y el formulario salía mudo sobre la causal.
--
-- La causal la declara el gestor en el paso de requerimientos (field_value `cancelacion_causal`) y
-- de ella cuelgan los documentos obligatorios:
--
--     Decisión judicial            → acto de decisión judicial
--     Pérdida total fuerza mayor   → cert. DIJIN o Policía + aseguradora/perito + autoridad admin.
--     Pérdida total por accidente  → cert. DIJIN o Policía + aseguradora/perito + autoridad admin.
--     Decisión voluntaria          → cert. DIJIN o Policía
--
-- Aquí se siembran los TIPOS de documento y se atan al trámite como OPCIONALES. La obligatoriedad
-- real es de la causal y la resuelve el motor de reglas condicionales
-- (`ConditionalDocumentRules.Cancelacion`), porque depende de un dato del expediente y no del tipo:
-- grabarlos obligatorios aquí exigiría los tres certificados también en una cancelación judicial.
-- Estar en la tabla es lo que les da identidad —el gestor los ve y los ordena en su matriz, y el
-- consolidado los coloca en su sitio en vez de mezclarlos con los anexos sueltos—.
--
-- El acto de decisión judicial reutiliza `oficio_judicial`, que ya existía y ya estaba atado a este
-- trámite: un código nuevo dejaría dos tipos casi idénticos conviviendo en el catálogo.
-- El certificado de tradición (`cert_tradicion`) sigue obligatorio de base en las cuatro causales.
--
-- Idempotente y reaplicable.
-- =============================================================================

-- ============================================================================
-- 1. Tipos de documento propios
-- ============================================================================
-- Mismas reglas de carga que el resto del catálogo (pdf/jpeg/png/webp, 20 MB): la parametrización
-- fina por tipo es del admin, este seed no la prejuzga.
INSERT INTO tramites.document_types (code, name, description, mime_types_allowed, max_size_bytes, is_active)
VALUES
    ('certificado_dijin',
     'Certificado DIJIN o Policía',
     'Certificado de la DIJIN o de la Policía Nacional sobre el vehículo',
     '["application/pdf","image/jpeg","image/png","image/webp"]',
     20971520,
     true),
    ('certificado_aseguradora_perito',
     'Certificado de aseguradora o perito',
     'Certificado de pérdida total emitido por la aseguradora o por el perito',
     '["application/pdf","image/jpeg","image/png","image/webp"]',
     20971520,
     true),
    ('certificado_autoridad_administrativa',
     'Certificado de autoridad administrativa',
     'Certificado de la autoridad administrativa competente',
     '["application/pdf","image/jpeg","image/png","image/webp"]',
     20971520,
     true)
ON CONFLICT (code) DO NOTHING;

-- ============================================================================
-- 2. Requisitos del trámite (opcionales; la causal los vuelve obligatorios)
-- ============================================================================
-- `oficio_judicial` ya venía del seed 82 con orden 11: se conserva tal cual y solo se le añaden los
-- tres certificados detrás, para no reordenar un checklist que los gestores ya conocen.
INSERT INTO tramites.procedure_document_requirements
    (id, procedure_type_id, document_type_id, is_mandatory, default_sort_order)
SELECT uuidv7(), pt.id, dt.id, false, d.orden::smallint
  FROM tramites.procedure_types pt
  JOIN (VALUES
        ('oficio_judicial',                      11),
        ('certificado_dijin',                    12),
        ('certificado_aseguradora_perito',       13),
        ('certificado_autoridad_administrativa', 14)
       ) AS d(code, orden) ON true
  JOIN tramites.document_types dt ON dt.code = d.code
 WHERE pt.code = 'CANCELACION_MATRICULA'
ON CONFLICT (procedure_type_id, document_type_id) DO UPDATE
   SET is_mandatory = false,
       default_sort_order = EXCLUDED.default_sort_order;

-- Guarda: si el tipo existe, sus cuatro documentos de causal tienen que existir con él. Un fallo
-- silencioso aquí dejaría al motor de reglas exigiendo un documento que el gestor no puede cargar
-- porque no está en su matriz.
DO $$
DECLARE
    faltante int;
BEGIN
    SELECT count(*) INTO faltante
      FROM tramites.procedure_types pt
      CROSS JOIN (VALUES
            ('oficio_judicial'),
            ('certificado_dijin'),
            ('certificado_aseguradora_perito'),
            ('certificado_autoridad_administrativa')
           ) AS d(code)
     WHERE pt.code = 'CANCELACION_MATRICULA'
       AND NOT EXISTS (
           SELECT 1
             FROM tramites.procedure_document_requirements r
             JOIN tramites.document_types dt ON dt.id = r.document_type_id
            WHERE r.procedure_type_id = pt.id
              AND dt.code = d.code);

    IF faltante > 0 THEN
        RAISE EXCEPTION 'CANCELACION_MATRICULA quedó sin % documento(s) de causal', faltante;
    END IF;
END $$;
