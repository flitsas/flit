-- =============================================================================
-- core-ict — Tipo de servicio del vehículo en el pre-trámite (opcional, solo matrícula).
-- Contrato de registro: vehicle_service_type (1 Particular, 2 Público, 3 Diplomático, 4 Oficial,
-- 5 Especial, 6 Otros). NULL = no declarado: al crear el borrador de matrícula se asume PARTICULAR.
-- Aditivo e idempotente (ADD COLUMN IF NOT EXISTS): no afecta pre-trámites existentes.
-- =============================================================================

ALTER TABLE ict.external_integration_master
  ADD COLUMN IF NOT EXISTS vehicle_service_type smallint;

COMMENT ON COLUMN ict.external_integration_master.vehicle_service_type IS
  'Tipo de servicio declarado en el registro (1 Particular, 2 Público, 3 Diplomático, 4 Oficial, 5 Especial, 6 Otros). Opcional y solo para matrícula; NULL = Particular por defecto al crear el borrador. Público sin empresa vinculadora no es novedad.';
