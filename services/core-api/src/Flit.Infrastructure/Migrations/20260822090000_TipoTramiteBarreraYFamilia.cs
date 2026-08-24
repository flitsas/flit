using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0050 (parte 1 de 2) — barrera <c>wizard_enabled</c>, dominio cerrado de <c>family</c> y
/// trigger de flujo de placa decidido por <c>gate_profile</c> en vez de por <c>modalidad_entrada</c>.
/// DDL: <c>79-tipo-tramite-barrera-y-familia.sql</c>.
/// <para>Aditiva y compatible hacia atrás: no borra datos ni columnas, así que convive con el código
/// que todavía lee <c>modalidad_entrada</c>. El corte destructivo está en
/// <c>80-tramites-reset-fuente-unica.sql</c> y aún no tiene migración registrada, a propósito.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260822090000_TipoTramiteBarreraYFamilia")]
public partial class TipoTramiteBarreraYFamilia : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("79-tipo-tramite-barrera-y-familia.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Restaura el trigger que decide por <c>modalidad_entrada</c>. Solo es válido mientras esa
    /// columna exista — es decir, mientras no se haya aplicado la parte 2.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS tramites.ix_procedure_types_family_wizard_enabled;

            ALTER TABLE tramites.procedure_types
                DROP CONSTRAINT IF EXISTS ck_procedure_types_family;

            ALTER TABLE tramites.procedure_types
                DROP COLUMN IF EXISTS wizard_enabled;

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
