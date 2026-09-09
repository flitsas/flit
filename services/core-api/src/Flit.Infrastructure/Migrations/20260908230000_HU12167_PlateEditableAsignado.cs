using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12167 (Feature #12156) — extiende <c>tramites.trg_field_value_immutable</c> para permitir
/// escribir <c>field_values(field_key='plate')</c> también con la instancia en sub-estado
/// <c>asignado</c> (corrección de placa dentro de la ventana de 1 hora), no solo en
/// <c>preasignado</c> (asignación inicial, HU #10654). DDL: <c>106-HU12167-plate-editable-asignado.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260908230000_HU12167_PlateEditableAsignado")]
public partial class HU12167_PlateEditableAsignado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("106-HU12167-plate-editable-asignado.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Restaura la versión anterior del trigger (20260729140000_PlateFlowTerminado): sin la rama de
    /// 'plate' en 'asignado'. No hay dato que perder — es lógica de trigger, no una columna.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
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
              IF v_plate = 'preasignado' AND v_key = 'plate' THEN
                RETURN COALESCE(NEW, OLD);
              END IF;
              IF v_plate = 'asignado' AND v_key IN ('soat_estado', 'soat_pagado', 'impuesto_departamental_pagado') THEN
                RETURN COALESCE(NEW, OLD);
              END IF;
              RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
                USING ERRCODE = 'check_violation';
            END; $$ LANGUAGE plpgsql;
            """);
}
