using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D2_TipoErrorCelda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tipo_error",
                schema: "diagnostico",
                table: "consultas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "Causa del ERROR (HU #13730): código de TipoErrorFuente o BLOQUEADA_POR_RUNT; solo con estado ERROR.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_consultas_tipo_error",
                schema: "diagnostico",
                table: "consultas",
                sql: "tipo_error IS NULL OR estado = 'ERROR'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_consultas_tipo_error",
                schema: "diagnostico",
                table: "consultas");

            migrationBuilder.DropColumn(
                name: "tipo_error",
                schema: "diagnostico",
                table: "consultas");
        }
    }
}
