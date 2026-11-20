-- =============================================================================
-- Bug #14625 (sigue a HU #13264) — el comprobante de FLITO marca el impuesto en cualquier estado salvo anulado/revocado.
-- Migración: 20261119140000_BUG14625_ImpuestoPagadoFlitoCualquierEstado.
--
-- POST /api/v1/external/tramites/{id}/adjuntos acepta el comprobante en todo estado salvo anulado y revocado, y en la
-- misma transacción escribe procedure_instance_field_values.impuesto_departamental_pagado = 'true' con source = 'flito'.
-- Hasta el DDL 130 el trigger rechazaba (check_violation) esa escritura en preparado, entregado, aprobado y rechazado
-- sin subsanación, y con eso revertía también el adjunto. Este DDL abre SOLO esa llave, SOLO con source = 'flito' y SOLO
-- INSERT/UPDATE, en todo estado salvo anulado y revocado. El gestor no gana ninguna edición nueva. El resto de la
-- función es idéntico al DDL 130.
--
-- Idempotente y reaplicable (CREATE OR REPLACE; el trigger existente ya apunta a esta función).
-- =============================================================================

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
  -- Bug #14625 — el comprobante de FLITO marca el impuesto como pagado en todo estado que admite el envío
  -- (todos salvo anulado y revocado). Solo esa llave, solo con source = 'flito' y solo INSERT/UPDATE.
  IF TG_OP IN ('INSERT', 'UPDATE')
     AND NEW.field_key = 'impuesto_departamental_pagado'
     AND NEW.source = 'flito'
     AND v_status NOT IN ('anulado', 'revocado') THEN
    RETURN NEW;
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
