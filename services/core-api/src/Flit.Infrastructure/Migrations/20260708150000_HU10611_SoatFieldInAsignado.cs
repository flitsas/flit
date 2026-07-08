using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10611 (Feature #10587). Amplía el trigger de inmutabilidad de field_values para la ruta de
    /// placa: además de <c>plate</c> en <c>preasignado</c> (HU #10606), permite escribir el field_value
    /// <c>soat_estado</c> cuando la instancia está en <c>asignado</c> (la compañía registra el estado
    /// del SOAT tras la asignación: PDF o consulta RUNT). El resto sigue inmutable fuera de borrador.
    /// Solo reemplaza la FUNCTION; SQL idempotente. Auto-aplicable en el arranque.
    /// </remarks>
    public partial class HU10611_SoatFieldInAsignado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
                DECLARE v_status varchar(20);
                DECLARE v_key varchar(80);
                BEGIN
                  SELECT status INTO v_status FROM tramites.procedure_instances
                    WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
                  IF v_status IS NULL THEN
                    RETURN OLD;
                  END IF;
                  IF v_status = 'borrador' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  v_key := COALESCE(NEW.field_key, OLD.field_key);
                  -- Preasignación de placa (Feature #10587): el OT escribe 'plate' en preasignado.
                  IF v_status = 'preasignado' AND v_key = 'plate' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  -- SOAT en la ruta de placa: la compañía escribe 'soat_estado' en asignado.
                  IF v_status = 'asignado' AND v_key = 'soat_estado' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
                    USING ERRCODE = 'check_violation';
                END; $$ LANGUAGE plpgsql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
                DECLARE v_status varchar(20);
                BEGIN
                  SELECT status INTO v_status FROM tramites.procedure_instances
                    WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
                  IF v_status IS NULL THEN
                    RETURN OLD;
                  END IF;
                  IF v_status = 'borrador' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  IF v_status = 'preasignado' AND COALESCE(NEW.field_key, OLD.field_key) = 'plate' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
                    USING ERRCODE = 'check_violation';
                END; $$ LANGUAGE plpgsql;
                """);
        }
    }
}
