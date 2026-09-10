-- =============================================================================
-- Quipux — external_refs de los tipos que en FLIT 1.0 eran variantes (ADR-0050).
--
-- El seed 33 parametrizó solo MATRICULA_NUEVA y TRASPASO_STANDARD, cada uno con
-- una `variante` condicionada a un field value: `es_leasing` (MI → MIL) y
-- `es_unilateral` (16/TR → 213/TRU). Eso era correcto cuando el catálogo tenía
-- dos tipos: el leasing y el unilateral no EXISTÍAN como tipo, eran una casilla
-- dentro del trámite canónico.
--
-- Ahora MATRICULA_LEASING y TRASPASO_UNILATERAL son tipos propios del catálogo,
-- y el gestor los elige en el selector de familia → tipo. Sin bloque `quipux`
-- propio, `QuipuxTipoTramiteMap.Parse` devuelve null y esos trámites quedan
-- simplemente sin radicar — el fallo seguro, pero silencioso desde el punto de
-- vista del gestor, que ya no marca ninguna casilla.
--
-- Los códigos NO se inventan: son los mismos que la variante ya declaraba (13/MIL
-- y 213/TRU), documentados contra FLIT 1.0 en el seed 33. Se conserva además la
-- variante en los tipos canónicos: un trámite creado antes de que existieran
-- estos tipos sigue resolviéndose igual.
--
-- LO QUE ESTE SCRIPT NO HACE, a propósito: los otros 17 tipos del catálogo
-- (blindaje, cambio de color, levantamiento de prenda...) no reciben bloque
-- `quipux` porque sus códigos de trámite en la secretaría no están documentados
-- en el repo y no son derivables de nada existente. Radicar con un código
-- inventado deja el trámite mal presentado en la secretaría, que es más caro que
-- no presentarlo. Quedan no elegibles hasta que negocio aporte los códigos.
--
-- Idempotente: el WHERE compara contra el bloque exacto que se va a escribir.
-- =============================================================================

-- Matrícula leasing — tipoTramite 13 (igual que la matrícula inicial), prefijo MIL.
-- Sin `variante`: el tipo YA ES el caso leasing, no hay casilla que consultar.
UPDATE tramites.procedure_types pt
SET external_refs = pt.external_refs || jsonb_build_object(
    'quipux', jsonb_build_object(
        'familia',            'MATRICULA',
        'tipoTramite',        13,
        'tipoRequisito',      51,
        'prefijo',            'MIL',
        'campoPlaca',         NULL,
        'campoVin',           'vin',
        'maxLongitudEmpresa', 25
    )
),
    updated_at = now()
WHERE pt.code = 'MATRICULA_LEASING'
  AND pt.external_refs -> 'quipux' IS DISTINCT FROM jsonb_build_object(
        'familia',            'MATRICULA',
        'tipoTramite',        13,
        'tipoRequisito',      51,
        'prefijo',            'MIL',
        'campoPlaca',         NULL,
        'campoVin',           'vin',
        'maxLongitudEmpresa', 25
      );

-- Traspaso unilateral — tipoTramite 213, prefijo TRU.
UPDATE tramites.procedure_types pt
SET external_refs = pt.external_refs || jsonb_build_object(
    'quipux', jsonb_build_object(
        'familia',            'TRASPASO',
        'tipoTramite',        213,
        'tipoRequisito',      51,
        'prefijo',            'TRU',
        'campoPlaca',         'plate',
        'campoVin',           NULL,
        'maxLongitudEmpresa', 35
    )
),
    updated_at = now()
WHERE pt.code = 'TRASPASO_UNILATERAL'
  AND pt.external_refs -> 'quipux' IS DISTINCT FROM jsonb_build_object(
        'familia',            'TRASPASO',
        'tipoTramite',        213,
        'tipoRequisito',      51,
        'prefijo',            'TRU',
        'campoPlaca',         'plate',
        'campoVin',           NULL,
        'maxLongitudEmpresa', 35
      );

-- Guarda de consistencia: la familia Quipux es un vocabulario ajeno (MATRICULA /
-- TRASPASO / OTROS de la secretaría), pero no puede contradecir la familia FLIT
-- del tipo — un traspaso radicado bajo la bandera de matrículas iría a la
-- secretaría equivocada. MATRICULAS ↔ MATRICULA y TRASPASO ↔ TRASPASO; OTROS de
-- FLIT admite cualquiera de las tres porque ahí caen trámites que la secretaría
-- puede clasificar distinto.
DO $$
DECLARE
    incoherentes text;
BEGIN
    SELECT string_agg(code, ', ') INTO incoherentes
    FROM tramites.procedure_types
    WHERE external_refs -> 'quipux' IS NOT NULL
      AND (
            (family = 'MATRICULAS' AND external_refs -> 'quipux' ->> 'familia' <> 'MATRICULA')
         OR (family = 'TRASPASO'   AND external_refs -> 'quipux' ->> 'familia' <> 'TRASPASO')
      );

    IF incoherentes IS NOT NULL THEN
        RAISE EXCEPTION 'Familia Quipux incoherente con procedure_types.family en: %', incoherentes;
    END IF;
END $$;
