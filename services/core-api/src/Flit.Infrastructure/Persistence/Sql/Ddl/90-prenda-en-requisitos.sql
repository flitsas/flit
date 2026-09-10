-- =============================================================================
-- La prenda de los tipos prendarios se captura en REQUISITOS, no en un paso propio.
-- Migración: 20260825160000_PrendaEnRequisitos
--
-- En traspaso y en matrícula la decisión de gravamen SIEMPRE ha vivido dentro del paso de
-- Requisitos, junto a las condiciones del trámite y la gestión documental. Los tipos prendarios de
-- la familia OTROS se parametrizaron con un paso aparte («Decisión de prenda»), de modo que el
-- único trámite cuyo objeto ES el gravamen resultaba ser también el único que lo capturaba fuera de
-- Requisitos. Aquí se retira ese paso y la captura vuelve al sitio común.
--
-- ALCANCE DELIBERADO: solo LEVANTAMIENTO_PRENDA y PRENDA_INSCRIPCION. LEVANTAR_INSCRIBIR_PRENDA y
-- CAMBIO_ACREEDOR comparten el recorrido PRENDA pero NO se tocan: ejecutan dos acciones sobre el
-- gravamen y su captura todavía no está resuelta, así que conservan su paso hasta que se decida.
-- Por eso el borrado es por CÓDIGO DE TIPO y no por recorrido.
--
-- Idempotente y reaplicable.
-- =============================================================================

CREATE TEMP TABLE _tipos_prenda_en_requisitos(code text) ON COMMIT DROP;
INSERT INTO _tipos_prenda_en_requisitos VALUES ('LEVANTAMIENTO_PRENDA'), ('PRENDA_INSCRIPCION');

-- ============================================================================
-- 1. Borradores que estaban parados en el paso que desaparece
-- ============================================================================
-- Se repuntan ANTES de borrar el paso: un trámite abierto en «Decisión de prenda» quedaría
-- apuntando a un paso inexistente. `documentos` es donde ahora viven sus datos, así que el gestor
-- retoma justo donde los dejó. (El asistente además tolera la clave vieja, pero el dato persistido
-- no debe quedar colgando.)
UPDATE tramites.procedure_instances pi
   SET current_step = 'documentos',
       updated_at = now()
  FROM tramites.procedure_types pt
 WHERE pi.procedure_type_id = pt.id
   AND pt.code IN (SELECT code FROM _tipos_prenda_en_requisitos)
   AND pi.current_step = 'prenda';

-- ============================================================================
-- 2. Retiro del paso (las secciones caen por ON DELETE CASCADE)
-- ============================================================================
DELETE FROM tramites.procedure_steps st
 USING tramites.procedure_types pt
 WHERE st.procedure_type_id = pt.id
   AND pt.code IN (SELECT code FROM _tipos_prenda_en_requisitos)
   AND st.code = 'prenda';

-- ============================================================================
-- 3. Recompactar el orden de los pasos que quedan
-- ============================================================================
-- Sin esto el recorrido queda con un hueco (1,2,3,5,6) y el numerito que ve el gestor deja de
-- corresponder con la posición real del paso.
WITH renumerado AS (
    SELECT st.id,
           row_number() OVER (PARTITION BY st.procedure_type_id ORDER BY st.sort_order, st.code) AS nuevo_orden
      FROM tramites.procedure_steps st
      JOIN tramites.procedure_types pt ON pt.id = st.procedure_type_id
     WHERE pt.code IN (SELECT code FROM _tipos_prenda_en_requisitos)
)
UPDATE tramites.procedure_steps st
   SET sort_order = r.nuevo_orden,
       updated_at = now()
  FROM renumerado r
 WHERE st.id = r.id
   AND st.sort_order IS DISTINCT FROM r.nuevo_orden::smallint;

-- ============================================================================
-- 4. Guardas
-- ============================================================================
DO $$
DECLARE
    con_paso int;
    sin_requisitos int;
BEGIN
    SELECT count(*) INTO con_paso
      FROM tramites.procedure_steps st
      JOIN tramites.procedure_types pt ON pt.id = st.procedure_type_id
     WHERE pt.code IN (SELECT code FROM _tipos_prenda_en_requisitos)
       AND st.code = 'prenda';

    IF con_paso > 0 THEN
        RAISE EXCEPTION 'Quedó el paso «prenda» en % tipo(s) que debían capturarla en Requisitos', con_paso;
    END IF;

    -- La contraparte: retirar el paso no puede dejar al trámite sin el paso que ahora la aloja.
    SELECT count(*) INTO sin_requisitos
      FROM tramites.procedure_types pt
     WHERE pt.code IN (SELECT code FROM _tipos_prenda_en_requisitos)
       AND NOT EXISTS (
           SELECT 1 FROM tramites.procedure_steps st
            WHERE st.procedure_type_id = pt.id AND st.code = 'documentos');

    IF sin_requisitos > 0 THEN
        RAISE EXCEPTION '% tipo(s) prendarios quedaron sin paso «documentos» donde capturar la prenda', sin_requisitos;
    END IF;
END $$;

-- ============================================================================
-- 5. PRENDA_INSCRIPCION: matriz documental que nunca llegó a existir
-- ============================================================================
-- El seed 38 BORRA sus procedure_document_requirements y no inserta ninguno; el seed 82 lo excluye
-- a propósito («PRENDA_INSCRIPCION y CAMBIO_LOCATARIO no se tocan: ya los parametrizó el seed 38»).
-- Resultado: el tipo se quedó sin matriz, el checklist cayó al catálogo en código —que solo conoce
-- MATRICULA_NUEVA y TRASPASO_STANDARD— y el paso de Requisitos respondía 422 «La tipología del
-- trámite no está configurada», un mensaje sobre un catálogo que ADR-0050 ya dejó atrás.
--
-- Se le da la MISMA base que el seed 82 da a sus hermanos de la familia OTROS, más su documento
-- propio: el registro de la prenda que se inscribe.
INSERT INTO tramites.procedure_document_requirements
    (id, procedure_type_id, document_type_id, is_mandatory, default_sort_order)
SELECT uuidv7(), pt.id, dt.id, r.obligatorio, r.orden
  FROM tramites.procedure_types pt
  CROSS JOIN (VALUES
      ('tarjeta_propiedad',         true,  1::smallint),
      ('doc_identidad_propietario', true,  2::smallint),
      ('soat',                      false, 3::smallint),
      ('paz_salvo',                 false, 4::smallint),
      -- Documento propio del trámite: es el soporte del acto que se radica, no un anexo.
      ('inscripcion_prenda',        true,  10::smallint)
  ) AS r(doc, obligatorio, orden)
  JOIN tramites.document_types dt ON dt.code = r.doc
 WHERE pt.code = 'PRENDA_INSCRIPCION'
ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING;

DO $$
DECLARE
    sin_matriz int;
BEGIN
    SELECT count(*) INTO sin_matriz
      FROM tramites.procedure_types pt
     WHERE pt.code = 'PRENDA_INSCRIPCION'
       AND NOT EXISTS (
           SELECT 1 FROM tramites.procedure_document_requirements r
            WHERE r.procedure_type_id = pt.id);

    IF sin_matriz > 0 THEN
        RAISE EXCEPTION 'PRENDA_INSCRIPCION sigue sin matriz documental';
    END IF;
END $$;
