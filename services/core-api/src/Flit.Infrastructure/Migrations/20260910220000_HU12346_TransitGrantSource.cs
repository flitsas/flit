using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>HU #12346 — columna <c>source</c> en habilitaciones OT por compañía.</summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260910220000_HU12346_TransitGrantSource")]
public partial class HU12346_TransitGrantSource : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("111-HU12346-transit-grant-source.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE admin.tenant_transit_office_grants
                DROP CONSTRAINT IF EXISTS ck_tenant_transit_office_grants_source;

            ALTER TABLE admin.tenant_transit_office_grants
                DROP COLUMN IF EXISTS source;
            """);
}
