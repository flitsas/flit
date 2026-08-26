using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Informes programados sobre una consulta guardada (Reportes 2.0, HU-D, segunda ola): permite
/// <c>tenant_id</c> nulo únicamente para <c>report_type='consulta'</c> con alcance SuperAdmin. Ver
/// el detalle comentado en <c>Persistence/Sql/Ddl/75-informes-consulta-personalizada.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260818120000_InformesConsultaPersonalizada")]
public partial class InformesConsultaPersonalizada : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("75-informes-consulta-personalizada.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE analytics.report_schedules " +
            "DROP CONSTRAINT IF EXISTS report_schedules_consulta_format_check, " +
            "DROP CONSTRAINT IF EXISTS report_schedules_consulta_shape_check, " +
            "DROP CONSTRAINT IF EXISTS report_schedules_saved_query_scope_check, " +
            "DROP CONSTRAINT IF EXISTS report_schedules_report_type_check, " +
            "DROP COLUMN IF EXISTS saved_query_id, " +
            "DROP COLUMN IF EXISTS saved_query_scope, " +
            "ALTER COLUMN tenant_id SET NOT NULL;");
        migrationBuilder.Sql(
            "ALTER TABLE analytics.report_schedules ADD CONSTRAINT report_schedules_report_type_check " +
            "CHECK (report_type IN ('resumen','operacion','ot','uso','productividad'));");
    }
}
