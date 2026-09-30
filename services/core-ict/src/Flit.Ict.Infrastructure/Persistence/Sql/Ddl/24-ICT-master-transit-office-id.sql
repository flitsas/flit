-- =============================================================================
-- core-ict — Organismo de tránsito resuelto por código (Bug #13109, puntos 1 y 4).
-- sp_processor_validation_business ya validaba traffic_secretary_code contra
-- catalogs.transit_offices y los grants del tenant, pero no guardaba el id: el borrador
-- ICT nacía con TransitOfficeId vacío y el dashboard volvía a pedir la secretaría.
-- El SP escribe aquí el id cuando el código existe, está activo y tiene grant; el cliente
-- gRPC lo envía a core-api y, si existe, gana sobre el nombre que devolvió el RUNT.
-- Aditivo e idempotente (ADD COLUMN IF NOT EXISTS): no afecta pre-trámites existentes.
-- =============================================================================

ALTER TABLE ict.external_integration_master
  ADD COLUMN IF NOT EXISTS transit_office_id uuid;

COMMENT ON COLUMN ict.external_integration_master.transit_office_id IS
  'Organismo de tránsito (catalogs.transit_offices.id) resuelto por traffic_secretary_code en la validación de negocio, solo si el código está activo y habilitado para el tenant. El borrador nace con este id; si es null se usa el nombre del RUNT (traspaso) o lo asigna el gestor.';
