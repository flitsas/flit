-- ADR-0050 (parte 1 de 2) — barrera de operación, dominio de familia y trigger por gate_profile.
-- Migración: 20260822090000_TipoTramiteBarreraYFamilia
--
-- ADITIVA Y COMPATIBLE HACIA ATRÁS: no borra datos ni columnas. Puede aplicarse con el código
-- actual, que todavía lee modalidad_entrada. El corte destructivo (borrado de expedientes, DROP de
-- modalidad_entrada/tipologia_codigo y migración de causales a familia) vive en
-- 80-tramites-reset-fuente-unica.sql y solo se activa cuando el backend deja de usar esas columnas.
--
-- El trigger de flujo de placa sí se reescribe aquí: pasa a decidir por gate_profile en vez de por
-- modalidad_entrada, y el resultado es equivalente porque MATRICULA_NUEVA declara
-- requiresPlateRequest = true. Deja de depender de la columna antes de que esta desaparezca.

-- 3. procedure_types — barrera de operación + dominio cerrado de familia
-- ============================================================================
ALTER TABLE tramites.procedure_types
    ADD COLUMN IF NOT EXISTS wizard_enabled boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN tramites.procedure_types.wizard_enabled IS
'Barrera de operación (ADR-0050): el tipo puede elegirse al crear un trámite. Independiente de publication_status, que solo gobierna visibilidad en administración. Se enciende cuando el tipo tiene pasos/secciones parametrizados, matriz documental, causales y homologación Quipux/ICT si aplica.';

-- Normalización defensiva antes del CHECK: el catálogo nunca tuvo restricción de dominio y está
-- solapado por seeds históricos. La migración SeedProcedureTypes sembró family = 'VEHICULAR', y
-- 33-HU10710 documenta que qué códigos existen depende del ambiente. Sin este remapeo el CHECK
-- reventaría el arranque en cualquier base que aún arrastre esas filas.
UPDATE tramites.procedure_types
   SET family = upper(btrim(family))
 WHERE family IS DISTINCT FROM upper(btrim(family));

UPDATE tramites.procedure_types
   SET family = 'OTROS'
 WHERE family NOT IN ('MATRICULAS', 'TRASPASO', 'OTROS');

ALTER TABLE tramites.procedure_types
    DROP CONSTRAINT IF EXISTS ck_procedure_types_family;
ALTER TABLE tramites.procedure_types
    ADD CONSTRAINT ck_procedure_types_family
        CHECK (family IN ('MATRICULAS', 'TRASPASO', 'OTROS'));

CREATE INDEX IF NOT EXISTS ix_procedure_types_family_wizard_enabled
    ON tramites.procedure_types(family, wizard_enabled)
    WHERE wizard_enabled;

-- ============================================================================
-- 4. Trigger de flujo de placa — por gate_profile, no por modalidad
-- ============================================================================
-- Antes: NEW.modalidad_entrada = 'matricula_inicial'.
-- Ahora: el tipo declara requiresPlateRequest (CFD-08). Es más preciso que la familia —
-- CANCELACION_MATRICULA es MATRICULAS y no asigna placa. Se lee del snapshot congelado
-- (ADR-0050) y se cae al catálogo vivo si el expediente no tuviera snapshot.
CREATE OR REPLACE FUNCTION tramites.trg_autoset_plate_flow_status() RETURNS trigger AS $$
DECLARE
  has_plate boolean;
  skip_gestor boolean;
  requires_plate boolean;
BEGIN
  IF NEW.status = 'entregado'
     AND OLD.status IS DISTINCT FROM 'entregado'
     AND NEW.plate_flow_status IS NULL
  THEN
    SELECT COALESCE(
             (SELECT (s.snapshot -> 'gateProfile' ->> 'requiresPlateRequest')::boolean
                FROM tramites.procedure_type_snapshots s
               WHERE s.procedure_instance_id = NEW.id),
             (SELECT (pt.gate_profile ->> 'requiresPlateRequest')::boolean
                FROM tramites.procedure_types pt
               WHERE pt.id = NEW.procedure_type_id),
             false)
      INTO requires_plate;

    IF requires_plate THEN
      has_plate := EXISTS (
        SELECT 1 FROM tramites.procedure_instance_field_values f
         WHERE f.procedure_instance_id = NEW.id
           AND f.field_key = 'plate'
           AND COALESCE(btrim(f.value_text), '') <> '');

      IF has_plate THEN
        SELECT COALESCE(p.plate_flow_skip_to_terminado, false)
          INTO skip_gestor
          FROM admin.tenant_operational_policies p
         WHERE p.tenant_id = NEW.tenant_id;

        NEW.plate_flow_status := CASE WHEN skip_gestor THEN 'terminado' ELSE 'asignado' END;
      ELSIF EXISTS (
            SELECT 1 FROM tramites.procedure_instance_field_values f
             WHERE f.procedure_instance_id = NEW.id
               AND f.field_key = 'plate_route_active'
               AND lower(btrim(f.value_text)) = 'true')
      THEN
        NEW.plate_flow_status := 'preasignado';
      END IF;
    END IF;
  END IF;
  RETURN NEW;
END; $$ LANGUAGE plpgsql;
