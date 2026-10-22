-- HU #13076 (Feature #13066, Épica #12737) — Índices del recorrido del feed de sincronización.
--
-- El feed se recorre por (transacción que selló, versión), no solo por versión (ADR-0066, enmienda de
-- la HU #13076): la versión se toma al escribir y no al confirmar, así que una transacción larga que
-- confirma después de otra con versión mayor quedaría detrás de un cursor solo por versión y nunca se
-- entregaría. La lectura solo entrega transacciones anteriores a la más antigua en curso
-- (pg_snapshot_xmin), que ya no pueden cambiar.
--
--   ix_procedure_instances_sync_cursor   WHERE (xact, version) > cursor ORDER BY xact, version LIMIT n.
--                                        Las filas de la asignación inicial (DDL 123) no tienen sync_xact:
--                                        cuentan como la transacción 0, la más antigua.
--   ix_procedure_instances_sync_changed  arranque por fecha (since): una sola vez por consumidor.
--
-- Excepción al criterio A11 (tenant_id como primera columna) documentada en ADR-0066, igual que los
-- índices del DDL 124. Sin CONCURRENTLY: la migración corre en la transacción de EF.
--
-- Idempotente y reaplicable.

CREATE INDEX IF NOT EXISTS ix_procedure_instances_sync_cursor
    ON tramites.procedure_instances ((COALESCE(sync_xact, '0'::xid8)), sync_version);

CREATE INDEX IF NOT EXISTS ix_procedure_instances_sync_changed
    ON tramites.procedure_instances (sync_changed_at);
