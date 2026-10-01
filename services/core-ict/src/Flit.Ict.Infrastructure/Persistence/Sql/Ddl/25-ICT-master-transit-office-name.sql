-- =============================================================================
-- core-ict — Nombre y municipio del organismo resuelto por código (Bug #13109, punto 1).
-- El 24 guardó solo transit_office_id; core-api no puede sacar el trámite del borrador sin
-- el field_value transit_office_code (FinalizeDraftGate/SubmitGate), y el mandato y la
-- entrega leen transit_office_*. El SP de negocio escribe aquí el nombre y el city_code de
-- la MISMA fila del catálogo que resolvió el id, y el cliente gRPC los envía con él para que
-- core-api siembre los field_values del OT sin volver a consultar el catálogo.
-- Aditivo e idempotente (ADD COLUMN IF NOT EXISTS): no afecta pre-trámites existentes.
-- =============================================================================

ALTER TABLE ict.external_integration_master
  ADD COLUMN IF NOT EXISTS transit_office_name varchar(200);

ALTER TABLE ict.external_integration_master
  ADD COLUMN IF NOT EXISTS transit_office_city_code varchar(10);

COMMENT ON COLUMN ict.external_integration_master.transit_office_name IS
  'Nombre del organismo (catalogs.transit_offices.name) de la misma fila que transit_office_id. Lo escribe la validación de negocio; null si el código no se resolvió.';

COMMENT ON COLUMN ict.external_integration_master.transit_office_city_code IS
  'city_code DANE del organismo (catalogs.transit_offices.city_code) de la misma fila que transit_office_id. Lo escribe la validación de negocio; null si el código no se resolvió.';
