using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// N 03 — MÓDULO ESTADOS (ADR-0022). Migra el vocabulario de estados del trámite al modelo
    /// de negocio en español: borrador · anulado · preparado · entregado · aprobado · rechazado.
    /// Mapeo (validado contra el seed 21-HU10240-analytics-dev-seed.sql):
    ///   draft→borrador · submitted/in_review/pending_ot→entregado · completed/approved_ot→aprobado
    ///   · cancelled→anulado · rejected_ot→rechazado.
    /// Además: DEFAULT de status → 'borrador'; recrea el trigger de inmutabilidad de field_values
    /// (comparaba contra 'draft') y el índice parcial de borradores finalizados (HU #10349).
    /// La tabla está ExcludeFromMigrations (DDL crudo, HU #10150): todo va como SQL idempotente.
    /// El Down aplica el mapeo inverso CON PÉRDIDA de granularidad (entregado→submitted,
    /// aprobado→approved_ot): in_review/pending_ot/completed no se distinguen tras migrar.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260702120000_N03_EstadosNegocioTramite")]
    public partial class N03_EstadosNegocioTramite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- 1) Datos: estado actual de las instancias.
                UPDATE tramites.procedure_instances SET status = CASE status
                    WHEN 'draft' THEN 'borrador'
                    WHEN 'submitted' THEN 'entregado'
                    WHEN 'in_review' THEN 'entregado'
                    WHEN 'pending_ot' THEN 'entregado'
                    WHEN 'completed' THEN 'aprobado'
                    WHEN 'approved_ot' THEN 'aprobado'
                    WHEN 'cancelled' THEN 'anulado'
                    WHEN 'rejected_ot' THEN 'rechazado'
                    ELSE status END
                WHERE status IN ('draft','submitted','in_review','pending_ot','completed','approved_ot','cancelled','rejected_ot');

                -- 2) Datos: historial de transiciones (from/to) con el mismo mapeo.
                UPDATE tramites.procedure_instance_status_history SET
                    from_status = CASE from_status
                        WHEN 'draft' THEN 'borrador'
                        WHEN 'submitted' THEN 'entregado'
                        WHEN 'in_review' THEN 'entregado'
                        WHEN 'pending_ot' THEN 'entregado'
                        WHEN 'completed' THEN 'aprobado'
                        WHEN 'approved_ot' THEN 'aprobado'
                        WHEN 'cancelled' THEN 'anulado'
                        WHEN 'rejected_ot' THEN 'rechazado'
                        ELSE from_status END,
                    to_status = CASE to_status
                        WHEN 'draft' THEN 'borrador'
                        WHEN 'submitted' THEN 'entregado'
                        WHEN 'in_review' THEN 'entregado'
                        WHEN 'pending_ot' THEN 'entregado'
                        WHEN 'completed' THEN 'aprobado'
                        WHEN 'approved_ot' THEN 'aprobado'
                        WHEN 'cancelled' THEN 'anulado'
                        WHEN 'rejected_ot' THEN 'rechazado'
                        ELSE to_status END
                WHERE from_status IN ('draft','submitted','in_review','pending_ot','completed','approved_ot','cancelled','rejected_ot')
                   OR to_status IN ('draft','submitted','in_review','pending_ot','completed','approved_ot','cancelled','rejected_ot');

                -- 3) DEFAULT del estado inicial.
                ALTER TABLE tramites.procedure_instances ALTER COLUMN status SET DEFAULT 'borrador';

                -- 4) Trigger de inmutabilidad de field_values (HU #10150 AC2): la edición solo se
                --    permite en 'borrador' (antes 'draft'). Mismo cuerpo, vocabulario N 03.
                CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
                DECLARE v_status varchar(20);
                BEGIN
                  SELECT status INTO v_status FROM tramites.procedure_instances
                    WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
                  -- Borrado en cascada: el padre ya fue eliminado (v_status NULL) → permitir
                  IF v_status IS NULL THEN
                    RETURN OLD;
                  END IF;
                  IF v_status IS DISTINCT FROM 'borrador' THEN
                    RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
                      USING ERRCODE = 'check_violation';
                  END IF;
                  RETURN COALESCE(NEW, OLD);
                END; $$ LANGUAGE plpgsql;

                -- 5) Índice parcial de borradores finalizados (HU #10349): filtro con 'borrador'.
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_draft_finalized;
                CREATE INDEX IF NOT EXISTS ix_procedure_instances_draft_finalized
                  ON tramites.procedure_instances(tenant_id, draft_finalized_at)
                  WHERE status = 'borrador' AND draft_finalized_at IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- Mapeo inverso CON PÉRDIDA: entregado→submitted, aprobado→approved_ot,
                -- rechazado→rejected_ot, anulado→cancelled (in_review/pending_ot/completed no se recuperan).
                UPDATE tramites.procedure_instances SET status = CASE status
                    WHEN 'borrador' THEN 'draft'
                    WHEN 'preparado' THEN 'draft'
                    WHEN 'entregado' THEN 'submitted'
                    WHEN 'aprobado' THEN 'approved_ot'
                    WHEN 'anulado' THEN 'cancelled'
                    WHEN 'rechazado' THEN 'rejected_ot'
                    ELSE status END
                WHERE status IN ('borrador','preparado','entregado','aprobado','anulado','rechazado');

                UPDATE tramites.procedure_instance_status_history SET
                    from_status = CASE from_status
                        WHEN 'borrador' THEN 'draft'
                        WHEN 'preparado' THEN 'draft'
                        WHEN 'entregado' THEN 'submitted'
                        WHEN 'aprobado' THEN 'approved_ot'
                        WHEN 'anulado' THEN 'cancelled'
                        WHEN 'rechazado' THEN 'rejected_ot'
                        ELSE from_status END,
                    to_status = CASE to_status
                        WHEN 'borrador' THEN 'draft'
                        WHEN 'preparado' THEN 'draft'
                        WHEN 'entregado' THEN 'submitted'
                        WHEN 'aprobado' THEN 'approved_ot'
                        WHEN 'anulado' THEN 'cancelled'
                        WHEN 'rechazado' THEN 'rejected_ot'
                        ELSE to_status END
                WHERE from_status IN ('borrador','preparado','entregado','aprobado','anulado','rechazado')
                   OR to_status IN ('borrador','preparado','entregado','aprobado','anulado','rechazado');

                ALTER TABLE tramites.procedure_instances ALTER COLUMN status SET DEFAULT 'draft';

                CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
                DECLARE v_status varchar(20);
                BEGIN
                  SELECT status INTO v_status FROM tramites.procedure_instances
                    WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
                  IF v_status IS NULL THEN
                    RETURN OLD;
                  END IF;
                  IF v_status IS DISTINCT FROM 'draft' THEN
                    RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo draft permite cambios)', v_status
                      USING ERRCODE = 'check_violation';
                  END IF;
                  RETURN COALESCE(NEW, OLD);
                END; $$ LANGUAGE plpgsql;

                DROP INDEX IF EXISTS tramites.ix_procedure_instances_draft_finalized;
                CREATE INDEX IF NOT EXISTS ix_procedure_instances_draft_finalized
                  ON tramites.procedure_instances(tenant_id, draft_finalized_at)
                  WHERE status = 'draft' AND draft_finalized_at IS NOT NULL;
                """);
        }
    }
}
