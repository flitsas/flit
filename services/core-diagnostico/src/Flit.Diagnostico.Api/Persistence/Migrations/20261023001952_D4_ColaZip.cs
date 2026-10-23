using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D4_ColaZip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "lease_hasta",
                schema: "diagnostico",
                table: "evidencias",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Cola del ZIP: vencimiento del lease (GENERANDO) o inicio del reintento con backoff (PENDIENTE); null si nadie lo tiene.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "Cola del ZIP: vencimiento del lease del trabajador; null si nadie lo tiene.");

            migrationBuilder.AddColumn<int>(
                name: "vehiculos_incluidos",
                schema: "diagnostico",
                table: "evidencias",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "ZIP: vehículos con su PDF en el archivo (al pedirlo, los esperados; LISTA, los que trae). 0 en PDF y XLSX.");

            migrationBuilder.AddColumn<int>(
                name: "vehiculos_pendientes",
                schema: "diagnostico",
                table: "evidencias",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "ZIP: vehículos fuera del archivo por no haber terminado (van en el índice). 0 en PDF y XLSX.");

            migrationBuilder.CreateIndex(
                name: "ux_evidencias_zip_en_cola",
                schema: "diagnostico",
                table: "evidencias",
                columns: new[] { "tenant_id", "diagnostico_id", "huella" },
                unique: true,
                filter: "tipo = 'ZIP' AND estado IN ('PENDIENTE', 'GENERANDO')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidencias_vehiculos_zip",
                schema: "diagnostico",
                table: "evidencias",
                sql: "vehiculos_incluidos >= 0 AND vehiculos_pendientes >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_evidencias_zip_en_cola",
                schema: "diagnostico",
                table: "evidencias");

            migrationBuilder.DropCheckConstraint(
                name: "ck_evidencias_vehiculos_zip",
                schema: "diagnostico",
                table: "evidencias");

            migrationBuilder.DropColumn(
                name: "vehiculos_incluidos",
                schema: "diagnostico",
                table: "evidencias");

            migrationBuilder.DropColumn(
                name: "vehiculos_pendientes",
                schema: "diagnostico",
                table: "evidencias");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "lease_hasta",
                schema: "diagnostico",
                table: "evidencias",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Cola del ZIP: vencimiento del lease del trabajador; null si nadie lo tiene.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "Cola del ZIP: vencimiento del lease (GENERANDO) o inicio del reintento con backoff (PENDIENTE); null si nadie lo tiene.");
        }
    }
}
