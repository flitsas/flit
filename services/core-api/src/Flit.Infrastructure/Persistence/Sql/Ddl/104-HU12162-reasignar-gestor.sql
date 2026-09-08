-- =============================================================================
-- Gestor asignado de un trámite (admin) — HU #12162 (Feature #12155, Dashboard admin).
-- Migración: 20260908130000_HU12162_ProcedureInstanceAssignedToUserId
-- Database Agent.
--
-- Contexto: `tramites.procedure_instances.created_by_user_id` (06-HU10150-procedure-instances.sql)
-- es el usuario que RADICÓ el trámite (auditoría del creador original, RESTRICT en delete). Hoy el
-- listado (`ListProcedureInstancesQuery`) lo muestra como "gestor" del trámite por default, pero no
-- existe forma de REASIGNAR la responsabilidad operativa a otro usuario sin tocar esa auditoría.
--
-- Esta migración agrega `assigned_to_user_id`: el gestor actualmente responsable del trámite,
-- reasignable por un administrador (permiso `AdminTramiteReasignarGestor`, catálogo HU #12157 en
-- AdminTramiteAuthorization.cs). Separa dos conceptos que hoy colapsan en una sola columna:
--   - created_by_user_id → QUIÉN RADICÓ (auditoría inmutable, no se toca en esta migración).
--   - assigned_to_user_id → QUIÉN ES RESPONSABLE HOY (mutable, la reasigna el admin).
--
-- Decisión de negocio (sin ADR — es una extensión de columna sobre tabla ya existente, no una
-- entidad de negocio nueva): NO se backfillea `assigned_to_user_id` con `created_by_user_id`. Todo
-- trámite existente nace con `assigned_to_user_id = NULL` ("sin gestor asignado"), tal como pide la
-- HU explícitamente. Copiar automáticamente el creador como asignado por default fabricaría una
-- reasignación que nunca ocurrió y volvería ambiguo el propósito de la columna (¿es un valor real
-- elegido por un admin, o solo el eco del creador?). El `backend-agent` decide en la capa de
-- aplicación/UI si un trámite sin gestor asignado se muestra con fallback visual al creador
-- (created_by_user_id) — eso es lógica de presentación, no un dato persistido.
--
-- Nullable a propósito: no todo trámite tiene (ni necesita) un gestor asignado distinto del
-- creador. FK a identity.users(id) con ON DELETE SET NULL (a diferencia de created_by_user_id que
-- es RESTRICT): si el usuario asignado se elimina, el trámite simplemente queda sin gestor asignado
-- en vez de bloquear el delete — es una asignación operativa mutable, no un registro de auditoría.
--
-- Índice compuesto (tenant_id, assigned_to_user_id) parcial: sostiene el filtro "trámites asignados
-- a este gestor" del dashboard admin (mismo patrón que
-- ix_procedure_instances_tenant_id_created_by_user_id, checklist A9/A11).
--
-- Aditiva y backward-compatible. Idempotente y reaplicable. Tabla ExcludeFromMigrations (DDL crudo);
-- EF solo mapea la columna (ver ProcedureInstanceConfiguration.cs).
-- =============================================================================

ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS assigned_to_user_id uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_procedure_instances_assigned_to_user')
    THEN
        ALTER TABLE tramites.procedure_instances
            ADD CONSTRAINT fk_procedure_instances_assigned_to_user
            FOREIGN KEY (assigned_to_user_id) REFERENCES identity.users(id)
            ON DELETE SET NULL ON UPDATE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_id_assigned_to_user_id
    ON tramites.procedure_instances (tenant_id, assigned_to_user_id)
    WHERE assigned_to_user_id IS NOT NULL;

COMMENT ON COLUMN tramites.procedure_instances.assigned_to_user_id IS
    'Gestor actualmente responsable del trámite (HU #12162), reasignable por un admin con el '
    'permiso AdminTramiteReasignarGestor. Distinto de created_by_user_id (quién radicó, inmutable). '
    'NULL = sin gestor asignado (no se backfillea con created_by_user_id, ver nota de la migración). '
    'FK identity.users(id) ON DELETE SET NULL.';
