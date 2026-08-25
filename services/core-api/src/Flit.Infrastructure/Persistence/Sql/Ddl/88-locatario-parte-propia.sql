-- =============================================================================
-- El locatario deja de ser el comprador disfrazado.
-- Migración: 20260824190000_LocatarioPartePropia
--
-- El arrendatario ya existía en casi todo el sistema: `LESSEE` está en
-- tramites.procedure_entities desde el seed HU10151, FurCommand le arma su DocumentParte, el
-- resolver de destinatarios le manda los correos de estado y TramiteLifecycleService mapea
-- 'locatario' → 'LESSEE'. Lo único que faltaba era poder CAPTURARLO: `ParteRol` no lo tenía, así que
-- PutActorsHandler respondía `invalid_rol` y ningún actor podía persistirse con ese rol.
--
-- Consecuencia visible, y el motivo real de esta migración: sin locatario,
-- `FurTramiteObservation.ComposeLeasing` cae al comprador, detecta que propietario y locatario son
-- la misma parte y devuelve null — es decir, MATRICULA_LEASING venía emitiendo el FUR SIN la
-- observación del párrafo 23 que el artefacto marca como obligatoria
-- (docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md, tabla 1: «Propietario y locatario son partes
-- distintas»). No fallaba nada; simplemente el bloque no salía.
--
-- Alcance: DOS tipos.
--   · CAMBIO_LOCATARIO  — el trámite ES el cambio de arrendatario.
--   · MATRICULA_LEASING — propietario (entidad financiera) y locatario son partes distintas.
--
-- TRASPASO_UNILATERAL queda FUERA a propósito: su literal del artefacto dice «el locatario
-- (comprador) no firma», o sea que ahí el adquirente ES el arrendatario y el fallback al comprador
-- de `ComposeUnilateral` es la semántica correcta, no un defecto. Añadirle una parte propia crearía
-- un duplicado. Si negocio confirma lo contrario, se agrega aquí y se le quita el fallback.
--
-- El locatario NO valida identidad ni firma: en el leasing quien autoriza es el propietario. Por eso
-- `requiresLessee` es una llave aparte y NO se toca `biometricActors`.
--
-- Idempotente y reaplicable.
-- =============================================================================

-- ============================================================================
-- 1. Capacidad del tipo
-- ============================================================================
UPDATE tramites.procedure_types
   SET gate_profile = coalesce(gate_profile, '{}'::jsonb) || '{"requiresLessee": true}'::jsonb,
       updated_at = now()
 WHERE code IN ('CAMBIO_LOCATARIO', 'MATRICULA_LEASING')
   AND gate_profile -> 'requiresLessee' IS DISTINCT FROM 'true'::jsonb;

-- Guarda: el locatario se identifica y se notifica, pero no valida identidad. Si alguien lo mete en
-- biometricActors, el trámite le exigirá una biometría que el leasing no le pide y el gate de
-- radicación quedará esperando a alguien que nunca la va a hacer.
DO $$
DECLARE
    con_biometria text;
BEGIN
    SELECT string_agg(code, ', ') INTO con_biometria
    FROM tramites.procedure_types
    WHERE gate_profile -> 'biometricActors' @> '["LESSEE"]'::jsonb;

    IF con_biometria IS NOT NULL THEN
        RAISE EXCEPTION
            'Tipos con LESSEE en biometricActors: %. El locatario no valida identidad ni firma: '
            'quien autoriza el leasing es el propietario.', con_biometria;
    END IF;
END $$;

-- ============================================================================
-- 2. Paso propio de captura
-- ============================================================================
-- Va DESPUÉS del propietario: primero se sabe quién es el dueño (en leasing, la entidad financiera)
-- y luego a nombre de quién está arrendado. La sección se codifica LOCATARIO, que es de donde el
-- asistente deduce el rol del actor.
DO $$
DECLARE
    con_expediente text;
    r record;
BEGIN
    -- form_field_id → form_fields es ON DELETE RESTRICT (06-HU10150): reordenar pasos de un tipo con
    -- expedientes abiertos abortaría el arranque con un 23503. Ninguno de los dos tiene expedientes
    -- (nunca fueron operables), pero si alguien creó uno en local se avisa y no se toca el recorrido.
    SELECT string_agg(DISTINCT pt.code, ', ') INTO con_expediente
    FROM tramites.procedure_instances pi
    JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
    WHERE pt.code IN ('CAMBIO_LOCATARIO', 'MATRICULA_LEASING');

    IF con_expediente IS NOT NULL THEN
        RAISE NOTICE
            'Paso de locatario sin agregar en %: hay expedientes creados con el recorrido anterior.',
            con_expediente;
        RETURN;
    END IF;

    FOR r IN
        SELECT pt.id AS type_id, pt.code,
               -- El paso del propietario: en CAMBIO_LOCATARIO se llama `propietario` (DDL 87) y en
               -- MATRICULA_LEASING todavía `comprador` (seed 82). Se ancla por la posición del paso
               -- de actores, no por su nombre, para no depender de cuál de los dos rótulos tenga.
               (SELECT ps.sort_order
                  FROM tramites.procedure_steps ps
                  JOIN tramites.procedure_sections sec ON sec.procedure_step_id = ps.id
                 WHERE ps.procedure_type_id = pt.id
                   AND sec.section_type = 'actor_form'
                 ORDER BY ps.sort_order
                 LIMIT 1) AS orden_propietario
          FROM tramites.procedure_types pt
         WHERE pt.code IN ('CAMBIO_LOCATARIO', 'MATRICULA_LEASING')
    LOOP
        IF r.orden_propietario IS NULL THEN
            RAISE EXCEPTION
                'El tipo % no tiene paso de actores: no hay dónde anclar el del locatario.', r.code;
        END IF;

        -- Reaplicable: si ya está, no se duplica.
        CONTINUE WHEN EXISTS (
            SELECT 1 FROM tramites.procedure_steps
             WHERE procedure_type_id = r.type_id AND code = 'locatario');

        -- Hueco para el paso nuevo, justo detrás del propietario.
        UPDATE tramites.procedure_steps
           SET sort_order = sort_order + 1
         WHERE procedure_type_id = r.type_id
           AND sort_order > r.orden_propietario;

        INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active)
        VALUES (uuidv7(), r.type_id, 'locatario', 'Locatario',
                (r.orden_propietario + 1)::smallint, true);

        INSERT INTO tramites.procedure_sections
            (id, procedure_step_id, code, title, sort_order, layout, section_type)
        SELECT uuidv7(), ps.id, 'LOCATARIO', 'Locatario', 1, 'single', 'actor_form'
          FROM tramites.procedure_steps ps
         WHERE ps.procedure_type_id = r.type_id AND ps.code = 'locatario';
    END LOOP;
END $$;

-- Guarda final: los dos tipos deben quedar con exactamente DOS pasos de actores (propietario y
-- locatario) y sin posiciones repetidas. Un recorrido con dos pasos peleando por el mismo orden es
-- invisible hasta que alguien abre el asistente.
DO $$
DECLARE
    malos text;
BEGIN
    SELECT string_agg(code, ', ') INTO malos
    FROM (
        SELECT pt.code
        FROM tramites.procedure_types pt
        JOIN tramites.procedure_steps ps ON ps.procedure_type_id = pt.id AND ps.is_active
        JOIN tramites.procedure_sections sec ON sec.procedure_step_id = ps.id
        WHERE pt.code IN ('CAMBIO_LOCATARIO', 'MATRICULA_LEASING')
          AND sec.section_type = 'actor_form'
        GROUP BY pt.code
        HAVING count(*) <> 2
    ) AS t;

    IF malos IS NOT NULL THEN
        RAISE EXCEPTION 'Tipos sin exactamente dos pasos de actores (propietario + locatario): %.', malos;
    END IF;

    SELECT string_agg(DISTINCT code, ', ') INTO malos
    FROM (
        SELECT pt.code, ps.sort_order
        FROM tramites.procedure_types pt
        JOIN tramites.procedure_steps ps ON ps.procedure_type_id = pt.id AND ps.is_active
        WHERE pt.code IN ('CAMBIO_LOCATARIO', 'MATRICULA_LEASING')
        GROUP BY pt.code, ps.sort_order
        HAVING count(*) > 1
    ) AS t;

    IF malos IS NOT NULL THEN
        RAISE EXCEPTION 'Pasos compitiendo por la misma posición en: %.', malos;
    END IF;
END $$;
