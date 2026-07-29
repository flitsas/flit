using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Sub-flujo post-radicación matrícula: parámetro compañía plate_flow_skip_to_terminado +
/// permitir field_values soat_pagado / impuesto_departamental_pagado en sub-estado asignado.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260729140000_PlateFlowTerminado")]
public partial class PlateFlowTerminado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.tenant_operational_policies
              ADD COLUMN IF NOT EXISTS plate_flow_skip_to_terminado boolean NOT NULL DEFAULT false;

            COMMENT ON COLUMN admin.tenant_operational_policies.plate_flow_skip_to_terminado IS
              'Con placa completa al radicar, omite Asignado (checks gestor) y aterriza en Terminado.';
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
              IF v_status = 'borrador' THEN
                RETURN COALESCE(NEW, OLD);
              END IF;
              -- Subsanación activa sobre rechazado: edición de datos permitida.
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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.tenant_operational_policies
              DROP COLUMN IF EXISTS plate_flow_skip_to_terminado;
            """);
    }
}
