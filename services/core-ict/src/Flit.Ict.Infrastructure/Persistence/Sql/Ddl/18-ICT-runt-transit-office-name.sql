-- =============================================================================
-- core-ict — Organismo de tránsito derivado del RUNT (paridad v1 para TRASPASO).
-- En traspaso (tipos 3/4) la secretaría NO la envía el cliente: v1 la obtenía del RUNT
-- al consultar por placa (organismoTransito) y con ella resolvía el código/OT. El
-- orquestador (Job 3) captura ese nombre de la consulta VEHICLE y lo persiste aquí;
-- core-api lo resuelve al transit_office_id del catálogo al materializar el borrador.
-- Aditivo e idempotente (ADD COLUMN IF NOT EXISTS): no afecta pre-trámites existentes.
-- =============================================================================

ALTER TABLE ict.external_integration_master
  ADD COLUMN IF NOT EXISTS runt_transit_office_name varchar(160);

COMMENT ON COLUMN ict.external_integration_master.runt_transit_office_name IS
  'Nombre del organismo de tránsito de matrícula devuelto por el RUNT (consulta VEHICLE). En traspaso, core-api lo resuelve al transit_office_id del catálogo (grants del tenant) al materializar el borrador.';
