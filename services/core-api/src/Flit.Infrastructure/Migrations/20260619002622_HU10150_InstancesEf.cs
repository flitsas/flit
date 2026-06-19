using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10150_InstancesEf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Las 4 tablas de instancias (procedure_instances, procedure_instance_actors,
            // procedure_instance_field_values, procedure_instance_status_history) las crea el
            // DDL de HU10150 (06-HU10150-procedure-instances.sql, migración 20260617230400).
            // Esta migración SÓLO reconcilia el modelo EF (snapshot/Designer mapean las entidades)
            // y añade el trigger AC2 de inmutabilidad. No se generan CreateTable/CreateIndex.

            // AC2: field_values inmutables si la instancia padre no está en 'draft'.
            // Idempotente (CREATE OR REPLACE + DROP TRIGGER IF EXISTS). El INSERT inicial del flujo
            // normal pasa porque la instancia recién creada está en 'draft'.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
DECLARE v_status varchar(20);
BEGIN
  SELECT status INTO v_status FROM tramites.procedure_instances
    WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
  -- Borrado en cascada: el padre ya fue eliminado (v_status NULL) → permitir
  IF v_status IS NULL THEN
    RETURN OLD;
  END IF;
  IF v_status IS DISTINCT FROM 'draft' THEN
    RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo draft permite cambios)', v_status
      USING ERRCODE = 'check_violation';
  END IF;
  RETURN COALESCE(NEW, OLD);
END; $$ LANGUAGE plpgsql;
DROP TRIGGER IF EXISTS tr_procedure_instance_field_values_immutable ON tramites.procedure_instance_field_values;
CREATE TRIGGER tr_procedure_instance_field_values_immutable
  BEFORE INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_field_values
  FOR EACH ROW EXECUTE FUNCTION tramites.trg_field_value_immutable();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_procedure_instance_field_values_immutable ON tramites.procedure_instance_field_values;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tramites.trg_field_value_immutable();");
        }
    }
}
