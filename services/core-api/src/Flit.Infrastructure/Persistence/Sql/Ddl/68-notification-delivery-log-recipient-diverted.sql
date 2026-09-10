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

-- ADR-0044 (2026-08-12) derogó la variable y el criterio citados en este COMMENT (la variable
-- RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED y el criterio "fuera de
-- producción" — ahora el desvío lo decide, en cualquier ambiente incluida producción, la variable
-- afirmativa RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED). Esta migración YA se aplicó en
-- bases DEV/QA/PDN, así que ejecutarla de nuevo no reescribe el COMMENT ya guardado en esas
-- bases: solo lo hará el próximo DDL que vuelva a tocar esta columna. Hasta entonces, un operador
-- que consulte el diccionario de datos en una base ya migrada verá el texto viejo.
COMMENT ON COLUMN admin.notification_delivery_logs.recipient_diverted IS
  'HU #11364 AC2 / ADR-0044 — true cuando el adaptador de canal sustituyó el destinatario real por uno de desvío antes de enviar (default seguro del canal Renting; ver RentingChannelOptions.SendRealRecipientsEnabled). Con true, el envío NO llegó al destinatario que figura en la columna recipient de esta misma fila: esa columna sigue guardando el destinatario ORIGINAL (para trazabilidad), no el de desvío.';
