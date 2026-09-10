-- =============================================================================
-- Radicado de cuenta: el organismo lo ELIGE el operador (destino), no lo impone el RUNT.
-- Migración: 20260825180000_RadicadoCuentaSecretariaDestino
--
-- En casi todos los trámites el organismo al que va el expediente coincide con el que reporta el
-- RUNT, porque se radica donde el vehículo está matriculado. Un radicado de cuenta existe
-- precisamente para llevar la cuenta a OTRO organismo: el destino es quien aprueba —y por tanto el
-- que gobierna el grant de la compañía y la bandeja— mientras que el del RUNT pasa a ser un dato
-- descriptivo que el FUR imprime en su encabezado.
--
-- Hasta ahora esa decisión se deducía del modo de entrada (VIN ⇒ lo elige el operador; placa ⇒ lo
-- impone el RUNT) y funcionaba porque las dos ramas agotaban el catálogo. El radicado la rompe:
-- entra por PLACA y aun así lo elige el operador. Por eso se declara, en vez de deducirse.
--
-- La llave AUSENTE no es 'RUNT': significa «lo que diga el modo de entrada», así que los veinte
-- tipos restantes y los snapshots ya congelados se comportan exactamente igual que antes.
--
-- Idempotente y reaplicable.
-- =============================================================================

-- ============================================================================
-- 1. Declaración de la capacidad
-- ============================================================================
UPDATE tramites.procedure_types
   SET gate_profile = coalesce(gate_profile, '{}'::jsonb)
                      || '{"transitOfficeSource": "OPERATOR"}'::jsonb,
       updated_at = now()
 WHERE code = 'RADICADO_CUENTA'
   AND gate_profile -> 'transitOfficeSource' IS DISTINCT FROM '"OPERATOR"'::jsonb;

-- Guarda: nadie más debe quedar marcado. Un tipo que impone el RUNT y quedara como OPERATOR pediría
-- una secretaría que el gestor no tiene por qué elegir, y dejaría el organismo real sin escribir.
DO $$
DECLARE
    de_mas int;
BEGIN
    SELECT count(*) INTO de_mas
      FROM tramites.procedure_types
     WHERE gate_profile -> 'transitOfficeSource' = '"OPERATOR"'::jsonb
       AND code <> 'RADICADO_CUENTA';

    IF de_mas > 0 THEN
        RAISE EXCEPTION '% tipo(s) quedaron marcados con transitOfficeSource=OPERATOR sin serlo', de_mas;
    END IF;
END $$;

-- ============================================================================
-- 2. Convivencia: borradores de radicado abiertos antes del cambio
-- ============================================================================
-- Sus `transit_office_*` los escribió el auto-bind con el organismo del RUNT. Bajo el significado
-- nuevo esas claves son el DESTINO, así que tal cual quedarían diciendo que la cuenta se radica en
-- el mismo organismo donde ya está — un radicado que no radica a ninguna parte.
--
-- Se copian a las claves descriptivas (que es lo que ese valor siempre significó de verdad) y se
-- retiran de las canónicas, para que el gestor elija el destino. Solo se tocan expedientes que
-- admiten edición: uno ya radicado conserva lo que se imprimió en su FUR.
INSERT INTO tramites.procedure_instance_field_values
    (id, tenant_id, procedure_instance_id, form_field_id, field_key, value_text, source, created_at)
SELECT uuidv7(), fv.tenant_id, fv.procedure_instance_id, NULL,
       replace(fv.field_key, 'transit_office_', 'transit_office_actual_'),
       fv.value_text, 'consultation', now()
  FROM tramites.procedure_instance_field_values fv
  JOIN tramites.procedure_instances pi ON pi.id = fv.procedure_instance_id
  JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
 WHERE pt.code = 'RADICADO_CUENTA'
   AND pi.status IN ('borrador', 'rechazado')
   AND fv.field_key IN ('transit_office_id', 'transit_office_code', 'transit_office_name', 'transit_office_city')
   AND NOT EXISTS (
       SELECT 1 FROM tramites.procedure_instance_field_values otro
        WHERE otro.procedure_instance_id = fv.procedure_instance_id
          AND otro.field_key = replace(fv.field_key, 'transit_office_', 'transit_office_actual_'));

DELETE FROM tramites.procedure_instance_field_values fv
 USING tramites.procedure_instances pi, tramites.procedure_types pt
 WHERE fv.procedure_instance_id = pi.id
   AND pi.procedure_type_id = pt.id
   AND pt.code = 'RADICADO_CUENTA'
   AND pi.status IN ('borrador', 'rechazado')
   AND fv.field_key IN ('transit_office_id', 'transit_office_code', 'transit_office_name', 'transit_office_city');

-- La columna promovida sigue apuntando al organismo viejo: se limpia por la misma razón.
UPDATE tramites.procedure_instances pi
   SET transit_office_id = NULL,
       updated_at = now()
  FROM tramites.procedure_types pt
 WHERE pi.procedure_type_id = pt.id
   AND pt.code = 'RADICADO_CUENTA'
   AND pi.status IN ('borrador', 'rechazado')
   AND pi.transit_office_id IS NOT NULL;
