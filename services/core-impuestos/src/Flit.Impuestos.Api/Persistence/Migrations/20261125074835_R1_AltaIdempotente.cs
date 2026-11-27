using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Impuestos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class R1_AltaIdempotente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cerrada_por_nombre",
                schema: "impuestos",
                table: "liquidaciones",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Nombre del actor según su token al cerrar (1.2.0).");

            migrationBuilder.AddColumn<string>(
                name: "creada_por_nombre",
                schema: "impuestos",
                table: "liquidaciones",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Nombre del actor según su token al crear (1.2.0).");

            migrationBuilder.AddColumn<string>(
                name: "motivo_cierre",
                schema: "impuestos",
                table: "liquidaciones",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "secretaria_nombre",
                schema: "impuestos",
                table: "liquidacion_items",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "webhook_secret_anterior_cifrado",
                schema: "impuestos",
                table: "credenciales_empresa",
                type: "bytea",
                nullable: true,
                comment: "@secret Data Protection, purpose impuestos.credenciales.v1.{tenant}.webhook_secret. Secreto anterior durante la ventana de rotación.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "webhook_secret_anterior_hasta",
                schema: "impuestos",
                table: "credenciales_empresa",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Fin de la ventana en la que se acepta el secreto anterior (24 h).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cerrada_por_nombre",
                schema: "impuestos",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "creada_por_nombre",
                schema: "impuestos",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "motivo_cierre",
                schema: "impuestos",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "secretaria_nombre",
                schema: "impuestos",
                table: "liquidacion_items");

            migrationBuilder.DropColumn(
                name: "webhook_secret_anterior_cifrado",
                schema: "impuestos",
                table: "credenciales_empresa");

            migrationBuilder.DropColumn(
                name: "webhook_secret_anterior_hasta",
                schema: "impuestos",
                table: "credenciales_empresa");
        }
    }
}
