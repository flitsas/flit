using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Extiende <c>trg_autoset_plate_flow_status</c>: al entrar a <c>entregado</c> en matrícula
/// inicial con <c>plate_flow_status</c> aún NULL, si ya hay field_value <c>plate</c> con valor
/// (RUNT o rango) fija <c>asignado</c>, o <c>terminado</c> si la compañía tiene
/// <c>plate_flow_skip_to_terminado</c>. Conserva el Flujo B (<c>preasignado</c>) cuando hay
/// <c>plate_route_active</c> y no hay placa. Red de seguridad ante binario API desfasado.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260729160000_AutosetPlateFlowFullPlate")]
public partial class AutosetPlateFlowFullPlate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION tramites.trg_autoset_plate_flow_status() RETURNS trigger AS $$
            DECLARE
              has_plate boolean;
              skip_gestor boolean;
            BEGIN
              IF NEW.status = 'entregado'
                 AND OLD.status IS DISTINCT FROM 'entregado'
                 AND NEW.modalidad_entrada = 'matricula_inicial'
                 AND NEW.plate_flow_status IS NULL
              THEN
                has_plate := EXISTS (
                  SELECT 1 FROM tramites.procedure_instance_field_values f
                   WHERE f.procedure_instance_id = NEW.id
                     AND f.field_key = 'plate'
                     AND COALESCE(btrim(f.value_text), '') <> '');

                IF has_plate THEN
                  SELECT COALESCE(p.plate_flow_skip_to_terminado, false)
                    INTO skip_gestor
                    FROM admin.tenant_operational_policies p
                   WHERE p.tenant_id = NEW.tenant_id;

                  NEW.plate_flow_status := CASE WHEN skip_gestor THEN 'terminado' ELSE 'asignado' END;
                ELSIF EXISTS (
                      SELECT 1 FROM tramites.procedure_instance_field_values f
                       WHERE f.procedure_instance_id = NEW.id
                         AND f.field_key = 'plate_route_active'
                         AND lower(btrim(f.value_text)) = 'true')
                THEN
                  NEW.plate_flow_status := 'preasignado';
                END IF;
              END IF;
              RETURN NEW;
            END; $$ LANGUAGE plpgsql;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION tramites.trg_autoset_plate_flow_status() RETURNS trigger AS $$
            BEGIN
              IF NEW.status = 'entregado'
                 AND OLD.status IS DISTINCT FROM 'entregado'
                 AND NEW.modalidad_entrada = 'matricula_inicial'
                 AND NEW.plate_flow_status IS NULL
              THEN
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
            """);
    }
}
