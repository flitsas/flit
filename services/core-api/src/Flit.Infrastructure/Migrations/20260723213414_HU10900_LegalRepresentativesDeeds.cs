using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10900 (ADR-0033) — directorio de representantes legales por compañía + escrituras. El DDL
    /// embebido (39-HU10900-legal-representatives-deeds.sql) crea cinco tablas del schema admin
    /// (represented_companies, company_legal_representatives, puente procedure_types, company_deeds,
    /// puente deed_companies) con RLS por tenant (app.current_tenant_id), índices únicos, CHECKs
    /// (vigencia, document_type), FKs (baúl ON DELETE SET NULL, procedure_types, cascadas de puentes)
    /// y triggers (row_version + audit) que el MigrationBuilder no modela. Las entidades están
    /// ExcludeFromMigrations (mismo patrón que HU10642_SignatureVault / HU10585_Prenda): el DDL crudo
    /// lleva el esquema y el snapshot solo refleja el modelo EF.
    ///
    /// <para>El scaffolding no emite <c>CreateTable</c> para estas tablas (excluidas); el
    /// <c>AddColumn(section_type)</c> que <c>ef migrations add</c> detectó pertenece a F08 (ya lo
    /// aplica su DDL cruda 35-F08-conformation-profile.sql) y se omite aquí a propósito para no
    /// duplicarlo. El snapshot regenerado sí registra la columna, resolviendo esa deriva latente.</para>
    /// </remarks>
    public partial class HU10900_LegalRepresentativesDeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("39-HU10900-legal-representatives-deeds.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.company_deed_companies CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.company_deeds CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.company_legal_representative_procedure_types CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.company_legal_representatives CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.represented_companies CASCADE;");
        }
    }
}
