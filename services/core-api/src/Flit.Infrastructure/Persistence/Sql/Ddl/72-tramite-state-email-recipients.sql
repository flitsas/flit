-- Feature #11791 — interruptores de aprobación/rechazo y destinatarios múltiples.
-- Backfill PO 2026-08-24: empresas nuevas y existentes, todas las banderas en true.
-- extraEmail es dato personal (Ley 1581): aviso operativo de estado del trámite.

ALTER TABLE admin.tenant_operational_policies
  ADD COLUMN IF NOT EXISTS tramite_approved_emails_enabled boolean NOT NULL DEFAULT true;

ALTER TABLE admin.tenant_operational_policies
  ADD COLUMN IF NOT EXISTS tramite_rejected_emails_enabled boolean NOT NULL DEFAULT true;

ALTER TABLE admin.tenant_operational_policies
  ADD COLUMN IF NOT EXISTS tramite_state_email_recipients jsonb NOT NULL
  DEFAULT '{"comprador":true,"vendedorOPropietario":true,"radicador":true,"extraEmail":null}'::jsonb;

UPDATE admin.tenant_operational_policies
SET
  tramite_approved_emails_enabled = true,
  tramite_rejected_emails_enabled = true,
  tramite_state_email_recipients = jsonb_build_object(
    'comprador', true,
    'vendedorOPropietario', true,
    'radicador', true,
    'extraEmail', null);

ALTER TABLE admin.tenant_operational_policies
  DROP COLUMN IF EXISTS tramite_state_emails_enabled;

COMMENT ON COLUMN admin.tenant_operational_policies.tramite_approved_emails_enabled IS
  'Interruptor de avisos de correo al APROBAR un trámite. true = el worker envía tramites.aprobado; false = deja pendiente sin gastar attempts.';

COMMENT ON COLUMN admin.tenant_operational_policies.tramite_rejected_emails_enabled IS
  'Interruptor de avisos de correo al RECHAZAR un trámite. true = el worker envía tramites.rechazado; false = deja pendiente sin gastar attempts.';

COMMENT ON COLUMN admin.tenant_operational_policies.tramite_state_email_recipients IS
  'Destinatarios combinables de avisos aprobado/rechazado (jsonb): comprador, vendedorOPropietario, radicador (boolean) y extraEmail (PII, Ley 1581, correo adicional de la compañía, finalidad: aviso operativo de estado del trámite).';
