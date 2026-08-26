-- =============================================================================
-- Blindaje: opción declarada (niveles / desmonte) y certificado con código propio.
-- Migración: 20260825120000_CertificadoBlindaje
--
-- El seed 82 dejó anotado el motivo por el que BLINDAJE exigía el genérico 'otro':
--
--     -- 'otro' hasta que el catálogo tenga código propio (certificado de blindaje).
--     ('BLINDAJE', 'otro', true, 10),
--
-- Ese aplazamiento tiene dos consecuencias visibles. En el checklist el gestor ve «Otro documento»
-- y tiene que saber por su cuenta que ahí va el certificado; y en el expediente consolidado el
-- documento no tiene identidad —el pie de página dice «Otro» y el orden de la matriz no lo
-- distingue de cualquier anexo suelto—, así que el organismo recibe el certificado mezclado con lo
-- demás en vez de en su sitio.
--
-- Aquí se le da código propio y se sustituye el requisito. El certificado es OBLIGATORIO en las
-- cuatro opciones del trámite, incluido el desmonte: también retirar un blindaje hay que
-- acreditarlo ante el organismo.
--
-- Idempotente y reaplicable.
-- =============================================================================

-- ============================================================================
-- 1. Tipo de documento propio
-- ============================================================================
-- Mismas reglas de carga que el resto del catálogo (pdf/jpeg/png/webp, 20 MB): la parametrización
-- por tipo es del admin, este seed no la prejuzga.
INSERT INTO tramites.document_types (code, name, description, mime_types_allowed, max_size_bytes, is_active)
VALUES (
    'certificado_blindaje',
    'Certificado de Blindaje',
    'Certificado de blindaje del vehículo (instalación por nivel o desmonte)',
    '["application/pdf","image/jpeg","image/png","image/webp"]',
    20971520,
    true)
ON CONFLICT (code) DO NOTHING;

-- ============================================================================
-- 2. BLINDAJE: 'otro' → 'certificado_blindaje'
-- ============================================================================
-- Se conserva obligatoriedad y orden (10) del requisito que se sustituye. Los adjuntos ya cargados
-- como 'otro' en trámites en curso NO se tocan: siguen en el expediente y en el consolidado; lo que
-- cambia es qué pide el checklist de aquí en adelante.
INSERT INTO tramites.procedure_document_requirements
    (id, procedure_type_id, document_type_id, is_mandatory, default_sort_order)
SELECT uuidv7(), pt.id, dt.id, true, 10::smallint
  FROM tramites.procedure_types pt
  JOIN tramites.document_types dt ON dt.code = 'certificado_blindaje'
 WHERE pt.code = 'BLINDAJE'
ON CONFLICT (procedure_type_id, document_type_id) DO UPDATE
   SET is_mandatory = true,
       default_sort_order = 10;

DELETE FROM tramites.procedure_document_requirements r
 USING tramites.procedure_types pt, tramites.document_types dt
 WHERE r.procedure_type_id = pt.id
   AND r.document_type_id = dt.id
   AND pt.code = 'BLINDAJE'
   AND dt.code = 'otro';

-- Guarda: el trámite no puede quedarse sin certificado exigido. Si el tipo BLINDAJE existe, el
-- requisito tiene que existir con él — un fallo silencioso aquí dejaría radicar blindajes sin
-- acreditar, que es exactamente lo que este DDL viene a cerrar.
DO $$
DECLARE
    faltante int;
BEGIN
    SELECT count(*) INTO faltante
      FROM tramites.procedure_types pt
     WHERE pt.code = 'BLINDAJE'
       AND NOT EXISTS (
           SELECT 1
             FROM tramites.procedure_document_requirements r
             JOIN tramites.document_types dt ON dt.id = r.document_type_id
            WHERE r.procedure_type_id = pt.id
              AND dt.code = 'certificado_blindaje'
              AND r.is_mandatory);

    IF faltante > 0 THEN
        RAISE EXCEPTION 'BLINDAJE quedó sin certificado de blindaje obligatorio (% tipo(s))', faltante;
    END IF;
END $$;
