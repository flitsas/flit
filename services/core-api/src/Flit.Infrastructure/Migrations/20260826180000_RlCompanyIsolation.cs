using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Ficha de empresa aislada por representante legal, bajas no visibles en consumo, y una
/// escritura activa por ficha. DDL: <c>94-rl-company-isolation.sql</c>
/// (el índice único por NIT se suelta antes de clonar filas; el UPDATE de escrituras no usa JOIN a la tabla destino).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260826180000_RlCompanyIsolation")]
public partial class RlCompanyIsolation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("94-rl-company-isolation.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS admin.uq_represented_companies_owner_nit;
            DROP INDEX IF EXISTS admin.uq_represented_companies_orphan_nit;
            ALTER TABLE admin.represented_companies
                DROP CONSTRAINT IF EXISTS fk_represented_companies_representative;
            DROP INDEX IF EXISTS admin.ix_represented_companies_representative_id;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_represented_companies_tenant_document
                ON admin.represented_companies (tenant_id, document_number);
            ALTER TABLE admin.represented_companies DROP COLUMN IF EXISTS representative_id;
            ALTER TABLE admin.represented_companies DROP COLUMN IF EXISTS is_active;
            """);
}