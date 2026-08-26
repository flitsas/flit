-- =============================================================================
-- ADR-0050 — la familia OTROS no acumula trámites complementarios.
-- Migración: 20260824180000_OtrosSinComplementarios
--
-- Matrícula y traspaso admiten radicar varios trámites sobre el mismo vehículo en un solo FUR
-- (art. 5.1.8): una prenda encima, un cambio de color encima. La familia OTROS no funciona así —el
-- cambio o el gravamen ES el trámite—, pero eso vivía únicamente como intención: el asistente
-- pintaba «Trámites Simultáneos» y «Asignación de Prenda» en todos los tipos, y nada en el servidor
-- lo impedía. Un CAMBIO_COLOR podía salir con una prenda y un blindaje encima, y el organismo
-- devuelve ese FUR.
--
-- Aquí se declara la regla donde el motor la lee: dos llaves nuevas del gate_profile
-- (`allowsComplementaryTransformations`, `allowsComplementaryPrenda`) que consumen el asistente
-- (WizardCapabilitiesDto), el PATCH de field_values, la decisión de prenda y el FUR/mandato.
--
-- La AUSENCIA de la llave NO es `false`: es «lo que diga la familia» (ProcedureTypeGateProfile).
-- Por eso este DDL solo tiene que tocar OTROS, y los perfiles ya congelados en
-- procedure_type_snapshots de un borrador en curso siguen resolviéndose bien sin reescribirlos.
--
-- Además alinea los dos tipos que el seed 38 dejó con un recorrido propio anterior a la regla
-- «un titular = el propietario inscrito, precargado desde el RUNT».
--
-- Idempotente y reaplicable.
-- =============================================================================

-- ============================================================================
-- 1. Familia OTROS: sin complementarios
-- ============================================================================
UPDATE tramites.procedure_types
   SET gate_profile = coalesce(gate_profile, '{}'::jsonb)
                      || '{"allowsComplementaryTransformations": false, "allowsComplementaryPrenda": false}'::jsonb,
       updated_at = now()
 WHERE family = 'OTROS'
   AND (gate_profile -> 'allowsComplementaryTransformations' IS DISTINCT FROM 'false'::jsonb
        OR gate_profile -> 'allowsComplementaryPrenda' IS DISTINCT FROM 'false'::jsonb);

-- Guarda: matrícula y traspaso NO pueden quedar marcadas. Apagarles los complementarios rompería en
-- silencio el art. 5.1.8 —los simultáneos desaparecerían de la pantalla sin que nadie lo pidiera— y
-- el defecto solo se vería al abrir un traspaso.
--
-- Excepción declarada: `CANCELACION_MATRICULA` (DDL 93). Es de MATRICULAS, pero acumular presupone un
-- vehículo que sigue inscrito y la cancelación lo saca del registro: una limitación a la propiedad
-- sobre una matrícula que se cancela es una contradicción, no un trámite simultáneo. En una base
-- nueva este DDL corre ANTES que el 93, así que la guarda no la vería igual; se excluye de forma
-- explícita para que una reaplicación posterior tampoco la denuncie como un error.
DO $$
DECLARE
    marcados text;
BEGIN
    SELECT string_agg(code, ', ') INTO marcados
    FROM tramites.procedure_types
    WHERE family <> 'OTROS'
      AND code <> 'CANCELACION_MATRICULA'
      AND (gate_profile ->> 'allowsComplementaryTransformations' = 'false'
           OR gate_profile ->> 'allowsComplementaryPrenda' = 'false');

    IF marcados IS NOT NULL THEN
        RAISE EXCEPTION
            'Tipos fuera de la familia OTROS con los complementarios apagados: %. '
            'Matrícula y traspaso deben conservar prenda y transformaciones adicionales (art. 5.1.8).',
            marcados;
    END IF;
END $$;

-- ============================================================================
-- 2. PRENDA_INSCRIPCION y CAMBIO_LOCATARIO — alineación con el recorrido de OTROS
-- ============================================================================
-- El seed 38 (FEATURE-08) les dio recorridos propios de cuando la familia OTROS no tenía forma
-- definida: PRENDA_INSCRIPCION pedía «Propietario y acreedor» en un paso sin actor declarado en el
-- perfil (así que el asistente no exigía ninguno) y no tenía paso de identidad; CAMBIO_LOCATARIO
-- ponía los documentos ANTES de saber quién es el titular y capturaba un «locatario» que el modelo
-- de actores no tiene (ActorType solo conoce comprador/vendedor/locatario en el FUR, no en captura).
--
-- Los dos pasan al recorrido de la familia: consulta → propietario → documentos → [prenda] →
-- identidad → FUR, con el titular precargado del RUNT. En CAMBIO_LOCATARIO quien autoriza es el
-- PROPIETARIO inscrito (la entidad financiera); la captura del locatario como parte propia queda
-- PENDIENTE —no existe hoy en el asistente— y no se sustituye por un comprador de traspaso, que es
-- lo que estaba pasando: `resolveActorRole` mapea cualquier paso que no sea «vendedor» a comprador.
DO $$
DECLARE
    con_expediente text;
BEGIN
    -- Los field_values apuntan a form_fields con ON DELETE RESTRICT (06-HU10150): reescribir los
    -- pasos de un tipo con expedientes abiertos abortaría el arranque con un 23503. Ninguno de los
    -- dos tiene expedientes (nunca fueron operables), pero si alguien creó uno en local, se avisa y
    -- se deja el recorrido viejo en vez de tumbar la migración.
    SELECT string_agg(DISTINCT pt.code, ', ') INTO con_expediente
    FROM tramites.procedure_instances pi
    JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
    WHERE pt.code IN ('PRENDA_INSCRIPCION', 'CAMBIO_LOCATARIO');

    IF con_expediente IS NOT NULL THEN
        RAISE NOTICE
            'Recorrido sin alinear en %: hay expedientes creados con el recorrido del seed 38. '
            'Bórralos en local y reaplica esta migración si necesitas el recorrido nuevo.',
            con_expediente;
        RETURN;
    END IF;

    UPDATE tramites.procedure_types
       SET gate_profile = coalesce(gate_profile, '{}'::jsonb) || jsonb_build_object(
               'entryMode', 'PLATE',
               'requiresBuyer', true,
               'requiresBiometrics', true,
               'biometricActors', jsonb_build_array('BUYER'),
               'requiresSignature', true,
               'validateOtOperability', true,
               'allowsComplementaryTransformations', false,
               'allowsComplementaryPrenda', false),
           updated_at = now()
     WHERE code IN ('PRENDA_INSCRIPCION', 'CAMBIO_LOCATARIO');

    -- El gate de prenda es del tipo prendario, no del locatario.
    UPDATE tramites.procedure_types
       SET gate_profile = gate_profile || '{"hasPrendaGate": true}'::jsonb
     WHERE code = 'PRENDA_INSCRIPCION';

    DELETE FROM tramites.procedure_steps
     WHERE procedure_type_id IN (
         SELECT id FROM tramites.procedure_types
          WHERE code IN ('PRENDA_INSCRIPCION', 'CAMBIO_LOCATARIO'));

    -- Mismo par (título que lee el operador, código que usa el motor) que el DDL 82: el paso se
    -- TITULA «Propietario» y su sección se CODIFICA COMPRADOR, que es el ActorType con el que se
    -- persiste el titular porque el modelo no tiene rol 'propietario'.
    CREATE TEMP TABLE _recorrido_otros(
        type_code text, step_order smallint, step_code text, step_title text,
        sec_code text, sec_type text) ON COMMIT DROP;

    INSERT INTO _recorrido_otros VALUES
        ('PRENDA_INSCRIPCION', 1, 'consulta',    'Consulta del vehículo', 'VEHICULO',  'vehicle_query'),
        ('PRENDA_INSCRIPCION', 2, 'propietario', 'Propietario',           'COMPRADOR', 'actor_form'),
        ('PRENDA_INSCRIPCION', 3, 'documentos',  'Documentos',            'CHECKLIST', 'document_checklist'),
        ('PRENDA_INSCRIPCION', 4, 'prenda',      'Decisión de prenda',    'PRENDA',    'prenda_decision'),
        ('PRENDA_INSCRIPCION', 5, 'identidad',   'Identidad',             'BIOMETRIA', 'biometric'),
        ('PRENDA_INSCRIPCION', 6, 'fur',         'Resumen del trámite',   'FUR',       'signature_fur'),

        ('CAMBIO_LOCATARIO',   1, 'consulta',    'Consulta del vehículo', 'VEHICULO',  'vehicle_query'),
        ('CAMBIO_LOCATARIO',   2, 'propietario', 'Propietario',           'COMPRADOR', 'actor_form'),
        ('CAMBIO_LOCATARIO',   3, 'documentos',  'Documentos',            'CHECKLIST', 'document_checklist'),
        ('CAMBIO_LOCATARIO',   4, 'identidad',   'Identidad',             'BIOMETRIA', 'biometric'),
        ('CAMBIO_LOCATARIO',   5, 'fur',         'Resumen del trámite',   'FUR',       'signature_fur');

    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active)
    SELECT uuidv7(), pt.id, r.step_code, r.step_title, r.step_order, true
      FROM _recorrido_otros r
      JOIN tramites.procedure_types pt ON pt.code = r.type_code;

    INSERT INTO tramites.procedure_sections
        (id, procedure_step_id, code, title, sort_order, layout, section_type)
    SELECT uuidv7(), st.id, r.sec_code, r.step_title, 1, 'single', r.sec_type
      FROM _recorrido_otros r
      JOIN tramites.procedure_types pt ON pt.code = r.type_code
      JOIN tramites.procedure_steps st
        ON st.procedure_type_id = pt.id AND st.code = r.step_code;

    DROP TABLE _recorrido_otros;
END $$;

-- Guarda final: todo tipo de OTROS con paso de actores debe pedir un titular. Sin `requiresBuyer`
-- el asistente pinta el paso y no exige a nadie, y el trámite llega al FUR sin propietario — que es
-- lo que hacían estos dos y no se veía hasta abrir el expediente generado.
DO $$
DECLARE
    sin_titular text;
BEGIN
    SELECT string_agg(DISTINCT pt.code, ', ') INTO sin_titular
    FROM tramites.procedure_types pt
    JOIN tramites.procedure_steps ps ON ps.procedure_type_id = pt.id AND ps.is_active
    JOIN tramites.procedure_sections sec ON sec.procedure_step_id = ps.id
    WHERE pt.family = 'OTROS'
      AND sec.section_type = 'actor_form'
      AND coalesce((pt.gate_profile ->> 'requiresBuyer')::boolean, false) = false
      AND coalesce((pt.gate_profile ->> 'requiresSeller')::boolean, false) = false;

    IF sin_titular IS NOT NULL THEN
        RAISE EXCEPTION
            'Tipos de OTROS con paso de actores y sin parte declarada en gate_profile: %.',
            sin_titular;
    END IF;
END $$;
