using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10936 (Feature #10929, RL-flujo-ajustes) — Persistir la escritura usada. El DDL embebido
    /// (43-HU10936-attachment-source-deed.sql) agrega la columna nullable
    /// tramites.procedure_instance_attachments.source_deed_id (ADD COLUMN IF NOT EXISTS, idempotente)
    /// con la referencia a la escritura (admin.company_deeds.id) que entró al registro. El snapshot EF
    /// refleja la nueva propiedad del modelo.
    /// </remarks>
    public partial class HU10936_AttachmentDeedRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("43-HU10936-attachment-source-deed.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverso best-effort no destructivo.
            migrationBuilder.Sql(
                "ALTER TABLE tramites.procedure_instance_attachments DROP COLUMN IF EXISTS source_deed_id;");
        }
    }
}
