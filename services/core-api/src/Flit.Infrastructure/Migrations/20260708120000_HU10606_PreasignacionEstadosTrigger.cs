using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10606 (Feature #10587, preasignación de placa). Amplía el trigger de inmutabilidad de
    /// field_values (N 03) para la ruta de preasignación: además de <c>borrador</c>, permite
    /// escribir/actualizar ÚNICAMENTE el field_value <c>plate</c> cuando la instancia está en
    /// estado <c>preasignado</c> (Flujo B: el OT asigna la placa a un trámite ya enviado). El resto
    /// de los field_values sigue inmutable fuera de borrador. Solo reemplaza la FUNCTION (el TRIGGER
    /// se conserva); tabla ExcludeFromMigrations (DDL crudo, HU #10150) → SQL idempotente.
    /// Los estados <c>preasignado</c>/<c>asignado</c> no requieren tocar CHECK (status es varchar(20)
    /// sin CHECK); su validez es aplicativa (TramiteEstado + TramiteStateMachine).
    /// </remarks>
    public partial class HU10606_PreasignacionEstadosTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
                DECLARE v_status varchar(20);
                BEGIN
                  SELECT status INTO v_status FROM tramites.procedure_instances
                    WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
                  -- Borrado en cascada: el padre ya fue eliminado (v_status NULL) → permitir
                  IF v_status IS NULL THEN
                    RETURN OLD;
                  END IF;
                  -- Borrador permite cualquier cambio.
                  IF v_status = 'borrador' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  -- Preasignación de placa (Feature #10587): en 'preasignado' el OT puede escribir o
                  -- actualizar SOLO el field_value 'plate' (Flujo B). El resto sigue inmutable.
                  IF v_status = 'preasignado' AND COALESCE(NEW.field_key, OLD.field_key) = 'plate' THEN
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
                -- Restaura la versión N 03: solo 'borrador' permite cambios.
                CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
                DECLARE v_status varchar(20);
                BEGIN
                  SELECT status INTO v_status FROM tramites.procedure_instances
                    WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
                  IF v_status IS NULL THEN
                    RETURN OLD;
                  END IF;
                  IF v_status IS DISTINCT FROM 'borrador' THEN
                    RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
                      USING ERRCODE = 'check_violation';
                  END IF;
                  RETURN COALESCE(NEW, OLD);
                END; $$ LANGUAGE plpgsql;
                """);
        }
    }
}
