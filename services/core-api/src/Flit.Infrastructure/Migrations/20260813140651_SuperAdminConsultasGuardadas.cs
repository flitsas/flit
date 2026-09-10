using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Consultas guardadas de SuperAdmin en modo «todas las compañías»: el gemelo de
/// <c>analytics.company_saved_queries</c>, pero sin <c>tenant_id</c> y compartido entre todo el
/// equipo de SuperAdmin. Ver el detalle comentado en
/// <c>Persistence/Sql/Ddl/72-superadmin-consultas-guardadas.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260813140651_SuperAdminConsultasGuardadas")]
public partial class SuperAdminConsultasGuardadas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("72-superadmin-consultas-guardadas.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS analytics.superadmin_saved_queries;");
}
