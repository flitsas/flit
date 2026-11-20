using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GDC_AdmiteDpPorCorreoOrganismo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organismo_transito_dp",
                schema: "comparendos",
                columns: table => new
                {
                    organismo_transito_id = table.Column<Guid>(type: "uuid", nullable: false),
                    admite_dp_por_correo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organismo_transito_dp", x => x.organismo_transito_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organismo_transito_dp",
                schema: "comparendos");
        }
    }
}
