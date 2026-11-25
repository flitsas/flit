using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0092 — tabla <c>platform.tenant_product_credentials</c> para la API key del impuesto por empresa.
/// DDL: <c>204-impuesto-tenant-product-credentials.sql</c>. Atributos inline (sin Designer).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261120000000_ImpuestoTenantProductCredentials")]
public partial class ImpuestoTenantProductCredentials : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("204-impuesto-tenant-product-credentials.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS platform.tenant_product_credentials;");
}
