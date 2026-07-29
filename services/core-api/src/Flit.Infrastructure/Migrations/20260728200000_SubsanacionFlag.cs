using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <remarks>
    /// Subsanación por flag (no estado de negocio): columnas <c>subsanacion_activa</c> /
    /// <c>subsanacion_count</c>; data-migration de filas <c>status='subsanacion'</c> →
    /// <c>rechazado</c> + flag; trigger de field_values editable con
    /// <c>borrador</c> o (<c>rechazado</c> + flag).
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260728200000_SubsanacionFlag")]
    public partial class SubsanacionFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS subsanacion_activa boolean NOT NULL DEFAULT false;

                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS subsanacion_count integer NOT NULL DEFAULT 0;

                COMMENT ON COLUMN tramites.procedure_instances.subsanacion_activa IS
                  'Subsanación activa sobre rechazado: reabre edición sin cambiar status de negocio.';
                COMMENT ON COLUMN tramites.procedure_instances.subsanacion_count IS
                  'Veces que se activó la subsanación en este expediente (contador monotónico).';
                """);

            migrationBuilder.Sql(
                """
                SET LOCAL row_security = off;
                UPDATE tramites.procedure_instances
                   SET status = 'rechazado',
                       subsanacion_activa = true,
                       subsanacion_count = GREATEST(subsanacion_count, 1)
                 WHERE status = 'subsanacion';
                """);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
                DECLARE v_status varchar(20);
                DECLARE v_plate varchar(20);
                DECLARE v_subs boolean;
                DECLARE v_key varchar(80);
                BEGIN
                  SELECT status, plate_flow_status, subsanacion_activa
                    INTO v_status, v_plate, v_subs
                    FROM tramites.procedure_instances
                   WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
                  IF v_status IS NULL THEN
                    RETURN OLD;
                  END IF;
                  IF v_status = 'borrador'
                     OR (v_status = 'rechazado' AND COALESCE(v_subs, false))
                     OR v_status = 'subsanacion' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  v_key := COALESCE(NEW.field_key, OLD.field_key);
                  IF v_plate = 'preasignado' AND v_key = 'plate' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  IF v_plate = 'asignado' AND v_key = 'soat_estado' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador o rechazado con subsanación activa permiten cambios)', v_status
                    USING ERRCODE = 'check_violation';
                END; $$ LANGUAGE plpgsql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET LOCAL row_security = off;
                UPDATE tramites.procedure_instances
                   SET status = 'subsanacion'
                 WHERE status = 'rechazado' AND subsanacion_activa = true;
                """);

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
                  IF v_status = 'borrador' OR v_status = 'subsanacion' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  v_key := COALESCE(NEW.field_key, OLD.field_key);
                  IF v_plate = 'preasignado' AND v_key = 'plate' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  IF v_plate = 'asignado' AND v_key = 'soat_estado' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador/subsanacion permiten cambios)', v_status
                    USING ERRCODE = 'check_violation';
                END; $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances DROP COLUMN IF EXISTS subsanacion_activa;
                ALTER TABLE tramites.procedure_instances DROP COLUMN IF EXISTS subsanacion_count;
                """);
        }
    }
}
