-- =============================================================================
-- Traslado de cuenta: declara el organismo de DESTINO, pero lo expide el de ORIGEN.
-- Migración: 20260825190000_TrasladoCuentaSecretariaDestino
--
-- Traslado y radicado son los dos tiempos del mismo movimiento, y son ESPEJO uno del otro:
--
--   1. Traslado  — el propietario pide mover la matrícula. El organismo de ORIGEN valida paz y salvo
--                  y EXPIDE el traslado. Quedan 60 días hábiles para radicar en el nuevo.
--   2. Radicado  — el vehículo se presenta en el organismo NUEVO, que lo registra definitivamente.
--
-- Por eso el traslado NO lleva `transitOfficeSource: OPERATOR`: su organismo —el que aprueba, el que
-- lo ve en su bandeja y el que imprime el encabezado del FUR— sigue siendo el que reporta el RUNT.
-- Lo que sí necesita es DECLARAR a dónde va la cuenta, y para eso está
-- `requiresDestinationTransitOffice`: el destino se guarda en `transit_office_destino_*` y solo
-- alimenta el párrafo 23 del formulario.
--
-- El radicado hace lo contrario y ya está resuelto en el DDL 92: allí el destino ES el organismo del
-- trámite, así que va en las claves canónicas y estas no se usan.
--
-- PENDIENTE DELIBERADO — el vínculo entre los dos trámites NO está implementado (decisión de negocio,
-- 2026-08-25). El flujo real encadena traslado → «pendiente de radicación» → radicado, con 60 días
-- HÁBILES contados desde que el organismo de origen expide el traslado. Aquí los dos trámites son
-- independientes: nada exige un traslado previo para radicar, ni vigila el vencimiento.
--
-- No es un olvido: no hay con qué validarlo. Los proveedores de consulta NO reportan traslados ni
-- radicaciones pendientes (verificado sobre Kyverum, Verifik e Intempo), así que FLIT solo conocería
-- los traslados expedidos DENTRO de FLIT. Un vehículo con traslado hecho en otra plataforma llegaría
-- a radicarse y el sistema afirmaría que no tiene traslado pendiente: bloquear con esa información
-- parcial rechazaría trámites legítimos. Además, 60 días hábiles exige el calendario de festivos de
-- Colombia, que el sistema tampoco tiene (≈ un mes de diferencia frente a 60 corridos).
--
-- Cuando exista la fuente: avisar antes que bloquear, mientras FLIT no sea la única vía posible.
--
-- Idempotente y reaplicable.
-- =============================================================================

UPDATE tramites.procedure_types
   SET gate_profile = coalesce(gate_profile, '{}'::jsonb)
                      || '{"requiresDestinationTransitOffice": true}'::jsonb,
       updated_at = now()
 WHERE code = 'TRASLADO_CUENTA'
   AND gate_profile -> 'requiresDestinationTransitOffice' IS DISTINCT FROM 'true'::jsonb;

-- Guardas: los dos trámites de cuenta tienen que quedar con la configuración OPUESTA. Cruzarlas
-- mandaría el traslado a la bandeja del organismo equivocado —el que aún no tiene el vehículo— o
-- dejaría el radicado pidiendo un destino que ya es su propio organismo.
DO $$
DECLARE
    mal int;
BEGIN
    SELECT count(*) INTO mal
      FROM tramites.procedure_types
     WHERE code = 'TRASLADO_CUENTA'
       AND gate_profile -> 'transitOfficeSource' = '"OPERATOR"'::jsonb;

    IF mal > 0 THEN
        RAISE EXCEPTION 'TRASLADO_CUENTA no puede elegir su organismo: lo expide el de origen';
    END IF;

    SELECT count(*) INTO mal
      FROM tramites.procedure_types
     WHERE code = 'RADICADO_CUENTA'
       AND gate_profile -> 'requiresDestinationTransitOffice' = 'true'::jsonb;

    IF mal > 0 THEN
        RAISE EXCEPTION 'RADICADO_CUENTA no declara destino aparte: su destino ES el organismo del trámite';
    END IF;

    SELECT count(*) INTO mal
      FROM tramites.procedure_types
     WHERE gate_profile -> 'requiresDestinationTransitOffice' = 'true'::jsonb
       AND code <> 'TRASLADO_CUENTA';

    IF mal > 0 THEN
        RAISE EXCEPTION '% tipo(s) quedaron pidiendo organismo de destino sin necesitarlo', mal;
    END IF;
END $$;
