using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GDC_ReprocesoOcr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "prompt_version_id",
                schema: "comparendos",
                table: "documento_ocr",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "reprocesos",
                schema: "comparendos",
                table: "documento_ocr",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "prompt_version_id",
                schema: "comparendos",
                table: "documento_ocr");

            migrationBuilder.DropColumn(
                name: "reprocesos",
                schema: "comparendos",
                table: "documento_ocr");
        }
    }
}
