using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Trámites Rework — Slice 7: firma electrónica (mock) + FUR/contrato (mock, sin librería PDF).
    /// El DDL (17-tramites-firma.sql) crea procedure_instance_signatures con sus índices (FK +
    /// composite tenant/instance), RLS por tenant y trigger de auditoría. El snapshot/Designer
    /// reconcilian el modelo EF (no se emite CreateTable desde el MigrationBuilder). El proveedor de
    /// firma y el generador de FUR son MOCKs (ver ISignatureProvider / IFurDocumentGenerator en
    /// Application). El FUR/contrato se persisten como adjuntos (procedure_instance_attachments).
    /// </remarks>
    public partial class TramitesFirma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("17-tramites-firma.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS tramites.procedure_instance_signatures;
");
        }
    }
}
