using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// FEATURE-08 / Fase 2b — columnas de perfil de conformación en tablas existentes:
    /// · <c>tramites.procedure_types</c>: <c>version integer</c>, <c>gate_profile jsonb</c> (CFD-01).
    /// · <c>tramites.procedure_sections</c>: <c>section_type varchar(40)</c> + CHECK cerrado (CFD-09).
    /// · <c>tramites.procedure_document_requirements</c>: <c>is_dummy</c>, <c>condition_group</c>,
    ///   <c>row_version</c>, <c>updated_at/by</c> + triggers A16 faltantes de HU10155 (CFD-06).
    /// DDL embebido: 35-F08-conformation-profile.sql. Down reversible.
    /// ⚠️ Designer.cs omitido — regenerar con <c>dotnet ef migrations add</c> antes del merge.
    /// </remarks>
    public partial class F08_ConformationProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("35-F08-conformation-profile.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS tr_procedure_doc_req_row_version
                    ON tramites.procedure_document_requirements;
                DROP TRIGGER IF EXISTS tr_procedure_doc_req_audit
                    ON tramites.procedure_document_requirements;
                ALTER TABLE tramites.procedure_document_requirements
                    DROP COLUMN IF EXISTS is_dummy,
                    DROP COLUMN IF EXISTS condition_group,
                    DROP COLUMN IF EXISTS row_version,
                    DROP COLUMN IF EXISTS updated_at,
                    DROP COLUMN IF EXISTS updated_by;
                ALTER TABLE tramites.procedure_sections
                    DROP CONSTRAINT IF EXISTS ck_procedure_sections_section_type;
                ALTER TABLE tramites.procedure_sections
                    DROP COLUMN IF EXISTS section_type;
                ALTER TABLE tramites.procedure_types
                    DROP COLUMN IF EXISTS version,
                    DROP COLUMN IF EXISTS gate_profile;
                """);
        }
    }
}
