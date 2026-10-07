-- =============================================================================
-- core-ict — Resultado completo de la consulta RUNT de vehículo (Bug #13304, numeral 3).
-- El orquestador guarda aquí lo que core-api devolvió en la consulta VEHICLE/VIN para que el
-- borrador lo reutilice sin volver a consultar el RUNT (prenda/gravamen incluidos). Va en una
-- columna aparte y NO en query_response porque esa se muestra tal cual en la trazabilidad.
-- Vigencia de 24 h la valida el envío: vencida o ausente ⇒ core-ict re-encola su consulta de vehículo
-- (nueva source_query; una vez por ventana, contada en la propia tabla) y, si sigue sin resultado, novedad.
-- Retención (minimización de PII): se vacía al materializar el
-- borrador, al anular o al marcar novedad, y el barrido de RetentionJob vacía lo que tenga más de 2 veces
-- la vigencia (48 h por defecto; cubre también una purga fallida).
-- Aditivo e idempotente (ADD COLUMN IF NOT EXISTS): no afecta respuestas existentes.
-- =============================================================================

ALTER TABLE ict.external_integration_source_response
  ADD COLUMN IF NOT EXISTS vehicle_snapshot jsonb NULL;

COMMENT ON COLUMN ict.external_integration_source_response.vehicle_snapshot IS
  '@pii:high Resultado completo de la consulta RUNT de vehículo (titular, acreedor de la prenda) que core-api reutiliza al crear el borrador ICT: {snapshot_json, consulted_at, provider, kind, plate, vin}. No se expone en trazabilidad. Retención: se vacía al materializar el borrador, al anular o al marcar novedad; el barrido de retención vacía las de más de 2 veces la vigencia (48 h por defecto).';

-- Índice PARCIAL del barrido de RetentionJob (vehicle_snapshot IS NOT NULL AND created_at < …): solo indexa las
-- filas que aún guardan el snapshot, así que se mantiene pequeño (salen al vaciarse). Idempotente.
CREATE INDEX IF NOT EXISTS ix_eisr_vehicle_snapshot_created
    ON ict.external_integration_source_response (created_at)
    WHERE vehicle_snapshot IS NOT NULL;
