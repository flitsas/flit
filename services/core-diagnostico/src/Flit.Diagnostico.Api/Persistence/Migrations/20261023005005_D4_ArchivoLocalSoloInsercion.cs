using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D4_ArchivoLocalSoloInsercion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "archivo_local",
                schema: "diagnostico",
                comment: "Archivos generados guardados en la base sin file-manager; solo pruebas automáticas (HU #13846). Solo inserción.",
                oldComment: "Archivos generados guardados en la base sin file-manager; solo pruebas automáticas (HU #13846).");

            migrationBuilder.AddCheckConstraint(
                name: "ck_archivo_local_tipo",
                schema: "diagnostico",
                table: "archivo_local",
                sql: "tipo IN ('PDF', 'XLSX', 'ZIP')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_archivo_local_tipo",
                schema: "diagnostico",
                table: "archivo_local");

            migrationBuilder.AlterTable(
                name: "archivo_local",
                schema: "diagnostico",
                comment: "Archivos generados guardados en la base sin file-manager; solo pruebas automáticas (HU #13846).",
                oldComment: "Archivos generados guardados en la base sin file-manager; solo pruebas automáticas (HU #13846). Solo inserción.");
        }
    }
}
