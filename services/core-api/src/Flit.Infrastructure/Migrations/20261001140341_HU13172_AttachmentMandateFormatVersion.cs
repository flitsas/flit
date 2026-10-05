using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #13172 (Feature #13118, Épica #13090) — registra en el adjunto del contrato de mandato el formato y la versión
    /// de plantilla con que se emitió (<c>tramites.procedure_instance_attachments.mandate_format_code</c> y
    /// <c>mandate_format_version</c>), para que regenerarlo reproduzca esa versión. Aditiva e idempotente; los mandatos de
    /// sistema ya emitidos quedan con versión 0 (redacción del generador). DDL:
    /// <c>125-HU13172-attachment-mandate-format-version.sql</c>. Descubrible al arrancar por el Designer.cs.
    /// </summary>
    public partial class HU13172_AttachmentMandateFormatVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("125-HU13172-attachment-mandate-format-version.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instance_attachments
                    DROP CONSTRAINT IF EXISTS ck_procedure_instance_attachments_mandate_format_version;
                ALTER TABLE tramites.procedure_instance_attachments
                    DROP COLUMN IF EXISTS mandate_format_version,
                    DROP COLUMN IF EXISTS mandate_format_code;
                """);
    }
}
