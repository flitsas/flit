using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GDC_EnvioAutomaticoDp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "automatizable",
                schema: "comparendos",
                table: "tipologia_empresa",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "nota_revision_juridica",
                schema: "comparendos",
                table: "plantilla_version",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revisada_juridicamente_en",
                schema: "comparendos",
                table: "plantilla_version",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "revisada_juridicamente_por",
                schema: "comparendos",
                table: "plantilla_version",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "dp_automatico_activo",
                schema: "comparendos",
                table: "configuracion_empresa",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "dp_automatico_tope_diario",
                schema: "comparendos",
                table: "configuracion_empresa",
                type: "smallint",
                nullable: false,
                defaultValue: (short)50);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "automatizable",
                schema: "comparendos",
                table: "tipologia_empresa");

            migrationBuilder.DropColumn(
                name: "nota_revision_juridica",
                schema: "comparendos",
                table: "plantilla_version");

            migrationBuilder.DropColumn(
                name: "revisada_juridicamente_en",
                schema: "comparendos",
                table: "plantilla_version");

            migrationBuilder.DropColumn(
                name: "revisada_juridicamente_por",
                schema: "comparendos",
                table: "plantilla_version");

            migrationBuilder.DropColumn(
                name: "dp_automatico_activo",
                schema: "comparendos",
                table: "configuracion_empresa");

            migrationBuilder.DropColumn(
                name: "dp_automatico_tope_diario",
                schema: "comparendos",
                table: "configuracion_empresa");
        }
    }
}
