-- HU #13075 (Feature #13062, Épica #12737) — Índices de la sincronización externa.
--
-- La lectura del feed cruza compañías y localiza por procedure_instance_id, no por tenant: los
-- índices que ya existen sobre historial y adjuntos empiezan por tenant_id y no le sirven. Aquí van
-- los que sí usa, todos parciales para que ocupen solo lo que la sincronización necesita:
--
--   uq_procedure_instances_sync_version   recorrer por versión: WHERE sync_version > cursor ORDER BY
--                                         sync_version LIMIT n. ÚNICO: la secuencia no repite y el
--                                         sellado es por transacción, así que dos trámites no pueden
--                                         compartir versión; si algún día pasara, la migración lo diría.
--   ix_pi_status_history_radicado         el alcance: «radicado al menos una vez» (EXISTS sobre el
--                                         historial con to_status preasignacion/entregado).
--   ix_pi_status_history_aprobado         fechaAprobacion: la última transición a aprobado.
--   ix_pi_attachments_factura             la factura más reciente (por uploaded_at). La tabla de adjuntos no tiene
--                                         deleted_at: el borrado de un adjunto es físico.
--
-- Actores, campos y comercial ya tienen índices únicos que empiezan por procedure_instance_id.
--
-- Excepción al criterio A11 (tenant_id como primera columna) documentada en ADR-0061: la lectura del
-- feed cruza compañías y la acota el ámbito exclusivo del servicio externo, no el índice.
--
-- Sin CONCURRENTLY: la migración corre en la transacción de EF. Son índices pequeños (tres de ellos
-- parciales) y se crean en la misma ventana corta que la asignación inicial del DDL 123.
--
-- Idempotente y reaplicable.

CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instances_sync_version
    ON tramites.procedure_instances (sync_version);

CREATE INDEX IF NOT EXISTS ix_pi_status_history_radicado
    ON tramites.procedure_instance_status_history (procedure_instance_id)
    WHERE to_status IN ('preasignacion', 'entregado');

CREATE INDEX IF NOT EXISTS ix_pi_status_history_aprobado
    ON tramites.procedure_instance_status_history (procedure_instance_id, changed_at DESC)
    WHERE to_status = 'aprobado';

CREATE INDEX IF NOT EXISTS ix_pi_attachments_factura
    ON tramites.procedure_instance_attachments (procedure_instance_id, uploaded_at DESC)
    WHERE tipo = 'factura';
