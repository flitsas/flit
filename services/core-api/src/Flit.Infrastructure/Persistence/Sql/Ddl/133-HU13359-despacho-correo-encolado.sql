-- =============================================================================
-- HU #13359 (Feature #13324) — los cupos de aviso de correo distinguen «encolado»
-- de «enviado».
--
-- Desde el corte de Notificaciones core-api no entrega correos: los deja como
-- trabajo notificaciones.email.send y los entrega core-notificaciones. Marcar la
-- fila «enviado» al encolar decía algo falso (con Notificaciones caído, o con el
-- correo rechazado por el proveedor y en mensajes muertos). Ahora queda
-- «encolado»; si llegó lo dice notificaciones.entregas. «enviado» se conserva
-- para las filas de antes del corte.
--
-- Solo se amplía el CHECK de status en las tres tablas de despacho. Idempotente.
-- =============================================================================

ALTER TABLE tramites.procedure_state_change_email_dispatches
    DROP CONSTRAINT IF EXISTS ck_psce_dispatches_status;
ALTER TABLE tramites.procedure_state_change_email_dispatches
    ADD CONSTRAINT ck_psce_dispatches_status
        CHECK (status IN ('pendiente', 'enviado', 'encolado', 'fallido', 'omitido'));
COMMENT ON COLUMN tramites.procedure_state_change_email_dispatches.status IS
  'Desenlace del cupo: pendiente | encolado (entregado a Notificaciones) | enviado (antes del corte) | fallido | omitido.';

ALTER TABLE tramites.plate_assignment_email_dispatches
    DROP CONSTRAINT IF EXISTS ck_pae_dispatches_status;
ALTER TABLE tramites.plate_assignment_email_dispatches
    ADD CONSTRAINT ck_pae_dispatches_status
        CHECK (status IN ('pendiente', 'enviado', 'encolado', 'fallido', 'omitido'));
COMMENT ON COLUMN tramites.plate_assignment_email_dispatches.status IS
  'Desenlace del cupo: pendiente | encolado (entregado a Notificaciones) | enviado (antes del corte) | fallido | omitido.';

ALTER TABLE tramites.revocation_request_email_dispatches
    DROP CONSTRAINT IF EXISTS ck_rre_dispatches_status;
ALTER TABLE tramites.revocation_request_email_dispatches
    ADD CONSTRAINT ck_rre_dispatches_status
        CHECK (status IN ('pendiente', 'enviado', 'encolado', 'fallido', 'omitido'));
COMMENT ON COLUMN tramites.revocation_request_email_dispatches.status IS
  'Desenlace del cupo: pendiente | encolado (entregado a Notificaciones) | enviado (antes del corte) | fallido | omitido.';
