-- Quipux (HU #10774) — stages manuales en la bitácora de radicaciones.
--
-- La consola de cola QX permite a un SuperAdmin re-encolar una radicación 'fallido' (reintento) y
-- cancelar una 'pendiente'. Esas dos acciones NO las produce ningún worker, pero deben quedar en
-- tramites.quipux_submission_events para que el timeline del trámite (y el futuro LOG QX) no tenga
-- huecos inexplicables — p. ej. un 'fallido' que reaparece como 'pendiente' sin motivo aparente.
--
-- Existe como migración aparte (no editando 32-HU10710) porque ese DDL ya está aplicado en los
-- ambientes: editar el .sql de una migración pasada no la re-ejecuta (EF solo mira
-- __EFMigrationsHistory). En una base nueva 32 ya trae el CHECK ampliado y este ALTER es idempotente.
--
-- El CHECK es inline y sin nombre en el DDL original, así que Postgres lo autonombra
-- <tabla>_<columna>_check = quipux_submission_events_stage_check (verificado en flit_v22_dev).
-- El actor de la acción NO viaja en la bitácora (regla PII del repo, ver QuipuxSubmissionAuditLog);
-- queda solo en el log de aplicación.

ALTER TABLE tramites.quipux_submission_events
  DROP CONSTRAINT IF EXISTS quipux_submission_events_stage_check;

ALTER TABLE tramites.quipux_submission_events
  ADD CONSTRAINT quipux_submission_events_stage_check
  CHECK (stage IN ('claimed','consolidado_generado','s3_subido',
                   'registro_enviado','registro_respuesta','registro_error',
                   'consulta_enviada','consulta_respuesta','consulta_error',
                   'aprobado','rechazado','dead_letter',
                   'reintento_manual','cancelado_manual'));
