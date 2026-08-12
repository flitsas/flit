-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11469 (Feature #11460) — interruptor operativo de avisos de correo al
-- cambio de estado del trámite. Se evalúa en el WORKER (HU #11467), no en el
-- sink: apagado, las filas quedan pendiente sin gastar intentos.
--
-- Default TRUE: al desplegar, los avisos quedan encendidos para todas las
-- compañías existentes (decisión PO). IDEMPOTENTE: ADD COLUMN IF NOT EXISTS.
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE admin.tenant_operational_policies
  ADD COLUMN IF NOT EXISTS tramite_state_emails_enabled boolean NOT NULL DEFAULT true;

COMMENT ON COLUMN admin.tenant_operational_policies.tramite_state_emails_enabled IS
  'HU #11469 — interruptor operativo global por compañía de los avisos de correo al cambio de estado (aprobado/rechazado). true = envía; false = el worker deja las filas pendiente sin gastar attempts.';
