-- Plataforma Mandatos — tres tipos de asignación de mandatario.
--
-- assignment_mode distingue el comportamiento de negocio sin inventar plantillas nuevas:
--   signer         → persona/RL firma (default; OTs sin fila implícitos)
--   institutional  → OT/UT actúa como mandatario (p. ej. Sabaneta UT-SETSA)
--   open           → contrato sin mandatario asignado (placeholders ___)
--
-- template_code (redacción) y mandatary_family siguen independientes.
-- DDL IDEMPOTENTE.

ALTER TABLE admin.transit_office_mandate_config
  ADD COLUMN IF NOT EXISTS assignment_mode varchar(20) NOT NULL DEFAULT 'signer';

ALTER TABLE admin.transit_office_mandate_config
  DROP CONSTRAINT IF EXISTS ck_transit_office_mandate_config_assignment_mode;
ALTER TABLE admin.transit_office_mandate_config
  ADD CONSTRAINT ck_transit_office_mandate_config_assignment_mode
  CHECK (assignment_mode IN ('signer', 'institutional', 'open'));

-- Backfill puntual: solo Sabaneta (institucional OT/UT). Bello queda en signer
-- (persona/RL de la UT con redacción bella) para alinear con el tipo de negocio.
UPDATE admin.transit_office_mandate_config cfg
SET assignment_mode = 'institutional'
FROM catalogs.transit_offices ot
WHERE cfg.transit_office_id = ot.id
  AND ot.code = '5631000'
  AND cfg.assignment_mode <> 'institutional';
