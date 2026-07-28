using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10932 (Feature #10929, RL-flujo-ajustes) — Representante multiempresa con firma única por
    /// persona. El DDL embebido (42-HU10932-legal-representative-companies.sql) crea el puente
    /// admin.legal_representative_companies (RLS + FKs + unique), hace backfill desde las filas
    /// actuales, colapsa duplicados por (tenant, documento) al canónico y ajusta la unicidad de la
    /// persona a (tenant, documento) parcial sobre activos, dejando represented_company_id nullable.
    /// Entidades ExcludeFromMigrations (patrón del baúl); el snapshot refleja el modelo EF.
    /// </remarks>
    public partial class HU10932_LegalRepresentativeCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("42-HU10932-legal-representative-companies.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverso best-effort no destructivo: restaura la unicidad previa (tenant, compañía,
            // documento) y elimina el puente. represented_company_id se deja nullable.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS admin.uq_company_legal_representatives_tenant_document;");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS uq_company_legal_representatives_tenant_company_document " +
                "ON admin.company_legal_representatives(tenant_id, represented_company_id, document_number);");
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.legal_representative_companies CASCADE;");
        }
    }
}
