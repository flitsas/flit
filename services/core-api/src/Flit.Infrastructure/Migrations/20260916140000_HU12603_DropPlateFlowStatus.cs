using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12603 (Feature #12595, ADR-0059) — elimina <c>tramites.procedure_instances.plate_flow_status</c>
/// y <c>admin.tenant_operational_policies.plate_flow_skip_to_terminado</c>. Último paso del retiro del
/// sub-estado de placa: la migración de datos (HU #12599) ya lo vació y el código dejó de leerlo.
/// DDL: <c>117-HU12603-drop-plate-flow-status.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260916140000_HU12603_DropPlateFlowStatus")]
public partial class HU12603_DropPlateFlowStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("117-HU12603-drop-plate-flow-status.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Recrea las dos columnas con el reverse map best-effort del sub-estado (mismo criterio que el
    /// Down de HU #12599): <c>preasignacion → preasignado</c>, <c>asignado → asignado</c>; el resto en
    /// NULL. El flag de compañía vuelve en <c>false</c>: su valor previo no es recuperable.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            SET LOCAL row_security = off;

            ALTER TABLE tramites.procedure_instances
                ADD COLUMN IF NOT EXISTS plate_flow_status varchar(20) NULL;

            UPDATE tramites.procedure_instances
               SET plate_flow_status = CASE status WHEN 'preasignacion' THEN 'preasignado' WHEN 'asignado' THEN 'asignado' END
             WHERE status IN ('preasignacion', 'asignado');

            ALTER TABLE admin.tenant_operational_policies
                ADD COLUMN IF NOT EXISTS plate_flow_skip_to_terminado boolean NOT NULL DEFAULT false;
            """);
}
