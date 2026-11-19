using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContingenciaProveedorRunt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runt_contingencies",
                schema: "consultas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    updated_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runt_contingencies", x => x.id);
                    table.CheckConstraint("ck_runt_contingencies_kind", "kind IN ('vehicle_vin', 'vehicle_plate', 'conductor')");
                    table.CheckConstraint("ck_runt_contingencies_kind_provider", "provider IS NULL OR (kind IN ('vehicle_vin', 'vehicle_plate') AND provider IN ('kyverum_runt', 'verifik')) OR (kind = 'conductor' AND provider IN ('kyverum_runt_conductor', 'verifik_conductor'))");
                },
                comment: "Contingencia de proveedor RUNT por tipo de consulta. Global de plataforma, sin tenant_id: la empresa no la ve. provider nulo = sin contingencia. Épica #14644.");

            migrationBuilder.CreateIndex(
                name: "uq_runt_contingencies_kind",
                schema: "consultas",
                table: "runt_contingencies",
                column: "kind",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "runt_contingencies",
                schema: "consultas");
        }
    }
}
