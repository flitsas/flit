using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D2_IntentosConsultaRegistro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "identificador_consultado",
                schema: "diagnostico",
                table: "intentos_consulta",
                type: "character varying(17)",
                maxLength: 17,
                nullable: true,
                comment: "@pii:low Placa o VIN consultado (HU #13737); null si la fuente es por persona: nunca documentos en claro.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_intentos_consulta_fin",
                schema: "diagnostico",
                table: "intentos_consulta",
                sql: "fin >= inicio");

            migrationBuilder.AddCheckConstraint(
                name: "ck_intentos_consulta_tipo_error",
                schema: "diagnostico",
                table: "intentos_consulta",
                sql: "estado = 'ERROR' OR tipo_error IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_intentos_consulta_fin",
                schema: "diagnostico",
                table: "intentos_consulta");

            migrationBuilder.DropCheckConstraint(
                name: "ck_intentos_consulta_tipo_error",
                schema: "diagnostico",
                table: "intentos_consulta");

            migrationBuilder.DropColumn(
                name: "identificador_consultado",
                schema: "diagnostico",
                table: "intentos_consulta");
        }
    }
}
