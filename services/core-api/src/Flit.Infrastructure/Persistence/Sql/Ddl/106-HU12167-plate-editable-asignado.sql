-- HU #12167 (Feature #12156) — corregir la placa por el OT dentro de la ventana de 1 hora exige
-- escribir field_values(field_key='plate') con la instancia YA en sub-estado 'asignado' (la placa
-- original ya se escribió; esto es una CORRECCIÓN posterior, no la asignación inicial). El trigger
-- tramites.trg_field_value_immutable (última versión: 20260729140000_PlateFlowTerminado) solo
-- permitía 'plate' en 'preasignado' — UpdatePlateAsync chocaba con
-- «procedure_instance_field_values son inmutables...» (verificado contra la BD real de dev; los
-- tests de este repo corren sobre EF InMemory y no ejecutan triggers de Postgres, por eso no lo
-- habían detectado). Se agrega 'plate' a la lista ya permitida en 'asignado'.

CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
DECLARE v_status varchar(20);
DECLARE v_plate varchar(20);
DECLARE v_key varchar(80);
BEGIN
  SELECT status, plate_flow_status INTO v_status, v_plate FROM tramites.procedure_instances
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
  IF v_plate = 'preasignado' AND v_key = 'plate' THEN
    RETURN COALESCE(NEW, OLD);
  END IF;
  -- HU #12167 — 'plate' se agrega aquí: la corrección dentro de la ventana de 1 hora escribe el
  -- field_value con el sub-estado ya en 'asignado' (a diferencia de la asignación inicial, que
  -- ocurre en 'preasignado' y ya estaba cubierta arriba).
  IF v_plate = 'asignado' AND v_key IN ('plate', 'soat_estado', 'soat_pagado', 'impuesto_departamental_pagado') THEN
    RETURN COALESCE(NEW, OLD);
  END IF;
  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
    USING ERRCODE = 'check_violation';
END; $$ LANGUAGE plpgsql;
