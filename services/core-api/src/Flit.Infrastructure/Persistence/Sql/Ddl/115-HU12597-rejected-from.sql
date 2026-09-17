-- HU #12597 (Feature #12595, ADR-0059) — origen del último rechazo del trámite.
--
-- El OT puede rechazar desde 'entregado' (decisión) o desde 'preasignacion' (cola de placa). El gestor
-- necesita distinguir el segundo caso para priorizarlo («Rechazado preasignación»), y el status por sí
-- solo no lo dice: los dos terminan en 'rechazado'. La columna la escribe TramiteLifecycleService al
-- entrar a 'rechazado' (valor = estado de origen) y se limpia al activar la subsanación o al salir de
-- 'rechazado' por cualquier arista.
--
-- Los estados de negocio nuevos 'preasignacion' y 'asignado' NO requieren DDL: `status` no tiene CHECK
-- de enum en BD (se valida en aplicación, TramiteEstado.EsValido). La migración de datos del sub-estado
-- `plate_flow_status` hacia estos estados es de la HU #12599.

ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS rejected_from varchar(20) NULL;

COMMENT ON COLUMN tramites.procedure_instances.rejected_from IS
    'ADR-0059: estado desde el que el OT rechazó por última vez (entregado | preasignacion). NULL si no aplica o ya se subsanó.';
