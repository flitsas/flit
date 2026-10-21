using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D0_AislamientoYAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lecturas_backoffice",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa del lector (token del backoffice)."),
                    empresa_consultada = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa cuyos datos se leyeron (?empresa=)."),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "@pii:low Usuario que leyó (claim sub, seudónimo)."),
                    ruta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Ruta y consulta leídas, sin cuerpo."),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()", comment: "Momento de la lectura.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lecturas_backoffice", x => x.id);
                },
                comment: "Lecturas entre empresas del backoffice FLIT (D-16, ADR-0069/ADR-0072). Solo inserción.");

            migrationBuilder.CreateIndex(
                name: "ix_lecturas_backoffice_tenant_empresa_fecha",
                schema: "diagnostico",
                table: "lecturas_backoffice",
                columns: new[] { "tenant_id", "empresa_consultada", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_lecturas_backoffice_tenant_fecha",
                schema: "diagnostico",
                table: "lecturas_backoffice",
                columns: new[] { "tenant_id", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lecturas_backoffice",
                schema: "diagnostico");
        }
    }
}
