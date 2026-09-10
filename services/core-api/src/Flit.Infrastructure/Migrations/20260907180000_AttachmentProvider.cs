using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Columna <c>tramites.procedure_instance_attachments.provider</c> para distinguir impronta
    /// Kyverum RUNT de carga manual al estampar sellos solo en la vía manual (consolidado → OT).
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260907180000_AttachmentProvider")]
    public partial class AttachmentProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("95-attachment-provider.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE tramites.procedure_instance_attachments DROP COLUMN IF EXISTS provider;");
        }
    }
}
