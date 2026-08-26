using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #11313 (Feature #11309) — tabla <c>admin.company_personalized_documents</c> (historial
    /// versionado y reversible del mandato / solicitud de trámite virtual personalizados por
    /// compañía) + columna <c>tramites.procedure_instance_attachments.source_personalized_document_id</c>
    /// (espejo de <c>source_deed_id</c>). Materializa ADR-0042. Posterior a
    /// <c>20260808110000_BackfillCertificaciones</c>.
    /// </summary>
    /// <remarks>
    /// El DDL va en crudo (CHECK de vocabulario cerrado, único parcial de una sola activa por
    /// (tenant_id, document_type), RLS y triggers de row_version/auditoría — nada de eso lo expresa el
    /// generador de EF). La entidad va con <c>ExcludeFromMigrations()</c>, igual que
    /// <c>CompanyDeedEntity</c>: por eso el scaffold de EF solo detectó la columna nueva de
    /// <c>procedure_instance_attachments</c>; se reemplaza aquí por el script completo (tabla + FK +
    /// columna) para que quede en un único script idempotente. Sin backfill: la tabla nace vacía y el
    /// comportamiento es el actual hasta que existan versiones activas (HU #11316).
    /// </remarks>
    public partial class CompanyPersonalizedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("61-company-personalized-documents.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instance_attachments
                  DROP COLUMN IF EXISTS source_personalized_document_id;
                DROP TABLE IF EXISTS admin.company_personalized_documents;
                """);
        }
    }
}
