-- HU #12599 (Feature #12595, ADR-0059) — el sub-estado de placa se PROMUEVE a estados de negocio.
--
-- Hasta aquí la ruta de placa vivía en `plate_flow_status` con el `status` clavado en 'entregado'
-- (HU #10785 hizo justo el movimiento contrario en 20260716154530). Desde ADR-0059 los estados son
-- 'preasignacion' y 'asignado' en `status`, y 'terminado' desaparece: ES 'entregado'.
--
-- Mapeo (idempotente: solo toca filas que todavía tienen sub-estado con status 'entregado'):
--   entregado + preasignado → preasignacion   (+ fila sintética de historial)
--   entregado + asignado    → asignado        (+ fila sintética de historial)
--   entregado + terminado   → entregado       (sin historial: el estado no cambia)
--   entregado + NULL        → entregado       (sin cambio)
-- La columna `plate_flow_status` se deja en NULL y NO se borra aquí (HU #12603 la retira junto con el
-- resto de la limpieza); nadie la lee ni la escribe en runtime desde HU #12597/#12598.
--
-- Trigger de autoset: decidía el sub-estado «a espaldas» del ciclo de vida al entrar a 'entregado'.
-- Con el modelo nuevo escribiría basura en una columna muerta, así que se retira aquí (función y
-- trigger). El trigger de inmutabilidad de field_values pasa a decidir por `status`.
--
-- Backup lógico previo recomendado (Infra):
--   CREATE TABLE tramites.bk_hu12599_plate_flow AS
--     SELECT id, status, plate_flow_status FROM tramites.procedure_instances WHERE plate_flow_status IS NOT NULL;

SET LOCAL row_security = off;

-- 1) Autoset: fuera antes de mover estados (no volvería a dispararse con los destinos nuevos, pero
--    dejarlo vivo sería dejar un escritor de una columna que ya no significa nada).
DROP TRIGGER IF EXISTS tr_procedure_instances_autoset_plate_flow ON tramites.procedure_instances;
DROP FUNCTION IF EXISTS tramites.trg_autoset_plate_flow_status();

-- 2) Historial sintético ANTES de mover el status (así from/to reflejan el movimiento real y la
--    guarda de idempotencia mira el estado previo). Marcado en metadata para poder distinguirlo.
INSERT INTO tramites.procedure_instance_status_history
    (tenant_id, procedure_instance_id, from_status, to_status, changed_at, changed_by, reason, metadata)
SELECT p.tenant_id,
       p.id,
       'entregado',
       CASE p.plate_flow_status WHEN 'preasignado' THEN 'preasignacion' ELSE 'asignado' END,
       now(),
       NULL,
       'Migración ADR-0059: el sub-estado de placa pasa a ser el estado del trámite.',
       jsonb_build_object('motivo', 'migracion_plate_flow_a_status', 'plate_flow_status', p.plate_flow_status)
  FROM tramites.procedure_instances p
 WHERE p.status = 'entregado'
   AND p.plate_flow_status IN ('preasignado', 'asignado')
   AND NOT EXISTS (
        SELECT 1 FROM tramites.procedure_instance_status_history h
         WHERE h.procedure_instance_id = p.id
           AND h.metadata ->> 'motivo' = 'migracion_plate_flow_a_status');

-- 3) Mover el estado y vaciar el sub-estado en un solo UPDATE (las expresiones SET leen la fila previa).
UPDATE tramites.procedure_instances
   SET status = CASE plate_flow_status
                  WHEN 'preasignado' THEN 'preasignacion'
                  WHEN 'asignado'    THEN 'asignado'
                  ELSE status            -- terminado: sigue en entregado
                END,
       plate_flow_status = NULL
 WHERE status = 'entregado'
   AND plate_flow_status IS NOT NULL;

-- 4) Inmutabilidad de field_values por `status` (misma regla que HU #12167, sin sub-estado):
--    · preasignacion: solo 'plate' (asignación inicial por el OT).
--    · asignado: 'plate' (corrección 1 h, HU #12167) y los checks del gestor (soat_estado,
--      soat_pagado, impuesto_departamental_pagado) antes de «Enviar al OT».
--    · entregado y demás: inmutable (borrador y rechazado+subsanación siguen editables).
CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
DECLARE v_status varchar(20);
DECLARE v_key varchar(80);
BEGIN
  SELECT status INTO v_status FROM tramites.procedure_instances
    WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
  IF v_status IS NULL THEN
    RETURN OLD;
  END IF;
  IF v_status = 'borrador' THEN
    RETURN COALESCE(NEW, OLD);
  END IF;
  -- Subsanación activa sobre rechazado: edición de datos permitida.
  IF v_status = 'rechazado' THEN
    IF EXISTS (
      SELECT 1 FROM tramites.procedure_instances
       WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id)
         AND subsanacion_activa IS TRUE
    ) THEN
      RETURN COALESCE(NEW, OLD);
    END IF;
  END IF;
  v_key := COALESCE(NEW.field_key, OLD.field_key);
  -- ADR-0059 — la ruta de placa vive en status.
  IF v_status = 'preasignacion' AND v_key = 'plate' THEN
    RETURN COALESCE(NEW, OLD);
  END IF;
  IF v_status = 'asignado' AND v_key IN ('plate', 'soat_estado', 'soat_pagado', 'impuesto_departamental_pagado') THEN
    RETURN COALESCE(NEW, OLD);
  END IF;
  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
    USING ERRCODE = 'check_violation';
END; $$ LANGUAGE plpgsql;
