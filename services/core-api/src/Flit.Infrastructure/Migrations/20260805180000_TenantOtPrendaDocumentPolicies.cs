using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Política de documento de prenda por compañía + OT: default obligatorio, check = opt-out (opcional).
/// DDL embebido 56-tenant-ot-prenda-document-policies.sql (RLS + triggers).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260805180000_TenantOtPrendaDocumentPolicies")]
public partial class TenantOtPrendaDocumentPolicies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("56-tenant-ot-prenda-document-policies.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "tenant_transit_office_prenda_document_policies",
            schema: "admin");
    }
}
