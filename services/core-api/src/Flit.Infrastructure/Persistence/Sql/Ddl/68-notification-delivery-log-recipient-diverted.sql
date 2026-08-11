-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11364 (Feature #11348) — marca de envío desviado en la bitácora de
-- notificaciones.
--
-- CONTEXTO (AC2): el desvío de destinatario fuera de producción (canal Renting,
-- RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED) sustituye el
-- destinatario real por uno de desvío DENTRO del adaptador
-- (RentingRecipientOverride). El decorador de bitácora (HU #11363,
-- NotificationDeliveryLoggingEmailSender) ya escribe el destinatario ORIGINAL en
-- cada fila (lee EmailMessage.ToEmail ANTES de que el adaptador lo sustituya) —
-- lo único que faltaba era la MARCA de que ese envío no llegó a ese destinatario.
-- Sin esta columna, una fila de un envío desviado afirma (falsamente) que el
-- correo llegó a quien figura en `recipient`.
--
-- IDEMPOTENTE: ADD COLUMN IF NOT EXISTS. Re-ejecutarlo no pierde datos.
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE admin.notification_delivery_logs
  ADD COLUMN IF NOT EXISTS recipient_diverted boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN admin.notification_delivery_logs.recipient_diverted IS
  'HU #11364 AC2 — true cuando el adaptador de canal sustituyó el destinatario real por uno de desvío (fuera de producción, RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED) antes de enviar. Con true, el envío NO llegó al destinatario que figura en la columna recipient de esta misma fila: esa columna sigue guardando el destinatario ORIGINAL (para trazabilidad), no el de desvío.';
