-- Bug #13194 (P3-26) — en 'asignado' el soporte del SOAT necesita registrar su fecha de vencimiento.
--
-- Con la compañía exigiendo SOAT vigente y un RUNT que no lo reporta, «Enviar al OT» pide cargar el PDF del
-- SOAT. El adjunto ya se podía subir en 'asignado' (AttachmentRules.AllowsUploadInState), pero el soporte
-- manual solo cuenta con soat_estado=vigente de origen OCR + una soat_vencimiento legible no vencida, y este
-- trigger rechazaba soat_vencimiento en ese estado: el gestor quedaba sin salida.
--
-- Cambio mínimo: 'soat_vencimiento' entra a la allowlist de 'asignado'. Ninguna otra llave. El único escritor
-- en ese estado es el OCR del SOAT (PersistOcrFieldsHandler), que exige un adjunto de SOAT no histórico; el
-- PATCH de soat_estado/soat_vencimiento sigue rechazado en la aplicación (clave_de_sistema).
-- Resto de la función: idéntico a 116-HU12599-plate-flow-a-status.sql.
--
-- Idempotente y reaplicable (CREATE OR REPLACE; el trigger existente ya apunta a esta función).

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
  -- Bug #13194 — 'soat_vencimiento': fecha del PDF del SOAT leído por OCR (soporte manual del gate).
  IF v_status = 'asignado' AND v_key IN ('plate', 'soat_estado', 'soat_vencimiento', 'soat_pagado', 'impuesto_departamental_pagado') THEN
    RETURN COALESCE(NEW, OLD);
  END IF;
  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
    USING ERRCODE = 'check_violation';
END; $$ LANGUAGE plpgsql;
