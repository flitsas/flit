using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14642 — overrides manuales de persona RUNT por tenant. DDL:
/// <c>203-HU14642-tenant-runt-person-overrides.sql</c>. Sin Designer: sin los atributos la migración no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261119000000_HU14642TenantRuntPersonOverrides")]
public partial class HU14642TenantRuntPersonOverrides : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("203-HU14642-tenant-runt-person-overrides.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS admin.tenant_runt_person_overrides;");
}
