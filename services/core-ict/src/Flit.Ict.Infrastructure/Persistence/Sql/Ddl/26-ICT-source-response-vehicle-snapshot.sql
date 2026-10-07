-- =============================================================================
-- core-ict — Resultado completo de la consulta RUNT de vehículo (Bug #13304, numeral 3).
-- El orquestador guarda aquí lo que core-api devolvió en la consulta VEHICLE/VIN para que el
-- borrador lo reutilice sin volver a consultar el RUNT (prenda/gravamen incluidos). Va en una
-- columna aparte y NO en query_response porque esa se muestra tal cual en la trazabilidad.
-- Vigencia de 24 h la valida el envío; al materializar se vacía (minimización de PII).
-- Aditivo e idempotente (ADD COLUMN IF NOT EXISTS): no afecta respuestas existentes.
-- =============================================================================

ALTER TABLE ict.external_integration_source_response
  ADD COLUMN IF NOT EXISTS vehicle_snapshot jsonb NULL;

COMMENT ON COLUMN ict.external_integration_source_response.vehicle_snapshot IS
  '@pii:high Resultado completo de la consulta RUNT de vehículo (titular, acreedor de la prenda) que core-api reutiliza al crear el borrador ICT: {snapshot_json, consulted_at, provider, kind, plate, vin}. No se expone en trazabilidad; se vacía al materializar.';
