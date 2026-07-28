-- =============================================================================
-- core-ict — Dirección del representante legal en los actores del pre-trámite.
-- El contrato v1 del register incluye seller/buyer → legal_representative con
-- legal_representative_city / _state / _address, que la tabla original no tenía
-- (solo documento, nombres, teléfono y correo). Se añaden de forma ADITIVA para
-- no descartar datos que el gestor sí envía. Idempotente (ADD COLUMN IF NOT EXISTS).
-- =============================================================================

ALTER TABLE ict.external_integration_actors
  ADD COLUMN IF NOT EXISTS legal_representative_city    varchar(30),
  ADD COLUMN IF NOT EXISTS legal_representative_state   varchar(22),
  ADD COLUMN IF NOT EXISTS legal_representative_address varchar(150);

COMMENT ON COLUMN ict.external_integration_actors.legal_representative_city IS '@pii:medium Municipio de la dirección del representante legal.';
COMMENT ON COLUMN ict.external_integration_actors.legal_representative_state IS '@pii:medium Departamento de la dirección del representante legal.';
COMMENT ON COLUMN ict.external_integration_actors.legal_representative_address IS '@pii:high Dirección del representante legal.';
