using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Bug #13194 (P3-26) — el trigger de inmutabilidad de <c>field_values</c> admite <c>soat_vencimiento</c>
    /// en <c>asignado</c>: es la fecha del PDF del SOAT que el soporte manual del gate necesita. Sin otras
    /// llaves. DDL: <c>129-BUG13194-soat-vencimiento-en-asignado.sql</c>.
    /// </summary>
    public partial class BUG13194_SoatVencimientoEnAsignado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("129-BUG13194-soat-vencimiento-en-asignado.sql"));

        /// <inheritdoc />
        /// <remarks>Restaura la versión de 116-HU12599 (allowlist de 'asignado' sin soat_vencimiento).</remarks>
        protected override void Down(MigrationBuilder migrationBuilder) =>
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
                  IF v_status = 'rechazado' THEN
                    IF EXISTS (
                      SELECT 1 FROM tramites.procedure_instances
                       WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id)
                         AND subsanacion_activa IS TRUE
                    ) THEN
                      RETURN COALESCE(NEW, OLD);
                    END IF;
                  END IF;
                  v_key := COALESCE(NEW.field_key, OLD.field_key);
                  IF v_status = 'preasignacion' AND v_key = 'plate' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  IF v_status = 'asignado' AND v_key IN ('plate', 'soat_estado', 'soat_pagado', 'impuesto_departamental_pagado') THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
                    USING ERRCODE = 'check_violation';
                END; $$ LANGUAGE plpgsql;
                """);
    }
}
