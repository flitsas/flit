using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Feature #10587 / HU #10785 — desacopla el progreso de la ruta de placa del status del trámite.
    /// Agrega <c>tramites.procedure_instances.plate_flow_status</c> (varchar(20), nullable) como SUB-ESTADO
    /// interno (null | preasignado | asignado), ortogonal al status global (que permanece en 'entregado').
    /// Migra los datos existentes (status preasignado/asignado → entregado, conservando el valor previo en
    /// plate_flow_status) y REDEFINE el trigger de inmutabilidad de field_values para que gobierne la
    /// editabilidad de <c>plate</c>/<c>soat_estado</c> por <c>plate_flow_status</c> en vez de por <c>status</c>
    /// (sin esto, al mover el status a 'entregado' el trigger bloquearía esas escrituras). La tabla está
    /// <c>ExcludeFromMigrations</c> (DDL por SQL crudo, HU #10150) → el diff EF queda vacío; todo va en SQL
    /// idempotente. <c>status</c> es varchar(20) sin CHECK: la validez de los estados es aplicativa.
    /// </remarks>
    public partial class HU10785_PlateFlowStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Columna del sub-estado interno de placa.
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS plate_flow_status varchar(20) NULL;

                COMMENT ON COLUMN tramites.procedure_instances.plate_flow_status IS
                  'Feature #10587 / HU #10785 — sub-estado interno de la ruta de placa (null | preasignado | asignado), ortogonal al status global que permanece en entregado.';
                """);

            // 2) Data-migration: los estados de placa dejan de vivir en status. Se mueven al sub-estado y el
            //    status global pasa a 'entregado'. En un solo UPDATE, las expresiones SET leen los valores
            //    ANTERIORES de la fila, así que plate_flow_status toma el status previo de forma atómica.
            migrationBuilder.Sql(
                """
                SET LOCAL row_security = off;
                UPDATE tramites.procedure_instances
                   SET plate_flow_status = status,
                       status = 'entregado'
                 WHERE status IN ('preasignado', 'asignado');
                """);

            // 3) Trigger de inmutabilidad de field_values: la editabilidad de 'plate'/'soat_estado' la gobierna
            //    ahora plate_flow_status (el status es siempre 'entregado' en la ruta de placa).
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
                DECLARE v_status varchar(20);
                DECLARE v_plate varchar(20);
                DECLARE v_key varchar(80);
                BEGIN
                  SELECT status, plate_flow_status INTO v_status, v_plate FROM tramites.procedure_instances
                    WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
                  IF v_status IS NULL THEN
                    RETURN OLD;
                  END IF;
                  IF v_status = 'borrador' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  v_key := COALESCE(NEW.field_key, OLD.field_key);
                  -- Ruta de placa (Feature #10587 / HU #10785): la editabilidad la gobierna el SUB-ESTADO
                  -- interno plate_flow_status, no el status (que permanece en 'entregado').
                  IF v_plate = 'preasignado' AND v_key = 'plate' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  IF v_plate = 'asignado' AND v_key = 'soat_estado' THEN
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
            // 1) Restaura el trigger que lee status (versión HU #10611).
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
                  IF v_status = 'preasignado' AND v_key = 'plate' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  IF v_status = 'asignado' AND v_key = 'soat_estado' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
                    USING ERRCODE = 'check_violation';
                END; $$ LANGUAGE plpgsql;
                """);

            // 2) Devuelve el sub-estado al status y limpia la columna.
            migrationBuilder.Sql(
                """
                SET LOCAL row_security = off;
                UPDATE tramites.procedure_instances
                   SET status = plate_flow_status,
                       plate_flow_status = NULL
                 WHERE plate_flow_status IN ('preasignado', 'asignado');
                """);

            // 3) Elimina la columna.
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  DROP COLUMN IF EXISTS plate_flow_status;
                """);
        }
    }
}
