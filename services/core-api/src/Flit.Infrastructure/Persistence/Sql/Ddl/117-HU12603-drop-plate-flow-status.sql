-- HU #12603 (Feature #12595, ADR-0059) — retiro definitivo del sub-estado de placa.
--
-- Desde HU #12599 la ruta de placa vive en `status` ('preasignacion' / 'asignado'), la columna
-- `plate_flow_status` está en NULL en todas las filas y nadie la lee ni la escribe en runtime
-- (HU #12597/#12598). Se elimina junto con `plate_flow_skip_to_terminado`: el «salto a terminado»
-- era una configuración de compañía del modelo viejo (Flujo A) que la Ruta Corta ya no contempla
-- (con placa se entrega directo). El trigger de autoset y su función se retiraron en 116.
--
-- Backup lógico previo recomendado (Infra): la misma tabla bk_hu12599_plate_flow de la migración
-- anterior cubre esta (ahí quedaron id, status y plate_flow_status antes de vaciarla).

SET LOCAL row_security = off;

ALTER TABLE tramites.procedure_instances
    DROP COLUMN IF EXISTS plate_flow_status;

ALTER TABLE admin.tenant_operational_policies
    DROP COLUMN IF EXISTS plate_flow_skip_to_terminado;
