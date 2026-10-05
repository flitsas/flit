-- HU #13264 (Feature #13261, Épica #12741) — «Impuesto pagado marcado por FLITO».
--
-- El endpoint externo de adjuntos escribe procedure_instance_field_values.impuesto_departamental_pagado=true
-- (source='flito') en la misma transacción que archiva el comprobante. Estados editables del endpoint:
--   * preasignacion  -> ESTE DDL abre SOLO esa llave (antes solo 'plate').
--   * asignado       -> ya la admitía la allowlist (DDL 129 y anteriores).
--   * rechazado + subsanacion_activa -> edición completa por el cortocircuito de la función.
--   * entregado      -> sigue lanzando check_violation (el adjunto se archiva sin tocar la marca).
-- Ninguna otra llave ni otro estado se abre. Resto de la función: idéntico a 129-BUG13194.
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
  -- HU #13264 — FLITO marca el impuesto departamental como pagado al adjuntar su comprobante
  -- (POST /api/v1/external/tramites/{id}/adjuntos). Solo esa llave y solo en preasignación; en 'asignado'
  -- ya estaba en la allowlist y en 'rechazado' con subsanación la edición ya es completa.
  IF v_status = 'preasignacion' AND v_key = 'impuesto_departamental_pagado' THEN
    RETURN COALESCE(NEW, OLD);
  END IF;
  -- Bug #13194 — 'soat_vencimiento': fecha del PDF del SOAT leído por OCR (soporte manual del gate).
  IF v_status = 'asignado' AND v_key IN ('plate', 'soat_estado', 'soat_vencimiento', 'soat_pagado', 'impuesto_departamental_pagado') THEN
    RETURN COALESCE(NEW, OLD);
  END IF;
  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
    USING ERRCODE = 'check_violation';
END; $$ LANGUAGE plpgsql;
