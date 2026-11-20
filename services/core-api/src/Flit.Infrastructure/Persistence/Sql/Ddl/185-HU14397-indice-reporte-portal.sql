-- =============================================================================
-- HU #14397 (Feature #14396, Épica #14297) — índice parcial del reporte de los trámites del portal.
-- Migración: 20261106130000_HU14397_IndiceReportePortal.
--
-- El reporte lee solo los canales del portal (pn_web, pn_punto, aplicacion), de la más reciente a la más antigua. Con 600 000
-- trámites (100 000 del portal) medido en una base de prueba SIN este índice: la vista del SuperAdmin hacía Parallel Seq Scan de
-- toda la tabla (477 ms la página, 431 ms el conteo; crece con la tabla porque el canal «aplicacion» no tiene dueño fijo y no se
-- puede acotar por empresa) y el conteo del organismo tardaba 5,9 s leyendo todas las filas del portal. Con el índice: página del
-- SuperAdmin 0,05 ms, conteo 144 ms, conteo del organismo 0,9 s.
--
-- Parcial: solo las filas del portal (una fracción de la tabla), así que casi no cuesta en escrituras de los demás canales. El
-- INCLUDE deja contar y filtrar por organismo sin ir al heap. Excepción al checklist A11 (tenant_id primero): el orden del reporte es
-- por fecha entre empresas para «aplicacion»; tenant_id va incluido para el filtro.
-- Idempotente.
-- =============================================================================
CREATE INDEX IF NOT EXISTS ix_procedure_instances_portal_channels_created_at
    ON tramites.procedure_instances (created_at DESC, id DESC)
    INCLUDE (tenant_id, channel, transit_office_id, channel_transit_office_id)
    WHERE deleted_at IS NULL AND channel IN ('pn_web', 'pn_punto', 'aplicacion');
