-- =============================================================================
-- ICT — Primitivo de PAUSA en el trámite (servicio v1 pauseDraftProcess + campo
-- starts_procedure_in_paused del register). FLIT 2.0 no tenía concepto de pausa;
-- se añade de forma ADITIVA a tramites.procedure_instances:
--   · is_paused          — bandera de pausa (por defecto false; no afecta trámites existentes).
--   · paused_observation — nota mostrada en el dashboard cuando el trámite está pausado.
-- La tabla es ExcludeFromMigrations (DDL crudo); ALTER idempotente (IF NOT EXISTS).
-- TODO(ICT-PAUSE-UI): el dashboard de trámites debe reflejar is_paused/paused_observation.
-- =============================================================================

ALTER TABLE tramites.procedure_instances
  ADD COLUMN IF NOT EXISTS is_paused boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS paused_observation varchar(250);

COMMENT ON COLUMN tramites.procedure_instances.paused_observation IS '@pii:none Nota mostrada en el dashboard cuando el trámite está pausado (origen ICT).';
