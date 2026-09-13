using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12407 — bloqueos de OT para cabezas Marca Blanca.
/// DDL: <c>110-HU12407-tenant-transit-office-blocks.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260910210000_HU12407_TenantTransitOfficeBlocks")]
public partial class HU12407_TenantTransitOfficeBlocks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("110-HU12407-tenant-transit-office-blocks.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_tenant_transit_office_blocks_marca_blanca ON admin.tenant_transit_office_blocks;
            DROP FUNCTION IF EXISTS admin.trg_tenant_transit_office_blocks_marca_blanca();
            DROP TABLE IF EXISTS admin.tenant_transit_office_blocks;
            """);
}
