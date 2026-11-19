using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P4c_DerivacionRepublicaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "republicaciones",
                schema: "diagnostico",
                table: "derivaciones",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0,
                comment: "Veces que el reconciliador re-publicó el ítem colgado (incremento 3); a la segunda queda FALLIDO.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_derivaciones_republicaciones",
                schema: "diagnostico",
                table: "derivaciones",
                sql: "republicaciones >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_derivaciones_republicaciones",
                schema: "diagnostico",
                table: "derivaciones");

            migrationBuilder.DropColumn(
                name: "republicaciones",
                schema: "diagnostico",
                table: "derivaciones");
        }
    }
}
