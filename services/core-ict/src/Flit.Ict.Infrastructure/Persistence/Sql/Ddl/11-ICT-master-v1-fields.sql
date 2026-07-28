-- =============================================================================
-- core-ict — Campos del contrato v1 del register que el pre-trámite aún descartaba.
-- El master ya tenía priority/transaction_flit; faltaban las banderas de comportamiento
-- (pausa inicial, envío automático al OT, tipo de asignación de placa) y los datos de
-- limitación/prenda, compañía relacionada, blindaje y nuevo combustible.
-- Aditivo e idempotente (ADD COLUMN IF NOT EXISTS): no afecta pre-trámites existentes.
-- =============================================================================

ALTER TABLE ict.external_integration_master
  -- Banderas de comportamiento del trámite resultante.
  ADD COLUMN IF NOT EXISTS starts_procedure_in_paused       boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS observation_when_paused          varchar(250),
  ADD COLUMN IF NOT EXISTS send_automatic_traffic_secretary boolean NOT NULL DEFAULT true,
  ADD COLUMN IF NOT EXISTS plate_assignment_type            smallint,
  -- Compañía relacionada (matrícula de servicio público).
  ADD COLUMN IF NOT EXISTS related_company_document         varchar(12),
  ADD COLUMN IF NOT EXISTS related_company_name             varchar(100),
  -- Limitación a la propiedad / garantía mobiliaria (prenda).
  ADD COLUMN IF NOT EXISTS limitations_operation_type       smallint,
  ADD COLUMN IF NOT EXISTS limitations_creditor             varchar(100),
  ADD COLUMN IF NOT EXISTS limitations_creditor_document_type   varchar(5),
  ADD COLUMN IF NOT EXISTS limitations_creditor_document_number varchar(12),
  ADD COLUMN IF NOT EXISTS limitations_inscription_date     varchar(10),
  -- Otros trámites: blindaje y conversión de combustible.
  ADD COLUMN IF NOT EXISTS armor_level_number_id            smallint,
  ADD COLUMN IF NOT EXISTS new_vehicle_fuel_type            smallint;

COMMENT ON COLUMN ict.external_integration_master.observation_when_paused IS 'Nota que se muestra en el dashboard cuando el trámite queda pausado al crearse.';
COMMENT ON COLUMN ict.external_integration_master.related_company_document IS '@pii:medium NIT de la compañía relacionada (servicio público).';
COMMENT ON COLUMN ict.external_integration_master.limitations_creditor IS '@pii:medium Nombre del acreedor de la limitación/prenda.';
COMMENT ON COLUMN ict.external_integration_master.limitations_creditor_document_number IS '@pii:high Documento del acreedor de la limitación/prenda.';
