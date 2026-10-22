-- HU #13265 (Feature #13261, Épica #12741) — «gana quien carga primero» del comprobante de FLITO, defendido en el MOTOR.
--
-- Los handlers del gestor y el endpoint externo ya deciden con lo que leyeron, pero el gestor no bloquea la fila del trámite:
-- si el gestor y FLITO cargan al mismo tiempo, cada uno puede no ver el insert sin confirmar del otro y quedarían dos
-- adjuntos vigentes del mismo tipo (uno de flito y uno de otro proveedor), lo que traba el trámite en ambos sentidos
-- (409 attachment_exists para FLITO y adjunto_bloqueado_flito para el gestor).
--
-- BEFORE INSERT OR UPDATE (de tipo, provider, is_historico, procedure_instance_id): si el adjunto es VIGENTE y su tipo es de los
-- que carga el cliente externo, toma el MISMO lock de fila que ExternalAttachmentRepository (FOR NO KEY UPDATE sobre el
-- trámite) —serializa las dos cargas y la segunda ve lo que la primera confirmó— y rechaza con 23505 y la constraint
-- 'ck_attachments_flito_gana_primero' si en el trámite ya hay un vigente del mismo tipo de «el otro bando»:
--   * NEW no es de flito y existe un vigente de flito  -> gana FLITO (cargó primero).
--   * NEW es de flito y existe un vigente de otro proveedor -> gana el otro (defensa del lado externo).
-- Los reemplazos legítimos no disparan: quien reemplaza lo suyo retira (DELETE) lo propio y su insert solo choca con el bando contrario.
-- Los adjuntos históricos (is_historico = true, p. ej. tras una revocación) no participan.
--
-- Lista de tipos del contrato externo: UN solo lugar, tramites.flito_attachment_tipos() (hoy liquidacion_impuesto).
--
-- Idempotente y reaplicable.

CREATE OR REPLACE FUNCTION tramites.flito_attachment_tipos() RETURNS text[] AS $$
  SELECT ARRAY['liquidacion_impuesto']::text[]
$$ LANGUAGE sql IMMUTABLE;

CREATE OR REPLACE FUNCTION tramites.trg_attachment_flito_gana_primero() RETURNS trigger AS $$
DECLARE v_es_flito boolean;
BEGIN
  IF NEW.is_historico IS TRUE OR NOT (lower(NEW.tipo) = ANY (tramites.flito_attachment_tipos())) THEN
    RETURN NEW;
  END IF;

  -- Mismo lock que ExternalAttachmentRepository. Sentencia aparte: bajo READ COMMITTED la siguiente ya ve lo que
  -- confirmó quien tenía el lock.
  PERFORM 1 FROM tramites.procedure_instances WHERE id = NEW.procedure_instance_id FOR NO KEY UPDATE;

  v_es_flito := lower(coalesce(NEW.provider, '')) = 'flito';

  IF EXISTS (
    SELECT 1
      FROM tramites.procedure_instance_attachments a
     WHERE a.procedure_instance_id = NEW.procedure_instance_id
       AND lower(a.tipo) = lower(NEW.tipo)
       AND a.is_historico = false
       AND a.id <> NEW.id
       AND (lower(coalesce(a.provider, '')) = 'flito') <> v_es_flito
  ) THEN
    RAISE EXCEPTION 'el trámite % ya tiene un adjunto vigente de tipo % cargado por %: gana quien carga primero',
      NEW.procedure_instance_id, NEW.tipo, CASE WHEN v_es_flito THEN 'otro proveedor' ELSE 'flito' END
      USING ERRCODE = 'unique_violation', CONSTRAINT = 'ck_attachments_flito_gana_primero';
  END IF;

  RETURN NEW;
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_attachments_flito_gana_primero ON tramites.procedure_instance_attachments;
CREATE TRIGGER tr_attachments_flito_gana_primero
  BEFORE INSERT OR UPDATE OF tipo, provider, is_historico, procedure_instance_id
  ON tramites.procedure_instance_attachments
  FOR EACH ROW EXECUTE FUNCTION tramites.trg_attachment_flito_gana_primero();
