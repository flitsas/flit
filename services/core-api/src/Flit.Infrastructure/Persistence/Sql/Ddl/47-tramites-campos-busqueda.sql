-- Denormalización de campos de búsqueda del listado de trámites (filtrado/ordenamiento server-side).

-- CONTEXTO: vin y placa viven en tramites.procedure_instance_field_values (field_key 'vin'/'plate');
-- el nombre del vendedor/propietario saliente y del comprador viven en
-- tramites.procedure_instance_actors (actor_type 'vendedor'/'comprador'). Con paginación real
-- (LIMIT/OFFSET) el listado ya no puede filtrar ni ordenar en memoria: necesita WHERE/ORDER BY directo
-- sobre tramites.procedure_instances.
ALTER TABLE tramites.procedure_instances
  ADD COLUMN IF NOT EXISTS vin varchar(20),
  ADD COLUMN IF NOT EXISTS plate varchar(20),
  ADD COLUMN IF NOT EXISTS vendedor_nombre varchar(200),
  ADD COLUMN IF NOT EXISTS comprador_nombre varchar(200);

-- vin/plate en inglés (igual que los field_key 'vin'/'plate' de los que se copian, y que las columnas
-- técnicas ya existentes de la tabla); vendedor_nombre/comprador_nombre en español (igual que el resto
-- del vocabulario de negocio de esta tabla: subsanacion_activa, prioritario, modalidad_entrada). Es el
-- mismo criterio ya usado en el resto de procedure_instances (columnas técnicas en inglés, conceptos
-- del dominio de trámites en español) — no una mezcla nueva.
COMMENT ON COLUMN tramites.procedure_instances.vin IS
  'Denormalizado de procedure_instance_field_values (field_key=vin) vía trigger tr_procedure_instance_field_values_denorm, para filtrar/ordenar el listado sin cargar el grafo completo. La fuente de verdad sigue siendo field_values; esta columna es SOLO LECTURA para el aplicativo. @pii:low (identifica el vehículo, no a una persona).';
COMMENT ON COLUMN tramites.procedure_instances.plate IS
  'Denormalizado de procedure_instance_field_values (field_key=plate) vía el mismo trigger que vin. Mismo propósito y misma advertencia de solo-lectura. @pii:low.';
COMMENT ON COLUMN tramites.procedure_instances.vendedor_nombre IS
  'Denormalizado de procedure_instance_actors.full_name (actor_type=vendedor) vía trigger tr_procedure_instance_actors_denorm. Null en matrícula inicial (no hay vendedor) o si aún no se ha registrado el actor. Solo lectura para el aplicativo. @pii:medium';
COMMENT ON COLUMN tramites.procedure_instances.comprador_nombre IS
  'Denormalizado de procedure_instance_actors.full_name (actor_type=comprador) vía el mismo trigger que vendedor_nombre. Solo lectura para el aplicativo. @pii:medium';

-- Índices para los ordenamientos/filtros pedidos (tenant_id primero, checklist A11). btree normal:
-- sirve tanto ASC como DESC (Postgres recorre el índice en reversa sin costo extra) y para el filtro de
-- igualdad de vin/placa.
CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_id_vin
  ON tramites.procedure_instances(tenant_id, vin);
CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_id_plate
  ON tramites.procedure_instances(tenant_id, plate);
CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_id_comprador_nombre
  ON tramites.procedure_instances(tenant_id, comprador_nombre);
-- No se pide ordenar por vendedor, pero sí filtrar: mismo índice sirve para el filtro exacto/prefijo;
-- una búsqueda por SUBCADENA (ILIKE '%term%'/Contains) no puede usarlo (necesitaría un índice de
-- trigramas pg_trgm), lo cual queda fuera de alcance de esta migración — no lo pide la HU y el volumen
-- actual de trámites por tenant no lo justifica todavía.
CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_id_vendedor_nombre
  ON tramites.procedure_instances(tenant_id, vendedor_nombre);
CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_id_created_at
  ON tramites.procedure_instances(tenant_id, created_at);
CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_id_updated_at
  ON tramites.procedure_instances(tenant_id, updated_at);
-- "Gestor" (created_by_user_id → identity.users.display_name) NO se denormaliza: ya es una columna con
-- FK real, así que basta un índice + JOIN — no vive en una tabla hija. Aprovechamos la migración para
-- cerrar una deuda preexistente (checklist A9: FK sin índice) porque el filtro/orden por gestor hace
-- JOIN exactamente por esta columna.
CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_id_created_by_user_id
  ON tramites.procedure_instances(tenant_id, created_by_user_id);

-- Trigger de sincronización vin/plate. Corre AFTER porque necesita el valor ya validado por
-- tr_procedure_instance_field_values_immutable (BEFORE, HU #10872/subsanación): si esa inmutabilidad
-- bloquea el write, este trigger ni se ejecuta. El filtrado por field_key/actor_type se resuelve DENTRO
-- de la función (no con un WHEN en el CREATE TRIGGER) porque un WHEN que combine NEW y OLD sobre un
-- trigger de INSERT/UPDATE/DELETE a la vez es frágil entre motores; el patrón COALESCE(NEW.x, OLD.x)
-- dentro del cuerpo ya es el que usa tr_field_value_immutable en esta misma tabla.
CREATE OR REPLACE FUNCTION tramites.trg_procedure_instance_denorm_field_value() RETURNS trigger AS $$
DECLARE v_instance_id uuid := COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
BEGIN
  -- El field_key SALIENTE (borrado, o reemplazado por otro en un UPDATE) deja de aportar el dato.
  IF TG_OP IN ('UPDATE', 'DELETE') AND OLD.field_key = 'vin'
     AND (TG_OP = 'DELETE' OR NEW.field_key <> 'vin') THEN
    UPDATE tramites.procedure_instances SET vin = NULL WHERE id = v_instance_id;
  ELSIF TG_OP IN ('UPDATE', 'DELETE') AND OLD.field_key = 'plate'
     AND (TG_OP = 'DELETE' OR NEW.field_key <> 'plate') THEN
    UPDATE tramites.procedure_instances SET plate = NULL WHERE id = v_instance_id;
  END IF;

  -- El field_key ENTRANTE (insert, o el que queda tras un update) sincroniza el duplicado.
  IF TG_OP IN ('INSERT', 'UPDATE') AND NEW.field_key = 'vin' THEN
    UPDATE tramites.procedure_instances SET vin = NEW.value_text WHERE id = v_instance_id;
  ELSIF TG_OP IN ('INSERT', 'UPDATE') AND NEW.field_key = 'plate' THEN
    UPDATE tramites.procedure_instances SET plate = NEW.value_text WHERE id = v_instance_id;
  END IF;

  RETURN COALESCE(NEW, OLD);
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_procedure_instance_field_values_denorm ON tramites.procedure_instance_field_values;
CREATE TRIGGER tr_procedure_instance_field_values_denorm
  AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_field_values
  FOR EACH ROW EXECUTE FUNCTION tramites.trg_procedure_instance_denorm_field_value();

-- Trigger de sincronización vendedor_nombre/comprador_nombre: mismo patrón que el de arriba, sobre
-- actor_type en vez de field_key.
CREATE OR REPLACE FUNCTION tramites.trg_procedure_instance_denorm_actor() RETURNS trigger AS $$
DECLARE v_instance_id uuid := COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
BEGIN
  IF TG_OP IN ('UPDATE', 'DELETE') AND OLD.actor_type = 'vendedor'
     AND (TG_OP = 'DELETE' OR NEW.actor_type <> 'vendedor') THEN
    UPDATE tramites.procedure_instances SET vendedor_nombre = NULL WHERE id = v_instance_id;
  ELSIF TG_OP IN ('UPDATE', 'DELETE') AND OLD.actor_type = 'comprador'
     AND (TG_OP = 'DELETE' OR NEW.actor_type <> 'comprador') THEN
    UPDATE tramites.procedure_instances SET comprador_nombre = NULL WHERE id = v_instance_id;
  END IF;

  IF TG_OP IN ('INSERT', 'UPDATE') AND NEW.actor_type = 'vendedor' THEN
    UPDATE tramites.procedure_instances SET vendedor_nombre = NEW.full_name WHERE id = v_instance_id;
  ELSIF TG_OP IN ('INSERT', 'UPDATE') AND NEW.actor_type = 'comprador' THEN
    UPDATE tramites.procedure_instances SET comprador_nombre = NEW.full_name WHERE id = v_instance_id;
  END IF;

  RETURN COALESCE(NEW, OLD);
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_procedure_instance_actors_denorm ON tramites.procedure_instance_actors;
CREATE TRIGGER tr_procedure_instance_actors_denorm
  AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_actors
  FOR EACH ROW EXECUTE FUNCTION tramites.trg_procedure_instance_denorm_actor();

-- BACKFILL de las filas existentes. Se desactivan los triggers de negocio DEL PADRE (row_version +
-- audit_log; DISABLE TRIGGER USER no toca triggers de sistema/FK) mientras dura el backfill: es una
-- migración de esquema, no una mutación de negocio, y sin esto cada trámite existente generaría hasta 2
-- filas de audit_log y un bump de row_version que no corresponde a ninguna acción real de un usuario —
-- ruido en la auditoría y riesgo de un DbUpdateConcurrencyException espurio si algún proceso tenía en
-- memoria el row_version anterior al desplegar. El backfill en sí NO dispara los triggers nuevos de
-- arriba (esos viven en las tablas hijas field_values/actors, no en procedure_instances).
ALTER TABLE tramites.procedure_instances DISABLE TRIGGER USER;

UPDATE tramites.procedure_instances pi
   SET vin = fv.value_text
  FROM tramites.procedure_instance_field_values fv
 WHERE fv.procedure_instance_id = pi.id AND fv.field_key = 'vin';

UPDATE tramites.procedure_instances pi
   SET plate = fv.value_text
  FROM tramites.procedure_instance_field_values fv
 WHERE fv.procedure_instance_id = pi.id AND fv.field_key = 'plate';

UPDATE tramites.procedure_instances pi
   SET vendedor_nombre = a.full_name
  FROM tramites.procedure_instance_actors a
 WHERE a.procedure_instance_id = pi.id AND a.actor_type = 'vendedor';

UPDATE tramites.procedure_instances pi
   SET comprador_nombre = a.full_name
  FROM tramites.procedure_instance_actors a
 WHERE a.procedure_instance_id = pi.id AND a.actor_type = 'comprador';

ALTER TABLE tramites.procedure_instances ENABLE TRIGGER USER;
