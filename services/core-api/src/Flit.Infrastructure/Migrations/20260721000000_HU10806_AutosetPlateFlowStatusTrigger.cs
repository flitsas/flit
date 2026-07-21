using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10806 (Feature #10587, Alternativa C) — red de seguridad en BD para el sub-estado de placa.
    /// Trigger <c>BEFORE UPDATE</c> en <c>tramites.procedure_instances</c> que, al entrar a
    /// <c>entregado</c> en una <c>matricula_inicial</c> cuyo <c>plate_flow_status</c> quedó en NULL
    /// (p. ej. binario del API desfasado que no aplicó <c>PlatePreassignPolicy</c>), lo fija en
    /// <c>preasignado</c> SIEMPRE que: exista el field_value <c>plate_route_active='true'</c> (ruta
    /// persistida en borrador por el wizard) Y NO exista un field_value <c>plate</c> con valor (Flujo B,
    /// sin placa). El trigger lee ÚNICAMENTE field_values del propio trámite (<c>procedure_instance_id =
    /// NEW.id</c>): sin lectura cross-tenant, sin <c>SECURITY DEFINER</c>. Es idempotente (solo actúa si
    /// <c>plate_flow_status IS NULL</c>, nunca pisa <c>asignado</c> del Flujo A) y degrada seguro (si no
    /// hay <c>plate_route_active</c>, no hace nada → ruta estándar, comportamiento previo). Tabla
    /// <c>ExcludeFromMigrations</c> (DDL por SQL crudo, HU #10150) → todo va en SQL idempotente.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260721000000_HU10806_AutosetPlateFlowStatusTrigger")]
    public partial class HU10806_AutosetPlateFlowStatusTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION tramites.trg_autoset_plate_flow_status() RETURNS trigger AS $$
                BEGIN
                  -- Solo al ENTRAR a 'entregado' (radicación), matrícula inicial y sub-estado aún sin fijar.
                  IF NEW.status = 'entregado'
                     AND OLD.status IS DISTINCT FROM 'entregado'
                     AND NEW.modalidad_entrada = 'matricula_inicial'
                     AND NEW.plate_flow_status IS NULL
                  THEN
                    -- Ruta de preasignación activa (persistida en borrador) Y sin placa elegida ⇒ Flujo B.
                    IF EXISTS (
                          SELECT 1 FROM tramites.procedure_instance_field_values f
                           WHERE f.procedure_instance_id = NEW.id
                             AND f.field_key = 'plate_route_active'
                             AND lower(btrim(f.value_text)) = 'true')
                       AND NOT EXISTS (
                          SELECT 1 FROM tramites.procedure_instance_field_values f
                           WHERE f.procedure_instance_id = NEW.id
                             AND f.field_key = 'plate'
                             AND COALESCE(btrim(f.value_text), '') <> '')
                    THEN
                      NEW.plate_flow_status := 'preasignado';
                    END IF;
                  END IF;
                  RETURN NEW;
                END; $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS tr_procedure_instances_autoset_plate_flow ON tramites.procedure_instances;
                CREATE TRIGGER tr_procedure_instances_autoset_plate_flow
                  BEFORE UPDATE ON tramites.procedure_instances
                  FOR EACH ROW
                  EXECUTE FUNCTION tramites.trg_autoset_plate_flow_status();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_procedure_instances_autoset_plate_flow ON tramites.procedure_instances;
                DROP FUNCTION IF EXISTS tramites.trg_autoset_plate_flow_status();
                """);
        }
    }
}
