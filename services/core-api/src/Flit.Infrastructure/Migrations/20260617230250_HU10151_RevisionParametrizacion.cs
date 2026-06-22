using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10151 (Revisión) — Motor parametrización: DDL incremental | Feature #10116
    /// Agrega: tramites.consultation_templates (G1), columnas publication_status /
    /// published_at / published_by / row_version en tramites.procedure_types (G2),
    /// columnas is_locked / lock_reason / consultation_template_id / row_version en
    /// tramites.form_fields (G3), row_version en procedure_steps / procedure_sections (A16).
    /// Seeds mínimos: 4 aristas, 6 fuentes, 4 plantillas, 5 tipos draft en 3 familias.
    /// </remarks>
    public partial class HU10151_RevisionParametrizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("04-HU10151-revision-parametrizacion.sql"));
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("04-HU10151-seeds-minimos.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(Phase1DdlDown.Hu10151Revision);
    }
}
